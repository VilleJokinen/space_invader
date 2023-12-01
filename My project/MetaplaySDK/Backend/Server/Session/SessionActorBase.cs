// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Entity.Synchronize;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Debugging;
using Metaplay.Core.Localization;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.Session;
using Metaplay.Server.Authentication;
using Metaplay.Server.GameConfig;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using static System.FormattableString;
using Metaplay.Server.EntityArchive;
using Metaplay.Core.Json;
using System.Threading;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using Metaplay.Core.Client;
using Metaplay.Cloud.Web3;
using Metaplay.Server.Web3;

#if !METAPLAY_DISABLE_GUILDS
using Metaplay.Core.Guild;
using Metaplay.Core.Guild.Messages.Core;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Server.Database;
using Metaplay.Server.Guild;
using Metaplay.Server.Guild.InternalMessages;
using Metaplay.Server.GuildDiscovery;
#endif

namespace Metaplay.Server
{
    [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
    [MetaImplicitMembersRange(1, 100)]
    public class MetaSessionAuxiliaryInfo
    {
        public ScheduledMaintenanceModeForClient            ScheduledMaintenanceMode    { get; private set; }
        public SessionProtocol.InitialPlayerState           PlayerInitialState          { get; private set; }
        public List<EntityInitialState>                     EntityInitialStates         { get; private set; }
        public OrderedDictionary<LanguageId, ContentHash>   LocalizationVersions        { get; private set; }
        public List<EntityActiveExperiment>                 ActiveExperiments           { get; private set; }
        public string                                       CorrectedDeviceGuid         { get; private set; }

        MetaSessionAuxiliaryInfo(){ }
        public MetaSessionAuxiliaryInfo(
            ScheduledMaintenanceModeForClient scheduledMaintenanceMode,
            SessionProtocol.InitialPlayerState playerInitialState,
            List<EntityInitialState> entityInitialStates,
            OrderedDictionary<LanguageId, ContentHash> localizationVersions,
            List<EntityActiveExperiment> activeExperiments,
            string correctedDeviceGuid)
        {
            ScheduledMaintenanceMode = scheduledMaintenanceMode;
            PlayerInitialState = playerInitialState;
            EntityInitialStates = entityInitialStates;
            LocalizationVersions = localizationVersions;
            ActiveExperiments = activeExperiments;
            CorrectedDeviceGuid = correctedDeviceGuid;
        }
    }

    [MetaSerializable]
    public abstract class SessionResumeFailure
    {
        [MetaSerializableDerived(1)]
        public class FailedToResume : SessionResumeFailure { }
    }

    public abstract class SessionActorBase<TGameSessionStartParams, TGameSessionState> : EphemeralEntityActor
    {
        /// <summary>
        /// Status of a pending social authentication request: either pending authentication or resolved to a conflict.
        /// </summary>
        public enum PendingSocialAuthRequestStatus
        {
            Authenticating,     // Authentication with platform vendor is in progress
            ConflictingProfile, // A conflicting player state was found for profile, waiting for conflict to be resolved
        }

        enum ActorPhase
        {
            WaitingForConnection,
            InSession,
            Terminating,
        }

        public class PendingSocialAuthRequest
        {
            public PendingSocialAuthRequestStatus   Status              { get; private set; } = PendingSocialAuthRequestStatus.Authenticating;
            public SocialAuthenticationClaimBase    AuthClaim           { get; private set; }
            public AuthenticatedSocialClaimKeys     AuthKeys            { get; private set; }
            public EntityId                         ExistingPlayerId    { get; private set; } = EntityId.None;

            public PendingSocialAuthRequest(SocialAuthenticationClaimBase authClaim)
            {
                AuthClaim = authClaim;
            }

            public void SetAuthenticationKeys(AuthenticatedSocialClaimKeys authKeys)
            {
                AuthKeys = authKeys;
            }

            public void SetConflictingPlayerId(EntityId existingPlayerId)
            {
                Status              = PendingSocialAuthRequestStatus.ConflictingProfile;
                ExistingPlayerId    = existingPlayerId;
            }
        }

        protected class MessageBarrier
        {
            public readonly int PiggingId;
            public bool         PlayerPigReceived;
            public bool         GuildPigReceived;
            public bool         PlayerPigNeeded;
            public bool         GuildPigNeeded;
            List<MetaMessage>   BufferedMessagesMaybe;

            public MessageBarrier(int piggingId, bool needsPlayerFlush, bool needsGuildFlush)
            {
                PiggingId = piggingId;
                PlayerPigNeeded = needsPlayerFlush;
                GuildPigNeeded = needsGuildFlush;
            }

            public bool IsSatisfied()
            {
                if (PlayerPigNeeded && !PlayerPigReceived)
                    return false;
                if (GuildPigNeeded && !GuildPigReceived)
                    return false;
                return true;
            }

            public IEnumerable<MetaMessage> EnumerateMessages()
            {
                if (BufferedMessagesMaybe == null)
                    return Array.Empty<MetaMessage>();
                else
                    return BufferedMessagesMaybe;
            }
            public void AddMessage(MetaMessage message)
            {
                if (BufferedMessagesMaybe == null)
                    BufferedMessagesMaybe = new List<MetaMessage>();
                BufferedMessagesMaybe.Add(message);
            }
        }

        /// <summary>
        /// Statistics of the session state. Useful for debugging connectivity issues.
        /// </summary>
        protected class SessionStatistics
        {
            public DateTime                     SessionStartedAt;
            public int                          NumConnections          = 0;
            public int                          NumConnectionEvents     = 0;
            public List<ConnectionEvent>        RecentConnectionEvents  = new List<ConnectionEvent>();
            public List<LoginDebugDiagnostics>  LoginDebugDiagnostics   = new List<LoginDebugDiagnostics>();
            public DateTime?                    LastMessageReceivedAt;
            public DateTime?                    LastPayloadMessageReceivedAt;
            public DateTime?                    ClientNotCommunicatingWarningLoggedAt;

            public List<Type>                   EarlySessionMessageTypes = new List<Type>();

            public DateTime?                    LastSessionPingReceivedAt;
            public int                          LastSessionPingIncidentReportConnectionIndex    = 0;
            public int                          NumSessionPingIncidentsReported                 = 0;

            public struct ConnectionEvent
            {
                public enum EventKind
                {
                    Start,
                    End,
                }

                public readonly EventKind   Kind;
                public readonly EntityId    ConnectionId;
                public readonly DateTime    Time;

                public ConnectionEvent(EventKind kind, EntityId connectionId, DateTime time)
                {
                    Kind = kind;
                    ConnectionId = connectionId;
                    Time = time;
                }

                public override string ToString()
                {
                    return $"({Kind}, {ConnectionId}, {Time.ToString("s", CultureInfo.InvariantCulture)}Z)";
                }
            }

            const int LoginHistoryMaxLength             = 6;
            const int EarlySessionMessageTypesMaxLength = 3;

            public void OnConnectionStarted(EntityId connectionId, LoginDebugDiagnostics loginDebugDiagnostics)
            {
                NumConnections++;
                AddConnectionEvent(new ConnectionEvent(ConnectionEvent.EventKind.Start, connectionId, DateTime.UtcNow));

                LoginDebugDiagnostics.Add(loginDebugDiagnostics);
                while (LoginDebugDiagnostics.Count > LoginHistoryMaxLength)
                    LoginDebugDiagnostics.RemoveAt(0);
            }

            public void OnConnectionEnded(EntityId connectionId)
            {
                AddConnectionEvent(new ConnectionEvent(ConnectionEvent.EventKind.End, connectionId, DateTime.UtcNow));
            }

            void AddConnectionEvent(ConnectionEvent connectionEvent)
            {
                const int ConnectionEventHistoryMaxLength = 2*LoginHistoryMaxLength;

                NumConnectionEvents++;
                RecentConnectionEvents.Add(connectionEvent);
                while (RecentConnectionEvents.Count > ConnectionEventHistoryMaxLength)
                    RecentConnectionEvents.RemoveAt(0);
            }

            public void OnReceivedSessionPayloadMessage(MetaMessage message)
            {
                LastPayloadMessageReceivedAt = DateTime.UtcNow;

                if (EarlySessionMessageTypes.Count < EarlySessionMessageTypesMaxLength)
                    EarlySessionMessageTypes.Add(message.GetType());
            }
        }

        /// <summary>
        /// Manages the logic for a single entity channel during a session. A channel is a bidirectional message queue between an entity on server and
        /// entity client on the client. Channel lifetime is a subset of a session lifetime and channels may be created and ended during a single session.
        /// <para>
        /// Game logic may inherit this manager to customize protocol management.
        /// </para>
        /// </summary>
        public class EntityChannelManager
        {
            public readonly IMetaLogger                 Log;
            public readonly EntitySubscription          Actor;
            public readonly int                         ChannelId;
            public readonly ClientSlot                  ClientSlot;
            public readonly EntityChannelManager        ParentEntity;
            public readonly List<EntityChannelManager>  ChildEntities;

            /// <summary>
            /// Is the entity state established on client, i.e. has the client downloaded the configs and activate the model.
            /// </summary>
            public bool IsActiveOnClient { get; private set; }

            public EntityId EntityId => Actor.EntityId;

            public enum SubscriptionEndResolution
            {
                /// <summary>
                /// Subscription ending is ignored. Client is not informed.
                /// </summary>
                Ignore,

                /// <summary>
                /// Client is informed the channel was closed.
                /// </summary>
                CloseChannel,

                /// <summary>
                /// Session is terminated with a fatal error.
                /// </summary>
                FatalError,
            }

            public EntityChannelManager(IMetaLogger log, EntitySubscription actor, int channelId, ClientSlot clientSlot, bool isActiveOnClient, EntityChannelManager parentManager)
            {
                Log = log;
                Actor = actor;
                ChannelId = channelId;
                ClientSlot = clientSlot;
                IsActiveOnClient = isActiveOnClient;
                ParentEntity = parentManager;
                ChildEntities = new List<EntityChannelManager>();
                if (parentManager != null)
                    parentManager.ChildEntities.Add(this);
            }

            internal void OnClientEntityActivatedCore()
            {
                IsActiveOnClient = true;
                OnClientEntityActivated();
            }

            /// <summary>
            /// Called when Entity kicks the Session. Default behavior is to terminate the session with an error.
            /// </summary>
            public virtual Task<SubscriptionEndResolution> OnSubscriptionKicked(MetaMessage msg) => Task.FromResult<SubscriptionEndResolution>(SubscriptionEndResolution.FatalError);

            /// <summary>
            /// Called when Entity terminates. Default behavior is to terminate the session with an error.
            /// </summary>
            public virtual Task<SubscriptionEndResolution> OnSubscriptionTerminated() => Task.FromResult<SubscriptionEndResolution>(SubscriptionEndResolution.FatalError);

            /// <summary>
            /// Called when client has set up the entity state.
            /// </summary>
            public virtual void OnClientEntityActivated() { }
        }

        /// <summary>
        /// Contains the temporary subscription state to an player-associated entity. The pending state only exists during the session <-> entity handshake.
        /// This duration spans from <see cref="BeginSubscribeToAssociatedEntityAsync"/> to <see cref="CompleteSubscribeToAssociatedEntity"/>. Note that if
        /// any subscription fails, complete handler is not called.
        /// <para>
        /// Game logic may inherit this class to track custom handshake properties. Use <see cref="Default"/> for default implementation.
        /// </para>
        /// </summary>
        public abstract class PendingAssociatedEntitySubscription
        {
            public class Default : PendingAssociatedEntitySubscription
            {
                public override EntitySubscription                      Actor               { get; }
                public InternalEntitySubscribeResponseBase              Response            { get; }
                public override EntitySubscription                      ParentSubscription  { get; }

                public override EntityInitialState                      State => Response.State;
                public override List<AssociatedEntityRefBase>           AssociatedEntities => Response.AssociatedEntities;

                public Default(EntitySubscription actor, InternalEntitySubscribeResponseBase response, EntitySubscription parentSubscription)
                {
                    Actor = actor;
                    Response = response;
                    ParentSubscription = parentSubscription;
                }
            }

            public abstract EntitySubscription Actor { get; }
            public abstract EntityInitialState State { get; }
            public abstract List<AssociatedEntityRefBase> AssociatedEntities { get; }

            /// <summary>
            /// The entity subscription of the entity that declared this entity-subscription in its associated entities. <c>Null</c>
            /// if this PendingAssociatedEntitySubscription is a root-subscription, e.g. Player, i.e it doesn't have a parent entity.
            /// </summary>
            public abstract EntitySubscription ParentSubscription { get; }
        }

        readonly struct PendingAssociationTarget
        {
            /// <summary>
            /// The entity association, targeted to the target entity.
            /// </summary>
            public readonly AssociatedEntityRefBase Association;

            /// <summary>
            /// Slot of the source entity. If there is no source entity, i.e. this is a root entity, e.g. the Player, the value is <c>null</c>.
            /// </summary>
            public readonly ClientSlot ParentSlot;

            public PendingAssociationTarget(AssociatedEntityRefBase association, ClientSlot parentSlot)
            {
                Association = association;
                ParentSlot = parentSlot;
            }
        }

        protected class SessionStartParams
        {
            public readonly InternalSessionStartNewRequest  Meta;
            public readonly TGameSessionStartParams         Game;

            public SessionStartParams(InternalSessionStartNewRequest meta, TGameSessionStartParams game)
            {
                Meta = meta;
                Game = game;
            }
        }

        static readonly Prometheus.Counter      c_sessionStart              = Prometheus.Metrics.CreateCounter("game_session_start_total", "Number of sessions started");
        static readonly Prometheus.Counter      c_sessionResume             = Prometheus.Metrics.CreateCounter("game_session_resume_total", "Number of session resumption attempts");
        static readonly Prometheus.Histogram    c_sessionDisconnectedTime   = Prometheus.Metrics.CreateHistogram("game_session_disconnected_time", "How long client was disconnected before resuming", Metaplay.Cloud.Metrics.Defaults.SessionDurationUntilResumeConfig);
        static readonly Prometheus.Counter      c_sessionResumeFail         = Prometheus.Metrics.CreateCounter("game_session_resume_fails_total", "Number of session resumption failures", "reason");
        static readonly Prometheus.Counter      c_sessionMessagesDropped    = Prometheus.Metrics.CreateCounter("game_session_messages_dropped", "Number of session payload messages dropped due to queue limit");

        protected override AutoShutdownPolicy                   ShutdownPolicy                              => AutoShutdownPolicy.ShutdownAfterSubscribersGone(lingerDuration: TimeSpan.FromSeconds(20));

        /// <summary>
        /// When client connection exists, this is the maximum number of messages to keep in outgoing queue before we start dropping them.
        /// Relevant for clients that don't send acknowledgements often enough (due to bad connection or modified client).
        /// </summary>
        protected virtual int                                   PayloadMessageQueueCountConnectedLimit      => 4 * SessionUtil.AcknowledgementMessageInterval;
        /// <summary>
        /// When client connection does not exist, this is the maximum number of messages to keep in outgoing queue before we start dropping them.
        /// </summary>
        protected virtual int                                   PayloadMessageQueueCountDisconnectedLimit   => 4 * PayloadMessageQueueCountConnectedLimit;

        protected EntityId                                      PlayerId                                    => EntityId.Create(EntityKindCore.Player, _entityId.Value);

        ActorPhase                                              _phase                                      = ActorPhase.WaitingForConnection;

        AtomicValueSubscriber<ActiveScheduledMaintenanceMode>   _activeScheduledMaintenanceModeSubscriber   = GlobalStateProxyActor.ActiveScheduledMaintenanceMode.Subscribe();
        int                                                     _runningPubsubPiggingId                     = 1;
        List<MessageBarrier>                                    _pendingBarriers;

        /// <summary>
        /// The current Connection actor connected to the client, or <c>null</c> if there is no currently connected client
        /// </summary>
        EntitySubscriber                                        _client;
        DateTime?                                               _clientLostAt                               = null;
        ClientAppPauseStatus                                    _clientPauseStatus;
        string                                                  _clientPauseReason;
        CancellationTokenSource                                 _clientPauseDeadlineExceededTimerCts;
        RefreshKeyCounter<EntityId>.RefreshHandle               _numConcurrentsTracker                      = null;
        Dictionary<int, PendingSocialAuthRequest>               _pendingSocialAuths                         = new Dictionary<int, PendingSocialAuthRequest>();
        int                                                     _socialAuthRunningId                        = 1;

        int                                                     _multiplayerEntityRunningId                 = 1;
        /// <summary>
        /// Mapping from Entity Channel Id to corresponding channel manager. Channel Ids are allocated with <see cref="_multiplayerEntityRunningId"/>.
        /// </summary>
        Dictionary<int, EntityChannelManager>                   _multiplayerEntities                        = new Dictionary<int, EntityChannelManager>();

        #if !METAPLAY_DISABLE_GUILDS

        /// <summary>
        /// The incarnation number of the guild from Player. Player increments on join. Player uses this to validate that requests refer to the same guild incarnation and reject stale requests.
        /// </summary>
        int                                                     _guildIncarnation;

        MetaTime                                                _nextInvitationInspectionEarliestAt         = MetaTime.Epoch;
        Dictionary<int, EntitySubscription>                     _guildViews                                 = new Dictionary<int, EntitySubscription>();
        bool                                                    _hasOngoingGuildSearch;
        #endif

        // Fields below are set once in succesful session start

        /// <summary>
        /// Cannot change during the session. If player actor is lost, session ends.
        /// </summary>
        protected EntitySubscription                            _playerActor;
        SessionParticipantState                                 _sessionParticipant;
        protected TGameSessionState                             _gameState;
        uint                                                    _lastAppLaunchId;
        uint                                                    _lastClientSessionNonce;
        uint                                                    _lastClientSessionConnectionNdx;
        AuthenticationKey                                       _authKey;
        protected int                                           _logicVersion;
        protected CompressionAlgorithmSet                       _supportedArchiveCompressions;
        ClientPlatform                                          _clientPlatform;
        SessionStatistics                                       _statistics;
        bool                                                    _hasDroppedAnyUnacknowledgedMessages;

        public SessionActorBase(EntityId entityId) : base(entityId)
        {
        }

        #region Actor stuff

        protected override void PreStart()
        {
            StartRandomizedPeriodicTimer(TimeSpan.FromSeconds(5), ActorTick.Instance);

            base.PreStart();
        }

        protected override async Task<MetaMessage> OnNewSubscriber(EntitySubscriber subscriber, MetaMessage message)
        {
            if (subscriber.Topic == EntityTopic.Owner)
            {
                if (message is InternalSessionStartNewRequest startNewRequest)
                    return await StartNewSessionAsync(subscriber, startNewRequest);
                else if (message is InternalSessionResumeRequest resumeRequest)
                    return ResumeSession(subscriber, resumeRequest);
                else
                    throw new ArgumentException($"Unknown session subscriber setup request message type {message.GetType()}");
            }
            else
            {
                _log.Warning("Subscriber {EntityId} on unknown topic [{Topic}]", subscriber.EntityId, subscriber.Topic);
                return null;
            }
        }

        [PubSubMessageHandler]
        async Task HandleMetaMessage(EntitySubscription subscription, MetaMessage message)
        {
            if (_playerActor == subscription)
            {
                if (TryGetPlayerBlockingBarrier(out MessageBarrier barrier))
                    barrier.AddMessage(message);
                else
                    SendOutgoingPayloadMessage(message);
                return;
            }

            // default path
            await HandleUnknownMessage(subscription.EntityId, message);
        }

        protected override Task HandleUnknownMessage(EntityId fromEntityId, MetaMessage message)
        {
            if (_phase != ActorPhase.InSession)
            {
                _log.Info("Received a {MessageType} from {FromEntityId} but there's no session; ignoring", message.GetType().Name, fromEntityId);
                return Task.CompletedTask;
            }

            // \note: some player messages are delivered directly and not on the Subscription channel. That should be fixed but until then, this remains here.
            if (fromEntityId == _playerActor?.EntityId)
            {
                if (TryGetPlayerBlockingBarrier(out MessageBarrier barrier))
                    barrier.AddMessage(message);
                else
                    SendOutgoingPayloadMessage(message);
            }
            else
            {
                // Maintain legacy behavior where all unknown messages are forwarded to client
                SendOutgoingPayloadMessage(message);
            }

            return Task.CompletedTask;
        }

        [MessageHandler]
        protected async Task HandleSessionMessageFromClient(EntityId fromEntityId, SessionMessageFromClient wrapper)
        {
            if (fromEntityId == _client?.EntityId)
                await HandleMessageFromClient(wrapper.Message);
            else
                _log.Warning("Got SessionMessageFromClient from unexpected entity {FromEntityId}", fromEntityId);
        }

        protected override void OnSubscriberLost(EntitySubscriber subscriber)
        {
            if (subscriber == _client)
                HandleClientLost();
            else
                _log.Warning("Unexpected subscriber entity {EntityId} lost", subscriber.EntityId);
        }

        protected override async Task OnSubscriptionLost(EntitySubscription subscription)
        {
            if (subscription == _playerActor)
            {
                await HandlePlayerActorLost();
                return;
            }

            // \todo: handle guild views.

            EntityChannelManager channelManager = TryGetChannelForSubscription(subscription);
            if (channelManager != null)
            {
                await HandleMultiplayerActorLost(channelManager);
                return;
            }

            if (_phase == ActorPhase.InSession)
            {
                if (!await GameTryHandleSubscriptionLost(subscription))
                    _log.Warning("Unhandled loss of subscription to entity {EntityId} [{Topic}]", subscription.EntityId, subscription.Topic);
            }
        }

        protected override async Task OnSubscriptionKicked(EntitySubscription subscription, MetaMessage message)
        {
            if (subscription == _playerActor)
            {
                await HandlePlayerActorKicked(message);
                return;
            }

            // \todo: handle guild views.

            EntityChannelManager channelManager = TryGetChannelForSubscription(subscription);
            if (channelManager != null)
            {
                await HandleMultiplayerActorKicked(channelManager, message);
                return;
            }

            if (_phase == ActorPhase.InSession)
            {
                if (!await GameTryHandleSubscriptionKicked(subscription, message))
                    _log.Warning("Unhandled kick of subscription to entity {EntityId} [{Topic}]: {Message}", subscription.EntityId, subscription.Topic, PrettyPrint.Compact(message));
            }
        }

        [CommandHandler]
        public async Task HandleActorTick(ActorTick _)
        {
            if (_phase == ActorPhase.InSession)
            {
                UpdateDebugChecks();

                // Update scheduled maintenance mode, sending to client if changed.
                if (TryUpdateScheduledMaintenanceMode(out ScheduledMaintenanceMode scheduledMaintenanceMode))
                    SendOutgoingPayloadMessage(new UpdateScheduledMaintenanceMode(ScheduledMaintenanceMode.GetEffectiveModeForClient(scheduledMaintenanceMode, _clientPlatform)));

                // Check session length
                SessionOptions sessionOpts = RuntimeOptionsRegistry.Instance.GetCurrent<SessionOptions>();
                if (sessionOpts.MaximumSessionLength.HasValue && DateTime.UtcNow >= _statistics.SessionStartedAt + sessionOpts.MaximumSessionLength)
                {
                    _log.Warning("Session has exceeded the maximum length ({MaxLength}). Disconnecting the player.", sessionOpts.MaximumSessionLength);
                    await TerminateSession(new SessionForceTerminateMessage(reason: new SessionForceTerminateReason.SessionTooLong()));
                }

                // Check maintenance mode. If a player slips through the login check, or was logged in when maintenance mode has started,
                // they'll be kicked here.
                // \note: This is the actual maintenance mode, not the above which is about the info about the upcoming maintenance
                if (GlobalStateProxyActor.IsInMaintenance &&
                    !GlobalStateProxyActor.ActiveScheduledMaintenanceMode.Get().IsPlatformExcluded(_clientPlatform) &&
                    !GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(PlayerId))
                {
                    _log.Debug("Maintenance mode started. Terminating session.");
                    await TerminateSession(new SessionForceTerminateMessage(reason: new SessionForceTerminateReason.MaintenanceModeStarted()));
                }
            }

            // Track concurrents
            _numConcurrentsTracker?.Refresh();

            await GameActorTick();
        }

        protected override void PostStop()
        {
            _numConcurrentsTracker?.Remove();
            base.PostStop();
        }

        #endregion

        #region Session management

        bool IsStaleConnection(uint appLaunchId, uint clientSessionNonce, uint clientSessionConnectionNdx)
        {
            if (_phase != ActorPhase.InSession)
                return false;
            if (appLaunchId == 0 || clientSessionNonce == 0)
                return false;
            if (appLaunchId != _lastAppLaunchId || clientSessionNonce != _lastClientSessionNonce)
                return false;
            return clientSessionConnectionNdx < _lastClientSessionConnectionNdx;
        }

        void LogConnectionStartDiagnostics(EntitySubscriber client, LoginDebugDiagnostics loginDebugDiagnostics)
        {
            _log.Debug("Setting up for subscriber {FromEntityId}. Limited debug diagnostics: {DiagTimestamp}, Session={DiagSession}, DurationSinceConnectionUpdate={DiagDurationSinceConnectionUpdate}, DurationSincePlayerContextUpdate={DiagDurationSincePlayerContextUpdate}",
                client.EntityId, loginDebugDiagnostics?.Timestamp, PrettyPrint.Compact(loginDebugDiagnostics?.Session), loginDebugDiagnostics?.DurationSinceConnectionUpdate, loginDebugDiagnostics?.DurationSincePlayerContextUpdate);
            if (_phase == ActorPhase.InSession)
                _log.Debug("Note: current session token is {CurrentSessionToken}", _sessionParticipant.Token);
        }

        async Task<InternalSessionStartNewResponse> StartNewSessionAsync(EntitySubscriber client, InternalSessionStartNewRequest startNewRequest)
        {
            SessionStartParams sessionStart = new SessionStartParams(
                meta: startNewRequest,
                game: GameCreateSessionStartParams(startNewRequest));

            LogConnectionStartDiagnostics(client, startNewRequest.DebugDiagnostics);

            // Check for stale connection
            if (IsStaleConnection(startNewRequest.AppLaunchId, startNewRequest.ClientSessionNonce, startNewRequest.ClientSessionConnectionNdx))
                throw InternalSessionStartNewRefusal.ForStaleConnection();

            // End old session, if any.
            if (_phase == ActorPhase.InSession)
            {
                _log.Debug("Old session already ongoing, shutting it down.");
                await TerminateSession(clientKickMessage: new SessionForceTerminateMessage(reason: new SessionForceTerminateReason.ReceivedAnotherConnection()));

                // This actor is now being shut down and has shutdown request in the mailbox.
                throw InternalSessionStartNewRefusal.ForEntityIsRestarting();
            }
            else if (_phase == ActorPhase.Terminating)
            {
                // This actor was already being shut down and shutdown request is in the mailbox.
                throw InternalSessionStartNewRefusal.ForEntityIsRestarting();
            }
            MetaDebug.Assert(_phase == ActorPhase.WaitingForConnection, "Session actor must be new actor for starting a new session");

            c_sessionStart.Inc();

            _log.Debug("Starting new session");

            int numSubscribeRetries = -1;

        subscribeAgain:
        {
            numSubscribeRetries++;
            if (numSubscribeRetries > 3)
                throw new Exception("Session initial entity relationships did not settle after 3 iterations. Cannot continue.");

            SessionProtocol.SessionResourceCorrection               resourceCorrection  = new SessionProtocol.SessionResourceCorrection();
            SessionToken                                            newSessionToken     = SessionToken.CreateRandom();
            List<PendingAssociationTarget>                          pendingAssociations = new List<PendingAssociationTarget>();
            OrderedDictionary<ClientSlot, EntitySubscription>       slotToSubscription  = new OrderedDictionary<ClientSlot, EntitySubscription>();
            PlayerSessionParamsBase                                 playerSessionParams = GameCreatePlayerSessionParams(sessionStart, newSessionToken);
            await using EntitySubscriptionGuard                     playerActorGuard    = new EntitySubscriptionGuard(this);
            InternalPlayerSessionSubscribeResponse                  playerResponse;

            #if !METAPLAY_DISABLE_GUILDS
            int                                                     guildIncarnation;
            #endif

            try
            {
                _log.Debug("Subscribing to player");

                (EntitySubscription playerActor, playerResponse) = await SubscribeToAsync<InternalPlayerSessionSubscribeResponse>(PlayerId, EntityTopic.Owner, new InternalPlayerSessionSubscribeRequest(playerSessionParams));
                playerActorGuard.Assign(playerActor);

                slotToSubscription.Add(ClientSlotCore.Player, playerActor);
                foreach (AssociatedEntityRefBase association in playerResponse.AssociatedEntities)
                    pendingAssociations.Add(new PendingAssociationTarget(association, parentSlot: ClientSlotCore.Player));

                #if !METAPLAY_DISABLE_GUILDS
                guildIncarnation = playerResponse.GuildIncarnation;
                #endif
            }
            catch (InternalPlayerSessionSubscribeRefused playerRefused)
            {
                foreach (AssociatedEntityRefBase association in playerRefused.AssociatedEntities)
                    pendingAssociations.Add(new PendingAssociationTarget(association, parentSlot: ClientSlotCore.Player));

                #if !METAPLAY_DISABLE_GUILDS
                guildIncarnation = playerRefused.GuildIncarnation;
                #endif

                if (playerRefused.Result == InternalPlayerSessionSubscribeRefused.ResultCode.TryAgain)
                {
                    goto subscribeAgain;
                }
                else if (playerRefused.Result == InternalPlayerSessionSubscribeRefused.ResultCode.ResourceCorrectionRequired)
                {
                    resourceCorrection = SessionProtocol.SessionResourceCorrection.Combine(resourceCorrection, playerRefused.ResourceCorrection);
                }
                else if (playerRefused.Result == InternalPlayerSessionSubscribeRefused.ResultCode.DryRunSuccess)
                {
                    // nothing done yet.
                }
                else if (playerRefused.Result == InternalPlayerSessionSubscribeRefused.ResultCode.Banned)
                {
                    // player is banned, refuse session immediately
                    throw InternalSessionStartNewRefusal.ForPlayerIsBanned();
                }
                else
                    throw new InvalidOperationException("unreachable");

                playerResponse = null;
            }

            await using EntitySubscriptionSetGuard      associatedEntityGuards          = new EntitySubscriptionSetGuard(this);
            List<PendingAssociatedEntitySubscription>   associatedEntitySubscriptions   = new List<PendingAssociatedEntitySubscription>();

            for (int associatedEntityNdx = 0; associatedEntityNdx < pendingAssociations.Count; ++associatedEntityNdx)
            {
                PendingAssociationTarget    associationTarget   = pendingAssociations[associatedEntityNdx];
                AssociatedEntityRefBase     association         = associationTarget.Association;
                _log.Debug("Subscribing into {EntityId} during init. Target slot: {TargetSlot}, parent slot {ParentSlot}", association.AssociatedEntity, association.GetClientSlot(), associationTarget.ParentSlot);

                try
                {
                    // \todo: slot incarnation numbers for all slots. (incs on slot change. Allows refusing stale requests to previous slot contents).
                    int slotIncarnation = 0;
                    #if !METAPLAY_DISABLE_GUILDS
                    if (association.GetClientSlot() == ClientSlotCore.Guild)
                        slotIncarnation = guildIncarnation;
                    #endif

                    // Get association parent subscription. This will be null if the parent is null (the entity is a root) or
                    // if the parent entity subscription never succeeded (for example, resource correction).
                    EntitySubscription parentSubscription = slotToSubscription.GetValueOrDefault(associationTarget.ParentSlot);

                    PendingAssociatedEntitySubscription subscription = await BeginSubscribeToAssociatedEntityAsync(
                        association:                    association,
                        clientChannelId:                NextEntityChannelId(),
                        resourceProposal:               startNewRequest.SessionResourceProposal,
                        isDryRun:                       startNewRequest.IsDryRun || resourceCorrection.HasAnyCorrection(),
                        supportedArchiveCompressions:   startNewRequest.SupportedArchiveCompressions,
                        slotIncarnation:                slotIncarnation,
                        parentSubscription:             parentSubscription);

                    slotToSubscription.Add(association.GetClientSlot(), subscription.Actor);
                    associatedEntityGuards.Subscriptions.Add(subscription.Actor);
                    associatedEntitySubscriptions.Add(subscription);

                    foreach (AssociatedEntityRefBase subAssociation in subscription.AssociatedEntities)
                        pendingAssociations.Add(new PendingAssociationTarget(subAssociation, parentSlot: association.GetClientSlot()));
                }
                catch (InternalEntitySubscribeRefusedBase.Builtins.ResourceCorrection refused)
                {
                    foreach (AssociatedEntityRefBase subAssociation in refused.AssociatedEntities)
                        pendingAssociations.Add(new PendingAssociationTarget(subAssociation, parentSlot: association.GetClientSlot()));

                    // Combine missing resources
                    resourceCorrection = SessionProtocol.SessionResourceCorrection.Combine(resourceCorrection, refused.ResourceCorrection);
                }
                catch (InternalEntitySubscribeRefusedBase.Builtins.DryRunSuccess refused)
                {
                    foreach (AssociatedEntityRefBase subAssociation in refused.AssociatedEntities)
                        pendingAssociations.Add(new PendingAssociationTarget(subAssociation, parentSlot: association.GetClientSlot()));
                }
                catch (InternalEntitySubscribeRefusedBase.Builtins.TryAgain)
                {
                    goto subscribeAgain;
                }
                catch (InternalEntitySubscribeRefusedBase refusal)
                {
                    // Source entity is not a participant. Clear state in source and try again.
                    _log.Warning("Associated entity refused join from participant during session init. Informing source entity and trying again. Source={Source} Associated={Associated}.", association.SourceEntity, association.AssociatedEntity);
                    await EntityAskAsync<EntityAskOk>(association.SourceEntity, new InternalEntityAssociatedEntityRefusedRequest(association, refusal));

                    goto subscribeAgain;
                }
            }

            // Did we have any missing resources?
            if (resourceCorrection.HasAnyCorrection())
                throw InternalSessionStartNewRefusal.ForResourceCorrection(resourceCorrection);

            // Dry run ends here, just before session would be started
            if (startNewRequest.IsDryRun)
                throw InternalSessionStartNewRefusal.ForDryRunSuccess();

            // Create new session state
            _phase                              = ActorPhase.InSession;
            _client                             = client;
            _playerActor                        = playerActorGuard.StealOwned();
            _sessionParticipant                 = new SessionParticipantState(newSessionToken);
            _gameState                          = GameCreateStateForNewSession(sessionStart);
            _statistics                         = new SessionStatistics();
            _statistics.SessionStartedAt        = DateTime.UtcNow;
            _lastAppLaunchId                    = startNewRequest.AppLaunchId;
            _lastClientSessionNonce             = startNewRequest.ClientSessionNonce;
            _lastClientSessionConnectionNdx     = startNewRequest.ClientSessionConnectionNdx;
            _authKey                            = startNewRequest.AuthKey;
            _logicVersion                       = startNewRequest.LogicVersion;
            _supportedArchiveCompressions       = startNewRequest.SupportedArchiveCompressions;
            _clientPlatform                     = startNewRequest.ClientPlatform;

            #if !METAPLAY_DISABLE_GUILDS
            _guildIncarnation                   = guildIncarnation;
            #endif

            associatedEntityGuards.Subscriptions.Clear();

            // Put Player, i.e a root object, on some channel.
            // \note: player is not a entitychannel-aware so this is just used for tracking Entity tree
            {
                EntityChannelManager playerChannelManager = new EntityChannelManager(_log, _playerActor, NextEntityChannelId(), ClientSlotCore.Player, isActiveOnClient: true, parentManager: null);
                _multiplayerEntities.Add(playerChannelManager.ChannelId, playerChannelManager);
            }

            List<EntityInitialState> entityInitialStates = new List<EntityInitialState>();
            foreach (PendingAssociatedEntitySubscription subscription in associatedEntitySubscriptions)
            {
                entityInitialStates.Add(subscription.State);

                EntityChannelManager channelManager = CompleteSubscribeToAssociatedEntity(subscription, isActiveOnClient: true);
                _multiplayerEntities.Add(channelManager.ChannelId, channelManager);
            }

            _log.Debug("New session started, token {SessionToken}", newSessionToken);

            // Common parts
            _statistics.OnConnectionStarted(_client.EntityId, startNewRequest.DebugDiagnostics);
            _numConcurrentsTracker = StatsCollectorProxy.NumConcurrentsCounter.Add(PlayerId);
            NotifySessionEntitiesClientAppStatusChanged();

            // Inform client of any messages that were generated during this method.
            SendUnacknowledgedSessionPayloadMessages();

            // Get auxiliary stuff
            TryUpdateScheduledMaintenanceMode(out ScheduledMaintenanceMode scheduledMaintenanceMode);
            MetaSessionAuxiliaryInfo metaAuxiliaryInfo = new MetaSessionAuxiliaryInfo(
                scheduledMaintenanceMode:       ScheduledMaintenanceMode.GetEffectiveModeForClient(scheduledMaintenanceMode, _clientPlatform),
                playerInitialState:             playerResponse.PlayerState,
                entityInitialStates:            entityInitialStates,
                localizationVersions:           playerResponse.LocalizationVersions,
                activeExperiments:              playerResponse.ActiveExperiments,
                correctedDeviceGuid:            playerResponse.CorrectedDeviceGuid
                );

            ISessionStartSuccessGamePayload gameSessionStartPayload = await GameGetSessionStartPayload(sessionStart);

            return new InternalSessionStartNewResponse(
                metaAuxiliaryInfo,
                gameSessionStartPayload,
                _sessionParticipant.Token);
        }
        }

        protected DefaultPlayerSessionParams CreateDefaultPlayerSessionParams(SessionStartParams sessionStart, SessionToken sessionToken)
        {
            return new DefaultPlayerSessionParams(
                sessionId:                      _entityId,
                sessionToken:                   sessionToken,
                deviceGuid:                     sessionStart.Meta.DeviceGuid,
                deviceModel:                    sessionStart.Meta.DeviceModel,
                logicVersion:                   sessionStart.Meta.LogicVersion,
                timeZoneInfo:                   sessionStart.Meta.PlayerTimeZoneInfo,
                location:                       sessionStart.Meta.PlayerLocation,
                clientVersion:                  sessionStart.Meta.ClientVersion,
                sessionResourceProposal:        sessionStart.Meta.SessionResourceProposal,
                isDryRun:                       sessionStart.Meta.IsDryRun,
                supportedArchiveCompressions:   sessionStart.Meta.SupportedArchiveCompressions,
                clientPlatform:                 sessionStart.Meta.ClientPlatform,
                authKey:                        sessionStart.Meta.AuthKey,
                sessionStartPayload:            sessionStart.Meta.SessionGamePayload);
        }

        InternalSessionResumeResponse ResumeSession(EntitySubscriber client, InternalSessionResumeRequest resumeRequest)
        {
            LogConnectionStartDiagnostics(client, resumeRequest.DebugDiagnostics);

            // Check for stale connection
            if (IsStaleConnection(resumeRequest.AppLaunchId, resumeRequest.ClientSessionNonce, resumeRequest.ClientSessionConnectionNdx))
                throw InternalSessionResumeRefusal.ForStaleConnection();

            // Kick current client, if any.
            if (_client != null)
                KickCurrentClient(new SessionForceTerminateMessage(reason: new SessionForceTerminateReason.ReceivedAnotherConnection()));

            // Check resumption requirements

            MetaDebug.Assert(_client == null, "Old client must be kicked before resuming session");
            c_sessionResume.Inc();

            SessionResumptionInfo clientSessionToResume = resumeRequest.SessionToResume;

            if (_phase != ActorPhase.InSession)
            {
                _log.Debug("Refused session resume: No ongoing session to resume");
                throw InternalSessionResumeRefusal.ForResumeFailure();
            }
            if (_logicVersion != resumeRequest.LogicVersion)
            {
                _log.Debug("Refused session resume: LogicVersion mismatch (session: {SessionLogicVersion}, request: {RequestLogicVersion})", _logicVersion, resumeRequest.LogicVersion);
                throw InternalSessionResumeRefusal.ForResumeFailure();
            }

            {
                SessionResumeFailure failure = GameCheckSessionResumeRequirements(resumeRequest);
                if (failure != null)
                {
                    _log.Debug("Refused session resume: GameCheckSessionResumeRequirements refused.");
                    throw InternalSessionResumeRefusal.ForResumeFailure();
                }
            }

            SessionUtil.ResumeResult resumeResult = SessionUtil.HandleResume(_sessionParticipant, clientSessionToResume);
            switch (resumeResult)
            {
                case SessionUtil.ResumeResult.Success resumeSuccess:
                    _log.Debug("Resumed session: {Success}", PrettyPrint.Compact(resumeSuccess));
                    break;

                case SessionUtil.ResumeResult.Failure resumeFailure:
                    c_sessionResumeFail.WithLabels(resumeFailure.GetType().Name).Inc();

                    switch (resumeFailure)
                    {
                        // \note Acknowledgement should never fail, assuming a legitimate client.
                        case SessionUtil.ResumeResult.Failure.ValidateAckFailure validateAckFailure:
                            throw new InvalidOperationException($"Invalid session acknowledgement on resume: {PrettyPrint.Compact(validateAckFailure.Value)}");

                        // \note Other failures can happen even with a legitimate client.
                        default:
                            _log.Debug("Cannot resume session: {Failure}", PrettyPrint.Compact(resumeFailure));
                            throw InternalSessionResumeRefusal.ForResumeFailure();
                    }

                default:
                    throw new MetaAssertException($"Unknown {nameof(SessionUtil.ResumeResult)}: {resumeResult}");
            }

            // Resume is successful, set up state.

            // Metrics for how long client was disconnected until resume
            if (_clientLostAt.HasValue)
                c_sessionDisconnectedTime.Observe((DateTime.UtcNow - _clientLostAt.Value).TotalSeconds);
            else
                _log.Warning("ClientLostAt was not set even though resumption was successful");

            // Remember new client
            _client = client;

            // Common parts
            _statistics.OnConnectionStarted(_client.EntityId, resumeRequest.DebugDiagnostics);
            _numConcurrentsTracker = StatsCollectorProxy.NumConcurrentsCounter.Add(PlayerId);
            NotifySessionEntitiesClientAppStatusChanged();

            // Inform client of any messages that were received while it was offline. (And any new messages generated during this method).
            SendUnacknowledgedSessionPayloadMessages();

            // Get auxiliary stuff
            TryUpdateScheduledMaintenanceMode(out ScheduledMaintenanceMode scheduledMaintenanceMode);

            return new InternalSessionResumeResponse(
                scheduledMaintenanceMode:   ScheduledMaintenanceMode.GetEffectiveModeForClient(scheduledMaintenanceMode, _clientPlatform),
                token:                      _sessionParticipant.Token,
                serverAcknowledgement:      SessionAcknowledgement.FromParticipantState(_sessionParticipant));
        }

        void SendUnacknowledgedSessionPayloadMessages()
        {
            if (_sessionParticipant.RememberedSent.Count > 0)
                _log.Debug("Sending {NumMessages} unacknowledged payload messages:", _sessionParticipant.RememberedSent.Count);

            foreach (MetaMessage payloadMessage in _sessionParticipant.RememberedSent)
            {
                _log.Verbose("Sending {MessageType}", payloadMessage.GetType().Name);
                SendMessage(_client, payloadMessage);
            }
        }

        Task HandlePlayerActorLost()
        {
            _log.Warning("Lost subscription to player actor");
            return TerminateSession(new SessionForceTerminateMessage(reason: new SessionForceTerminateReason.InternalServerError()));
        }

        Task HandlePlayerActorKicked(MetaMessage message)
        {
            _log.Warning("Kicked from subscription to player actor: {Message}", PrettyPrint.Compact(message));

            SessionForceTerminateReason terminateReason;
            if (message is PlayerForceKickOwner playerForceKickOwner)
                terminateReason = TryConvertPlayerForceKickOwnerReason(playerForceKickOwner.Reason);
            else
                terminateReason = new SessionForceTerminateReason.Unknown();

            return TerminateSession(new SessionForceTerminateMessage(reason: terminateReason));
        }

        SessionForceTerminateReason TryConvertPlayerForceKickOwnerReason(PlayerForceKickOwnerReason reason)
        {
            switch (reason)
            {
                case PlayerForceKickOwnerReason.ReceivedAnotherOwnerSubscriber: return new SessionForceTerminateReason.InternalServerError(); // \note Error, because this is not supposed to happen.
                case PlayerForceKickOwnerReason.AdminAction:                    return new SessionForceTerminateReason.KickedByAdminAction();
                case PlayerForceKickOwnerReason.ClientTimeTooFarBehind:         return new SessionForceTerminateReason.ClientTimeTooFarBehind();
                case PlayerForceKickOwnerReason.ClientTimeTooFarAhead:          return new SessionForceTerminateReason.ClientTimeTooFarAhead();
                case PlayerForceKickOwnerReason.InternalError:                  return new SessionForceTerminateReason.InternalServerError();
                case PlayerForceKickOwnerReason.PlayerBanned:                   return new SessionForceTerminateReason.PlayerBanned();
                default: return new SessionForceTerminateReason.Unknown();
            }
        }

        async Task HandleMessageFromClient(MetaMessage message)
        {
            _statistics.LastMessageReceivedAt = DateTime.UtcNow;

            SessionUtil.ReceiveResult result = SessionUtil.HandleReceive(_sessionParticipant, message);
            switch (result.Type)
            {
                case SessionUtil.ReceiveResult.ResultType.ReceivedValidAck:
                    _log.Verbose("Handled acknowledgement from client (length {NumCovered}, end {EndIndex})", result.ValidAck.OurNumToNewlyForget, result.ValidAck.TheirNumReceived);
                    break;

                case SessionUtil.ReceiveResult.ResultType.ReceivedFaultyAck:
                    // \note Acknowledgement should never fail, assuming a legitimate client.
                    throw new InvalidOperationException($"Invalid session acknowledgement on ack receive: {PrettyPrint.Compact(result.FaultyAck)}");

                case SessionUtil.ReceiveResult.ResultType.ReceivedPayloadMessage:
                    _statistics.OnReceivedSessionPayloadMessage(message);
                    await OnPayloadMessageReceivedFromClient(result.PayloadMessage, message);
                    break;

                default:
                    throw new MetaAssertException($"Unknown {nameof(SessionUtil.ReceiveResult)}: {result}");
            }
        }

        async Task OnPayloadMessageReceivedFromClient(SessionUtil.ReceivePayloadMessageResult result, MetaMessage payloadMessage)
        {
            if (_log.IsVerboseEnabled)
                _log.Verbose("Incoming payload message (type {MessageType}, index {MessageIndex})", payloadMessage.GetType().Name, result.PayloadMessageIndex);

            if (result.ShouldSendAcknowledgement)
            {
                _log.Verbose("Sending acknowledgement for {ServerNumReceived} received payload messages", _sessionParticipant.NumReceived);
                PublishMessage(EntityTopic.Owner, new SessionAcknowledgementMessage(SessionAcknowledgement.FromParticipantState(_sessionParticipant)));
            }

            // \note HandleIncomingPayloadMessage should be the last thing done here, as it might terminate the session.
            //       Other stuff in this function may require session to exists.

            await HandleIncomingPayloadMessage(payloadMessage);
        }

        void HandleClientLost()
        {
            _log.Debug("Lost client {ClientEntityId}", _client.EntityId);
            UnsetClient();
        }

        void KickCurrentClient(MetaMessage kickMessage)
        {
            _log.Debug("Kicking client {ClientEntityId} of current session with token {SessionToken} ({Message})", _client.EntityId, _sessionParticipant.Token, PrettyPrint.Compact(kickMessage));
            KickSubscriber(_client, kickMessage);
            UnsetClient();
        }

        void UnsetClient()
        {
            _statistics.OnConnectionEnded(_client.EntityId);
            _client = null;
            _clientLostAt = DateTime.UtcNow;
            _numConcurrentsTracker.Remove();

            NotifySessionEntitiesClientAppStatusChanged();
        }

        async Task HandleIncomingPayloadMessage(MetaMessage message)
        {
            // \note Message direction was already checked in ClientConnection.
            //       Routing rule is checked here.

            MetaMessageSpec     messageSpec = MetaMessageRepository.Instance.GetFromType(message.GetType());
            MessageRoutingRule  routingRule = messageSpec.RoutingRule;

            switch (routingRule)
            {
                case MessageRoutingRuleSession _:
                    await HandleIncomingPayloadMessageForSessionTarget(message);
                    break;

                case MessageRoutingRuleOwnedPlayer _:
                    SendMessage(_playerActor, message);
                    break;

                #if !METAPLAY_DISABLE_GUILDS
                case MessageRoutingRuleCurrentGuild _:
                    // If we get kicked (or protocol violation by client), we might get guild messages even though we are not in a guild.
                    EntityChannelManager guildChannel = TryGetChannelForClientSlot(ClientSlotCore.Guild);
                    if (guildChannel == null)
                        _log.Warning("Got stale incoming guild payload message of type {MessageType}. Session is not in a guild. Ignored", message.GetType().Name);
                    else
                        SendMessage(guildChannel.Actor, message);
                    break;
                #endif

                default:
                    if (!await GameTryHandleIncomingPayloadMessage(routingRule, message))
                        _log.Warning("Unhandled incoming session payload message of type {MessageType} with routing rule {RoutingRule}", message.GetType().Name, PrettyPrint.Compact(routingRule));
                    break;
            }
        }

        async Task HandleIncomingPayloadMessageForSessionTarget(MetaMessage message)
        {
            switch (message)
            {
                case SessionPing ping:
                    _statistics.LastSessionPingReceivedAt = DateTime.UtcNow;
                    SendOutgoingPayloadMessage(new SessionPong(ping.Id));
                    break;

                case SessionMetaRequestMessage request:
                    bool handled = false;
                    switch (request.Payload)
                    {
                        case SocialAuthenticateRequest socialAuthReq:
                            await HandleSocialAuthenticateRequest(socialAuthReq, request.Id);
                            handled = true;
                            break;
                        case DevOverwritePlayerStateRequest overwriteReq:
                            await HandleDevOverwritePlayerStateRequest(overwriteReq, request.Id);
                            handled = true;
                            break;
                        case ImmutableXLoginChallengeRequest imxLoginChallengeReq:
                            HandleImmutableXLoginChallengeRequest(imxLoginChallengeReq, request.Id);
                            handled = true;
                            break;
                    }
                    if (handled)
                        break;
                    goto default;

                case SocialAuthenticateResolveConflict socialAuthResolveConflict:
                    await HandleSocialAuthenticateResolveConflict(socialAuthResolveConflict);
                    break;

                #if !METAPLAY_DISABLE_GUILDS
                case GuildJoinRequest guildJoinRequest:
                    await HandleGuildJoinRequest(guildJoinRequest);
                    break;

                case GuildLeaveRequest guildLeaveRequest:
                    await HandleGuildLeaveRequest(guildLeaveRequest);
                    break;

                case GuildEnqueueActionsRequest guildEnqueueActionsRequest:
                    HandleGuildEnqueueActionsRequest(guildEnqueueActionsRequest);
                    break;

                case GuildDiscoveryRequest guildDiscovery:
                    await HandleGuildDiscoveryRequest(guildDiscovery);
                    break;

                case GuildSearchRequest guildSearch:
                    await HandleGuildSearchRequest(guildSearch);
                    break;

                case GuildTransactionRequest guildTransaction:
                    await HandleGuildTransactionRequest(guildTransaction);
                    break;

                case GuildBeginViewRequest guildBeginView:
                    await HandleGuildBeginViewRequest(guildBeginView);
                    break;

                case GuildEndViewRequest guildEndView:
                    await HandleGuildEndViewRequest(guildEndView);
                    break;

                case GuildInspectInvitationRequest inspectInvitation:
                    await HandleGuildInspectInvitationRequest(inspectInvitation);
                    break;
                #endif

                case ClientLifecycleHintPausing clientPausing:
                    HandleClientLifecycleHintPausing(clientPausing);
                    break;

                case ClientLifecycleHintUnpausing clientUnpausing:
                    HandleClientLifecycleHintUnpausing(clientUnpausing);
                    break;

                case ClientLifecycleHintUnpaused clientUnpaused:
                    HandleClientLifecycleHintUnpaused(clientUnpaused);
                    break;

                case EntityActivated activated:
                    HandleEntityActivated(activated);
                    break;

                case EntityClientToServerEnvelope envelope:
                    HandleEntityClientToServerEnvelope(envelope);
                    break;

                default:
                    if (!await GameTryHandleIncomingPayloadMessage(MessageRoutingRuleSession.Instance, message))
                        _log.Warning("Unhandled incoming session payload message of type {MessageType} routed to session actor", message.GetType().Name);
                    break;
            }
        }

        protected void SendResponse(int requestId, MetaResponse response)
        {
            SessionMetaResponseMessage message = new SessionMetaResponseMessage()
            {
                RequestId = requestId,
                Payload = response
            };
            SendOutgoingPayloadMessage(message);
        }

        protected void SendOutgoingPayloadMessage(MetaMessage payloadMessage)
        {
            if (_phase == ActorPhase.WaitingForConnection)
            {
                // Trying to send before session is fatal. This is most likely a logic error.
                throw new InvalidOperationException("SendOutgoingPayloadMessage may not be called before session has been established");
            }
            else if (_phase == ActorPhase.Terminating)
            {
                // Trying to send after session is not fatal. This is most harmless async response leaking from just terminating session.
                _log.Warning("Attempted to send session message {MessageType} but session was already terminated. Dropping.", payloadMessage.GetType().Name);
                return;
            }

            if (_client != null)
                _log.Verbose("Sending payload message (type {MessageType}, index {MessageIndex})", payloadMessage.GetType().Name, _sessionParticipant.NumSent);
            else
                _log.Verbose("Buffering payload message (type {MessageType}, index {MessageIndex})", payloadMessage.GetType().Name, _sessionParticipant.NumSent);

            // Carry on with handling this message.

            SessionUtil.HandleSendPayloadMessage(_sessionParticipant, payloadMessage);

            if (_client != null)
                SendMessage(_client, payloadMessage);

            // Queue limiting

            // \todo [nuutti] When disconnected, we could instead terminate the session at the point where we would drop the
            //                first message in the queue that hasn't been actually sent to the client (due to it happening
            //                while disconnected). Resumption cannot succeed in that case.

            int limit       = _client != null
                                ? PayloadMessageQueueCountConnectedLimit
                                : PayloadMessageQueueCountDisconnectedLimit;

            int numDropped  = SessionUtil.LimitRememberedSentQueue(_sessionParticipant, limit);
            if (numDropped > 0)
            {
                c_sessionMessagesDropped.Inc(numDropped);
                if (!_hasDroppedAnyUnacknowledgedMessages)
                {
                    _hasDroppedAnyUnacknowledgedMessages = true;
                    _log.Info("Client has not acknowledged sent messages and the recovery buffer size limit for the session resume has been exceeded. Dropping oldest unacknowlodged messages. Next session resume may fail.");
                }
                else
                {
                    _log.Verbose("Dropped {NumMessagesDropped} messages from queue (using limit {Limit})", numDropped, limit);
                }
            }
        }

        protected async Task TerminateSession(MetaMessage clientKickMessage)
        {
            // If session is terminated for multiple reasons at the same time, ignore subsequent calls.
            if (_phase == ActorPhase.Terminating)
                return;

            // If we never had a session, no need to clean up
            if (_phase == ActorPhase.WaitingForConnection)
            {
                _phase = ActorPhase.Terminating;
                RequestShutdown();
                return;
            }

            // Clean up session

            if (_client != null)
                KickCurrentClient(clientKickMessage);

            foreach (EntityChannelManager channelManager in _multiplayerEntities.Values.ToArray())
                await UnsubscribeFromEntityAsync(channelManager, informClient: false);

            // Unsubscribe from all subscribed-to entities. This is done automatically when
            // the actor dies but it's cleaner to shut down subscriptions eagerly. This way
            // we don't need to worry about Terminating state in PubSub message handlers.
            foreach (EntitySubscription subscription in _subscriptions.Values.ToList()) // \note ToList to create copy, because the collection will be mutated in unsub.
                await UnsubscribeFromAsync(subscription);

            await GameTerminateSession();

            _client = null; // \note: this is actually no-op, due to KickCurrentClient, but kept here for documenting the post-condition
            _playerActor = null;
            _sessionParticipant = null;
            _gameState = default;

            #if !METAPLAY_DISABLE_GUILDS
            _guildIncarnation = 0;
            #endif

            _phase = ActorPhase.Terminating;
            RequestShutdown();
        }

        #endregion

        #region Debug

        void UpdateDebugChecks()
        {
            DateTime currentTime = DateTime.UtcNow;

            // If a session resumption has been done (i.e. NumConnections > 1), and
            // we haven't received a SessionPing from the client, and it's already been
            // a while, then report that as a connection issue incident.

            if (_statistics.NumConnections > 1 // There must have been a session resumption.
             && _statistics.RecentConnectionEvents.Last().Kind == SessionStatistics.ConnectionEvent.EventKind.Start) // Connection must be ongoing.
            {
                LoginDebugDiagnostics diag = _statistics.LoginDebugDiagnostics.Last();

                if (diag != null
                 && diag.ExpectSessionResumptionPing // Client reported that it intends to send a session ping at resumption.
                 && !diag.CurrentPauseDuration.HasValue) // Client wasn't on pause when it sent the login.
                {
                    DateTime                connectionStartedAt             = _statistics.RecentConnectionEvents.Last().Time;
                    DateTime?               pingReceivedAt                  = _statistics.LastSessionPingReceivedAt;
                    bool                    pingReceivedInThisConnection    = pingReceivedAt.HasValue && pingReceivedAt.Value >= connectionStartedAt;
                    int                     connectionIndex                 = _statistics.NumConnections - 1;
                    PlayerIncidentOptions   options                         = RuntimeOptionsRegistry.Instance.GetCurrent<PlayerIncidentOptions>();

                    if (!pingReceivedInThisConnection
                     && currentTime > connectionStartedAt + options.SessionPingDurationIncidentThreshold // Has been long enough without ping since connection start. \note Doesn't include estimated roundtrip
                     && connectionIndex != _statistics.LastSessionPingIncidentReportConnectionIndex // Only report once per transport connection.
                     && _statistics.NumSessionPingIncidentsReported < options.MaxSessionPingDurationIncidentsPerSession) // Cap number of reports per session.
                    {
                        ReportSessionPingDurationThresholdExceeded(connectionIndex, timeSinceConnectionStart: MetaDuration.FromTimeSpan(currentTime - connectionStartedAt));
                        _statistics.LastSessionPingIncidentReportConnectionIndex = connectionIndex;
                        _statistics.NumSessionPingIncidentsReported++;
                    }
                }
            }

            // If we haven't received client communication in a long time,
            // yet we have an existing subscriber connection,
            // log a warning (unless already logged recently, or client is paused) with a bunch of info.

            DateTime clientCommunicationReceivedAt  = _statistics.LastMessageReceivedAt ?? _statistics.SessionStartedAt;

            if (currentTime >= clientCommunicationReceivedAt + TimeSpan.FromSeconds(80)
             && _client != null
             && _clientPauseStatus == ClientAppPauseStatus.Running)
            {
                DateTime? warningLoggedAt = _statistics.ClientNotCommunicatingWarningLoggedAt;
                if (!warningLoggedAt.HasValue || currentTime >= warningLoggedAt.Value + TimeSpan.FromSeconds(80))
                {
                    _statistics.ClientNotCommunicatingWarningLoggedAt = currentTime;

                    SessionParticipantState sp = _sessionParticipant;

                    _log.Info("No session communication has been received from client in {TimeSinceCommunicationReceived}. " +
                        "Time since payload message: {TimeSincePayloadMessage}. " +
                        "Session: {Token}, StartedAt={SessionStartedAt}, Sent={NumSent}, Remembered={NumRememberedSent}, AcknowledgedSent={NumAcknowledgedSent}, Received={NumReceived}, AcknowledgedReceived={AcknowledgedNumReceived}. " +
                        "{NumConnectionEvents} connection events, recent: [ {ConnectionEvents} ], " +
                        "Early session message types: [ {EarlySessionMessageTypes} ]",
                        currentTime - clientCommunicationReceivedAt,
                        currentTime - _statistics.LastPayloadMessageReceivedAt,
                        sp.Token, _statistics.SessionStartedAt.ToString("s", CultureInfo.InvariantCulture)+"Z", sp.NumSent, sp.RememberedSent.Count, sp.NumAcknowledgedSent, sp.NumReceived, sp.AcknowledgedNumReceived,
                        _statistics.NumConnectionEvents, string.Join(", ", _statistics.RecentConnectionEvents),
                        string.Join(", ", _statistics.EarlySessionMessageTypes.Select(t => t.Name)));

                    _log.Info("{NumLoginDebugDiagnostics} login debug diagnostics:", _statistics.LoginDebugDiagnostics.Count);
                    foreach (LoginDebugDiagnostics loginDebugDiagnostics in _statistics.LoginDebugDiagnostics)
                        _log.Info("  Login debug diagnostics: {LoginDebugDiagnostics}", PrettyPrint.Compact(loginDebugDiagnostics));
                }
            }
        }

        void ReportSessionPingDurationThresholdExceeded(int connectionIndex, MetaDuration timeSinceConnectionStart)
        {
            SessionToken sessionToken = _sessionParticipant.Token;

            MetaTime occurredAt = MetaTime.Now;

            PlayerIncidentReport incident = new PlayerIncidentReport.SessionCommunicationHanged(
                id:                         PlayerIncidentUtil.EncodeIncidentId(occurredAt, RandomPCG.CreateNew().NextULong()),
                occurredAt:                 occurredAt,
                logEntries:                 null,
                systemInfo:                 null,
                platformInfo:               null,
                gameConfigInfo:             null,
                applicationInfo:            null,
                issueType:                  "Metaplay.ServerSideSessionPingDurationThresholdExceeded",
                issueInfo:                  Invariant($"Metaplay.ServerSideSessionPingDurationThresholdExceeded(pingId: {connectionIndex})"),
                debugDiagnostics:           _statistics.LoginDebugDiagnostics.Last(),
                roundtripEstimate:          MetaDuration.Zero,
                serverGateway:              null,
                networkReachability:        null,
                tlsPeerDescription:         null,
                sessionToken:               sessionToken,
                pingId:                     connectionIndex,
                elapsedSinceCommunication:  timeSinceConnectionStart);

            _log.Info("Expected SessionPing after session resumption but did not receive it in a while, reporting incident {IncidentId}", incident.IncidentId);

            byte[] serialized = MetaSerialization.SerializeTagged(incident, MetaSerializationFlags.IncludeAll, logicVersion: null);
            byte[] compressed = CompressUtil.DeflateCompress(serialized);

            SendMessage(_playerActor, new PlayerUploadIncidentReport(incident.IncidentId, compressed));
        }

        #endregion

        #region Auxiliary info

        /// <summary>
        /// Try to update _activeScheduledMaintenanceModeSubscriber with the latest value from the server
        /// </summary>
        /// <param name="scheduledMaintenanceMode">Returns the current value as a ScheduledMaintenancMode, null if no value currently (after it is updated)</param>
        /// <returns>True if the value was updated</returns>
        bool TryUpdateScheduledMaintenanceMode(out ScheduledMaintenanceMode scheduledMaintenanceMode)
        {
            bool wasUpdated = false;
            _activeScheduledMaintenanceModeSubscriber.Update((ActiveScheduledMaintenanceMode newState, ActiveScheduledMaintenanceMode prevState) =>
            {
                wasUpdated = true;
            });
            scheduledMaintenanceMode = _activeScheduledMaintenanceModeSubscriber.Current.Mode;
            return wasUpdated;
        }

        #endregion

        #region Social authentication

        Task HandleSocialAuthenticateRequest(SocialAuthenticateRequest authReq, int requestId)
        {
            SocialAuthenticationClaimBase claim = authReq.Claim;

            // Limit num parallel social auths to 10
            if (_pendingSocialAuths.Count >= 10)
            {
                _log.Warning("Too many concurrent social authentication request during a session. Rejected {Claim}", PrettyPrint.Compact(claim));

                // \note: we send debugOnlyErrorMessage always as it does not contain any sensitive information
                MetaSerialized<IPlayerModelBase> noConflictingPlayer = MetaSerialization.ToMetaSerialized<IPlayerModelBase>(value: null, MetaSerializationFlags.SendOverNetwork, logicVersion: _logicVersion);
                SendResponse(requestId, new SocialAuthenticateResult(claim.Platform, SocialAuthenticateResult.ResultCode.TemporarilyUnavailable, conflictingPlayerId: EntityId.None, conflictingPlayer: noConflictingPlayer, conflictResolutionId: 0, debugOnlyErrorMessage: "too many concurrent social auth requests"));
                return Task.CompletedTask;
            }

            int socialAuthId = _socialAuthRunningId++;
            _log.Info("Authenticating social platform claim: {Claim} (serial={AuthenticationId})", PrettyPrint.Compact(claim), socialAuthId);
            _pendingSocialAuths.Add(socialAuthId, new PendingSocialAuthRequest(claim));

            ContinueTaskOnActorContext(
                Authenticator.AuthenticateSocialAccountAsync(_log, asker: this, PlayerId, claim),
                async (SocialAuthenticationResult authResult) =>
                {
                    // If session was lost while background task was executing, do nothing.
                    if (_phase != ActorPhase.InSession)
                        return;

                    EntityId existingPlayerId = authResult.ExistingPlayerId;

                    // Mark as resolved
                    _log.Info("Social authentication succeeded for {Platform}: existingPlayerId={ExistingPlayerId} (self={PlayerId}) (serial={AuthenticationId})", claim.Platform, existingPlayerId, PlayerId, socialAuthId);

                    // Store authentication key in pending claim
                    _pendingSocialAuths[socialAuthId].SetAuthenticationKeys(authResult.AuthKeys);

                    // If social auth already mapped to a different playerId, mark it as conflicting (and wait for client to resolve)
                    if (existingPlayerId.IsValid && existingPlayerId != PlayerId)
                    {
                        // Fetch the state of the existing player
                        IPlayerModelBase otherState;
                        try
                        {
                            InternalEntityStateResponse response                = await EntityAskAsync<InternalEntityStateResponse>(existingPlayerId, InternalEntityStateRequest.Instance);
                            FullGameConfig              otherSpecializedConfig  = await ServerGameConfigProvider.Instance.GetSpecializedGameConfigAsync(response.StaticGameConfigId, response.DynamicGameConfigId, response.SpecializationKey);
                            IGameConfigDataResolver     otherResolver           = otherSpecializedConfig.SharedConfig;

                            otherState = (IPlayerModelBase)response.Model.Deserialize(resolver: otherResolver, logicVersion: response.LogicVersion);
                        }
                        catch (Exception ex)
                        {
                            _log.Error("Failed to get state of other player ({ExistingPlayerId}) for social conflict: {Error}", existingPlayerId, ex);
                            otherState = null;
                        }

                        // Set pending auth as conflicting
                        _pendingSocialAuths[socialAuthId].SetConflictingPlayerId(existingPlayerId);

                        // Inform client of pending conflict
                        MetaSerialized<IPlayerModelBase> serializedPlayerInfo = MetaSerialization.ToMetaSerialized(otherState, MetaSerializationFlags.SendOverNetwork, _logicVersion);
                        SendResponse(requestId, new SocialAuthenticateResult(claim.Platform, SocialAuthenticateResult.ResultCode.Success, existingPlayerId, serializedPlayerInfo, conflictResolutionId: socialAuthId, debugOnlyErrorMessage: null));
                    }
                    else
                    {
                        // Resolve pending social authentication
                        _pendingSocialAuths.Remove(socialAuthId);

                        // If new social account, write to auth table
                        if (existingPlayerId == EntityId.None)
                            await Authenticator.StoreSocialAuthenticationEntryAsync(authResult.AuthKeys, PlayerId);

                        // Store social auth in player model
                        // \note: Specifically tolerate (existingPlayerId == PlayerId), i.e. double attach. In that case, we still run the
                        //        player update but ignore the already set auth table. This will update stale data in player model.
                        await EntityAskAsync<EntityAskOk>(PlayerId, new InternalPlayerAddNewSocialAccountRequest(authResult.AuthKeys));

                        // Inform client of success
                        MetaSerialized<IPlayerModelBase> noConflictingPlayer = MetaSerialization.ToMetaSerialized<IPlayerModelBase>(value: null, MetaSerializationFlags.SendOverNetwork, logicVersion: _logicVersion);
                        SendResponse(requestId, new SocialAuthenticateResult(claim.Platform, SocialAuthenticateResult.ResultCode.Success, conflictingPlayerId: EntityId.None, conflictingPlayer: noConflictingPlayer, conflictResolutionId: 0, debugOnlyErrorMessage: null));
                    }
                },
                ex =>
                {
                    _pendingSocialAuths.Remove(socialAuthId);
                    _log.Error("Social authentication of {Platform} failed (serial={AuthenticationId}): {Exception}", claim.Platform, socialAuthId, ex);

                    SocialAuthenticateResult.ResultCode errorCode;
                    if (ex is AuthenticationTemporarilyUnavailable)
                        errorCode = SocialAuthenticateResult.ResultCode.TemporarilyUnavailable;
                    else
                        errorCode = SocialAuthenticateResult.ResultCode.AuthError;

                    // For developers, send the raw error back
                    string debugOnlyErrorMessage = null;
                    if (RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>().EnableDevelopmentFeatures || GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(PlayerId))
                        debugOnlyErrorMessage = Util.ShortenStringToUtf8ByteLength(ex.ToString(), maxNumBytes: 128 * 1024); // limit to 128KB

                    // Inform client of failure
                    MetaSerialized<IPlayerModelBase> noConflictingPlayer = MetaSerialization.ToMetaSerialized<IPlayerModelBase>(value: null, MetaSerializationFlags.SendOverNetwork, logicVersion: _logicVersion);
                    SendResponse(requestId, new SocialAuthenticateResult(claim.Platform, errorCode, conflictingPlayerId: EntityId.None, conflictingPlayer: noConflictingPlayer, conflictResolutionId: 0, debugOnlyErrorMessage));
                });

            return Task.CompletedTask;
        }

        async Task HandleSocialAuthenticateResolveConflict(SocialAuthenticateResolveConflict resolveConflict)
        {
            _log.Info("Resolving social authenticate profile conflict id {ConflictResolutionId}, useOther={UseOther}", resolveConflict.ConflictResolutionId, resolveConflict.UseOther);

            // Fetch & remove pending social auth
            if (_pendingSocialAuths.Remove(resolveConflict.ConflictResolutionId, out PendingSocialAuthRequest pendingAuth))
            {
                // Check that pending auth conflict has conflicting player
                if (pendingAuth.Status != PendingSocialAuthRequestStatus.ConflictingProfile || !pendingAuth.ExistingPlayerId.IsOfKind(EntityKindCore.Player))
                    throw new InvalidOperationException($"Trying to resolve social authentication conflict in invalid state: status={pendingAuth.Status}, existingPlayerId={pendingAuth.ExistingPlayerId}");

                // Resolve the conflict
                if (resolveConflict.UseOther)
                {
                    // Must be authenticated via DeviceId
                    if (_authKey.Platform != AuthenticationPlatform.DeviceId)
                        throw new InvalidOperationException($"Trying to resolve social conflict by remapping DeviceId from {PlayerId} to {pendingAuth.ExistingPlayerId} when not logged in via DeviceId");

                    // Switch this deviceId to point to the other player
                    _log.Info("Re-attaching own deviceId {DeviceId} to {ExistingPlayerId} (from current {PlayerId})", _authKey.Id, pendingAuth.ExistingPlayerId, PlayerId);
                    await Authenticator.UpdateDeviceToPlayerMappingAsync(_authKey.Id, pendingAuth.ExistingPlayerId);

                    // Re-attach current deviceId from current Player to conflicting Player
                    AuthenticationKey deviceAuthKey = _authKey;

                    InternalPlayerResolveDeviceIdAuthConflictResponse removalResponse = await EntityAskAsync<InternalPlayerResolveDeviceIdAuthConflictResponse>(PlayerId, InternalPlayerResolveDeviceIdAuthConflictRequest.ForDeviceMigrationSource(deviceAuthKey, pendingAuth.AuthKeys, pendingAuth.ExistingPlayerId));
                    _ = await EntityAskAsync<InternalPlayerResolveDeviceIdAuthConflictResponse>(pendingAuth.ExistingPlayerId, InternalPlayerResolveDeviceIdAuthConflictRequest.ForDeviceMigrationDestination(deviceAuthKey, removalResponse.RemovedAuthEntryOrNull, pendingAuth.AuthKeys, PlayerId));

                    // Request client to re-reconnect (to get player state of the switched-to player),
                    // and end session.
                    _log.Debug("Terminating session due to social auth resolve");
                    await TerminateSession(new SocialAuthenticateForceReconnect(pendingAuth.ExistingPlayerId));
                }
                else
                {
                    // Attach pending social auth to this player
                    _log.Info("Associating pending auth {SocialAuthKey} to current player {PlayerId}", pendingAuth.AuthKeys.PrimaryAuthenticationKey, PlayerId);
                    await Authenticator.UpdateSocialAuthenticationAsync(pendingAuth.AuthKeys, PlayerId);

                    // Re-attach social auth from conflicting Player to current Player
                    InternalPlayerResolveSocialAuthConflictResponse removed = await EntityAskAsync<InternalPlayerResolveSocialAuthConflictResponse>(pendingAuth.ExistingPlayerId, InternalPlayerResolveSocialAuthConflictRequest.ForSocialAuthMigrationSource(pendingAuth.AuthKeys, PlayerId));
                    _ = await EntityAskAsync<InternalPlayerResolveSocialAuthConflictResponse>(PlayerId, InternalPlayerResolveSocialAuthConflictRequest.ForSocialAuthMigrationDestination(removed.RemovedSocialKeys, pendingAuth.ExistingPlayerId));
                }
            }
            else
            {
                _log.Warning("Tried to resolve a non-existent social authentication conflict with conflict resolution id {ConflictResolutionId}", resolveConflict.ConflictResolutionId);
            }
        }

        #endregion

        #region Barriers

        int NextPiggingId()
        {
            return _runningPubsubPiggingId++;
        }

        void AddBarrier(MessageBarrier barrier)
        {
            // \note: if state is null, throws. We cannot handle it here
            if (_pendingBarriers == null)
                _pendingBarriers = new List<MessageBarrier>(capacity: 1);
            _pendingBarriers.Add(barrier);
        }

        bool TryGetPlayerBlockingBarrier(out MessageBarrier outBarrier)
        {
            if (_pendingBarriers != null)
            {
                for (int ndx = _pendingBarriers.Count - 1; ndx >= 0; --ndx)
                {
                    // received the Pig, i.e all further messages are AFTER the barrier. Must block here.
                    if (_pendingBarriers[ndx].PlayerPigReceived)
                    {
                        outBarrier = _pendingBarriers[ndx];
                        return true;
                    }
                }
            }

            outBarrier = null;
            return false;
        }

        bool TryGetGuildBlockingBarrier(out MessageBarrier outBarrier)
        {
            if (_pendingBarriers != null)
            {
                for (int ndx = _pendingBarriers.Count - 1; ndx >= 0; --ndx)
                {
                    // received the Pig, i.e all further messages are AFTER the barrier. Must block here.
                    if (_pendingBarriers[ndx].GuildPigReceived)
                    {
                        outBarrier = _pendingBarriers[ndx];
                        return true;
                    }
                }
            }

            outBarrier = null;
            return false;
        }

        [PubSubMessageHandler]
        void HandleInternalPig(EntitySubscription subscription, InternalPig pig)
        {
            if (_pendingBarriers != null)
            {
                for (int triggeringNdx = 0; triggeringNdx < _pendingBarriers.Count; ++triggeringNdx)
                {
                    MessageBarrier barrier = _pendingBarriers[triggeringNdx];
                    if (barrier.PiggingId != pig.PiggingId)
                        continue;

                    if (_playerActor == subscription)
                    {
                        if (barrier.PlayerPigReceived)
                            _log.Warning("Got InternalPig for Player barrier that was already set. PiggingId={PiggingId}", pig.PiggingId);
                        if (!barrier.PlayerPigNeeded)
                            _log.Warning("Got InternalPig for Player barrier that did not expect guild barrier. PiggingId={PiggingId}", pig.PiggingId);
                        barrier.PlayerPigReceived = true;

                        // Implicitly trigger all below (older) barries from this source as well
                        for (int ndx = 0; ndx < triggeringNdx; ++ndx)
                        {
                            MessageBarrier earlierBarrier = _pendingBarriers[ndx];
                            if (earlierBarrier.PlayerPigNeeded && !earlierBarrier.PlayerPigReceived)
                            {
                                earlierBarrier.PlayerPigReceived = true;
                                _log.Warning("Player message barrier triggered out of order. Flushing missed barrier. Source={FromEntityId}, PiggingId={PiggingId}, MissedPiggingId={MissedPiggingId}", subscription.EntityId, pig.PiggingId, earlierBarrier.PiggingId);
                            }
                        }
                    }
                    #if !METAPLAY_DISABLE_GUILDS
                    else if (TryGetChannelForClientSlot(ClientSlotCore.Guild)?.Actor == subscription)
                    {
                        if (barrier.GuildPigReceived)
                            _log.Warning("Got InternalPig for Guild barrier that was already set. PiggingId={PiggingId}", pig.PiggingId);
                        if (!barrier.GuildPigNeeded)
                            _log.Warning("Got InternalPig for Guild barrier that did not expect guild barrier. PiggingId={PiggingId}", pig.PiggingId);
                        barrier.GuildPigReceived = true;

                        // Implicitly trigger all below (older) barries from this source as well
                        for (int ndx = 0; ndx < triggeringNdx; ++ndx)
                        {
                            MessageBarrier earlierBarrier = _pendingBarriers[ndx];
                            if (earlierBarrier.GuildPigNeeded && !earlierBarrier.GuildPigReceived)
                            {
                                earlierBarrier.GuildPigReceived = true;
                                _log.Warning("Guild message barrier triggered out of order. Flushing missed barrier. Source={FromEntityId}, PiggingId={PiggingId}, MissedPiggingId={MissedPiggingId}", subscription.EntityId, pig.PiggingId, earlierBarrier.PiggingId);
                            }
                        }
                    }
                    #endif
                    else
                    {
                        _log.Warning("Got InternalPig for barrier from unexpected source. Source={FromEntityId}, PiggingId={PiggingId}", subscription.EntityId, pig.PiggingId);
                        return;
                    }

                    // barrier release
                    if (barrier.IsSatisfied())
                        OnBarrierSatisfied(triggeringNdx);

                    return;
                }
            }

            _log.Warning("Got InternalPig unexpectedly from {FromEntityId}, PiggingId={PiggingId}", subscription.EntityId, pig.PiggingId);
        }

        /// <summary>
        /// Completes all guild-side barriers. Barriers may still wait for player-side messages.
        /// </summary>
        void FlushGuildMessageBarriers()
        {
            if (_pendingBarriers == null)
                return;

            // trigger all guilds
            foreach (MessageBarrier barrier in _pendingBarriers)
            {
                if (barrier.GuildPigNeeded)
                    barrier.GuildPigReceived = true;
            }

            // release highest (newest) barrier. OnBarrierSatisfied will check the lower automatically
            for (int ndx = _pendingBarriers.Count - 1; ndx >= 0; --ndx)
            {
                MessageBarrier barrier = _pendingBarriers[ndx];
                if (barrier.IsSatisfied())
                {
                    OnBarrierSatisfied(ndx);
                    break;
                }
            }
        }

        void OnBarrierSatisfied(int satisfiedBarrierNdx)
        {
            // release it and all before that are satisfied. Not all may be satisfied
            // if they (only) wait for guild or player, for example.

            for (int ndx = 0; ndx <= satisfiedBarrierNdx; ++ndx)
            {
                MessageBarrier barrier = _pendingBarriers[ndx];

                if (!barrier.IsSatisfied())
                    continue;

                foreach (MetaMessage bufferedMessage in barrier.EnumerateMessages())
                    SendOutgoingPayloadMessage(bufferedMessage);

                _pendingBarriers[ndx] = null;
            }

            _pendingBarriers.RemoveAll(barrier => barrier == null);
            if (_pendingBarriers.Count == 0)
                _pendingBarriers = null;
        }

        #endregion

        #region Guild
        #if !METAPLAY_DISABLE_GUILDS

        async Task HandleGuildJoinRequest(GuildJoinRequest request)
        {
            // Check the guild exists before joining to it. Otherwise an empty Waiting-For-Setup guild would be generated on-demand, and it would reject us.
            if (!await CheckGuildIdIsValidAndExistsAsync(request.GuildId))
            {
                _log.Warning("Rejected guild view begin. Guild Id failed validity check: {GuildId}", request.GuildId);
                SendOutgoingPayloadMessage(GuildJoinResponse.CreateRefusal());
                return;
            }

            InternalPlayerJoinGuildResponse response = await EntityAskAsync<InternalPlayerJoinGuildResponse>(PlayerId, new InternalPlayerJoinGuildRequest(request, _guildIncarnation));
            if (!response.IsSuccess)
            {
                SendOutgoingPayloadMessage(GuildJoinResponse.CreateRefusal());
                return;
            }

            // We cannot have both a view and be a guild member. Close the view if such exists.
            await CloseGuildViewToGuildAsync(request.GuildId);

            // Now session tries to open a session to the guild. There is a slim
            // chance that somebody kicks the player during this time. We just fatal
            // error for now.

            InternalEntitySubscribeRequestBase.Default guildSubscribeRequest = new InternalEntitySubscribeRequestBase.Default(
                associationRef:                 response.AssociationRef,
                clientChannelId:                NextEntityChannelId(),
                resourceProposal:               null,
                isDryRun:                       false,
                supportedArchiveCompressions:   _supportedArchiveCompressions);
            EntitySubscription guildActor;
            InternalEntitySubscribeResponseBase.Default guildResponse;

            try
            {
                (guildActor, guildResponse) = await SubscribeToAsync<InternalEntitySubscribeResponseBase.Default>(request.GuildId, EntityTopic.Member, guildSubscribeRequest);
            }
            catch (InternalGuildMemberSubscribeRefused guildRefused)
            {
                _log.Warning("Guild refused subscribe after successful join. Result={Result}, GuildId={GuildId}.", guildRefused.Result, request.GuildId);
                throw new InvalidOperationException("Successfully joined-to guild refused subscription");
            }

            EntityChannelManager guildManager = new EntityChannelManager(_log, guildActor, guildResponse.State.ChannelId, guildResponse.State.ContextData.ClientSlot, isActiveOnClient: false, parentManager: GetPlayerChannel());
            _multiplayerEntities.Add(guildManager.ChannelId, guildManager);

            _guildIncarnation = response.GuildIncarnation;
            SendOutgoingPayloadMessage(new GuildJoinResponse(guildResponse.State.State, guildResponse.State.ChannelId));

            foreach (AssociatedEntityRefBase entityRef in guildResponse.AssociatedEntities)
                await SubscribeToEntityAsync(parentManager: guildManager, entityRef);
        }

        async Task HandleGuildLeaveRequest(GuildLeaveRequest request)
        {
            EntityChannelManager guildChannel = TryGetChannelForClientSlot(ClientSlotCore.Guild);
            if (guildChannel == null)
            {
                _log.Warning("Got stale guild leave request. Ignored. Was not in a guild.");
                return;
            }
            if (guildChannel.ChannelId != request.ChannelId)
            {
                _log.Warning("Got stale guild leave request. Ignored. ChannelId was {ChannelId}, expected {ExpectedChannelId}", request.ChannelId, guildChannel.ChannelId);
                return;
            }

            InternalPlayerGuildLeaveResponse response = await EntityAskAsync<InternalPlayerGuildLeaveResponse>(PlayerId, new InternalPlayerGuildLeaveRequest(guildChannel.EntityId, _guildIncarnation));

            if (response.IsStaleRequest)
            {
                // The leave request was stale. We (session) are still in that guild but player is not. There
                // will probably be a message soon from player that corrects this.
                return;
            }
            if (response.SessionDesynchronized)
            {
                // during processing of leave, player was modified in a way that cannot be represented with a session.
                // Killing.
                await TerminateSession(new SessionForceTerminateMessage(reason: new SessionForceTerminateReason.InternalServerError()));
                return;
            }

            // Request is legit.
            await UnsubscribeFromEntityAsync(guildChannel);

            // Guild is gone so barriers are invalid. Flush them
            FlushGuildMessageBarriers();

            _guildIncarnation = response.GuildIncarnation;
        }

        void HandleGuildEnqueueActionsRequest(GuildEnqueueActionsRequest request)
        {
            EntityChannelManager guildManager = TryGetChannelForClientSlot(ClientSlotCore.Guild);
            if (guildManager == null)
            {
                _log.Warning("Got stale guild add actions request. Ignored. Was not in a guild.");
                return;
            }
            if (guildManager.ChannelId != request.ChannelId)
            {
                _log.Warning("Got stale guild add actions request. Ignored. ChannelId was {ChannelId}, expected {ExpectedChannelId}", request.ChannelId, guildManager.ChannelId);
                return;
            }

            // forward to the guild
            SendMessage(guildManager.Actor, new InternalGuildEnqueueActionsRequest(PlayerId, request.Actions));
        }

        async Task HandleGuildDiscoveryRequest(GuildDiscoveryRequest request)
        {
            // get context from player
            InternalGuildDiscoveryPlayerContextResponse contextResponse = await EntityAskAsync<InternalGuildDiscoveryPlayerContextResponse>(PlayerId, InternalGuildDiscoveryPlayerContextRequest.Instance);
            GuildDiscoveryPlayerContextBase context = contextResponse.Result;

            InternalGuildRecommendationResponse response;
            GuildRecommenderQueryMetricsCollector metrics = GuildRecommenderQueryMetricsCollector.Begin();
            try
            {
                response = await EntityAskAsync<InternalGuildRecommendationResponse>(GuildRecommenderActorBase.EntityId, new InternalGuildRecommendationRequest(context));
                metrics.OnSuccess();
            }
            catch
            {
                metrics.OnTimeout();
                throw;
            }

            List<GuildDiscoveryInfoBase> guildInfos = response.GuildInfos;
            SendOutgoingPayloadMessage(new GuildDiscoveryResponse(guildInfos));
        }

        async Task HandleGuildSearchRequest(GuildSearchRequest request)
        {
            if (_hasOngoingGuildSearch)
            {
                _log.Warning("Got GuildSearchRequest while previous search was still ongoing. Ignored.");
                return;
            }

            // Early validation
            if (!request.SearchParams.Validate())
            {
                _log.Warning("GuildSearchRequest rejected early. GuildSearchParams validate failed. SearchParams: {Params}", PrettyPrint.Compact(request.SearchParams));
                SendOutgoingPayloadMessage(new GuildSearchResponse(isError: true, guildInfos: null));
                return;
            }

            // get context from player
            InternalGuildDiscoveryPlayerContextResponse contextResponse = await EntityAskAsync<InternalGuildDiscoveryPlayerContextResponse>(PlayerId, InternalGuildDiscoveryPlayerContextRequest.Instance);
            GuildDiscoveryPlayerContextBase context = contextResponse.Result;

            _hasOngoingGuildSearch = true;

            ContinueTaskOnActorContext(
                EntityAskAsync<InternalGuildSearchResponse>(GuildSearchActorBase.EntityIdOnCurrentNode, new InternalGuildSearchRequest(request.SearchParams, context)),
                (InternalGuildSearchResponse response) =>
                {
                    // If session was lost while background task was executing, do nothing.
                    if (_phase != ActorPhase.InSession)
                        return;

                    if (response.IsError)
                    {
                        SendOutgoingPayloadMessage(new GuildSearchResponse(isError: true, guildInfos: null));
                    }
                    else
                    {
                        SendOutgoingPayloadMessage(new GuildSearchResponse(isError: false, guildInfos: response.GuildInfos));
                    }
                    _hasOngoingGuildSearch = false;
                },
                (Exception ex) =>
                {
                    // If session was lost while background task was executing, do nothing.
                    if (_phase != ActorPhase.InSession)
                        return;

                    _hasOngoingGuildSearch = false;
                    SendOutgoingPayloadMessage(new GuildSearchResponse(isError: true, guildInfos: null));
                });
        }

        async Task HandleGuildBeginViewRequest(GuildBeginViewRequest request)
        {
            // Check the guild exists before joining to it. Otherwise an empty Waiting-For-Setup guild would be generated on-demand, and it would reject us.
            if (!await CheckGuildIdIsValidAndExistsAsync(request.GuildId))
            {
                _log.Warning("Rejected guild view begin. Guild Id failed validity check: {GuildId}", request.GuildId);
                SendOutgoingPayloadMessage(GuildViewResponse.CreateRefusal(request.QueryId));
                return;
            }

            // Cannot have a view and be a member at the same time.
            EntityChannelManager guildManager = TryGetChannelForClientSlot(ClientSlotCore.Guild);
            if (request.GuildId == guildManager?.EntityId)
            {
                _log.Warning("Rejected guild view begin. Attempted to view the same guild player was a member in: {GuildId}", request.GuildId);
                SendOutgoingPayloadMessage(GuildViewResponse.CreateRefusal(request.QueryId));
                return;
            }

            // Cannot have multiple views at the same time
            bool hasPreExistingView = false;
            foreach (EntitySubscription viewSubscription in _guildViews.Values)
            {
                if (viewSubscription.EntityId == request.GuildId)
                {
                    hasPreExistingView = true;
                    break;
                }
            }
            if (hasPreExistingView)
            {
                _log.Warning("Rejected guild view begin. Attempted to view the same guild twice: {GuildId}", request.GuildId);
                SendOutgoingPayloadMessage(GuildViewResponse.CreateRefusal(request.QueryId));
                return;
            }

            (EntitySubscription subscription, InternalGuildViewerSubscribeResponse response) = await SubscribeToAsync<InternalGuildViewerSubscribeResponse>(request.GuildId, EntityTopic.Spectator, InternalGuildViewerSubscribeRequest.Instance);

            if (response.IsRefusal())
            {
                await UnsubscribeFromAsync(subscription);
                SendOutgoingPayloadMessage(GuildViewResponse.CreateRefusal(request.QueryId));
                return;
            }

            int guildChannelId = NextEntityChannelId();
            _guildViews[guildChannelId] = subscription;

            SendOutgoingPayloadMessage(GuildViewResponse.CreateSuccess(response.GuildState.Value, guildChannelId, request.QueryId));
        }

        async Task HandleGuildEndViewRequest(GuildEndViewRequest request)
        {
            if (!_guildViews.TryGetValue(request.GuildChannelId, out EntitySubscription subscription))
            {
                _log.Warning("Attempted to end view to an unviewed channel {ChannelId}", request.GuildChannelId);
                return;
            }

            _guildViews.Remove(request.GuildChannelId);
            await UnsubscribeFromAsync(subscription);
        }

        async Task HandleGuildTransactionRequest(GuildTransactionRequest request)
        {
            // Deserialize transaction first here to make sure it is legal
            IGuildTransaction transaction = request.Transaction.Deserialize(resolver: null, logicVersion: _logicVersion);

            // Collect metrics from session point-of-view
            // \note: GuildTransactionMetricsCollector detects unclean completion with Dispose().
            using GuildTransactionMetricsCollector metrics = GuildTransactionMetricsCollector.Begin(transaction.GetType().GetNestedClassName());

            // Check request.GuildChannelId early. I.e. if we (the session) already
            // know the target guild was already gone

            EntityChannelManager guildManager = TryGetChannelForClientSlot(ClientSlotCore.Guild);
            bool forcePlayerCancel = false;
            if (guildManager == null)
            {
                // Player is being kicked, and we (session) got the notification already. It is not guaranteed that the
                // player has received the same notification, but since we dropped the _state.GuildActor, we cannot continue.
                // We could alternatively not drop _state.GuildActor on kick instead keep a separate state, but would that
                // win as anything?
                _log.Warning("Received guild transaction but there is no current guild. Requested ChannelId={RequestChannelId}.", request.GuildChannelId);
                forcePlayerCancel = true;
            }
            else if (request.GuildChannelId != guildManager.ChannelId)
            {
                _log.Warning("Guild transaction targeted a guild it is not a member of. Requested ChannelId={RequestChannelId}, Current channelId={CurrentChannelId}.", request.GuildChannelId, guildManager.ChannelId);

                // Cancel by forcing client to cancel.
                forcePlayerCancel = true;
            }

            int pubsubPiggingId = NextPiggingId();

            InternalGuildTransactionPlayerSync.Begin playerSyncBegin = new InternalGuildTransactionPlayerSync.Begin(
                transaction:        request.Transaction,
                forcePlayerCancel:  forcePlayerCancel,
                guildIncarnation:   _guildIncarnation,
                pubsubPiggingId:    pubsubPiggingId
                );

            InternalGuildTransactionGuildSync.PlannedAndCommitted   guildCommit;
            InternalGuildTransactionPlayerSync.Committed            playerCommit;
            bool                                                    isLateCancel;

            using (EntitySynchronize playerSync = await EntitySynchronizeAsync(PlayerId, playerSyncBegin))
            {
                InternalGuildTransactionPlayerSync.Planned playerBeginResponse = await playerSync.ReceiveAsync<InternalGuildTransactionPlayerSync.Planned>();

                if (playerBeginResponse.Result == InternalGuildTransactionPlayerSync.Planned.ResultCode.InternalError)
                {
                    // Internal error while processing, or protocol precondition failed. Cannot continue.
                    metrics.OnPlayerError();
                    throw new InvalidOperationException(playerBeginResponse.ErrorString);
                }
                else if (playerBeginResponse.Result == InternalGuildTransactionPlayerSync.Planned.ResultCode.Cancel)
                {
                    // Player cancelled.

                    metrics.OnPlayerCancel(wasClientForcedCancel: forcePlayerCancel);

                    GuildTransactionResponse response = new GuildTransactionResponse(
                        playerAction:           playerBeginResponse.EarlyCancelAction,
                        guildAction:            default,
                        playerActionTrackingId: playerBeginResponse.CancelTrackingId
                        );

                    // respond after previous messages are flushed
                    MessageBarrier barrier = new MessageBarrier(pubsubPiggingId, needsPlayerFlush: true, needsGuildFlush: false);
                    barrier.AddMessage(response);
                    AddBarrier(barrier);

                    return;
                }

                ITransactionPlan playerPlan = playerBeginResponse.PlayerPlan;
                ITransactionPlan serverPlan = await GetGuildTransactionServerPlanAsync(request.Transaction);

                InternalGuildTransactionGuildSync.Begin guildSyncBegin = new InternalGuildTransactionGuildSync.Begin(
                    playerId:           PlayerId,
                    memberInstanceId:   playerBeginResponse.MemberInstanceId,
                    lastPlayerOpEpoch:  playerBeginResponse.LastPlayerOpEpoch,
                    transaction:        request.Transaction,
                    playerPlan:         playerPlan,
                    serverPlan:         serverPlan,
                    pubsubPiggingId:    pubsubPiggingId
                    );

                using (EntitySynchronize guildSync = await EntitySynchronizeAsync(guildManager.EntityId, guildSyncBegin))
                {
                    guildCommit = await guildSync.ReceiveAsync<InternalGuildTransactionGuildSync.PlannedAndCommitted>();

                    if (guildCommit.Result == InternalGuildTransactionGuildSync.PlannedAndCommitted.ResultCode.InternalError)
                    {
                        // Internal error while processing, or protocol precondition failed. Cannot continue.
                        metrics.OnGuildError();
                        throw new InvalidOperationException(guildCommit.ErrorString);
                    }
                    else if (guildCommit.Result == InternalGuildTransactionGuildSync.PlannedAndCommitted.ResultCode.Cancel)
                    {
                        // Inform player that guild cancelled and wait for it to complete
                        metrics.OnGuildCancel();
                        playerSync.Send(InternalGuildTransactionPlayerSync.Commit.CreateCancel(guildCommit.PreceedingPlayerOps));
                        isLateCancel = true;
                    }
                    else
                    {
                        // Inform player we should continue
                        playerSync.Send(InternalGuildTransactionPlayerSync.Commit.CreateOk(guildCommit.GuildPlan, serverPlan, guildCommit.PreceedingPlayerOps, guildCommit.ExpectedPlayerOpEpoch));
                        isLateCancel = false;
                    }
                }

                playerCommit = await playerSync.ReceiveAsync<InternalGuildTransactionPlayerSync.Committed>();
            }
            metrics.OnComplete();

            if (isLateCancel)
            {
                GuildTransactionResponse response = new GuildTransactionResponse(
                    playerAction:           playerCommit.Action,
                    guildAction:            default,
                    playerActionTrackingId: playerCommit.ActionTrackingId
                    );

                MessageBarrier barrier = new MessageBarrier(pubsubPiggingId, needsPlayerFlush: true, needsGuildFlush: false);
                barrier.AddMessage(response);
                AddBarrier(barrier);
            }
            else
            {
                GuildTransactionResponse response = new GuildTransactionResponse(
                    playerAction:           playerCommit.Action,
                    guildAction:            guildCommit.GuildFinalizingAction,
                    playerActionTrackingId: playerCommit.ActionTrackingId
                    );

                // respond after previous messages are flushed
                MessageBarrier barrier = new MessageBarrier(pubsubPiggingId, needsPlayerFlush: true, needsGuildFlush: true);
                barrier.AddMessage(response);
                AddBarrier(barrier);
            }
        }

        Task<ITransactionPlan> GetGuildTransactionServerPlanAsync(MetaSerialized<IGuildTransaction> transaction)
        {
            // \todo: add EntityDispatch-like mechanism to make this usable
            return Task.FromResult<ITransactionPlan>(null);
        }

        async Task HandleGuildInspectInvitationRequest(GuildInspectInvitationRequest request)
        {
            // rate limit before any observable DB action
            MetaTime now = MetaTime.Now;
            if (_nextInvitationInspectionEarliestAt > MetaTime.Now)
            {
                _log.Debug("Client was rate limited for attempting to inspect invitations too frequently: {InvitationCode}", request.InviteCode.ToString());
                SendOutgoingPayloadMessage(GuildInspectInvitationResponse.CreateRateLimited(request.QueryId));
                return;
            }
            _nextInvitationInspectionEarliestAt = now + MetaDuration.FromSeconds(1);

            PersistedGuildInviteCode invitePointer = await MetaDatabase.Get().TryGetAsync<PersistedGuildInviteCode>(request.InviteCode.ToString());

            if (invitePointer == null)
            {
                _log.Debug("Client attempted to inspect invitation that did not exist (no pointer): {InvitationCode}", request.InviteCode.ToString());
                SendOutgoingPayloadMessage(GuildInspectInvitationResponse.CreateInvalidOrExpired(request.QueryId));
                return;
            }

            EntityId guildId = EntityId.ParseFromStringWithKind(EntityKindCore.Guild, invitePointer.GuildId);
            InternalGuildInspectInviteCodeResponse guildResponse = await EntityAskAsync<InternalGuildInspectInviteCodeResponse>(guildId, new InternalGuildInspectInviteCodeRequest(invitePointer.InviteId, request.InviteCode));

            if (!guildResponse.Success)
            {
                _log.Debug("Client attempted to inspect invitation that did not exist (no guild record): {InvitationCode}", request.InviteCode.ToString());
                SendOutgoingPayloadMessage(GuildInspectInvitationResponse.CreateInvalidOrExpired(request.QueryId));
                return;
            }

            InternalPlayerGetGuildInviterAvatarResponse playerResponse = await EntityAskAsync<InternalPlayerGetGuildInviterAvatarResponse>(guildResponse.PlayerId, InternalPlayerGetGuildInviterAvatarRequest.Instance);
            SendOutgoingPayloadMessage(GuildInspectInvitationResponse.CreateSuccess(request.QueryId, invitePointer.InviteId, guildResponse.DiscoveryInfo, playerResponse.InviterAvatar));
        }

        // Forward guild messages

        [PubSubMessageHandler]
        void HandleInternalSessionGuildTimelineUpdate(EntitySubscription subscription, InternalSessionGuildTimelineUpdate internalUpdate)
        {
            if (_phase != ActorPhase.InSession)
            {
                _log.Warning("Got InternalSessionGuildTimelineUpdate, no session. Ignored");
                return;
            }

            EntityChannelManager guildChannel = TryGetChannelForClientSlot(ClientSlotCore.Guild);
            if (guildChannel?.Actor == subscription)
            {
                GuildTimelineUpdateMessage guildUpdate = internalUpdate.CreateUpdateForClient(guildChannel.ChannelId);

                if (TryGetGuildBlockingBarrier(out MessageBarrier barrier))
                    barrier.AddMessage(guildUpdate);
                else
                    SendOutgoingPayloadMessage(guildUpdate);
                return;
            }

            foreach ((int channelId, EntitySubscription viewSubscription) in _guildViews)
            {
                if (subscription != viewSubscription)
                    continue;

                // stamp channel and forward to client
                GuildTimelineUpdateMessage guildUpdate = internalUpdate.CreateUpdateForClient(channelId);
                SendOutgoingPayloadMessage(guildUpdate);
                return;
            }

            _log.Warning("Got InternalSessionGuildTimelineUpdate from unviewed guild {FromEntityId}", subscription.EntityId);
        }

        [PubSubMessageHandler]
        async Task HandleInternalSessionPlayerJoinedAGuild(EntitySubscription subscription, InternalSessionPlayerJoinedAGuild notify)
        {
            EntityChannelManager preexistingGuildChannel = TryGetChannelForClientSlot(ClientSlotCore.Guild);
            if (preexistingGuildChannel != null)
                throw new InvalidOperationException("Player guild creation notification unexpected. Session already registered to a guild.");

            _log.Debug("Subscribing to a new guild {GuildId} at runtime", notify.AssociationRef.AssociatedEntity);

            // We cannot have both a view and be a guild member. Close the view if such exists.
            await CloseGuildViewToGuildAsync(notify.AssociationRef.AssociatedEntity);

            // As with joining, there is a slim chance that somebody kicks the player during this
            // time. We just fatal error for now.

            InternalEntitySubscribeRequestBase.Default guildSubscribeRequest = new InternalEntitySubscribeRequestBase.Default(
                associationRef:                 notify.AssociationRef,
                clientChannelId:                NextEntityChannelId(),
                resourceProposal:               null,
                isDryRun:                       false,
                supportedArchiveCompressions:   _supportedArchiveCompressions);
            EntitySubscription guildActor;
            InternalEntitySubscribeResponseBase.Default guildResponse;

            try
            {
                (guildActor, guildResponse) = await SubscribeToAsync<InternalEntitySubscribeResponseBase.Default>(notify.AssociationRef.AssociatedEntity, EntityTopic.Member, guildSubscribeRequest);
            }
            catch (InternalGuildMemberSubscribeRefused guildRefused)
            {
                _log.Warning("Guild refused join from owner. Result={Result}, GuildId={GuildId}.", guildRefused.Result, notify.AssociationRef.AssociatedEntity);
                throw new InvalidOperationException("Newly created guild refused initial join");
            }

            EntityChannelManager guildManager = new EntityChannelManager(_log, guildActor, guildResponse.State.ChannelId, guildResponse.State.ContextData.ClientSlot, isActiveOnClient: false, parentManager: GetPlayerChannel());
            _multiplayerEntities.Add(guildManager.ChannelId, guildManager);

            _guildIncarnation = notify.GuildIncarnation;

            if (notify.CreatedTheGuild)
                SendOutgoingPayloadMessage(new GuildCreateResponse(guildResponse.State.State, guildResponse.State.ChannelId));
            else
                SendOutgoingPayloadMessage(new GuildSwitchedMessage(guildResponse.State.State, guildResponse.State.ChannelId));

            foreach (AssociatedEntityRefBase entityRef in guildResponse.AssociatedEntities)
                await SubscribeToEntityAsync(parentManager: guildManager, entityRef);
        }

        [PubSubMessageHandler]
        async Task HandleInternalSessionPlayerKickedFromGuild(EntitySubscription subscription, InternalSessionPlayerKickedFromGuild notify)
        {
            _log.Debug("Received kicked-from-guild notification from player.");

            EntityChannelManager existingGuildChannel = TryGetChannelForClientSlot(ClientSlotCore.Guild);
            if (existingGuildChannel != null)
                await UnsubscribeFromEntityAsync(existingGuildChannel);

            _guildIncarnation = notify.GuildIncarnation;

            // Guild is gone so barriers are invalid. Flush them
            FlushGuildMessageBarriers();

            SendOutgoingPayloadMessage(new GuildSwitchedMessage(null, guildChannelId: -1));
        }

        [PubSubMessageHandler]
        void HandleInternalSessionPlayerGuildCreateFailed(EntitySubscription subscription, InternalSessionPlayerGuildCreateFailed notify)
        {
            EntityChannelManager existingGuildChannel = TryGetChannelForClientSlot(ClientSlotCore.Guild);
            if (existingGuildChannel != null)
                throw new InvalidOperationException("Player guild creation notification unexpected. Session already registered to a guild.");

            _guildIncarnation = notify.GuildIncarnation;
            SendOutgoingPayloadMessage(new GuildCreateResponse(null, guildChannelId: -1));
        }

        // Utils

        async Task<bool> CheckGuildIdIsValidAndExistsAsync(EntityId guildId)
        {
            // is not invalid
            if (guildId.Kind != EntityKindCore.Guild)
                return false;

            // check the guild exists from DB
            // \todo: we could query from guild metadata (with flag to prevent recomputation on demand).
            // \todo: we could cache results (on the node), and even remember results from Discovery and search operations
            // \todo: we sign/MAC GuildIds as we send them to allow client prove the entityIds are valid and untampered

            PersistedGuildBase persisted = await MetaDatabase.Get().TryGetAsync<PersistedGuildBase>(guildId.ToString()).ConfigureAwait(false);
            return persisted != null;
        }

        async Task CloseGuildViewToGuildAsync(EntityId guildId)
        {
            foreach (EntitySubscription subscription in _subscriptions.Values)
            {
                if (subscription.Topic != EntityTopic.Spectator)
                    continue;
                if (subscription.EntityId != guildId)
                    continue;

                foreach((int guildChannel, EntitySubscription guildViewSubscription) in _guildViews)
                {
                    if (guildViewSubscription != subscription)
                        continue;

                    SendOutgoingPayloadMessage(new GuildViewEnded(guildChannel));
                    _guildViews.Remove(guildChannel);
                }
                await UnsubscribeFromAsync(subscription);

                return;
            }
        }

        #endif
        #endregion

        #region Client Pause Hints

        void HandleClientLifecycleHintPausing(ClientLifecycleHintPausing clientPausing)
        {
            string sanitizedReason = null;
            if (clientPausing.PauseReason != null)
                sanitizedReason = Util.ClampStringToLengthCodepoints(clientPausing.PauseReason, maxCodepoints: 64);
            _log.Info("Client app paused. Announced max duration {MaxDuration} with reason {Reason}", clientPausing.MaxPauseDuration, sanitizedReason);

            _clientPauseStatus = ClientAppPauseStatus.Paused;
            _clientPauseReason = clientPausing.PauseReason;
            NotifySessionEntitiesClientAppStatusChanged();

            // Client must start unpausing by the declared time (clamped to the maximum limit)
            PlayerOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<PlayerOptions>();
            TimeSpan clientMaxPauseDuration;
            if (clientPausing.MaxPauseDuration.HasValue)
                clientMaxPauseDuration = Util.Min(clientPausing.MaxPauseDuration.Value.ToTimeSpan(), options.MaxClientPauseDuration);
            else
                clientMaxPauseDuration = options.MaxClientPauseDuration;

            _clientPauseDeadlineExceededTimerCts?.Cancel();
            _clientPauseDeadlineExceededTimerCts?.Dispose();
            _clientPauseDeadlineExceededTimerCts = new CancellationTokenSource();
            ScheduleExecuteOnActorContext(clientMaxPauseDuration, ClientPauseDeadlineExceeded, _clientPauseDeadlineExceededTimerCts.Token);
        }

        void HandleClientLifecycleHintUnpausing(ClientLifecycleHintUnpausing clientUnpausing)
        {
            _log.Debug("Client app started unpausing.");

            _clientPauseStatus = ClientAppPauseStatus.Unpausing;
            _clientPauseReason = null;
            NotifySessionEntitiesClientAppStatusChanged();

            // Client must complete unpausing by the the time limit
            PlayerOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<PlayerOptions>();
            _clientPauseDeadlineExceededTimerCts?.Cancel();
            _clientPauseDeadlineExceededTimerCts?.Dispose();
            _clientPauseDeadlineExceededTimerCts = new CancellationTokenSource();
            ScheduleExecuteOnActorContext(options.MaxClientUnpausingToUnpausedDuration, ClientPauseDeadlineExceeded, _clientPauseDeadlineExceededTimerCts.Token);
        }

        void HandleClientLifecycleHintUnpaused(ClientLifecycleHintUnpaused clientUnpaused)
        {
            _log.Info("Client app unpaused.");

            _clientPauseStatus = ClientAppPauseStatus.Running;
            NotifySessionEntitiesClientAppStatusChanged();

            // Cancel any deadline timers if we had any
            _clientPauseDeadlineExceededTimerCts?.Cancel();
            _clientPauseDeadlineExceededTimerCts?.Dispose();
            _clientPauseDeadlineExceededTimerCts = null;
        }

        void NotifySessionEntitiesClientAppStatusChanged()
        {
            InternalSessionNotifyClientAppStatusChanged message = new InternalSessionNotifyClientAppStatusChanged(isClientConnected: _client != null, _clientPauseStatus);

            SendMessage(_playerActor, message);

            #if !METAPLAY_DISABLE_GUILDS
            // \todo[jarkko]: handle on guilds
            //if (_state.GuildActor != null)
            //    SendMessage(_guildActor, message);
            #endif
        }

        async Task ClientPauseDeadlineExceeded()
        {
            if (_phase != ActorPhase.InSession)
                return;

            if (_clientPauseStatus == ClientAppPauseStatus.Paused)
                _log.Warning("Client did not unpause by the deadline. Closing session. Pause reason was {ClientReason}.", _clientPauseReason);
            else if (_clientPauseStatus == ClientAppPauseStatus.Unpausing)
                _log.Warning("Client started unpausing but did not complete it by the deadline. Closing session. Pause reason was {ClientReason}.", _clientPauseReason);
            else
                return;

            await TerminateSession(new SessionForceTerminateMessage(reason: new SessionForceTerminateReason.PauseDeadlineExceeded()));
        }

        #endregion

        #region Client state overwrite

        public class PlayerOverwriteRequestPayload
        {
            public int FormatVersion;
            public OrderedDictionary<string, OrderedDictionary<string, ExportedEntity>> Entities;
        };

        async Task HandleDevOverwritePlayerStateRequest(DevOverwritePlayerStateRequest overwrite, int requestId)
        {
            // Dev overwrite always allowed in development environments, only allowed for developer players in
            // production-like environments.
            if (!RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>().EnableDevelopmentFeatures &&
                !GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(PlayerId))
            {
                _log.Warning("Got DevOverwritePlayerStateRequest for non-developer player with development features disabled!");
                SendResponse(requestId, new DevOverwritePlayerStateFailure("Not allowed"));
                return;
            }

            DevOverwritePlayerStateFailure failure = null;
            try
            {
                PlayerOverwriteRequestPayload data = JsonSerialization.Deserialize<PlayerOverwriteRequestPayload>(overwrite.EntityArchiveJson);

                if (data.Entities.Keys.Count != 1 || data.Entities.Keys.First() != "player" || data.Entities["player"].Keys.Count != 1)
                    throw new InvalidOperationException("Entity archive invalid for single player overwrite: must contain only a single player entity!");

                string sourcePlayerId = data.Entities["player"].Keys.First();

                _log.Info("Overwriting player {PlayerId} with state provided by client (source player {SourcePlayerId})", PlayerId, sourcePlayerId);

                ImportResponse response = await EntityArchiveUtils.Import(
                    formatVersion: data.FormatVersion,
                    importEntities: data.Entities,
                    userRemaps: new OrderedDictionary<string, OrderedDictionary<string, string>>()
                    {{
                        "player",
                        new OrderedDictionary<string, string>() {{ sourcePlayerId, PlayerId.ToString() } }
                    }},
                    overwritePolicy: OverwritePolicy.Overwrite,
                    verifyOnly: false,
                    asker: this
                );

                ImportResponse.EntityResult entityResult = response.Entities.First();
                if (entityResult.Status == ImportResponse.EntityResultStatus.Error)
                    failure = new DevOverwritePlayerStateFailure($"{entityResult.Error.Message} ({entityResult.Error.Details})");
                else if (entityResult.Status != ImportResponse.EntityResultStatus.Success)
                    throw new InvalidOperationException("unreachable");

                // If operation was successful client was kicked, no response necessary
            }
            catch (Exception ex)
            {
                failure = new DevOverwritePlayerStateFailure("Exception: " + ex.Message);
            }

            if (failure != null)
            {
                _log.Error("Player dev overwrite failed: {Reason})", failure.Reason);
                SendResponse(requestId, failure);
            }
        }

        #endregion

        #region Multiplayer Entities

        void HandleEntityActivated(EntityActivated activated)
        {
            if (!_multiplayerEntities.TryGetValue(activated.ChannelId, out EntityChannelManager channelManager))
            {
                _log.Warning("Got stale EntityActivated to unknown channel {ChannelId}. Ignored", activated.ChannelId);
                return;
            }

            // Client side entity state has been established.
            channelManager.OnClientEntityActivatedCore();
        }

        void HandleEntityClientToServerEnvelope(EntityClientToServerEnvelope envelope)
        {
            // Forward client the message to the entity on the channel
            if (!_multiplayerEntities.TryGetValue(envelope.ChannelId, out EntityChannelManager channelManager))
            {
                _log.Warning("Got stale incoming entity message of type {MessageType} to unknown channel {ChannelId}. Ignored", MetaSerializationUtil.PeekMessageName(envelope.Message), envelope.ChannelId);
                return;
            }

            SendMessage(channelManager.Actor, envelope);
        }

        [PubSubMessageHandler]
        void HandleEntityServerToClientEnvelope(EntitySubscription subscription, EntityServerToClientEnvelope envelope)
        {
            // Forward to client as is. The Entity sets the appropriate channel Ids already.
            SendOutgoingPayloadMessage(envelope);
        }

        async Task HandleMultiplayerActorLost(EntityChannelManager channelManager)
        {
            EntityChannelManager.SubscriptionEndResolution resolution = await channelManager.OnSubscriptionTerminated();
            _log.Warning("Lost subscription to multiplayer actor {EntityId}. Resolution: {Result}", channelManager.EntityId, resolution);

            switch (resolution)
            {
                case EntityChannelManager.SubscriptionEndResolution.Ignore:
                    await UnsubscribeFromEntityAsync(channelManager, informClient: false);
                    break;

                case EntityChannelManager.SubscriptionEndResolution.CloseChannel:
                    await UnsubscribeFromEntityAsync(channelManager, informClient: true);
                    break;

                case EntityChannelManager.SubscriptionEndResolution.FatalError:
                default:
                    await TerminateSession(new SessionForceTerminateMessage(reason: new SessionForceTerminateReason.InternalServerError()));
                    return;
            }
        }

        async Task HandleMultiplayerActorKicked(EntityChannelManager channelManager, MetaMessage message)
        {
            EntityChannelManager.SubscriptionEndResolution resolution;

            #if !METAPLAY_DISABLE_GUILDS
            if (channelManager.ClientSlot == ClientSlotCore.Guild)
            {
                if (message is InternalSessionGuildMemberKicked)
                {
                    _log.Info("Guild subscription was kicked with {Message}. Player is being kicked from the guild.", PrettyPrint.Compact(message));

                    // We are being kicked from the guild. Just clear the state now. The player will inform us soon how to proceed.
                    // There is no race. In case player is faster to inform use than the guild, we have unsubscribed from the old guild and
                    // cannot handle this message. (Even if we receive the message, we wouldn't handle it).

                    FlushGuildMessageBarriers();

                    resolution = EntityChannelManager.SubscriptionEndResolution.CloseChannel;
                }
                else
                {
                    // unhandled
                    resolution = EntityChannelManager.SubscriptionEndResolution.FatalError;
                }
            }
            else
            {
                resolution = await channelManager.OnSubscriptionKicked(message);
            }
            #else
            resolution = await channelManager.OnSubscriptionKicked(message);
            #endif

            _log.Warning("Kicked from multiplayer actor {EntityId}: {Message}. Resolution: {Result}", channelManager.EntityId, PrettyPrint.Compact(message), resolution);

            switch (resolution)
            {
                case EntityChannelManager.SubscriptionEndResolution.Ignore:
                    await UnsubscribeFromEntityAsync(channelManager, informClient: false);
                    break;

                case EntityChannelManager.SubscriptionEndResolution.CloseChannel:
                    await UnsubscribeFromEntityAsync(channelManager, informClient: true);
                    break;

                case EntityChannelManager.SubscriptionEndResolution.FatalError:
                default:
                    await TerminateSession(new SessionForceTerminateMessage(reason: new SessionForceTerminateReason.InternalServerError()));
                    return;
            }
        }

        [PubSubMessageHandler]
        async Task HandleInternalSessionEntityAssociationUpdate(EntitySubscription entity, InternalSessionEntityAssociationUpdate update)
        {
            EntityChannelManager existingChannel = TryGetChannelForClientSlot(update.Slot);
            if (update.AssociationRef == null && existingChannel == null)
            {
                _log.Debug("Removal of subentity in slot {Slot} was requested but slot was already empty", update.Slot);
                return;
            }
            else if (update.AssociationRef == null && existingChannel != null)
            {
                _log.Debug("Removing of subentity in slot {Slot}", update.Slot);
                await UnsubscribeFromEntityAsync(existingChannel);
                return;
            }

            // Remove entity from the slot. This removes also its child entities.
            if (existingChannel != null)
            {
                _log.Debug("Update of subentity in slot {Slot} requested. Unsubscribing from existing entity first.", existingChannel.ClientSlot);
                await UnsubscribeFromEntityAsync(existingChannel);
            }

            // Subscribe to the tree
            EntityChannelManager parentChannel = TryGetChannelForSubscription(entity);
            if (parentChannel == null)
                throw new InvalidOperationException($"Got InternalSessionEntityAssociationUpdate from an entity that was not an associated entity itself: {entity.EntityId}");
            await SubscribeToEntityAsync(parentChannel, update.AssociationRef);
        }

        [PubSubMessageHandler]
        void HandleInternalSessionEntityBroadcastMessage(EntitySubscription entity, InternalSessionEntityBroadcastMessage broadcast)
        {
            foreach (EntityChannelManager manager in _multiplayerEntities.Values)
            {
                if (broadcast.TargetSlots.Contains(manager.ClientSlot))
                    SendMessage(manager.Actor, broadcast.PayloadMessage);
            }
        }

        #endregion

        #region EntityChannels

        protected int NextEntityChannelId()
        {
            return _multiplayerEntityRunningId++;
        }

        /// <summary>
        /// Subscribes to the entity defined by <paramref name="association"/> and recursively to all associated entities, opens the channels
        /// to the appropriate client slots, and informs client of the new entities.
        /// </summary>
        /// <param name="parentManager">
        /// Parent entity manager if any. Otherwise <c>null</c>. Parent entity is the entity that may declare this association.<br/>
        /// Hint: If this entity is defined by the Player, use <see cref="GetPlayerChannel"/>.
        /// </param>
        protected async Task SubscribeToEntityAsync(EntityChannelManager parentManager, AssociatedEntityRefBase association)
        {
            // Subscribe to the tree. DoSubscribeToEntityAsync may throw if it runs out of retries. This is ok.
            List<PendingAssociatedEntitySubscription> pendingSubscriptions = new List<PendingAssociatedEntitySubscription>();
            await DoSubscribeToEntityAsync(pendingSubscriptions, parentManager?.Actor, association);

            // Complete subscriptions and create managers for them
            foreach (PendingAssociatedEntitySubscription subscription in pendingSubscriptions)
            {
                EntityChannelManager channelManager = CompleteSubscribeToAssociatedEntity(subscription, isActiveOnClient: false);
                _multiplayerEntities.Add(channelManager.ChannelId, channelManager);
                SendOutgoingPayloadMessage(new EntitySwitchedMessage(oldChannelId: -1, newState: subscription.State));
            }
        }

        async Task DoSubscribeToEntityAsync(List<PendingAssociatedEntitySubscription> pendingSubscriptions, EntitySubscription parentSubscriptionMaybe, AssociatedEntityRefBase association)
        {
            // Subtree cannot overlap. That would imply shared ownership of a slot, and in this case in particular that the mutual exclusion
            // is not working properly.
            EntityChannelManager existingChannel = TryGetChannelForClientSlot(association.GetClientSlot());
            if (existingChannel != null)
                throw new InvalidOperationException($"Cannot subscribe to the entity tree. Slot {existingChannel.ClientSlot} was already taken.");

            // all subscription and subsubscriptions of this entity will be in [successfullSubcriptionsStartNdx, MAX] range.
            int successfullSubcriptionsStartNdx = pendingSubscriptions.Count;

            // Subscribe to the entity
            int attemptNumber = 0;

            retry:
            attemptNumber++;
            if (attemptNumber > 3)
                throw new InvalidOperationException("Entity relationships did not settle after 3 iterations. Cannot continue.");

            // Subscribe to the entity.
            PendingAssociatedEntitySubscription subscription;
            try
            {
                subscription = await BeginSubscribeToAssociatedEntityAsync(
                    association:                    association,
                    clientChannelId:                NextEntityChannelId(),
                    resourceProposal:               null,
                    isDryRun:                       false,
                    supportedArchiveCompressions:   _supportedArchiveCompressions,
                    slotIncarnation:                0,
                    parentSubscription:             parentSubscriptionMaybe);
                pendingSubscriptions.Add(subscription);
            }
            catch (InternalEntitySubscribeRefusedBase.Builtins.TryAgain)
            {
                goto retry;
            }
            catch (InternalEntitySubscribeRefusedBase)
            {
                // parent handles
                throw;
            }

            // Subscribe recursively into child entities
            foreach (AssociatedEntityRefBase subAssociation in subscription.AssociatedEntities)
            {
                try
                {
                    await DoSubscribeToEntityAsync(pendingSubscriptions, subscription.Actor, subAssociation);
                }
                catch (InternalEntitySubscribeRefusedBase refusal)
                {
                    // remove the successful parent (current) and (recursively) successful sub-entity subscriptions
                    for (int ndx = pendingSubscriptions.Count; ndx >= successfullSubcriptionsStartNdx; --ndx)
                        await UnsubscribeFromAsync(pendingSubscriptions[ndx].Actor);
                    pendingSubscriptions.RemoveRange(successfullSubcriptionsStartNdx, pendingSubscriptions.Count - successfullSubcriptionsStartNdx);

                    // Child entity reports the current entity is not a participant. Clear state in source and try again.
                    _log.Warning("Associated entity refused join from participant during session. Informing source entity and trying again. Source={Source} Associated={Associated}.", subAssociation.SourceEntity, subAssociation.AssociatedEntity);
                    await EntityAskAsync<EntityAskOk>(association.AssociatedEntity, new InternalEntityAssociatedEntityRefusedRequest(subAssociation, refusal));

                    goto retry;
                }
            }
        }

        /// <summary>
        /// Closes entity channel and all subentity channels. If <paramref name="informClient"/> is <c>false</c>, the client is not informed of the channel closing.
        /// </summary>
        protected async Task UnsubscribeFromEntityAsync(EntityChannelManager channel, bool informClient = true)
        {
            if (channel.ParentEntity != null)
                channel.ParentEntity.ChildEntities.Remove(channel);

            // Gather channel and sub channels.
            List<EntityChannelManager> descendantsAndSelf = new List<EntityChannelManager>();
            descendantsAndSelf.Add(channel);
            for (int ndx = 0; ndx < descendantsAndSelf.Count; ++ndx)
            {
                EntityChannelManager subchannel = descendantsAndSelf[ndx];
                descendantsAndSelf.AddRange(subchannel.ChildEntities);
            }

            // Remove entities in reverse order on client, and unsubscribe concurrently
            List<Task> unsubscribes = new List<Task>();
            for (int ndx = descendantsAndSelf.Count - 1; ndx >= 0; --ndx)
            {
                EntityChannelManager subchannel = descendantsAndSelf[ndx];
                if (!_multiplayerEntities.Remove(subchannel.ChannelId))
                    continue;

                if (informClient)
                    SendOutgoingPayloadMessage(new EntitySwitchedMessage(oldChannelId: subchannel.ChannelId, newState: null));

                // If we are handling subscription lost/kick event, the subscription is already gone. Rather than have call-site
                // define special flags, ignore already removed automatically
                if (!_subscriptions.ContainsKey(subchannel.Actor.InChannelId))
                    continue;

                unsubscribes.Add(UnsubscribeFromAsync(subchannel.Actor));
            }
            foreach (Task task in unsubscribes)
                await task;
        }

        protected EntityChannelManager TryGetChannelForClientSlot(ClientSlot slot)
        {
            foreach (EntityChannelManager manager in _multiplayerEntities.Values)
            {
                if (manager.ClientSlot == slot)
                    return manager;
            }
            return null;
        }

        protected EntityChannelManager TryGetChannelForSubscription(EntitySubscription subscription)
        {
            foreach (EntityChannelManager manager in _multiplayerEntities.Values)
            {
                if (manager.Actor == subscription)
                    return manager;
            }
            return null;
        }

        /// <summary>
        /// Returns the Player channel manager. Player is the root entity in a session, and this channel always
        /// exists as long as there is an ongoing session. Throws on failure.
        /// </summary>
        protected EntityChannelManager GetPlayerChannel()
        {
            EntityChannelManager playerChannel = TryGetChannelForClientSlot(ClientSlotCore.Player);
            if (playerChannel == null)
                throw new InvalidOperationException("No player entity present in the session. GetPlayerChannel may only be called when there is an ongoing session.");
            return playerChannel;
        }

        #endregion

        #region Miscellaneous

        /// <summary>
        /// Execute the given action on the given player in unsynchronized manner.
        /// </summary>
        protected async Task ExecutePlayerServerActionAsync(EntityId playerId, PlayerActionBase action, int logicVersion)
        {
            MetaSerialized<PlayerActionBase> serialized = MetaSerialization.ToMetaSerialized(action, MetaSerializationFlags.IncludeAll, logicVersion);
            await EntityAskAsync<InternalPlayerExecuteServerActionResponse>(playerId, new InternalPlayerExecuteServerActionRequest(serialized));
        }

        /// <summary>
        /// Enqueue the given action on the given player in a synchronized manner.
        /// </summary>
        protected async Task EnqueuePlayerServerActionAsync(EntityId playerId, PlayerActionBase action, int logicVersion)
        {
            MetaSerialized<PlayerActionBase> serialized = MetaSerialization.ToMetaSerialized(action, MetaSerializationFlags.IncludeAll, logicVersion);
            await EntityAskAsync<InternalPlayerEnqueueServerActionResponse>(playerId, new InternalPlayerEnqueueServerActionRequest(serialized));
        }

        #endregion

        #region ImmutableX login

        void HandleImmutableXLoginChallengeRequest(ImmutableXLoginChallengeRequest request, int requestId)
        {
            (Web3Options web3Options, EnvironmentOptions environmentOpts) = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options, EnvironmentOptions>();
            if (!web3Options.EnableImmutableXPlayerAuthentication)
                throw new InvalidOperationException($"Cannot request for imx login challenge, Web3:{nameof(web3Options.EnableImmutableXPlayerAuthentication)} is false");

            EthereumAddress ethAddress = EthereumAddress.FromStringWithoutChecksumCasing(request.ClaimedEthereumAccount);
            StarkPublicKey imxAddress = StarkPublicKey.FromString(request.ClaimedImmutableXAccount);
            MetaTime issuedAt = MetaTime.Now;
            ImmutableXLoginChallenge challenge = ImmutableXLoginChallenge.Create(web3Options, environmentOpts, issuedAt, PlayerId, ethAddress, imxAddress);

            SendResponse(requestId, new ImmutableXLoginChallengeResponse(challenge.Message, challenge.Description, challenge.PlayerId, challenge.Timestamp));
        }

        #endregion

        #region Game-specific Hooks

        protected virtual TGameSessionStartParams               GameCreateSessionStartParams        (InternalSessionStartNewRequest startNewRequest) => default;
        protected virtual PlayerSessionParamsBase               GameCreatePlayerSessionParams       (SessionStartParams sessionStart, SessionToken sessionToken) => CreateDefaultPlayerSessionParams(sessionStart, sessionToken);
        protected virtual TGameSessionState                     GameCreateStateForNewSession        (SessionStartParams sessionStart) => default;
        protected virtual SessionResumeFailure                  GameCheckSessionResumeRequirements  (InternalSessionResumeRequest resumeRequest) => null;
        protected virtual Task<ISessionStartSuccessGamePayload> GameGetSessionStartPayload          (SessionStartParams sessionStart) { return Task.FromResult<ISessionStartSuccessGamePayload>(null); }
        protected virtual Task                                  GameTerminateSession                ()                                                      { return Task.CompletedTask; }
        protected virtual Task<bool>                            GameTryHandleIncomingPayloadMessage (MessageRoutingRule routingRule, MetaMessage message)   { return Task.FromResult(false); }
        protected virtual Task<bool>                            GameTryHandleSubscriptionLost       (EntitySubscription subscription)                       { return Task.FromResult(false); }
        protected virtual Task<bool>                            GameTryHandleSubscriptionKicked     (EntitySubscription subscription, MetaMessage message)  { return Task.FromResult(false); }
        protected virtual Task                                  GameActorTick                       ()                                                      { return Task.CompletedTask; }

        #pragma warning disable 1998
        /// <summary>
        /// Subscribes into the associated entity and creates a <see cref="PendingAssociatedEntitySubscription"/> representing the effects of an succesful results. Implementation does not need to handle any error in <see cref="InternalEntitySubscribeRefusedBase.Builtins"/>
        /// and may forward them to the caller.
        /// </summary>
        /// <param name="parentSubscription">Subscription to the entity that declared the target entity in its associated entities. If the target entity is a root entitity, e.g. player, the parent is <c>null</c>.</param>
        protected virtual async Task<PendingAssociatedEntitySubscription> BeginSubscribeToAssociatedEntityAsync(AssociatedEntityRefBase association, int clientChannelId, SessionProtocol.SessionResourceProposal? resourceProposal, bool isDryRun, CompressionAlgorithmSet supportedArchiveCompressions, int slotIncarnation, EntitySubscription parentSubscription)
        {
            EntityTopic topic = EntityTopic.Participant;

            #if !METAPLAY_DISABLE_GUILDS

            if (association.GetClientSlot() == ClientSlotCore.Guild)
                topic = EntityTopic.Member;

            #endif

            // Default subscription

            InternalEntitySubscribeRequestBase subscribeRequest = new InternalEntitySubscribeRequestBase.Default(
                associationRef:                 association,
                clientChannelId:                NextEntityChannelId(),
                resourceProposal:               resourceProposal,
                isDryRun:                       isDryRun,
                supportedArchiveCompressions:   supportedArchiveCompressions);

            (EntitySubscription actor, InternalEntitySubscribeResponseBase.Default response) = await SubscribeToAsync<InternalEntitySubscribeResponseBase.Default>(association.AssociatedEntity, topic, subscribeRequest);
            return new PendingAssociatedEntitySubscription.Default(actor, response, parentSubscription);
        }
        #pragma warning restore 1998

        /// <summary>
        /// Commits any pending state from <see cref="BeginSubscribeToAssociatedEntityAsync"/> into actor state and creates appropriate channel manager for the entity. Implementation should fall back to the base implementation as the last resort.
        /// </summary>
        /// <param name="isActiveOnClient">Has client has completely loaded the model and resources. This is false if entity is loaded mid-session, in which case the client will on-demand load the game config. Session start entities handle resources during handshake.</param>
        protected virtual EntityChannelManager CompleteSubscribeToAssociatedEntity(PendingAssociatedEntitySubscription subscription, bool isActiveOnClient)
        {
            EntityChannelManager parentManager = TryGetChannelForSubscription(subscription.ParentSubscription);

            // Default manager
            return new EntityChannelManager(_log, subscription.Actor, subscription.State.ChannelId, subscription.State.ContextData.ClientSlot, isActiveOnClient: isActiveOnClient, parentManager);
        }

        #endregion
    }

    public static class SessionIdUtil
    {
        public static EntityId ToPlayerId(EntityId sessionId)
        {
            if (sessionId.Kind != EntityKindCore.Session)
                throw new InvalidOperationException($"session id must have Session kind. Got {sessionId}");
            return EntityId.Create(EntityKindCore.Player, sessionId.Value);
        }
    }

    public abstract class SessionConfigBase : EphemeralEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCore.Session;
        public override IShardingStrategy   ShardingStrategy        => new HashedShardingStrategy(Application.Instance.ClusterConfig, EntityKindCore.Session);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Convenience wrapper for <see cref="SessionActorBase{TGameSessionStartParams, TGameSessionState}"/>
    /// for when game-specific session start parameters and session state are not needed.
    /// </summary>
    public abstract class SessionActorBase : SessionActorBase</*TGameSessionStartParams:*/ object, /*TGameSessionState:*/ object>
    {
        protected SessionActorBase(EntityId entityId) : base(entityId)
        {
        }
    }
}
