// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Client.Messages;
using Metaplay.Cloud;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Network;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.InGameMail;
using Metaplay.Core.Debugging;
using static Metaplay.Core.Debugging.PlayerIncidentReport;
using static System.FormattableString;
using static Metaplay.BotClient.BotCoordinator;

#if !METAPLAY_DISABLE_GUILDS
using Metaplay.Core.Guild;
using Metaplay.Core.Guild.Messages.Core;
using Metaplay.Core.GuildDiscovery;
#endif

namespace Metaplay.BotClient
{
    public abstract class BotClientConfigBase : EphemeralEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindBotCore.BotClient;
        public override Type                EntityShardType         => typeof(BotCoordinator);
        public override IShardingStrategy   ShardingStrategy        => new StaticShardingStrategy();
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(60);
    }

    public abstract class BotClientBase : EphemeralEntityActor, IPlayerModelClientListenerCore
    {
        public class SessionEndTimeReached { public static readonly SessionEndTimeReached Instance = new SessionEndTimeReached(); }
        public class InitDeadlineReached { public static readonly InitDeadlineReached Instance = new InitDeadlineReached(); }

        public static readonly double[] LoginDurationBuckets        = new double[] { 0.1, 0.2, 0.5, 1.0, 2.0, 3.0, 4.0, 5.0, 10.0, 20.0, 60.0 };
        static Prometheus.Counter       c_connectAttempts           = Prometheus.Metrics.CreateCounter("botclient_connect_attempts_total", "Cumulative number of connection attempts to the server");
        static Prometheus.Counter       c_connectionsEstablished    = Prometheus.Metrics.CreateCounter("botclient_connections_established_total", "Cumulative number of connections established to server");
        static Prometheus.Histogram     c_connectDuration           = Prometheus.Metrics.CreateHistogram("botclient_connect_duration", "Duration from beginning of connection to protocol handshake, as observed by botclient", new Prometheus.HistogramConfiguration { Buckets = LoginDurationBuckets });
        static Prometheus.Histogram     c_loginDuration             = Prometheus.Metrics.CreateHistogram("botclient_login_duration", "Duration from beginning of connection to login success, as observed by botclient", new Prometheus.HistogramConfiguration { Buckets = LoginDurationBuckets });
        static Prometheus.Counter       c_connectionsFailed         = Prometheus.Metrics.CreateCounter("botclient_connection_failed_total", "Cumulative number of botclient connections to server lost or failed to connect", "reason", "state");
        static Prometheus.Counter       c_sessionTimeouts           = Prometheus.Metrics.CreateCounter("botclient_session_timeout_total", "Cumulative number of botclient sessions ended due to a timeout");

        /// <summary>
        /// The time interval for Ticks(). This corresponds roughly to frame time on client i.e (1s / interval) FPS.
        /// As on the client, this sets the poll rate for incoming messages.
        /// </summary>
        protected virtual TimeSpan UpdateTickInterval => TimeSpan.FromMilliseconds(100);
        protected override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        enum LifecycleState
        {
            NotStarted,
            Ticking,
            Terminated,
        }

        #if !METAPLAY_DISABLE_GUILDS

        public class GuildInviteSample
        {
            public readonly GuildInviteCode InviteCode;

            public GuildInviteSample(GuildInviteCode inviteCode)
            {
                InviteCode = inviteCode;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is GuildInviteSample sample))
                    return false;
                if (InviteCode != sample.InviteCode)
                    return false;
                return true;
            }

            public override int GetHashCode() => HashCode.Combine(InviteCode);
        }
        #endif

        // \note Caching the options for the lifecycle of each bot
        protected BotOptions            _botOpts    = RuntimeOptionsRegistry.Instance.GetCurrent<BotOptions>();

        LifecycleState                  _lifecycleState;
        SessionNonceService             _sessionNonceService;
        BotClientDeviceGuidService      _sessionGuidService;
        BotClientCredentialsService     _sessionCredentialsService;
        ServerConnection                _serverConnection;
        BotMessageDispatcher            _messageDispatcher;
        FaultInjectingMessageTransport  _latestTransport;
        MetaTime?                       _appOnBackgroundBackgroundEndsAt;       // if null, not on simulated background. Otherwise the point in time on which we resume
        bool                            _appOnBackgroundDropConnectionOnResume; // if set, the connection will be observed lost the moment we return from simulated background
        bool                            _appOnBackgroundAcceptErrorOnResume;    // if set, connection error is an accepted result when waking from simulated pause
        bool                            _droppingConnectionOnWrite;             // if set, the connection is pending death and is killed when data is written to it
        MetaTime                        _dropConnectionDeadline;                // if connection is pending death, the final deadline unto which we wait for the triggering write

        List<MetaMessage>               _messageBuffer  = new List<MetaMessage>(); // Temporary message buffer (to avoid creating new lists)

        protected EntityId              _actualPlayerId;        // Actual PlayerId, as specified by the server (can happen if account is transferred)
        protected LogChannel            _logChannel;
        protected int                   _logicVersion;          // LogicVersion to use for session

        protected abstract IPlayerClientContext PlayerContext { get; }

        #if !METAPLAY_DISABLE_GUILDS
        protected GuildClient GuildClient => ClientStore.TryGetClient<GuildClient>(ClientSlotCore.Guild);
        protected IGuildClientContext GuildContext => GuildClient?.GuildContext;

        /// <summary>
        /// A sample of all guild invites.
        /// </summary>
        public static readonly ConcurrentBag<GuildInviteSample> GuildInviteSamples = new ConcurrentBag<GuildInviteSample>();
        #endif

        protected LocalizationLanguage ActiveLanguage { get; private set; }
        protected IMessageDispatcher MessageDispatcher => _messageDispatcher;

        protected ClientSessionNegotiationResources                     _sessionNegotiationResources = new ClientSessionNegotiationResources();
        protected ClientSessionStartResources                           _sessionStartResources;

        /// <summary>
        /// The set of active experiments the current player is a member of. An active experiment is an experiment that is
        /// neither disabled or in an invalid state, i.e. the experiment's config changes have been applied.
        /// </summary>
        protected OrderedDictionary<PlayerExperimentId, ExperimentMembershipStatus> ActiveExperiments { get; private set; }

        protected virtual IMetaplaySubClient[] AdditionalSubClients => Array.Empty<IMetaplaySubClient>();
        protected virtual bool EnablePlayerConsistencyChecks => false;
        protected MetaplayClientStore ClientStore { get; }

        public BotClientBase(EntityId entityId) : base(entityId)
        {
            // Log channel for logic
            _logChannel = new LogChannel("bot", _log, MetaLogger.MetaLogLevelSwitch);

            _messageDispatcher = new BotMessageDispatcher(_logChannel);

            ActiveLanguage = new LocalizationLanguage(LanguageId.FromString("none"), ContentHash.None, new OrderedDictionary<TranslationId, string>());
            _sessionNegotiationResources.ActiveLanguage = ActiveLanguage;

            ClientStore = new MetaplayClientStore(() => PlayerContext);

#if !METAPLAY_DISABLE_GUILDS
            // \todo: Use IntegrationRegistry for overriding guild client
            bool shouldAddDefaultGuildClient = AdditionalSubClients?.All(client => client.ClientSlot != ClientSlotCore.Guild) ?? true;
            if (shouldAddDefaultGuildClient)
                ClientStore.AddClient(new GuildClient());
#endif

            foreach (IMetaplaySubClient client in AdditionalSubClients)
                ClientStore.AddClient(client);
            IMetaplaySubClientServices clientServices = new BotSubClientServices(_messageDispatcher, ClientStore, EnablePlayerConsistencyChecks, _log);
            ClientStore.Initialize(clientServices);

#if !METAPLAY_DISABLE_GUILDS
            GuildClient.PhaseChanged += HarvestGuildInvitationSamples;
#endif
        }

        protected override void PreStart()
        {
            base.PreStart();

            Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(5), _self, InitDeadlineReached.Instance, _self);
        }

        protected override void PostStop()
        {
            // Dispose server connection
            if (_serverConnection != null)
            {
                _serverConnection.Dispose();
                _serverConnection = null;
            }

            ClientStore.Dispose();

            base.PostStop();
        }

        protected bool SendToServer(MetaMessage message)
        {
            return _serverConnection.EnqueueSendMessage(message);
        }

        protected override async Task Initialize()
        {
            _lifecycleState = LifecycleState.Ticking;

            // Tick timer
            StartPeriodicTimer(UpdateTickInterval, UpdateTickInterval, ActorTick.Instance);

            // Schedule session stop in 0.5..1.5 * expectedSessionDuration
            TimeSpan sessionDuration = _botOpts.ExpectedSessionDuration * (0.5 + new Random().NextDouble());
            _log.Verbose("Attempting to run game session for {SessionDuration}", sessionDuration);
            Context.System.Scheduler.ScheduleTellOnce(sessionDuration, _self, SessionEndTimeReached.Instance, _self);

            ServerEndpoint endpoint = new ServerEndpoint(
                _botOpts.ServerHost,
                _botOpts.ServerPort,
                _botOpts.EnableTls,
                _botOpts.CdnBaseUrl,
                _botOpts.ServerBackupGateways.Select(opt => opt.ToServerGateway()).ToList());

            // Establish connection to server

            _sessionNonceService = new SessionNonceService(appLaunchId: Guid.NewGuid());
            _sessionNonceService.NewSession();
            _sessionGuidService = new BotClientDeviceGuidService();

            // Use botId as playerId (or force all bots to use EntityId Player:000000000, if conflicts enabled)
            EntityId expectedPlayerId = EntityId.Create(EntityKindCore.Player, _botOpts.ForceConflictingPlayerId ? 0 : _entityId.Value);
            _sessionCredentialsService = new BotClientCredentialsService(expectedPlayerId);
            await _sessionCredentialsService.InitializeAsync();

            _serverConnection = new ServerConnection(
                _logChannel,
                new ServerConnection.Config()
                {
                    CommitIdCheckRule = ClientServerCommitIdCheckRule.Disabled,
                    DeviceModel = "bot",
                    LoginGamePayload = CreateLoginRequestGamePayload(),
                    SessionStartGamePayload = CreateSessionStartRequestGamePayload(),
                },
                endpoint:                       endpoint,
                nonceService:                   _sessionNonceService,
                guidService:                    _sessionGuidService,
                loginMethod:                    await _sessionCredentialsService.GetCurrentLoginMethodAsync(),
                createTransport:                CreateTransport,
                buildVersion:                   GetBuildVersion(),
                initialResourceProposal:        _sessionNegotiationResources.ToResourceProposal(),
                initialClientAppPauseStatus:    ClientAppPauseStatus.Running,
                getDebugDiagnostics:            (bool isSessionResumption) => null,
                earlyMessageFilterSync:         null,
                enableWatchdog:                 true,
                reportWatchdogViolation:        () => { },
                numFailedConnectionAttempts:    0);

            _messageDispatcher.SetConnection(_serverConnection);
            c_connectAttempts.Inc();
        }

        BuildVersion GetBuildVersion() => new BuildVersion(version: "0.0.1-bot", buildNumber: CloudCoreVersion.BuildNumber ?? "undefined", commitId: CloudCoreVersion.CommitId ?? "undefined");

        protected virtual Handshake.ILoginRequestGamePayload                CreateLoginRequestGamePayload()         => null;
        protected virtual ISessionStartRequestGamePayload                   CreateSessionStartRequestGamePayload()  => null;

        IMessageTransport CreateTransport(ServerGateway gateway)
        {
            TcpMessageTransport transport;
            if (gateway.EnableTls)
                transport = new TlsMessageTransport(_logChannel);
            else
                transport = new TcpMessageTransport(_logChannel);

            TcpMessageTransport.ConfigArgs transportConfig = transport.Config();

            transportConfig.GameMagic                   = MetaplayCore.Options.GameMagic;
            transportConfig.Version                     = GetBuildVersion().Version;
            transportConfig.BuildNumber                 = GetBuildVersion().BuildNumber;
            transportConfig.SupportedLogicVersions      = MetaplayCore.Options.SupportedLogicVersions;
            transportConfig.FullProtocolHash            = MetaSerializerTypeRegistry.FullProtocolHash;
            transportConfig.CommitId                    = GetBuildVersion().CommitId;
            transportConfig.ClientSessionConnectionNdx  = _sessionNonceService.GetSessionConnectionIndex();
            transportConfig.ClientSessionNonce          = _sessionNonceService.GetSessionNonce();
            transportConfig.AppLaunchId                 = _sessionNonceService.GetTransportAppLaunchId();
            transportConfig.LoginProtocolVersion        = MetaplayCore.LoginProtocolVersion;

            transportConfig.ServerHostIPv4              = MetaplayHostnameUtil.GetV4V6SpecificHost(gateway.ServerHost, isIPv4: true);
            // IPv6 disabled for now
            //transportConfig.ServerHostIPv6            = MetaplayHostnameUtil.GetV4V6SpecificHost(gateway.ServerHost, isIPv4: false);
            transportConfig.ServerPort                  = gateway.ServerPort;

            // Some random to avoid all queries expiring at the same time
            transportConfig.DnsCacheMaxTTL              = TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(2.0 * (new Random()).NextDouble());

            _latestTransport = new FaultInjectingMessageTransport(transport);

            return _latestTransport;
        }

        protected override void RegisterHandlers()
        {
            Receive<InitDeadlineReached>(ReceiveInitDeadlineReached);
            Receive<SessionEndTimeReached>(ReceiveSessionEndTimeReached);
            ReceiveAsync<ActorTick>(ReceiveActorTick);
            base.RegisterHandlers();
        }

        void ReceiveSessionEndTimeReached(SessionEndTimeReached request)
        {
            c_sessionTimeouts.Inc();
            _log.Debug("Session timeout, requesting shutdown");
            RequestShutdown();
        }

        void ReceiveInitDeadlineReached(InitDeadlineReached request)
        {
            if (_lifecycleState == LifecycleState.NotStarted)
            {
                _log.Warning("BotClient did not start a session after waking up. Shutting down.");
                RequestShutdown();
            }
        }

        async Task ReceiveActorTick(ActorTick tick)
        {
            if (_lifecycleState != LifecycleState.Ticking)
                return;

            MetaTime        now                     = MetaTime.Now;
            RandomPCG       rnd                     = RandomPCG.CreateNew();
            bool            acceptConnectionError   = false;
            bool            resumedFromPause        = false;

            // Simulate app put to background
            if (_appOnBackgroundBackgroundEndsAt.HasValue)
            {
                if (now < _appOnBackgroundBackgroundEndsAt.Value)
                {
                    // app is on background, we are paused.
                    return;
                }
                else
                {
                    // app just comes on focus after being on background.
                    // ~ OnApplicationPause(false)
                    _log.Debug("Resuming from pause");
                    _appOnBackgroundBackgroundEndsAt = null;

                    SendToServer(new ClientLifecycleHintUnpausing());
                    resumedFromPause = true;

                    // Simulate waking up the connection thread ...
                    if (_appOnBackgroundDropConnectionOnResume)
                    {
                        // ... and noticing the connection gone.
                        _latestTransport?.InjectError(new StreamMessageTransport.StreamClosedError());

                        // tolerate if can be tolerated
                        acceptConnectionError = _appOnBackgroundAcceptErrorOnResume;
                    }
                    else
                    {
                        // .. and everthing is fine
                        _latestTransport.Resume();
                    }
                }
            }
            else if (rnd.NextDouble() < _botOpts.PutOnBackgroundProbabilityPerTick)
            {
                // We model pausing an application on two axis: time and impact.
                // Time:
                // * is the pause long or short.
                // Impact of the pause:
                // * low impact, app remains active: low-level connection remains alive, and session remains alive.
                // * medium impact, app is suspended: low-level connection gets suspended, but wakes on resume
                // * high impact, app is suspended and connection is lost: low-level connection gets suspended, dies, and is observed dead on resume.
                //
                // We accept loss of an session only with long pauses when app is suspended and connection is lost, as that case cannot be discriminated from
                // app being closed.

                bool isLongPause                = false;//rnd.NextBool();
                int impactLevel                 = rnd.NextInt(3);
                MetaDuration pauseBaseDuration  = MetaDuration.FromTimeSpan(isLongPause ? (_botOpts.PutOnBackgroundLongPause) : (_botOpts.PutOnBackgroundShortPause));

                SendToServer(new ClientLifecycleHintPausing(pauseBaseDuration, "bot-pause"));

                if (impactLevel == 0)
                {
                    // keep 'em active
                    _appOnBackgroundDropConnectionOnResume = false;
                    _appOnBackgroundAcceptErrorOnResume = false;
                }
                else if (impactLevel == 1)
                {
                    // suspend
                    _latestTransport.Halt();
                    _appOnBackgroundDropConnectionOnResume = false;
                    _appOnBackgroundAcceptErrorOnResume = false;
                }
                else
                {
                    // drop connection silently.
                    _latestTransport.Halt();
                    _latestTransport.Dispose();

                    _appOnBackgroundDropConnectionOnResume = true;
                    _appOnBackgroundAcceptErrorOnResume = isLongPause; // only if also long pause
                }

                // was put on background
                // ~ OnApplicationPause(true)
                _log.Debug("Starting simulated application pause. ImpactLevel={ImpactLevel}. IsLongPause={IsLongPause}", impactLevel, isLongPause);
                _appOnBackgroundBackgroundEndsAt = now + pauseBaseDuration;
                return;
            }

            // simulate random connection losses
            else if (_droppingConnectionOnWrite && (_latestTransport.NumHaltedSendMessages > 0 || now > _dropConnectionDeadline))
            {
                _log.Debug("Write pending, triggering connection drop now.");
                _droppingConnectionOnWrite = false;
                _latestTransport?.InjectError(new StreamMessageTransport.StreamClosedError());
            }
            else if (!_droppingConnectionOnWrite && rnd.NextDouble() < _botOpts.DropConnectionProbabilityPerTick)
            {
                // Occasionally, close message transport in order to exercise session resumption code. We have a session, so
                // this should always be tolerated.
                // In realistic cases, connection drop is only observed when we try to use the transport. So let's halt here and
                // drop the connection when we notice any sent messages in the queue. Add a relative large deadline to simulate
                // rare pings.
                _log.Debug("Simulated conncection drop primed. Dropping on next write");

                _droppingConnectionOnWrite = true;
                _dropConnectionDeadline = now + MetaDuration.FromSeconds(5);
                _latestTransport.Halt();
            }

            // Handle any incoming messages, throw infos away
            MessageTransport.Error connectionError = _serverConnection.ReceiveMessages(_messageBuffer);
            foreach (MetaMessage msg in _messageBuffer)
            {
                if (await FilterNetworkMessageAsync(msg))
                {
                    await OnNetworkMessage(msg);
                }
                _messageDispatcher.OnReceiveMessage(msg);
            }
            _messageBuffer.Clear();

            // Check for connection lost
            if (connectionError != null)
            {
                if (acceptConnectionError)
                {
                    // connection loss was expected, no need do anything
                }
                else
                {
                    string reason = connectionError.GetType().Name;
                    c_connectionsFailed.WithLabels(reason, GetCurrentStateLabel()).Inc();
                    _log.Error("Connection to server failed ({Reason})...", reason);
                }

                ClientStore.OnDisconnected();
                ClientStore.OnSessionStop();

                _lifecycleState = LifecycleState.Terminated;
                RequestShutdown();
            }

            bool isOnPause = _appOnBackgroundBackgroundEndsAt.HasValue;
            if (!isOnPause && _lifecycleState == LifecycleState.Ticking)
            {
                if (_botOpts.EnableDummyIncidentReports && rnd.NextInt(100) < 5)
                {
                    _log.Debug("Sending dummy incident report");
                    SendToServer(CreateDummyIncidentReport());
                }

                // \todo: should we check last update was not too far away, like MetaplaySDK does on client.
                ClientStore.EarlyUpdate();

                PlayerContext?.Update(now);

                ClientStore.UpdateLogic(now);

                await OnUpdate();
            }

            if (resumedFromPause)
                SendToServer(new ClientLifecycleHintUnpaused());
        }

        /// <summary>
        /// Returns true if message should be delivered to <see cref="OnNetworkMessage(MetaMessage)"/>.
        /// </summary>
        protected virtual async ValueTask<bool> FilterNetworkMessageAsync(MetaMessage message)
        {
            switch (message)
            {
                case ConnectedToServer connected:
                    _log.Verbose("Connected to server");
                    c_connectionsEstablished.Inc();
                    c_connectDuration.Observe(_serverConnection.Statistics.DurationToConnected.TotalSeconds);
                    return false;

                case DisconnectedFromServer disconnected:
                    _log.Verbose("Received DisconnectedFromServer, shutting down..");
                    RequestShutdown();
                    return false;

                case ConnectionHandshakeFailure handshakeFailure:
                    _log.Error("Received: {Message}", PrettyPrint.Compact(handshakeFailure));
                    RequestShutdown();
                    return false;

                case SessionProtocol.SessionStartSuccess success:
                    Tell(_shard, BotStartedSession.Instance);
                    _log.Debug("Login success with new session");
                    c_loginDuration.Observe(_serverConnection.Statistics.DurationToLoginSuccess.TotalSeconds);
                    _logicVersion = success.LogicVersion;

                    // Store actual PlayerId (it should almost always be same as _expectedPlayerId, but transferring accounts can change it)
                    _actualPlayerId = success.PlayerId;
                    if (!_actualPlayerId.IsOfKind(EntityKindCore.Player))
                        throw new InvalidDataException($"Got invalid PlayerId '{_actualPlayerId}' for bot");
                    else if (_actualPlayerId != _sessionCredentialsService.ExpectedPlayerId)
                        _log.Warning("Actual server-provided {ActualPlayerId} differs from expected {ExpectedPlayerId}", _actualPlayerId, _sessionCredentialsService.ExpectedPlayerId);

                    // \todo [petri] duplicate in Authenticator.cs, refactor to common place
                    const ulong NumReservedBotIds = 1UL << 32; // reserve 4 billion lowest ids for bots to use
                    if (_actualPlayerId.Value >= NumReservedBotIds)
                        _log.Warning("Non-bot id {PlayerId} given to a bot", _actualPlayerId);

                    // Activate language
                    ActiveLanguage = _sessionNegotiationResources.ActiveLanguage;

                    // Activate game configs
                    Func<ConfigArchive, (GameConfigSpecializationPatches, GameConfigSpecializationKey)?, ISharedGameConfig> gameConfigImporter = (ConfigArchive archive, (GameConfigSpecializationPatches, GameConfigSpecializationKey)? specialization) =>
                    {
                        if (specialization is (GameConfigSpecializationPatches patches, GameConfigSpecializationKey specializationKey))
                        {
                            return BotGameConfigProvider.Instance.GetSpecializedGameConfig(archive, patches, specializationKey);
                        }
                        else
                        {
                            return BotGameConfigProvider.Instance.GetSpecializedGameConfig(archive, null, default);
                        }
                    };
                    _sessionStartResources = ClientSessionStartResources.SpecializeResources(success, _sessionNegotiationResources, gameConfigImporter);

                    ActiveExperiments = success.ActiveExperiments.ToOrderedDictionary(experiment => experiment.ExperimentId, experiment => ExperimentMembershipStatus.FromSessionInfo(experiment));

                    ISharedGameConfig playerGameConfig = _sessionStartResources.GameConfigs[ClientSlotCore.Player];
                    IPlayerModelBase playerModel = success.PlayerState.PlayerModel.Deserialize(playerGameConfig, _logicVersion);
                    if (success.PlayerId != playerModel.PlayerId)
                        throw new InvalidOperationException("Player id in state and protocol do not match");

                    // Initialize player state services
                    playerModel.LogicVersion        = _logicVersion;
                    playerModel.GameConfig          = playerGameConfig;
                    playerModel.Log                 = _logChannel;
                    playerModel.ClientListenerCore  = this;
                    HandleStartSession(success, playerModel, playerGameConfig);

                    ClientStore.OnSessionStart(success, _sessionStartResources);

                    // Set name for the bot
                    if (playerModel.Stats.TotalLogins == 1)
                        SendToServer(new PlayerChangeOwnNameRequest(GenerateRandomPlayerName(new Random())));
                    return true;

                case SessionProtocol.SessionStartFailure failed:
                {
                    switch (failed.Reason)
                    {
                        case SessionProtocol.SessionStartFailure.ReasonCode.InternalError:
                        {
                            _log.Error("Session start failed: {Reason}", failed.Reason);
                            break;
                        }

                        case SessionProtocol.SessionStartFailure.ReasonCode.DryRun:
                        {
                            _log.Debug("Session start dry run completed successfully");
                            break;
                        }
                    }
                    return false;
                }

                case SessionProtocol.SessionStartResourceCorrection resourceCorrection:
                {
                    _log.Debug("Session resources are out of date, applying correction and retrying");
                    await ApplyResourceCorrectionAsync(resourceCorrection.ResourceCorrection);
                    _serverConnection.RetrySessionStart(_sessionNegotiationResources.ToResourceProposal(), ClientAppPauseStatus.Running);
                    return false;
                }

                case SessionProtocol.SessionResumeSuccess success:
                    _log.Debug("Session resumed");
                    return false;

                case SessionProtocol.SessionResumeFailure failed:
                    _log.Warning("Session resume failed.");
                    return false;

                case Handshake.ClientHelloAccepted _:
                    return false;

                case Handshake.LoginSuccessResponse _:
                    return false;

                case SessionAcknowledgementMessage _:
                    // Explicitly do nothing so we don't print a warning
                    return false;

                case SessionForceTerminateMessage forceTerminate:
                    _log.Info("Session force terminated due to another client logging in with same credentials");
                    RequestShutdown();
                    return false;

                case PlayerEnqueueSynchronizedServerAction enqueueSynchronizedServerAction:
                    PlayerContext.ExecuteServerAction(enqueueSynchronizedServerAction);
                    return false;

                case EntityServerToClientEnvelope envelope:
                {
                    // Entity channel messages are handled via dedicated message dispatcher
                    return false;
                }

                case EntitySwitchedMessage switched:
                {
                    // Affected subclients handle this automatically
                    return false;
                }

                case MessageTransportInfoWrapperMessage infoWrapper:
                {
                    switch (infoWrapper.Info)
                    {
                        case ServerConnection.SessionConnectionErrorLostInfo connectionLost:
                        {
                            TimeSpan maxSessionResumptionDuration = TimeSpan.FromSeconds(5);
                            DateTime deadlineAt = connectionLost.Attempt.StartTime + maxSessionResumptionDuration;
                            if (ReconnectScheduler.TryGetDurationToSessionResumeAttempt(connectionLost.Attempt, deadlineAt, out TimeSpan durationTillReconnect))
                            {
                                ScheduleExecuteOnActorContext(durationTillReconnect, () =>
                                {
                                    _serverConnection.ResumeSessionAfterConnectionDrop();
                                });
                            }
                            else
                            {
                                _serverConnection.AbortSessionAfterConnectionDrop();
                            }
                            break;
                        }
                    }
                    // filter out to avoid spammy logging
                    return false;
                }

#if !METAPLAY_DISABLE_GUILDS
                case GuildTimelineUpdateMessage:
                case GuildCreateResponse:
                case GuildJoinResponse:
                case GuildDiscoveryResponse:
                case GuildTransactionResponse:
                case GuildSearchResponse:
                case GuildViewResponse:
                case GuildViewEnded:
                case GuildSwitchedMessage:
                case GuildCreateInvitationResponse:
                case GuildInspectInvitationResponse:
                    // Handled by GuildClient, so ignore here so we don't print a warning.
                    // \todo Ignore automatically if handled by a sub-client.
                    return false;
#endif

                case Handshake.OperationStillOngoing:
                    return false;

                case PlayerAckIncidentReportUpload:
                    return false;
            }

            return true;
        }

        protected abstract void HandleStartSession(SessionProtocol.SessionStartSuccess success, IPlayerModelBase playerModelBase, ISharedGameConfig gameConfig);

        #if !METAPLAY_DISABLE_GUILDS

        /// <summary>
        /// Handles response to GuildDiscoveryRequest by attempting to join a random guild.
        /// </summary>
        protected void DefaultHandleGuildDiscoveryResult(GuildDiscoveryResponse response)
        {
            if (response.GuildInfos.Count > 0 && GuildClient.Phase == GuildClientPhase.NoGuild)
            {
                int ndx = (new Random()).Next(response.GuildInfos.Count);
                GuildClient.BeginJoinGuild(response.GuildInfos[ndx].GuildId, onCompletion: null);
            }
        }

        /// <summary>
        /// Handles response to GuildDiscoveryRequestby attempting to join a random guild.
        /// </summary>
        protected void DefaultHandleGuildSearchResult(GuildSearchResponse response)
        {
            if (response.IsError)
            {
                _log.Warning("Guild search failed due to a server error.");
                return;
            }

            if (response.GuildInfos.Count > 0 && GuildClient.Phase == GuildClientPhase.NoGuild)
            {
                int ndx = (new Random()).Next(response.GuildInfos.Count);
                GuildClient.BeginJoinGuild(response.GuildInfos[ndx].GuildId, onCompletion: null);
            }
        }

        /// <summary>
        /// Handles response to GuildInspectInvitationRequest. Defaults to attempting to join a random guild.
        /// </summary>
        protected void DefaultHandleGuildInspectInvitationResult(GuildInviteInfo inviteInfo)
        {
            if (inviteInfo == null)
                return;

            if (GuildClient.Phase != GuildClientPhase.NoGuild)
                return;

            GuildDiscoveryInfoBase guildDiscoveryInfo = inviteInfo.GuildInfo;
            GuildClient.BeginJoinGuildWithInviteCode(guildDiscoveryInfo.GuildId, inviteInfo.InviteId, inviteInfo.InviteCode, onCompletion: null);
        }

        /// <summary>
        /// Handles response to BeginCreateGuildInviteCode. Defaults to adding the invite into <see cref="GuildInviteSamples"/>.
        /// </summary>
        protected void DefaultHandleGuildCreateInvitationResult(GuildCreateInvitationResult result)
        {
            if (result.Status != GuildCreateInvitationResponse.StatusCode.Success)
            {
                _log.Debug("GuildCreateInvitation request failed: {Reason}", result.Status);
            }
            else
            {
                GuildInviteSamples.Add(new GuildInviteSample(result.InviteState.InviteCode));
            }
        }

        void HarvestGuildInvitationSamples()
        {
            if (GuildContext == null)
                return;
            foreach (EntityId playerId in GuildContext.CommittedModel.EnumerateMembers())
            {
                if (!GuildContext.CommittedModel.TryGetMember(playerId, out GuildMemberBase member))
                    continue;
                foreach (GuildInviteState invite in member.Invites.Values)
                    GuildInviteSamples.Add(new GuildInviteSample(invite.InviteCode));
            }
        }

        #endif

        protected async Task ApplyResourceCorrectionAsync(SessionProtocol.SessionResourceCorrection correction)
        {
            if (correction.LanguageUpdate.HasValue)
            {
                SessionProtocol.SessionResourceCorrection.LanguageUpdateInfo languageUpdate = correction.LanguageUpdate.Value;
                _sessionNegotiationResources.ActiveLanguage = await BotLocalizationLanguageProvider.Instance.GetAsync(languageUpdate.ActiveLanguage, languageUpdate.LocalizationVersion);
            }

            foreach ((ClientSlot slot, SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo configUpdate) in correction.ConfigUpdates)
                _sessionNegotiationResources.ConfigArchives[slot] = await BotGameConfigProvider.Instance.GetBaselineConfigArchiveAsync(configUpdate);

            foreach ((ClientSlot slot, SessionProtocol.SessionResourceCorrection.ConfigPatchesUpdateInfo configPatches) in correction.PatchUpdates)
                _sessionNegotiationResources.PatchArchives[slot] = await BotGameConfigProvider.Instance.GetSpecializationPatchesAsync(configPatches.PatchesVersion);
        }

        public static string GetRandomSubstring(Random random, string sourceString)
        {
            int startNdx = random.Next(sourceString.Length);
            int len = random.Next(sourceString.Length - startNdx) + 1;
            return sourceString.Substring(startNdx, len);
        }

        static readonly string[] _botNameFormats    = new string[] { "[Wrapper] [Adjective] [Wrapper] [Creature] [Wrapper]", "[Wrapper][Adjective][Creature][Extra][Wrapper]", "[Adjective][Creature]", "[Adjective][Creature][Number]", "[Adjective] [Creature] [Extra]", "[ADJECTIVE] [Extra] [CREATURE]", "[Adjective] [Wrapper] [Creature] [Wrapper] [Number]" };
        static readonly string[] _botNameAdjectives = new string[] { "abashed", "abrasive", "adamant", "admiral", "aloof", "autoritarian", "axiomatic", "barbarous", "beginner", "brittle", "casual", "cranky", "crude", "dapper", "dark", "deceptive", "digital", "draconian", "eager", "easy", "erratic", "fake", "friendly", "genuine", "heady", "helpful", "idle", "insolent", "invisible", "irksome", "irresponsible", "magical", "mannered", "master", "native", "naïve", "nebulous", "obtuse", "professional", "provocative", "rare", "shoddy", "slow", "smart", "sparkling", "special", "spontaneous", "swollen", "talkative", "tenacious", "viral", "weak", "wild", "wise", "witty" };
        static readonly string[] _botNameCreatures  = new string[] { "angel", "ant", "armadillo", "bat", "bear", "bee", "beetle", "blobfish", "boar", "bull", "camel", "chinchilla", "cobra", "cow", "crab", "deer", "dolphin", "dragon", "eagle", "elephant", "flamingo", "fox", "gecko", "giraffe", "goat", "goblin", "grasshopper", "hobbit", "hyena", "kong", "liger", "lizard", "manatee", "mustang", "narwhal", "octopus", "owl", "panda", "pangolin", "parrot", "pegasus", "penguin", "pig", "pigeon", "rabbit", "rat", "raven", "salamander", "salmon", "shark", "sloth", "spider", "squid", "squirrel", "turtle", "unicorn", "wasp", "whale", "wolf", "wolverine", "wombat", "zebra" };
        static readonly string[] _botNameWrappers   = new string[] { "-", "==", "^", "_", "+", "‼️", "❤️", "☠️", "⭐️⭐️", "✨", "⚡️" };
        static readonly string[] _botNameExtras     = new string[] { "🔥", "💯", "🙌", "🚀", "🎩" };

        public static string GenerateRandomPlayerName(Random random)
        {
            string name;
            do
            {
                string selectedFormat = _botNameFormats[random.Next(_botNameFormats.Length)];
                string selectedAdjectiveName = _botNameAdjectives[random.Next(_botNameAdjectives.Length)];
                string selectedCreatureName = _botNameCreatures[random.Next(_botNameCreatures.Length)];
                string selectedWrapper = _botNameWrappers[random.Next(_botNameWrappers.Length)];
                string selectedExtra = _botNameExtras[random.Next(_botNameExtras.Length)];
                string selectedNumber = random.Next(99).ToString(CultureInfo.InvariantCulture.NumberFormat);
                name = "🤖 " + selectedFormat
                    .Replace("[Adjective]", CultureInfo.CurrentCulture.TextInfo.ToTitleCase(selectedAdjectiveName))
                    .Replace("[ADJECTIVE]", selectedAdjectiveName.ToUpper(CultureInfo.CurrentCulture))
                    .Replace("[Creature]", CultureInfo.CurrentCulture.TextInfo.ToTitleCase(selectedCreatureName))
                    .Replace("[CREATURE]", selectedCreatureName.ToUpper(CultureInfo.CurrentCulture))
                    .Replace("[Extra]", selectedExtra)
                    .Replace("[Wrapper]", selectedWrapper)
                    .Replace("[Number]", selectedNumber);
            }
            while (name.Length < 5 || name.Length > 20);
            return name;
        }

        static readonly string[] _guildNameFormats  = new string[] { "[PREFIX] [TYPE]s", "The [PREFIX] [TYPE]s", "Society of [PREFIX] [TYPE]s", "[PREFIX] [TYPE] Guild", "[PREFIX] [TYPE] Killers", "[PREFIX] [TYPE]s Gang" };
        static readonly string[] _guildNamePrefixes = new string[] { "Dark", "Angry", "Clever", "Happy", "Wild", "Powerful", "Incredible", "Careless", "Bad", "Mean", "Brutal", "Epic", "Classy", "Optimal", "Casual", "Serious", "Stealthy", "Junior", "AI", "Super", "Mighty" };
        static readonly string[] _guildNameTypes    = new string[] { "Angel", "Zombie", "Titan", "Duck", "Coder", "Leopard", "Quagga", "Wasp", "Giant", "Droid", "Grinder", "Killer", "Farmer", "Cleaner", "DJ", "Gangster", "PK", "Adventurer", "Trader", "Miner", "Soldier", "Professional", "Gamer" };

        public static string GenerateRandomGuildName(Random random)
        {
            string selectedFormat = _guildNameFormats[random.Next(_guildNameFormats.Length)];
            string selectedPrefix = _guildNamePrefixes[random.Next(_guildNamePrefixes.Length)];
            string selectedType = _guildNameTypes[random.Next(_guildNameTypes.Length)];
            return selectedFormat
                .Replace("[PREFIX]", selectedPrefix)
                .Replace("[TYPE]", selectedType);
        }

        static readonly string[] _guildLoremTexts = new string[] { "Lorem ipsum dolor sit amet.", "Consectetur adipiscing elit.", "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.", "Ut enim ad minim veniam.", "Quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.", "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.", "Excepteur sint occaecat cupidatat non proident.", "Sunt in culpa qui officia deserunt mollit anim id est laborum." };

        public static string GenerateRandomGuildDescription(Random random)
        {
            return string.Join(" ", Enumerable.Range(0, 2).Select(_ => _guildLoremTexts[random.Next(_guildLoremTexts.Length)]));
        }

        PlayerUploadIncidentReport CreateDummyIncidentReport()
        {
            MetaTime now = MetaTime.Now;

            List<ClientLogEntry> logEntries = new List<ClientLogEntry>();
            for (int i = 0; i < 10; i++)
            {
                logEntries.Add(new ClientLogEntry(
                    timestamp: now - MetaDuration.FromSeconds(1),
                    level: ClientLogEntryType.Log,
                    message: Invariant($"botclient dummy message {i}"),
                    stackTrace: Invariant($"botclient dummy stack trace {i}")));
            }

            RandomPCG rnd = RandomPCG.CreateNew();

            string randomTag = rnd.NextInt(10).ToString(CultureInfo.InvariantCulture);

            PlayerIncidentReport report = new UnhandledExceptionError(
                id: PlayerIncidentUtil.EncodeIncidentId(now, rnd.NextULong()),
                occurredAt: now,
                logEntries: logEntries,
                systemInfo: new UnitySystemInfo(),
                platformInfo: new UnityPlatformInfo(),
                applicationInfo: new IncidentApplicationInfo(
                    buildVersion:                   GetBuildVersion(),
                    deviceGuid:                     _sessionGuidService.TryGetDeviceGuid(),
                    activeLanguage:                 ActiveLanguage.LanguageId.Value,
                    highestSupportedLogicVersion:   MetaplayCore.Options.SupportedLogicVersions.MaxVersion),
                gameConfigInfo: new IncidentGameConfigInfo(ContentHash.None, ContentHash.None, new List<ExperimentVariantPair>()),
                exceptionName: "BotclientDummyExceptionName",
                exceptionMessage: $"Botclient dummy exception message {randomTag}",
                stackTrace: $"Botclient dummy exception stack trace {randomTag}");

            return new PlayerUploadIncidentReport(
                report.IncidentId,
                PlayerIncidentUtil.CompressIncidentForNetworkDelivery(report));
        }

        protected abstract string GetCurrentStateLabel();
        protected abstract Task OnNetworkMessage(MetaMessage message);
        protected abstract Task OnUpdate();

        #region IPlayerModelClientListenerCore

        public virtual void OnPlayerNameChanged(string newName) { }
        public virtual void PendingDynamicPurchaseContentAssigned(InAppProductId productId) { }
        public virtual void PendingStaticInAppPurchaseContextAssigned(InAppProductId productId) { }
        public virtual void InAppPurchaseValidationFailed(InAppPurchaseEvent ev) { }
        public virtual void InAppPurchaseValidated(InAppPurchaseEvent ev) { }
        public virtual void InAppPurchaseClaimed(InAppPurchaseEvent ev) { }
        public virtual void DuplicateInAppPurchaseCleared(InAppPurchaseEvent ev) { }
        public virtual void OnPlayerScheduledForDeletionChanged() { }
        public virtual void OnNewInGameMail(PlayerMailItem mail) { }

        #endregion
    }
}
