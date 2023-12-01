// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Message;
using Metaplay.Core.Network;
using Metaplay.Core.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Metaplay.Unity.Localization
{
    /// <summary>
    /// Manages the download cache folder of localizations and fetching new localizations there.
    /// </summary>
    public class LocalizationDownloadCache
    {
        class LanguageLocalizationFiles
        {
            OrderedSet<(ContentHash, uint)> _versionTimestamps = new OrderedSet<(ContentHash, uint)>();

            public int NumFiles => _versionTimestamps.Count;

            public void AddEntry(ContentHash version, uint timestamp)
            {
                _versionTimestamps.Add((version, timestamp));
            }
            public void RemoveEntry(ContentHash version, uint timestamp)
            {
                _versionTimestamps.Remove((version, timestamp));
            }
            public ContentHash[] GetVersions()
            {
                OrderedSet<ContentHash> versions = new OrderedSet<ContentHash>();
                foreach ((ContentHash version, uint timestamp) in _versionTimestamps)
                    versions.Add(version);
                return versions.ToArray();
            }
            public uint[] GetTimestampsForVersion(ContentHash version)
            {
                OrderedSet<uint> timestamps = new OrderedSet<uint>();
                foreach ((ContentHash fileVersion, uint timestamp) in _versionTimestamps)
                {
                    if (fileVersion == version)
                        timestamps.Add(timestamp);
                }
                return timestamps.ToArray();
            }
        }

        LogChannel _log;
        string _downloadDir;
        object _lock;
        OrderedDictionary<LanguageId, LanguageLocalizationFiles> _cached;
        uint _lastTimestamp;
        Task _initTask;

        public LocalizationDownloadCache()
        {
            if (!MetaplayCore.Options.FeatureFlags.EnableLocalizations)
                throw new NotSupportedException("LocalizationDownloadCache requires EnableLocalizations feature to be enabled");

            _log = MetaplaySDK.Logs.Localization;

#if UNITY_WEBGL && !UNITY_EDITOR
            string temporaryCachePath = "/temp";
#else
            string temporaryCachePath = Application.temporaryCachePath;
#endif
            _downloadDir = Path.Combine(temporaryCachePath, "Localizations");

            _lock = new object();
            _cached = null;
            _initTask = MetaTask.Run(async () => await InitializeAsync());
        }

        internal void Start()
        {
            MetaplaySDK.MessageDispatcher.AddListener<SessionProtocol.SessionStartSuccess>(OnSessionStart);
            MetaplaySDK.MessageDispatcher.AddListener<UpdateLocalizationVersions>(OnUpdateLocalizationVersions);
        }

        internal void Stop()
        {
            MetaplaySDK.MessageDispatcher.RemoveListener<SessionProtocol.SessionStartSuccess>(OnSessionStart);
            MetaplaySDK.MessageDispatcher.RemoveListener<UpdateLocalizationVersions>(OnUpdateLocalizationVersions);
        }

        async Task InitializeAsync()
        {
            string[] cachedLocalizations = await DirectoryUtil.GetDirectoryAndSubdirectoryFilesAsync(_downloadDir);
            Array.Sort(cachedLocalizations, StringComparer.Ordinal);

            // Localizations should follow following name <cacheRootDir>/Localizations/<languageName.mpc>/<timestamp>-<hash>
            // * Unknown languages are deleted
            // * If language version is builtin, it is deleted
            // * If language has too many versions, all are deleted (*).
            // * All other files are garbage and should be deleted.
            //
            // (*) Rationale copy-pasted from config cache upkeep logic:
            // ""
            // Run periodic cleaning of the cached versions. If number of cached versions exceeds the
            // limit, remove all cached versions that are not currently in use. Otherwise, do nothing.
            // This prevents the cache from growing boundlessly while still keeping a reasonable number
            // of recent entries in cache. Unlike more intelligent schemes like LRU, this requires no
            // state.
            // ""

            OrderedDictionary<LanguageId, ContentHash> builtins = BuiltinLanguageRepository.GetBuiltinLanguages();
            OrderedDictionary<LanguageId, List<string>> languageFiles = new OrderedDictionary<LanguageId, List<string>>();
            OrderedDictionary<LanguageId, LanguageLocalizationFiles> cached = new OrderedDictionary<LanguageId, LanguageLocalizationFiles>();

            foreach (string path in cachedLocalizations)
            {
                string subpath = path.Substring(_downloadDir.Length + 1);
                if (!ShouldKeepCached(subpath, builtins, out LanguageId lang, out uint timestamp, out ContentHash version))
                {
                    _log.Debug("DLCache: Removing unrecognized entry: {Path}", path);
                    await FileUtil.DeleteAsync(path);
                    continue;
                }

                if (!cached.ContainsKey(lang))
                    cached[lang] = new LanguageLocalizationFiles();

                // Remove duplicates
                if (cached[lang].GetTimestampsForVersion(version).Length > 0)
                {
                    _log.Debug("DLCache: Removing duplicate entry: {Path}", path);
                    await FileUtil.DeleteAsync(path);
                    continue;
                }

                cached[lang].AddEntry(version, timestamp);

                if (!languageFiles.ContainsKey(lang))
                    languageFiles[lang] = new List<string>();
                languageFiles[lang].Add(path);

                _log.Debug("DLCache: Found entry for {Language} - {Version}.", lang, version);
            }

            foreach ((LanguageId language, List<string> singleLangFiles) in languageFiles)
            {
                if (singleLangFiles.Count >= 10)
                {
                    _log.Debug("DLCache: Too many cached version for {Language}, removing all.", language);
                    cached.Remove(language);

                    foreach (string path in singleLangFiles)
                        await FileUtil.DeleteAsync(path);
                }
            }

            _log.Debug("DLCache initialization complete.");

            lock (_lock)
            {
                _cached = cached;
            }
        }

        static bool ShouldKeepCached(string subpath, OrderedDictionary<LanguageId, ContentHash> builtins, out LanguageId language, out uint timestamp, out ContentHash version)
        {
            language = null;
            version = ContentHash.None;
            timestamp = 0;

            try
            {
                string folder = subpath.Split('/')[0];
                if (folder.Length <= 4 || !folder.EndsWith(".mpc"))
                    return false;

                LanguageId parsedLang = LanguageId.FromString(folder.Substring(0, folder.Length - 4));
                if (!builtins.ContainsKey(parsedLang))
                    return false;

                string filename = subpath.Substring(folder.Length + 1);
                string timestampPart = filename.Substring(0, 8);
                string versionPart = filename.Substring(8 + 1);

                timestamp = Convert.ToUInt32(timestampPart, fromBase: 16);

                if (!ContentHash.TryParseString(versionPart, out ContentHash parsedContentHash))
                    return false;
                if (builtins[parsedLang] == parsedContentHash)
                    return false;

                language = parsedLang;
                version = parsedContentHash;
                return true;
            }
            catch
            {
                return false;
            }
        }

        string GetDownloadedLocalizationPath(LanguageId language, uint timestamp, ContentHash version)
        {
            string filename = $"{timestamp:X8}-{version}";
            return Path.Combine(_downloadDir, $"{language}.mpc", filename);
        }

        /// <summary>
        /// Retrieves the localization language from the download cache, or if that is not available, from the given CDN.
        /// </summary>
        public Task<LocalizationLanguage> GetLocalizationAsync(LanguageId language, ContentHash version, MetaplayCdnAddress cdnAddress, int numFetchAttempts, MetaDuration fetchTimeout, CancellationToken ct)
        {
            return MetaTask.Run(async () =>
            {
                await _initTask;

                // Fetch from cache
                uint[] timestamps = Array.Empty<uint>();
                lock (_lock)
                {
                    if (_cached.TryGetValue(language, out LanguageLocalizationFiles versionsInCache))
                    {
                        timestamps = versionsInCache.GetTimestampsForVersion(version);
                    }
                }
                if (timestamps.Length > 0)
                {
                    string pathInCache = GetDownloadedLocalizationPath(language, timestamps[0], version);
                    try
                    {
                        _log.Debug("Serving localization fetch of {Language} : {ConfigVersion} from cache", language.Value, version);

                        byte[] blob = await FileUtil.ReadAllBytesAsync(pathInCache);
                        LocalizationLanguage localization = LocalizationLanguage.FromBytes(language, version, blob);
                        return localization;
                    }
                    catch
                    {
                        _log.Warning("Localization {Language} : {ConfigVersion} in cache was invalid, deleting.", language.Value, version);
                        await FileUtil.DeleteAsync(pathInCache);
                    }
                }

                _log.Debug("Starting CDN fetch of localization {Language} : {ConfigVersion}", language.Value, version);

                int retryNdx = 0;
                for (;;)
                {
                    // \note: the actual download and caching are fire-and-forget and we don't try to cancel them. They will complete in the background.
                    Task                        cancellableTimeout  = MetaTask.Delay(fetchTimeout.ToTimeSpan(), ct);
                    Task<LocalizationLanguage>  fetchTask           = DownloadAndPutToCacheAsync(language, version, cdnAddress);
                    Exception                   fetchException;

                    await Task.WhenAny(fetchTask, cancellableTimeout).ConfigureAwaitFalse();

                    ct.ThrowIfCancellationRequested();

                    switch (fetchTask.Status)
                    {
                        case TaskStatus.RanToCompletion:
                            return fetchTask.GetCompletedResult();

                        case TaskStatus.Faulted:
                            fetchException = fetchTask.Exception.InnerException;
                            break;

                        default:
                        {
                            // timeout
                            fetchException = new TimeoutException();
                            break;
                        }
                    }

                    if (retryNdx < numFetchAttempts || numFetchAttempts == -1)
                    {
                        ++retryNdx;
                        _log.Debug("Localization fetching failed, will retry (retryno={RetryNo}): {Exception}", retryNdx, fetchException);
                        continue;
                    }

                    _log.Warning("Localization fetching failed, will not retry anymore: {Exception}", fetchException);
                    throw fetchException;
                }
            });
        }

        async Task<LocalizationLanguage> DownloadAndPutToCacheAsync(LanguageId language, ContentHash version, MetaplayCdnAddress cdnAddress)
        {
            HttpBlobProvider httpProvider = new HttpBlobProvider(MetaHttpClient.DefaultInstance, cdnAddress.GetSubdirectoryAddress("GameConfig").GetSubdirectoryAddress("Localizations"));
            byte[] bytes = await httpProvider.GetAsync($"{language}.mpc", version);

            // Test we can parse it
            LocalizationLanguage localization = LocalizationLanguage.FromBytes(language, version, bytes);

            // Put into cache
            uint timestamp;
            lock (_lock)
            {
                timestamp = (uint)(MetaTime.Now.MillisecondsSinceEpoch / 1000);
                timestamp = Math.Max(timestamp, _lastTimestamp + 1);
                _lastTimestamp = timestamp;
            }
            string pathInCache = GetDownloadedLocalizationPath(language, timestamp, version);

            await DirectoryUtil.EnsureDirectoryExistsAsync(Path.GetDirectoryName(pathInCache));
            await FileUtil.WriteAllBytesAtomicAsync(pathInCache, bytes);

            // Mark as available
            lock (_lock)
            {
                if (!_cached.ContainsKey(language))
                    _cached[language] = new LanguageLocalizationFiles();
                _cached[language].AddEntry(version, timestamp);
            }

            return localization;
        }

        void OnSessionStart(SessionProtocol.SessionStartSuccess sessionStart)
        {
            OrderedDictionary<LanguageId, ContentHash> latestLocalizations = sessionStart.LocalizationVersions;
            PruneOldLocalizationsInBackground(latestLocalizations);
        }

        void OnUpdateLocalizationVersions(UpdateLocalizationVersions update)
        {
            OrderedDictionary<LanguageId, ContentHash> latestLocalizations = update.LocalizationVersions;
            PruneOldLocalizationsInBackground(latestLocalizations);
        }

        void PruneOldLocalizationsInBackground(OrderedDictionary<LanguageId, ContentHash> latestLocalizations)
        {
            List<(LanguageId, uint, ContentHash)> versionsToDelete = new List<(LanguageId, uint, ContentHash)>();
            lock (_lock)
            {
                // If we haven't completely init'd by now, just ignore.
                if (_cached == null)
                    return;

                foreach ((LanguageId language, LanguageLocalizationFiles files) in _cached)
                {
                    foreach (ContentHash version in files.GetVersions())
                    {
                        bool shouldDelete;

                        if (!latestLocalizations.ContainsKey(language))
                        {
                            // If the language is not known to the server, delete.
                            shouldDelete = true;
                        }
                        else if (latestLocalizations[language] != version)
                        {
                            // If the language in the cache is not the latest known by the server, delete.
                            shouldDelete = true;
                        }
                        else
                        {
                            // If the language is known to the server and is the most recent, keep.
                            shouldDelete = false;
                        }

                        if (shouldDelete)
                        {
                            foreach (uint timestamp in files.GetTimestampsForVersion(version))
                                versionsToDelete.Add((language, timestamp, version));
                        }
                    }
                }

                // Remove visibility first, then delete in background
                foreach ((LanguageId language, uint timestamp, ContentHash version) in versionsToDelete)
                {
                    _cached[language].RemoveEntry(version, timestamp);
                    if (_cached[language].NumFiles == 0)
                        _cached.Remove(language);
                }
            }

            // Delete in background
            _ = MetaTask.Run(async () =>
            {
                foreach ((LanguageId language, uint timestamp, ContentHash version) in versionsToDelete)
                {
                    string path = GetDownloadedLocalizationPath(language, timestamp, version);
                    _log.Debug("DLCache: Removing pruned old entry: {Path}", path);
                    await FileUtil.DeleteAsync(path);
                    await Task.Yield();
                }
            });
        }

#if UNITY_EDITOR
        public ContentHash[] EditorTryGetCachedVersions(LanguageId language)
        {
            lock (_lock)
            {
                if (_cached == null)
                    return null;
                return _cached.GetValueOrDefault(language)?.GetVersions() ?? Array.Empty<ContentHash>();
            }
        }
#endif
    }
}
