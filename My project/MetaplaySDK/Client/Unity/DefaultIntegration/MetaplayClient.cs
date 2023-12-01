// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#   define UNITY_WEBGL_BUILD
#endif

using Metaplay.Client.Messages;
using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
#if !METAPLAY_DISABLE_GUILDS
using Metaplay.Core.Guild;
#endif
using Metaplay.Core.Localization;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Network;
using Metaplay.Core.Web3;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Unity.ConnectionStates;
using Metaplay.Unity.IAP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static System.FormattableString;

namespace Metaplay.Unity.DefaultIntegration
{
    public class MetaplayClientOptions
    {
        /// <summary>
        /// True, if Metaplay SDK should create and inject MetaplaySDKBehavior GameObject script into the scene.
        /// False, if MetaplaySDKBehavior should be expected in the scene.
        /// </summary>
        public bool AutoCreateMetaplaySDKBehavior = false;

        public IMetaplayLifecycleDelegate LifecycleDelegate; // Optional (no-op by default)

        public ConnectionConfig ConnectionConfig; // Optional
        public MetaplayOfflineOptions OfflineOptions = new MetaplayOfflineOptions(); // Optional (by default, offline state is persisted)
        public MetaplayAppInfoOptions AppInfoOptions; // Optional (has defaults)
        public MetaplayIAPOptions IAPOptions; // Optional (has defaults)

        public IMetaplayClientConnectionDelegate ConnectionDelegate; // Optional (uses DefaultMetaplayConnectionDelegate by default)
        public IMetaplayLocalizationDelegate LocalizationDelegate; // Optional (uses DefaultMetaplayLocalizationDelegate by default)
        public IMetaplayClientAnalyticsDelegate AnalyticsDelegate; // Optional (null to disable)
        public IMetaplayClientSocialAuthenticationDelegate SocialAuthenticationDelegate; // Optional (null to disable)
        public IMetaplayClientGameConfigDelegate GameConfigDelegate; // Optional (null to disable)

        public MetaplayClientCreatePlayerContextFunc CreatePlayerContext; // Optional (uses MetaplayClient.DefaultCreatePlayerContext by default)

        public IMetaplaySubClient[] AdditionalClients;
    }

    public delegate IPlayerClientContext MetaplayClientCreatePlayerContextFunc(IPlayerModelBase model, int currentOperation, EntityId playerId, int logicVersion, MetaTime now);
    
    public class MetaplayAppInfoOptions
    {
        public BuildVersion? BuildVersion;
    }

    public class MetaplayIAPOptions
    {
        public bool EnableIAPManager = false;
    }


    /// <summary>
    /// Connection quality.
    /// </summary>
    public enum ConnectionHealth
    {
        /// <summary>
        /// The connection is not established.
        /// </summary>
        NotConnected,

        /// <summary>
        /// The connection is healthy. The SDK has not detected any signs of a flaky or poor network connectivity.
        /// </summary>
        Healthy,

        /// <summary>
        /// The connection quality is degraded due to packet loss or high latencies,
        /// or other suspicious behavior. Consider showing a connection quality warning to the
        /// user.
        /// </summary>
        Unhealthy,
    }

    public class MetaplayClientState : ISessionContextProvider, ISessionStartHook, IDisposable
    {
        public IPlayerClientContext PlayerContext { get; private set; }
        public MetaplayClientStore ClientStore { get; }
        public MetaplayClientOptions Options { get; }
        public IAPManager IAPManager { get; }
        public IAPFlowTracker IAPFlowTracker { get; }
        public NftClient NftClient => ClientStore.TryGetClient<NftClient>(ClientSlotCore.Nft);

        bool _hasHandledCurrentConnectionError = false;
        readonly LogChannel _mainLog = MetaplaySDK.Logs.CreateChannel("main");
        readonly LogChannel _playerLog = MetaplaySDK.Logs.CreateChannel("player");
        readonly IMetaplayClientConnectionDelegate _connectionDelegate;
        SessionProtocol.SessionStartSuccess _initialState;
        Queue<Action> _pendingSessionFuncs = new Queue<Action>();

        static MetaplayConnection Connection => MetaplaySDK.Connection;

        public IPlayerModelBase PlayerModel => PlayerContext?.Journal.StagedModel;
        public T GetPlayerModel<T>() where T : IPlayerModelBase => (T)PlayerModel;

        public MetaplayClientState(MetaplayClientOptions options, Action deinitializeFunc)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));

            _connectionDelegate = options.ConnectionDelegate ?? new DefaultMetaplayConnectionDelegate();
            _connectionDelegate.SessionStartHook = this;
            _connectionDelegate.SessionContext = this;
            
            MetaplayAppInfoOptions appInfoOptions = options.AppInfoOptions ?? new MetaplayAppInfoOptions();

            MetaplaySDKConfig config = new MetaplaySDKConfig
            {
                BuildVersion = appInfoOptions.BuildVersion ?? new BuildVersion(version: Application.version, buildNumber: "", commitId: ""),
                AutoCreateMetaplaySDKBehavior = options.AutoCreateMetaplaySDKBehavior,
                ConnectionConfig = options.ConnectionConfig ?? new ConnectionConfig(),
                OfflineOptions = options.OfflineOptions ?? new MetaplayOfflineOptions(),
                ConnectionDelegate = _connectionDelegate,
                LocalizationDelegate = options.LocalizationDelegate ?? new DefaultMetaplayLocalizationDelegate(),
                SocialAuthenticationDelegate = options.SocialAuthenticationDelegate,
                SessionContext = this,
                ExitRequestedCallback = deinitializeFunc,
            };
            MetaplaySDK.Start(config);

            // Event listeners
            MetaplaySDK.MessageDispatcher.AddListener<SessionProtocol.SessionStartSuccess>(OnSessionStartSuccess);
            MetaplaySDK.MessageDispatcher.AddListener<SocialAuthenticateForceReconnect>(OnSocialAuthenticateForceReconnect);
            MetaplaySDK.MessageDispatcher.AddListener<PlayerChecksumMismatch>(OnPlayerChecksumMismatch);
            MetaplaySDK.MessageDispatcher.AddListener<PlayerAckActions>(OnPlayerAckActions);
            MetaplaySDK.MessageDispatcher.AddListener<PlayerExecuteUnsynchronizedServerAction>(OnPlayerExecuteUnsynchronizedServerAction);
            MetaplaySDK.MessageDispatcher.AddListener<PlayerEnqueueSynchronizedServerAction>(OnPlayerEnqueueSynchronizedServerAction);
            MetaplaySDK.MessageDispatcher.AddListener<DisconnectedFromServer>(OnDisconnectedFromServer);

            // Create IAPManager if desired
            MetaplayIAPOptions iapOptions = options.IAPOptions ?? new MetaplayIAPOptions();
            if (iapOptions.EnableIAPManager)
            {
                IAPManager = new IAPManager(sessionContextProvider: this);
                IAPFlowTracker = new IAPFlowTracker(IAPManager);
            }

            ClientStore = new MetaplayClientStore(() => PlayerContext);

#if !METAPLAY_DISABLE_GUILDS
            // \todo: Use IntegrationRegistry for overriding guild client
            bool shouldAddDefaultGuildClient = options.AdditionalClients?.All(client => client.ClientSlot != ClientSlotCore.Guild) ?? true;
            if (shouldAddDefaultGuildClient)
                ClientStore.AddClient(new GuildClient());
#endif

            if (MetaplayCore.Options.FeatureFlags.EnableWeb3)
                ClientStore.AddClient(new NftClient());

            // Add additional clients from options
            foreach (IMetaplaySubClient entityClient in options.AdditionalClients ?? Enumerable.Empty<IMetaplaySubClient>())
                ClientStore.AddClient(entityClient);

            // Initialize all clients
            IMetaplaySubClientServices clientServices = new MetaplayUnitySubClientServices(ClientStore, IEnvironmentConfigProvider.Get().ClientDebugConfig.EnablePlayerConsistencyChecks);
            ClientStore.Initialize(clientServices);

            // Register Firebase tokens to Metaplay server (if Firebase is enabled)
            // \note Explicitly disabled in WebGL builds because it seems that there even the
            //       Firebase namespace is not available even if the Firebase package is present.
#if METAPLAY_ENABLE_FIREBASE_MESSAGING && !UNITY_WEBGL_BUILD
            Firebase.Messaging.FirebaseMessaging.TokenReceived += (sender, tokenReceived) =>
                OnFirebaseTokenReceived(tokenReceived.Token);
#endif
        }

        public void Dispose()
        {
            if (PlayerContext != null)
                ClearSessionState();

            if (IAPFlowTracker != null)
                IAPFlowTracker.Dispose();

            // Dispose all clients
            ClientStore.Dispose();

            // Unregister listeners
            MetaplaySDK.MessageDispatcher.RemoveListener<SessionProtocol.SessionStartSuccess>(OnSessionStartSuccess);
            MetaplaySDK.MessageDispatcher.RemoveListener<SocialAuthenticateForceReconnect>(OnSocialAuthenticateForceReconnect);
            MetaplaySDK.MessageDispatcher.RemoveListener<PlayerChecksumMismatch>(OnPlayerChecksumMismatch);
            MetaplaySDK.MessageDispatcher.RemoveListener<PlayerAckActions>(OnPlayerAckActions);
            MetaplaySDK.MessageDispatcher.RemoveListener<PlayerExecuteUnsynchronizedServerAction>(OnPlayerExecuteUnsynchronizedServerAction);
            MetaplaySDK.MessageDispatcher.RemoveListener<PlayerEnqueueSynchronizedServerAction>(OnPlayerEnqueueSynchronizedServerAction);
            MetaplaySDK.MessageDispatcher.RemoveListener<DisconnectedFromServer>(OnDisconnectedFromServer);

            MetaplaySDK.Stop();
        }

        public void OnSessionStarted(ClientSessionStartResources startResources)
        {
            // custom resource management could happen here.
        }

        /// <summary>
        /// Server has acknowledged all ticks and actions until a given tick. Any data kept for
        /// handling desyncs for those ticks can now be purged.
        /// </summary>
        /// <param name="ackActions"></param>
        void OnPlayerAckActions(PlayerAckActions ackActions)
        {
            JournalPosition untilPosition = JournalPosition.FromTickOperationStep(ackActions.UntilPositionTick, ackActions.UntilPositionOperation, ackActions.UntilPositionStep);

            //_mainLog.Debug("MetaplayClient.OnPlayerAckActions(): UntilPosition={UntilPosition}", untilPosition);
            PlayerContext.PurgeSnapshotsUntil(untilPosition);
        }

        /// <summary>
        /// Handle initial player state received from server.
        /// </summary>
        void OnSessionStartSuccess(SessionProtocol.SessionStartSuccess sessionStartSuccess)
        {
            _initialState = sessionStartSuccess;

            // Suspend any further messages until we have handled _initialState. This keeps handling of
            // messages in order (in particular, we don't handle any other message until _initialState
            // has been handled).
            Connection.SuspendMessageProcessing(true);
        }

        void OnSocialAuthenticateForceReconnect(SocialAuthenticateForceReconnect reconnect)
        {
            _mainLog.Info("Received SocialAuthenticateForceReconnect");

            // Route as a connection error via MetaplayConnection to handle it via the general path
            Connection.CloseWithError(flushEnqueuedMessages: true, new SocialAuthenticateForceReconnectConnectionError());
        }

        void OnPlayerChecksumMismatch(PlayerChecksumMismatch mismatch)
        {
            _mainLog.Warning("Received PlayerChecksumMismatch from server, resolving details and ending session");

            // Report the mismatch
            PlayerChecksumMismatchDetails details = PlayerContext.ResolveChecksumMismatch(mismatch);

            // Send details to server
            MetaplaySDK.MessageDispatcher.SendMessage(details);

            // Route as a connection error via MetaplayConnection to handle it via the general path
            PlayerActionBase action = details.Action.IsEmpty ? null : details.Action.Deserialize(PlayerModel.GameConfig, PlayerModel.LogicVersion);
            Connection.CloseWithError(flushEnqueuedMessages: true, new PlayerChecksumMismatchConnectionError(details.TickNumber, action, details.PlayerModelDiff, details.VagueDifferencePathsMaybe));
        }

        public void OnFirebaseTokenReceived(string token)
        {
            _mainLog.Debug("Received Firebase token: " + token);

            EnqueueFuncToExecuteDuringSession(() =>
            {
                if (!PlayerModel.PushNotifications.HasFirebaseMessagingToken(deviceId: PlayerModel.SessionDeviceGuid, token: token))
                    PlayerContext.ExecuteAction(new PlayerAddFirebaseMessagingToken(token));
            });
        }

        void EnqueueFuncToExecuteDuringSession(Action func)
        {
            if (PlayerContext != null)
                func();
            else
            {
                _mainLog.Debug("Enqueued func to execute when session starts");
                _pendingSessionFuncs.Enqueue(func);
            }
        }

        /// <summary>
        /// Start a new connection to the server.
        /// This can only be called when there is no ongoing connection.
        /// </summary>
        public void Connect()
        {
            // For convenience, allow game to call Connect in an error state,
            // without requiring it to first Close the errored connection
            // explicitly. On the abstraction level relevant for the game,
            // the error state is often as good as "not connected".
            if (Connection.State.Status == ConnectionStatus.Error)
            {
                _hasHandledCurrentConnectionError = false;
                Connection.Close(flushEnqueuedMessages: false);
            }

            Connection.Connect();
        }

        /// <summary>
        /// Update Metaplay connection and other SDK state.
        /// This should be called by the game every frame.
        /// </summary>
        public void Update()
        {
            // If we have session state but are in a connection status where
            // we do not want session state, clear the session state now.
            if (PlayerContext != null // Have session state?
             && Connection.State.Status != ConnectionStatus.Connected // Having session state is expected for Connected status
             && Connection.State.Status != ConnectionStatus.Error)    // Having session state is ok in Error status because we want to permit the game scene to linger even upon error
            {
                ClearSessionState();
            }

            // If _hasHandledCurrentConnectionError has been left on after an Error status, turn it off now.
            if (_hasHandledCurrentConnectionError && Connection.State.Status != ConnectionStatus.Error)
                _hasHandledCurrentConnectionError = false;

            // Manage connections
            MetaplaySDK.Update();

            ClientStore.EarlyUpdate();

            // When all the async loaded resources are available...
            if (_initialState != null)
            {
                // Resume message processing we suspended when _initialState was set
                Connection.SuspendMessageProcessing(false);

                _mainLog.Info("Initial state received, starting session");

                ISharedGameConfig   playerGameConfig    = Connection.SessionStartResources.GameConfigs[ClientSlotCore.Player];
                int                 logicVersion        = Connection.SessionStartResources.LogicVersion;

                // Deserialize and initialize PlayerModel
                IPlayerModelBase model = _initialState.PlayerState.PlayerModel.Deserialize(playerGameConfig, logicVersion);
                model.LogicVersion = logicVersion;
                model.GameConfig = playerGameConfig;
                model.Log = _playerLog;

                // Note: Enable the following line to log the initial PlayerModel into the console upon connecting to the server
                //_mainLog.Debug("Initial player state: {InitialPlayerModel}", PrettyPrint.Verbose(model));

                // If analytics delegate specified, enable routing player events to it
                if (Options.AnalyticsDelegate != null)
                {
                    model.AnalyticsEventHandler = new AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>((IPlayerModelBase analyticsSourceModel, PlayerEventBase payload) =>
                    {
                        // Only forward player events if we have an active session
                        if (Connection.State.Status == ConnectionStatus.Connected)
                            TryCallAnalyticsDelegate(payload, analyticsSourceModel);
                    });
                }

                // Initialize PlayerModel updater
                MetaplayClientCreatePlayerContextFunc createPlayerContext = Options.CreatePlayerContext ?? DefaultCreatePlayerContext;
                PlayerContext = createPlayerContext(
                    model,
                    _initialState.PlayerState.CurrentOperation,
                    _initialState.PlayerId,
                    logicVersion,
                    MetaTime.Now);

                ClientStore.OnSessionStart(_initialState, Connection.SessionStartResources);

                // If in offline mode, hook OfflineServer as the listener
                if (Connection.Endpoint.IsOfflineMode)
                {
                    IOfflineServer offlineServer = Connection.OfflineServer;
                    offlineServer?.SetPlayerJournal(PlayerContext.Journal);
                    offlineServer?.SetListeners(ClientStore);
                }

                MetaplaySDK.LocalizationManager.OnSessionStart(_initialState, model);

                // Forget initial state now that it is loaded
                _initialState = null;

                // Execute funcs that were enqueued to be executed during session
                if (_pendingSessionFuncs.Count > 0)
                {
                    _mainLog.Debug("Flushing {Count} session funcs", _pendingSessionFuncs.Count);

                    while (_pendingSessionFuncs.TryDequeue(out Action func))
                    {
                        try
                        {
                            func();
                        }
                        catch (Exception ex)
                        {
                            _mainLog.Error("Pending session func failed: {Error}", ex);
                            MetaplaySDK.IncidentTracker.ReportUnhandledException(ex);
                        }
                    }
                }

                // Inform IAPManager
                if (IAPManager != null)
                    IAPManager.OnSessionStarted();

                // Inform the game
                Options.LifecycleDelegate?.OnSessionStarted();
            }

            // Handle connection errors.
            if (Connection.State.Status == ConnectionStatus.Error && !_hasHandledCurrentConnectionError)
            {
                _hasHandledCurrentConnectionError = true;
                HandleConnectionError(Connection.State, Connection.Statistics);
            }

            // Update IAP manager
            if (IAPManager != null)
                IAPManager.Update();

            // Update player logic (when player state exists)
            if (PlayerContext != null)
                PlayerContext.Update(MetaTime.Now);

            ClientStore.UpdateLogic(MetaTime.Now);

            // Flush analytics events produced by incidents
            foreach (PlayerEventIncidentRecorded ev in MetaplaySDK.IncidentTracker.GetAndClearPendingAnalyticsEvents())
            {
                // \note PlayerModel may be null here, if there is no session.
                //       Incident analytics are a bit tricky because it's not easy
                //       to tell if they are PlayerModel-related.
                TryCallAnalyticsDelegate(ev, PlayerModel);
            }
        }

        IPlayerClientContext DefaultCreatePlayerContext(IPlayerModelBase model, int currentOperation, EntityId playerId, int logicVersion, MetaTime now)
        {
            return new DefaultPlayerClientContext(
                log:                        _playerLog,
                playerModel:                model,
                currentOperation:           currentOperation,
                playerId:                   playerId,
                logicVersion:               logicVersion,
                timelineHistory:            MetaplaySDK.TimelineHistory,
                sendMessageToServer:        MetaplaySDK.MessageDispatcher.SendMessage,
                enableConsistencyChecks:    IEnvironmentConfigProvider.Get().ClientDebugConfig.EnablePlayerConsistencyChecks,
                checksumGranularity:        IEnvironmentConfigProvider.Get().ClientDebugConfig.PlayerChecksumGranularity,
                startTime:                  now);
        }


        /// <summary>
        /// If a <see cref="IMetaplayClientAnalyticsDelegate"/> is registered, call it
        /// with the given parameters, tolerating exceptions from it.
        /// </summary>
        void TryCallAnalyticsDelegate(AnalyticsEventBase payload, IModel model)
        {
            if (Options.AnalyticsDelegate != null)
            {
                AnalyticsEventSpec eventSpec = AnalyticsEventRegistry.GetEventSpec(payload.GetType());
                try
                {
                    Options.AnalyticsDelegate.OnAnalyticsEvent(eventSpec, payload, model);
                }
                catch (Exception ex)
                {
                    _mainLog.Error("Exception during OnAnalyticsEvent() for {EventType}: {Exception}", eventSpec.EventType, ex);
                }
            }
        }

        /// <summary>
        /// The server requests us to execute an action. Execute it immediately.
        /// Note that the ServerActions have already been executed on the server (on a previous
        /// tick), so they may only modify state that is marked with the [NoChecksum] attribute.
        /// </summary>
        /// <param name="execute">Message containing the ServerAction to execute</param>
        void OnPlayerExecuteUnsynchronizedServerAction(PlayerExecuteUnsynchronizedServerAction execute)
        {
            PlayerContext.ExecuteServerAction(execute);
        }

        void OnPlayerEnqueueSynchronizedServerAction(PlayerEnqueueSynchronizedServerAction enqueued)
        {
            PlayerContext.ExecuteServerAction(enqueued);
        }

        void OnDisconnectedFromServer(DisconnectedFromServer disconnected)
        {
            PlayerContext?.OnDisconnected();
            ClientStore.OnDisconnected();
        }

        /// <summary>
        /// Throw away the current session state.
        /// </summary>
        public void ClearSessionState()
        {
            _mainLog.Info("Clearing session state");

            if (IAPManager != null)
                IAPManager.OnSessionEnded();

            PlayerContext = null;

            ClientStore.OnSessionStop();

            MetaplaySDK.MessageDispatcher.ClearPendingRequests();
        }

        /// <summary>
        /// Handler for <see cref="SharedGameConfig"/> being updated while the game is running (in offline mode).
        ///
        /// Loads the new configs by instantiating a new <see cref="SharedGameConfig"/>, and updates any references
        /// to the configs in <see cref="PlayerModel"/>.
        /// </summary>
        public void OnSharedGameConfigUpdated(ConfigArchive newArchive)
        {
            // Must have active session and context as well as be in offline mode for hot-loading to work.
            if (PlayerContext == null)
                return;

            // Must be in offline mode for the hot-loading to work
            if (IEnvironmentConfigProvider.Get().ConnectionEndpointConfig.IsOfflineMode != true)
                return;

            // \todo [petri] Add global enable flag for the feature? Can the delegate existence of the delegate be that flag?

            try
            {
                // Load and parse GameConfig, to see if it parses correctly
                ISharedGameConfig newGameConfig = GameConfigUtil.ImportSharedConfig(newArchive);

                // Flush any old actions with the old configs (and old checksums)
                PlayerContext.FlushActions();

                // Set new GameConfig as active (now that it's been validated)
                PlayerContext.Journal.UpdateSharedGameConfig(newGameConfig);

                // Inform the game about the updated SharedGameConfig
                Options.GameConfigDelegate?.OnSharedGameConfigUpdated(newGameConfig, newArchive.Version);
            }
            catch (Exception ex)
            {
                DebugLog.Warning("Failed to update SharedGameConfig: {Exception}", ex);
            }
        }

        void HandleConnectionError(ConnectionState connectionState, ConnectionStatistics connectionStatistics)
        {
            // \todo Handle IHasNetworkDiagnosticReport? #helloworld

            ConnectionStatistics.CurrentConnectionStats connStats = connectionStatistics.CurrentConnection;

            if (connectionState is TransientError.Closed)
            {
                MetaplayClientErrorInfo errorInfo
                    = connStats.HasCompletedSessionInit     ? MetaplayClientConnectionErrors.Closed_HasCompletedSessionInit
                    : connStats.HasCompletedHandshake       ? MetaplayClientConnectionErrors.Closed_HasCompletedHandshake
                    : connStats.NetworkProbeStatus == true  ? MetaplayClientConnectionErrors.Closed_HasNotCompletedHandshake
                    : connStats.NetworkProbeStatus == false ? MetaplayClientConnectionErrors.Closed_ProbeFailed
                    :                                         MetaplayClientConnectionErrors.Closed_ProbeMissing;

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TransientError.Timeout timeout)
            {
                MetaplayClientErrorInfo errorInfo;
                if (timeout.Source == TransientError.Timeout.TimeoutSource.Connect)
                {
                    errorInfo
                        = connStats.HasCompletedSessionInit     ? MetaplayClientConnectionErrors.TimeoutConnect_HasCompletedSessionInit
                        : connStats.HasCompletedHandshake       ? MetaplayClientConnectionErrors.TimeoutConnect_HasCompletedHandshake
                        : connStats.NetworkProbeStatus == true  ? MetaplayClientConnectionErrors.TimeoutConnect_HasNotCompletedHandshake
                        : connStats.NetworkProbeStatus == false ? MetaplayClientConnectionErrors.TimeoutConnect_ProbeFailed
                        :                                         MetaplayClientConnectionErrors.TimeoutConnect_ProbeMissing;
                }
                else if (timeout.Source == TransientError.Timeout.TimeoutSource.ResourceFetch)
                {
                    errorInfo
                        = connStats.NetworkProbeStatus == true  ? MetaplayClientConnectionErrors.TimeoutResourceFetch
                        : connStats.NetworkProbeStatus == false ? MetaplayClientConnectionErrors.TimeoutResourceFetch_ProbeFailed
                        :                                         MetaplayClientConnectionErrors.TimeoutResourceFetch_ProbeMissing;
                }
                else if (timeout.Source == TransientError.Timeout.TimeoutSource.Stream)
                {
                    errorInfo
                        = connStats.HasCompletedSessionInit     ? MetaplayClientConnectionErrors.TimeoutStream_HasCompletedSessionInit
                        : connStats.HasCompletedHandshake       ? MetaplayClientConnectionErrors.TimeoutStream_HasCompletedHandshake
                        :                                         MetaplayClientConnectionErrors.TimeoutStream_HasNotCompletedHandshake;
                }
                else
                    errorInfo = MetaplayClientConnectionErrors.TimeoutUnhandled;

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TransientError.ClusterNotReady clusterNotReady)
            {
                MetaplayClientErrorInfo errorInfo
                    = clusterNotReady.Reason == TransientError.ClusterNotReady.ClusterStatus.ClusterStarting     ? MetaplayClientConnectionErrors.ClusterStarting
                    : clusterNotReady.Reason == TransientError.ClusterNotReady.ClusterStatus.ClusterShuttingDown ? MetaplayClientConnectionErrors.ClusterShuttingDown
                    :                                                                                              MetaplayClientConnectionErrors.ClusterNotReadyUnhandled;

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TransientError.ConfigFetchFailed configFetchFailed)
            {
                MetaplayClientErrorInfo errorInfo;
                if (configFetchFailed.Source == TransientError.ConfigFetchFailed.FailureSource.ResourceFetch)
                {
                    errorInfo
                        = connStats.NetworkProbeStatus == true  ? MetaplayClientConnectionErrors.ResourceFetchFailed
                        : connStats.NetworkProbeStatus == false ? MetaplayClientConnectionErrors.ResourceFetchFailed_ProbeFailed
                        :                                         MetaplayClientConnectionErrors.ResourceFetchFailed_ProbeMissing;
                }
                else if (configFetchFailed.Source == TransientError.ConfigFetchFailed.FailureSource.Activation)
                    errorInfo = MetaplayClientConnectionErrors.ResourceActivationFailed;
                else
                    errorInfo = MetaplayClientConnectionErrors.ResourceLoadFailedUnhandled;

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TransientError.FailedToResumeSession)
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.FailedToResumeSession,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TransientError.SessionForceTerminated sessionForceTerminated)
            {
                SessionForceTerminateReason terminatedReason = sessionForceTerminated.Reason;

                MetaSerializableType unhandledTypeSpec = null;

                MetaplayClientErrorInfo errorInfo
                    = terminatedReason is SessionForceTerminateReason.ReceivedAnotherConnection             ? MetaplayClientConnectionErrors.SessionTerminatedReceivedAnotherConnection
                    : terminatedReason is SessionForceTerminateReason.KickedByAdminAction                   ? MetaplayClientConnectionErrors.SessionTerminatedKickedByAdminAction
                    : terminatedReason is SessionForceTerminateReason.InternalServerError                   ? MetaplayClientConnectionErrors.SessionTerminatedInternalServerError
                    : terminatedReason is SessionForceTerminateReason.Unknown                               ? MetaplayClientConnectionErrors.SessionTerminatedUnknown
                    : terminatedReason is SessionForceTerminateReason.ClientTimeTooFarBehind                ? MetaplayClientConnectionErrors.SessionTerminatedClientTimeTooFarBehind
                    : terminatedReason is SessionForceTerminateReason.ClientTimeTooFarAhead                 ? MetaplayClientConnectionErrors.SessionTerminatedClientTimeTooFarAhead
                    : terminatedReason is SessionForceTerminateReason.SessionTooLong                        ? MetaplayClientConnectionErrors.SessionTerminatedSessionTooLong
                    : terminatedReason is SessionForceTerminateReason.PlayerBanned                          ? MetaplayClientConnectionErrors.SessionTerminatedPlayerBanned
                    : terminatedReason is SessionForceTerminateReason.MaintenanceModeStarted                ? MetaplayClientConnectionErrors.SessionTerminatedMaintenanceModeStarted
                    : terminatedReason is SessionForceTerminateReason.PauseDeadlineExceeded                 ? MetaplayClientConnectionErrors.SessionTerminatedPauseDeadlineExceeded
                    : terminatedReason != null
                      && MetaSerializerTypeRegistry.TryGetTypeSpec(terminatedReason.GetType(),
                                                                   out unhandledTypeSpec)                   ? MetaplayClientConnectionErrors.SessionTerminatedUnhandled_Base
                    :                                                                                         MetaplayClientConnectionErrors.SessionTerminatedUnhandledInvalid;

                // If reason was unhandled, encode the reason's typecode into the error code.
                if (errorInfo.TechnicalCode == MetaplayClientConnectionErrors.SessionTerminatedUnhandled_Base.TechnicalCode && unhandledTypeSpec != null)
                {
                    int baseCode    = MetaplayClientConnectionErrors.SessionTerminatedUnhandled_Base.TechnicalCode;
                    int maxCode     = MetaplayClientConnectionErrors.SessionTerminatedUnhandled_MaxCode;

                    errorInfo = new MetaplayClientErrorInfo(
                        technicalCode: Math.Min(maxCode, baseCode + unhandledTypeSpec.TypeCode),
                        technicalString: errorInfo.TechnicalString + "_" + unhandledTypeSpec.TypeCode.ToString(CultureInfo.InvariantCulture),
                        reason: errorInfo.Reason,
                        docString: errorInfo.DocString);
                }

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    extraTechnicalInfo: unhandledTypeSpec != null ? Invariant($"typecode_{unhandledTypeSpec.TypeCode}") : "",
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TransientError.ProtocolError protocolError)
            {
                MetaplayClientErrorInfo errorInfo
                    = protocolError.Kind == TransientError.ProtocolError.ErrorKind.UnexpectedLoginMessage ? MetaplayClientConnectionErrors.ProtocolErrorUnexpectedLoginMessage
                    : protocolError.Kind == TransientError.ProtocolError.ErrorKind.MissingServerHello     ? MetaplayClientConnectionErrors.ProtocolErrorMissingServerHello
                    : protocolError.Kind == TransientError.ProtocolError.ErrorKind.SessionStartFailed     ? MetaplayClientConnectionErrors.ProtocolErrorSessionStartFailed
                    : protocolError.Kind == TransientError.ProtocolError.ErrorKind.SessionProtocolError   ? MetaplayClientConnectionErrors.ProtocolErrorSessionProtocolError
                    :                                                                                       MetaplayClientConnectionErrors.ProtocolErrorUnhandled;

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TransientError.SessionLostInBackground
                     && !(connectionState is TransientError.AppTooLongSuspended)) // Excluding the subclass AppTooLongSuspended, it's handled in its own branch.
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.SessionLostInBackground,
                    autoReconnectRecommended: true,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TransientError.InternalWatchdogDeadlineExceeded internalWatchdogDeadlineExceeded)
            {
                ConnectionInternalWatchdogType watchdogType = internalWatchdogDeadlineExceeded.WatchdogType;

                MetaplayClientErrorInfo errorInfo;
                if (watchdogType == ConnectionInternalWatchdogType.Transport)
                {
                    errorInfo
                        = connStats.HasCompletedSessionInit     ? MetaplayClientConnectionErrors.TransportWatchdogExceeded_HasCompletedSessionInit
                        : connStats.HasCompletedHandshake       ? MetaplayClientConnectionErrors.TransportWatchdogExceeded_HasCompletedHandshake
                        : connStats.NetworkProbeStatus == true  ? MetaplayClientConnectionErrors.TransportWatchdogExceeded_HasNotCompletedHandshake
                        : connStats.NetworkProbeStatus == false ? MetaplayClientConnectionErrors.TransportWatchdogExceeded_ProbeFailed
                        :                                         MetaplayClientConnectionErrors.TransportWatchdogExceeded_ProbeMissing;
                }
                else if (watchdogType == ConnectionInternalWatchdogType.Resetup)
                    errorInfo = MetaplayClientConnectionErrors.ResetupWatchdogExceeded;
                else
                    errorInfo = MetaplayClientConnectionErrors.ConnectionWatchdogExceededUnhandled;

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TransientError.AppTooLongSuspended)
            {
                // \note Treated basically the same as SessionLostInBackground.
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.AppTooLongSuspended,
                    autoReconnectRecommended: true,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TransientError.TlsError tlsError)
            {
                MetaplayClientErrorInfo errorInfo
                    = tlsError.Error == TransientError.TlsError.ErrorCode.Unknown                    ? MetaplayClientConnectionErrors.TlsErrorUnknown
                    : tlsError.Error == TransientError.TlsError.ErrorCode.NotAuthenticated           ? MetaplayClientConnectionErrors.TlsErrorNotAuthenticated
                    : tlsError.Error == TransientError.TlsError.ErrorCode.FailureWhileAuthenticating ? MetaplayClientConnectionErrors.TlsErrorFailureWhileAuthenticating
                    : tlsError.Error == TransientError.TlsError.ErrorCode.NotEncrypted               ? MetaplayClientConnectionErrors.TlsErrorNotEncrypted
                    :                                                                                  MetaplayClientConnectionErrors.TlsErrorUnhandled;

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TerminalError.WireProtocolVersionMismatch wireProtocolVersionMismatch)
            {
                int client = wireProtocolVersionMismatch.ClientProtocolVersion;
                int server = wireProtocolVersionMismatch.ServerProtocolVersion;

                ConnectionLostReason reason
                    = client < server ? ConnectionLostReason.ClientVersionTooOld
                    :                   ConnectionLostReason.ServerMaintenance;

                MetaplayClientErrorInfo errorInfo
                    = client < server ? MetaplayClientConnectionErrors.WireProtocolVersionClientTooOld
                    : client > server ? MetaplayClientConnectionErrors.WireProtocolVersionClientTooNew
                    :                   MetaplayClientConnectionErrors.WireProtocolVersionInvalidMismatch;

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    extraTechnicalInfo: Invariant($"client_{client}_server_{server}"),
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TerminalError.InvalidGameMagic invalidGameMagic)
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.InvalidGameMagic,
                    extraTechnicalInfo: Invariant($"client_{MetaplayCore.Options.GameMagic}_server_{invalidGameMagic.Magic:x8}"),
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TerminalError.InMaintenance)
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.InMaintenance,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TerminalError.LogicVersionMismatch logicVersionMismatch)
            {
                MetaVersionRange client = logicVersionMismatch.ClientSupportedLogicVersions;
                MetaVersionRange server = logicVersionMismatch.ServerAcceptedVersions;

                ConnectionLostReason reason
                    = client.MaxVersion < server.MinVersion ? ConnectionLostReason.ClientVersionTooOld
                    : client.MinVersion > server.MaxVersion ? ConnectionLostReason.ServerMaintenance
                    :                                         ConnectionLostReason.ServerMaintenance; // \todo When can this happen?

                MetaplayClientErrorInfo errorInfo
                    = client.MaxVersion < server.MinVersion ? MetaplayClientConnectionErrors.LogicVersionClientTooOld
                    : client.MinVersion > server.MaxVersion ? MetaplayClientConnectionErrors.LogicVersionClientTooNew
                    :                                         MetaplayClientConnectionErrors.LogicVersionInvalidMismatch;

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    extraTechnicalInfo: Invariant($"client_{client.MinVersion}_to_{client.MaxVersion}_server_{server.MinVersion}_to_{server.MaxVersion}"),
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TerminalError.LoginProtocolVersionMismatch protocolVersionMismatch)
            {
                ConnectionLostReason reason
                    = protocolVersionMismatch.ClientVersion < protocolVersionMismatch.ServerVersion ? ConnectionLostReason.ClientVersionTooOld
                    : protocolVersionMismatch.ClientVersion > protocolVersionMismatch.ServerVersion ? ConnectionLostReason.ServerMaintenance
                    :                                                                                 ConnectionLostReason.ServerMaintenance; // \todo When can this happen?

                MetaplayClientErrorInfo errorInfo
                    = protocolVersionMismatch.ClientVersion < protocolVersionMismatch.ServerVersion ? MetaplayClientConnectionErrors.LoginProtocolVersionClientTooOld
                    : protocolVersionMismatch.ClientVersion > protocolVersionMismatch.ServerVersion ? MetaplayClientConnectionErrors.LoginProtocolVersionClientTooNew
                    :                                                                                 MetaplayClientConnectionErrors.LoginProtocolVersionInvalidMismatch;

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    extraTechnicalInfo: Invariant($"client_{protocolVersionMismatch.ClientVersion}_server_{protocolVersionMismatch.ServerVersion}"),
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TerminalError.CommitIdMismatch)
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.CommitIdMismatch,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TerminalError.WireFormatError)
            {
                MetaplayClientErrorInfo errorInfo
                    = connStats.HasCompletedSessionInit ? MetaplayClientConnectionErrors.WireFormatError_HasCompletedSessionInit
                    : connStats.HasCompletedHandshake   ? MetaplayClientConnectionErrors.WireFormatError_HasCompletedHandshake
                    :                                     MetaplayClientConnectionErrors.WireFormatError_HasNotCompletedHandshake;

                OnConnectionLost(new ConnectionLostEvent(
                    errorInfo,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TerminalError.NoNetworkConnectivity)
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.NoNetworkConnectivity,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TerminalError.PlayerIsBanned)
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.PlayerIsBanned,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TerminalError.ServerPlayerDeserializationFailure)
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.ServerPlayerDeserializationFailure,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is SocialAuthenticateForceReconnectConnectionError)
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.SocialAuthenticateForceReconnect,
                    autoReconnectRecommended: true,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is PlayerChecksumMismatchConnectionError)
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.PlayerChecksumMismatch,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is ClientTerminatedConnectionConnectionError)
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.ClientTerminatedConnection,
                    autoReconnectRecommended: true,
                    technicalError: connectionState,
                    connStats));
            }
            else if (connectionState is TerminalError.ClientSideConnectionError csError)
            {
                if (csError.Exception is CannotWriteCredentialsOnDiskError)
                {
                    OnConnectionLost(new ConnectionLostEvent(
                        MetaplayClientConnectionErrors.DeviceLocalStorageWriteError,
                        autoReconnectRecommended: false,
                        technicalError: connectionState,
                        connStats));
                }
                else
                {
                    OnConnectionLost(new ConnectionLostEvent(
                        MetaplayClientConnectionErrors.ClientSideConnectionError,
                        autoReconnectRecommended: false,
                        technicalError: connectionState,
                        connStats));
                }
            }
            else if (connectionState is TerminalError.Unknown)
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.ExplicitUnknown,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
            else
            {
                OnConnectionLost(new ConnectionLostEvent(
                    MetaplayClientConnectionErrors.Unhandled,
                    autoReconnectRecommended: false,
                    technicalError: connectionState,
                    connStats));
            }
        }

        void OnConnectionLost(ConnectionLostEvent connectionLost)
        {
            bool hadSession = PlayerContext != null;

            if (hadSession)
                _mainLog.Info("Session lost (reason={Reason}, errorCode={TechnicalErrorCode}, errorString={TechnicalErrorString}, extraInfo={ExtraTechnicalInfo}, errorType={TechnicalErrorType})", connectionLost.Reason, connectionLost.TechnicalErrorCode, connectionLost.TechnicalErrorString, connectionLost.ExtraTechnicalInfo, connectionLost.TechnicalError.GetType());
            else
                _mainLog.Info("Connection lost before session started (reason={Reason}, errorCode={TechnicalErrorCode}, errorString={TechnicalErrorString}, extraInfo={ExtraTechnicalInfo}, errorType={TechnicalErrorType})", connectionLost.Reason, connectionLost.TechnicalErrorCode, connectionLost.TechnicalErrorString, connectionLost.ExtraTechnicalInfo, connectionLost.TechnicalError.GetType());

            // Analytics
            TryCallAnalyticsDelegate(connectionLost.AnalyticsEvent, model: null);

            // Inform lifecycle delegate
            if (hadSession)
                Options.LifecycleDelegate?.OnSessionLost(connectionLost);
            else
                Options.LifecycleDelegate?.OnFailedToStartSession(connectionLost);
        }
    }

    public abstract class MetaplayClientBase<TPlayerModel> where TPlayerModel : IPlayerModelBase
    {
        public static MetaplayClientState State { get; private set; }
        public static MetaplayConnection Connection => MetaplaySDK.Connection;
        public static NetworkDiagnosticsManager NetworkDiagnosticsManager => MetaplaySDK.NetworkDiagnosticsManager;
        public static string ClientBuildVersion => MetaplaySDK.BuildVersion.Version;
        public static string BackendBuildVersion => Connection.LatestServerVersion ?? "unavailable";
        public static LocalizationLanguage ActiveLanguage => MetaplaySDK.ActiveLanguage;
        public static IPlayerClientContext PlayerContext => State.PlayerContext;
        public static TPlayerModel PlayerModel => State != null ? State.GetPlayerModel<TPlayerModel>() : default;

        public static string ConfigVersion
        {
            get
            {
                OrderedDictionary<ClientSlot, ContentHash> baselineVersions = Connection?.SessionStartResources?.GameConfigBaselineVersions;
                if (baselineVersions == null)
                    return "unavailable";
                return baselineVersions.TryGetValue(ClientSlotCore.Player, out ContentHash version) ? version.ToString() : "unavailable";
            }
        }

        public static MetaplayClientStore ClientStore => State.ClientStore;

        #if !METAPLAY_DISABLE_GUILDS
        public static GuildClient GuildClient => State.ClientStore.TryGetClient<GuildClient>(ClientSlotCore.Guild);
        #endif

        public static NftClient NftClient => State.NftClient;

        public static MessageDispatcher MessageDispatcher => MetaplaySDK.MessageDispatcher;
        public static SocialAuthManager SocialAuthManager => MetaplaySDK.SocialAuthentication;

        /// <summary>
        /// The IAP manager singleton.
        /// Available if <c>true</c> was given in <see cref="MetaplayIAPOptions.EnableIAPManager"/>
        /// when initializing <see cref="MetaplayClient"/>; null otherwise.
        /// </summary>
        public static IAPManager IAPManager => State.IAPManager;

        /// <summary>
        /// Helper for tracking IAP purchase flow states managed by <see cref="IAPManager"/>.
        /// Available if <c>true</c> was given in <see cref="MetaplayIAPOptions.EnableIAPManager"/>
        /// when initializing <see cref="MetaplayClient"/>; null otherwise.
        /// </summary>
        public static IAPFlowTracker IAPFlowTracker => State.IAPFlowTracker;

        /// <summary>
        /// Quality of the current connection.
        /// </summary>
        public static ConnectionHealth ConnectionHealth
        {
            get
            {
                if (Connection?.State is Connected connected)
                {
                    if (connected.IsHealthy)
                        return ConnectionHealth.Healthy;
                    else
                        return ConnectionHealth.Unhealthy;
                }
                else
                    return ConnectionHealth.NotConnected;
            }
        }

        public static void Initialize(MetaplayClientOptions options)
        {
            State = new MetaplayClientState(options, Deinitialize);
        }

        public static void Update()
        {
            State.Update();
        }

        public static void Connect()
        {
            State.Connect();
        }

        public static void Deinitialize()
        {
            State.Dispose();
            State = null;
        }

        /// <inheritdoc cref="MetaplaySDK.ChangeServerEndpoint(ServerEndpoint)"/>
        public static void ChangeServerEndpoint(ServerEndpoint serverEndpoint)
        {
            MetaplaySDK.ChangeServerEndpoint(serverEndpoint);
        }
    }

#region Pseudo connection errors, invoked from within MetaplayClient itself

    /// <summary>
    /// Connection terminated. Protocol re-authentication is needed after social authentication operations.
    /// </summary>
    public class SocialAuthenticateForceReconnectConnectionError : TransientError
    {
    }

    /// <summary>
    /// Connection error due a checksum mismatch.
    /// </summary>
    public class PlayerChecksumMismatchConnectionError : TransientError
    {
        public int              TickNumber;
        public PlayerActionBase Action;
        public string           ModelDiff;
        public List<string>     VagueDifferencePathsMaybe;

        public override string TryGetReasonOverrideForIncidentReport()
        {
            IndentedStringBuilder sb = new IndentedStringBuilder(outputDebugCode: false);

            sb.AppendLine(nameof(PlayerChecksumMismatchConnectionError));

            if (Action != null)
                sb.AppendLine($"ActionType: {Action.GetType().Name}");
            else
                sb.AppendLine($"Tick: {TickNumber}");

            // \note VagueDifferencePathsMaybe comes from SerializedObjectComparer.Compare, and
            //       can be missing in some special cases. For checksum mismatches, this should not
            //       normally happen, but tolerating anyway for safety. In that case, we fall back
            //       to the ModelDiff which is the human-readable report produced by SerializedObjectComparer.Compare.
            if (VagueDifferencePathsMaybe != null)
            {
                sb.AppendLine("DiffPaths:");
                sb.Indent();
                foreach ((string path, int index) in VagueDifferencePathsMaybe.ZipWithIndex())
                    sb.AppendLine($"{path}");
                sb.Unindent();
            }
            else if (ModelDiff != null)
            {
                sb.AppendLine($"DiffReport: {ModelDiff}");
            }

            return sb.ToString();
        }

        public PlayerChecksumMismatchConnectionError(int tickNumber, PlayerActionBase action, string modelDiff, List<string> vagueDifferencePathsMaybe)
        {
            TickNumber = tickNumber;
            Action = action;
            ModelDiff = modelDiff;
            VagueDifferencePathsMaybe = vagueDifferencePathsMaybe;
        }
    }

#endregion
}
