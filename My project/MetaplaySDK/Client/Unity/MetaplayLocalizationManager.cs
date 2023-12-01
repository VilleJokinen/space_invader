// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Client.Messages;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Message;
using Metaplay.Core.Network;
using Metaplay.Core.Player;
using Metaplay.Core.Tasks;
using Metaplay.Unity.Localization;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Unity
{
    public interface IMetaplayLocalizationDelegate
    {
        /// <summary>
        /// If true, SDK may switch the active language to a more-up-to-date language version during an ongoing session.
        /// Otherwise, language is only changed during session start or as a reaction to explicit language change operation.
        /// </summary>
        bool AutoActivateLanguageUpdates { get; }

        /// <summary>
        /// Called by SDK when the initial language has been loaded during SDK init.
        /// </summary>
        void OnInitialLanguageSet();

        /// <summary>
        /// Called by SDK when Active Language or Localization version has changed AFTER SDK init. This can happen
        /// during as a reaction to connection succeeding to the server and language selection being synchronized
        /// from server state, or as a reaction to user language change.
        /// </summary>
        void OnActiveLanguageChanged();
    }

    public sealed class MetaplayLocalizationManager
    {
#if UNITY_EDITOR
        /// <summary>
        /// Editor only: Event called just before <see cref="IMetaplayLocalizationDelegate.OnActiveLanguageChanged"/>.
        /// </summary>
        public Action EditorHookLocalizationUpdatedEvent = null;

        /// <summary>
        /// Editor only.
        /// </summary>
        public LocalizationDownloadCache DownloadCache => _dlCache;
#endif

        LogChannel                                  _log;
        LocalizationDownloadCache                   _dlCache;
        OrderedDictionary<LanguageId, ContentHash>  _builtinLanguages;
        CancellationTokenSource                     _stopCts;
        OrderedDictionary<LanguageId, ContentHash>  _serverLocalizationVersions;
        IMetaplayLocalizationDelegate               _delegate;
        bool                                        _hasSession;

        internal MetaplayLocalizationManager()
        {
            _log = MetaplaySDK.Logs.Localization;
            _builtinLanguages = null;
            _stopCts = new CancellationTokenSource();
            _stopCts.Cancel();

            if (MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                _dlCache = new LocalizationDownloadCache();
        }

        internal void Start(IMetaplayLocalizationDelegate managerDelegate)
        {
            _delegate = managerDelegate;

            MetaplaySDK.MessageDispatcher.AddListener<UpdateLocalizationVersions>(OnUpdateLocalizationVersions);
            MetaplaySDK.MessageDispatcher.AddListener<SessionProtocol.SessionStartSuccess>(OnSessionStarted);
            MetaplaySDK.MessageDispatcher.AddListener<DisconnectedFromServer>(OnDisconnectedFromServer);

            if (MetaplayCore.Options.FeatureFlags.EnableLocalizations)
            {
                _builtinLanguages = BuiltinLanguageRepository.GetBuiltinLanguages();
                LocalizationLanguage localization = BuiltinLanguageRepository.GetAppStartLocalization();

                MetaplaySDK.ActiveLanguage = localization;
                _dlCache.Start();
            }
            else
            {
                LocalizationLanguage noneLanguage = new LocalizationLanguage(LanguageId.FromString("none"), ContentHash.None, new OrderedDictionary<TranslationId, string>());
                MetaplaySDK.ActiveLanguage = noneLanguage;
            }

            _stopCts = new CancellationTokenSource();
        }

        internal void Stop()
        {
            _delegate = null;
            _hasSession = false;

            MetaplaySDK.MessageDispatcher.RemoveListener<UpdateLocalizationVersions>(OnUpdateLocalizationVersions);
            MetaplaySDK.MessageDispatcher.RemoveListener<SessionProtocol.SessionStartSuccess>(OnSessionStarted);
            MetaplaySDK.MessageDispatcher.RemoveListener<DisconnectedFromServer>(OnDisconnectedFromServer);

            // \note: _serverLocalizationVersions intentionally leaks over. It is the latest known info.

            _dlCache?.Stop();
            _stopCts.Cancel();
        }

        internal void AfterSDKInit()
        {
            if (MetaplayCore.Options.FeatureFlags.EnableLocalizations)
            {
                try
                {
                    _delegate?.OnInitialLanguageSet();
                }
                catch (Exception ex)
                {
                    _log.Warning("Failure in localization delegate: {Error}", ex);
                }
            }
        }

        void OnUpdateLocalizationVersions(UpdateLocalizationVersions update)
        {
            _serverLocalizationVersions = update.LocalizationVersions;
            HandleServerLocalizationsChanged();
        }

        void OnSessionStarted(SessionProtocol.SessionStartSuccess sessionStart)
        {
            _hasSession = true;
            _serverLocalizationVersions = sessionStart.LocalizationVersions;
            HandleServerLocalizationsChanged();
        }

        void OnDisconnectedFromServer(DisconnectedFromServer _)
        {
            _hasSession = false;
        }

        void HandleServerLocalizationsChanged()
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                return;

            // If got update for the active language, start downloading it.
            LanguageId language = MetaplaySDK.ActiveLanguage.LanguageId;
            ContentHash currentVersion = MetaplaySDK.ActiveLanguage.Version;
            if (_serverLocalizationVersions.TryGetValue(language, out ContentHash updatedVersion) && currentVersion != updatedVersion)
            {
                // \note: FetchLanguageAsync will fetch from builtin if content is available there.
                Task<LocalizationLanguage> fetchTask = FetchLanguageAsync(language, updatedVersion, MetaplaySDK.CdnAddress, MetaplaySDK.Connection.Config.ConfigFetchAttemptsMaxCount, MetaplaySDK.Connection.Config.ConfigFetchTimeout, default(CancellationToken));

                // Enqueue switch to it when it completes, but only if state has not been changed by then.
                if (_delegate != null && _delegate.AutoActivateLanguageUpdates)
                {
                    EnqueueSwitchToLanguageOnComplete(WithCancelAtSdkStop(fetchTask), onCompleted: null);
                }
            }
        }

        Task<LocalizationLanguage> WithCancelAtSdkStop(Task<LocalizationLanguage> fetchTask)
        {
            CancellationToken ct = _stopCts.Token;
            return fetchTask.ContinueWith(async (task) =>
                {
                    LocalizationLanguage result = await task;
                    ct.ThrowIfCancellationRequested();
                    return result;
                }, scheduler: MetaTask.UnityMainScheduler)
                .Unwrap();
        }

        void EnqueueSwitchToLanguageOnComplete(Task<LocalizationLanguage> fetchTask, Action onCompleted)
        {
            _ = fetchTask.ContinueWith((task) =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    LocalizationLanguage localization = task.GetCompletedResult();

                    _log.Debug("Language fetch completed. Switching to language {Language}, version {Version}.", localization.LanguageId, localization.Version);

                    // \note: Continuation runs on Unity thread, so no need to lock.
                    UpdateActiveLanguage(localization);

                    // Try send change action. If we are not in a session, this won't work but it does not need to
                    ISharedGameConfig activeGameConfig = MetaplaySDK.SessionContext?.PlayerContext?.Journal?.StagedModel.GameConfig;
                    if (activeGameConfig != null)
                    {
                        LanguageInfo languageInfoMaybe = activeGameConfig.Languages.GetValueOrDefault(localization.LanguageId);
                        if (languageInfoMaybe != null)
                        {
                            PlayerChangeLanguage action = new PlayerChangeLanguage(languageInfoMaybe, localization.Version);
                            MetaplaySDK.SessionContext?.PlayerContext?.ExecuteAction(action);
                        }
                    }

                    // External callback
                    onCompleted?.Invoke();
                }
                else if (task.Status == TaskStatus.Canceled)
                {
                    // cancelled.
                    _log.Debug("Language fetch completed but switch request was already cancelled. Ignored.");
                }
                else if (task.Status == TaskStatus.Faulted)
                {
                    _log.Warning("Language fetch failed: {Error}", task.Exception);

                    onCompleted?.Invoke();
                }
            }, scheduler: MetaTask.UnityMainScheduler);
        }

        void UpdateActiveLanguage(LocalizationLanguage localization)
        {
            MetaplaySDK.ActiveLanguage = localization;

#if UNITY_EDITOR
            EditorHookLocalizationUpdatedEvent?.Invoke();
#endif
            try
            {
                _delegate?.OnActiveLanguageChanged();
            }
            catch (Exception ex)
            {
                _log.Warning("Failure when switching to language {Language}:{Version}: {Error}", localization.LanguageId.ToString(), localization.Version, ex);
            }
        }

        /// <summary>
        /// Fetches the language from builtin resources, DL cache or CDN.
        /// </summary>
        public Task<LocalizationLanguage> FetchLanguageAsync(LanguageId language, ContentHash version, MetaplayCdnAddress cdnAddress, int numFetchAttempts, MetaDuration fetchTimeout, CancellationToken ct)
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                throw new NotSupportedException("MetaplayLocalizationManager.FetchLanguageAsync requires EnableLocalizations feature to be enabled");

            if (_builtinLanguages.TryGetValue(language, out ContentHash builtinVersion) && version == builtinVersion)
                return BuiltinLanguageRepository.GetLocalizationAsync(language);

            return _dlCache.GetLocalizationAsync(language, version, cdnAddress, numFetchAttempts, fetchTimeout, ct);
        }

        internal void ActivateSessionStartLanguage(LocalizationLanguage localization)
        {
            UpdateActiveLanguage(localization);
        }

        public void OnSessionStart(SessionProtocol.SessionStartSuccess startSuccess, IPlayerModelBase model)
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                return;

            if (model.LanguageSelectionSource == LanguageSelectionSource.UserSelected)
                BuiltinLanguageRepository.StoreAppStartLanguage(model.Language);
        }

#if UNITY_EDITOR
        public ContentHash EditorTryGetLocalizationServerVersion(LanguageId language)
        {
            // \note: This is editor-only, don't bother locking.
            if (_serverLocalizationVersions == null)
                return ContentHash.None;
            return _serverLocalizationVersions.GetValueOrDefault(language);
        }
#endif

        /// <summary>
        /// Sets the given language as the current active app language. The language change will happen on the background.
        /// After a successful or unsuccessful language change, <paramref name="onCompleted"/> is called on Unity thread.
        /// If a new language version is available on backend, the latest version is attempted to be downloaded first.
        /// </summary>
        public void SetCurrentLanguage(LanguageId language, Action onCompleted)
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                throw new NotSupportedException("MetaplayLocalizationManager.SetCurrentLanguage requires EnableLocalizations feature to be enabled");

            Action wrappedOnCompleted = () =>
            {
                try
                {
                    onCompleted?.Invoke();
                }
                catch (Exception ex)
                {
                    _log.Warning("Language update notification failed: {Error}", ex);
                }
            };

            _log.Debug("Language change to {Language} requested.", language);

            // If language can be switched to at launch time too, remember the setting
            ContentHash builtinVersion = _builtinLanguages.GetValueOrDefault(language);
            if (builtinVersion != ContentHash.None)
                BuiltinLanguageRepository.StoreAppStartLanguage(language);

            // If we have a session, and there is a newer version available, try to switch to server version. On failure, use builtin language.
            if (_hasSession)
            {
                ContentHash versionOnServerMaybe = ContentHash.None;
                if (_serverLocalizationVersions != null)
                    versionOnServerMaybe = _serverLocalizationVersions.GetValueOrDefault(language);

                // Newer available than builtin.
                if (versionOnServerMaybe != ContentHash.None && versionOnServerMaybe != builtinVersion)
                {
                    _log.Debug("Built-in language {Language} is not up-to-date, try fetch from CDN (or cache).", language);

                    Task<LocalizationLanguage> fetchTask = _dlCache.GetLocalizationAsync(language, versionOnServerMaybe, MetaplaySDK.CdnAddress, MetaplaySDK.Connection.Config.ConfigFetchAttemptsMaxCount, MetaplaySDK.Connection.Config.ConfigFetchTimeout, default(CancellationToken));
                    Task<LocalizationLanguage> fetchOrFallbackTask = MetaTask.Run<LocalizationLanguage>(async () =>
                    {
                        try
                        {
                            return await fetchTask;
                        }
                        catch
                        {
                            return await BuiltinLanguageRepository.GetLocalizationAsync(language);
                        }
                    });

                    EnqueueSwitchToLanguageOnComplete(WithCancelAtSdkStop(fetchTask), wrappedOnCompleted);
                    return;
                }
            }

            _log.Debug("Completing request by using built-in localization for {Language}.", language);

            // Use builtin version
            Task<LocalizationLanguage> builtinFetchTask = BuiltinLanguageRepository.GetLocalizationAsync(language);
            EnqueueSwitchToLanguageOnComplete(WithCancelAtSdkStop(builtinFetchTask), wrappedOnCompleted);
        }
    }
}
