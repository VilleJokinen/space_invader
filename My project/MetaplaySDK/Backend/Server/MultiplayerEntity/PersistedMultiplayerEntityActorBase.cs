// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Model.JournalCheckers;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Serialization;
using Metaplay.Server.Database;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Server.MultiplayerEntity
{
    /// <summary>
    /// The base of <see cref="IPersistedEntity"/> for persisted Multiplayer Entity. Implementations should inherit this class
    /// for each concrete MultiplayerEntity and add the appropriate [Table(...)] attribute. If new fields are added,
    /// actor implementation should override CreatePersisted with a custom implementation.
    /// </summary>
    public abstract class PersistedMultiplayerEntityBase : IPersistedEntity
    {
        [Key]
        [PartitionKey]
        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string EntityId { get; set; }

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime PersistedAt { get; set; }

        public byte[] Payload { get; set; }

        [Required]
        public int SchemaVersion { get; set; }

        [Required]
        public bool IsFinal { get; set; }
    }

    /// <summary>
    /// Default implementation of <see cref="IActiveEntityInfo"/> for Multiplayer Entity.
    /// </summary>
    [MetaSerializableDerived(103)]
    public class DefaultMultiplayerEntityActiveEntityInfo : IActiveEntityInfo
    {
        [MetaMember(1)] public EntityId EntityId    { get; private set; }
        [MetaMember(2)] public MetaTime ActivityAt  { get; private set; }
        [MetaMember(3)] public MetaTime CreatedAt   { get; private set; }
        [MetaMember(4)] public string   DisplayName { get; private set; }

        DefaultMultiplayerEntityActiveEntityInfo() { }
        public DefaultMultiplayerEntityActiveEntityInfo(EntityId entityId, MetaTime activityAt, MetaTime createdAt, string displayName)
        {
            EntityId = entityId;
            ActivityAt = activityAt;
            CreatedAt = createdAt;
            DisplayName = displayName;
        }
    }

    /// <summary>
    /// Base class for actors managing a database-persisted a Multiplayer Entity.
    /// <para>
    /// Multiplayer Entity is an basic implementation for server-driven Entity. Multiple Clients may subscribe to an Multiplayer Entity and propose
    /// actions. Actions and Ticks executed by server are delivered back to client, allowing clients to see the changes in their copy of the Model.
    /// </para>
    /// <para>
    /// To use this base class, you should create actor-specific <typeparamref name="TPersisted"/> which may be empty, with an appropriate [Table(...)] attribute.
    /// </para>
    /// </summary>
    public abstract class PersistedMultiplayerEntityActorBase<TModel, TAction, TPersisted>
        : PersistedEntityActor<TPersisted, TModel>
        where TModel : class, IMultiplayerModel<TModel>, new()
        where TAction : ModelAction
        where TPersisted : PersistedMultiplayerEntityBase, new()
    {
        internal sealed class FlushActionsCommand { public static FlushActionsCommand Instance = new FlushActionsCommand(); }
        internal sealed class PruneDesyncSnapshotsCommand { public static PruneDesyncSnapshotsCommand Instance = new PruneDesyncSnapshotsCommand(); }

        protected sealed class Journal : ModelJournal<TModel>.Leader
        {
            public Journal(LogChannel log, TModel model, bool enableConsistencyChecks)
                : base(log, enableConsistencyChecks: enableConsistencyChecks, computeChecksums: true)
            {
                if (enableConsistencyChecks)
                {
                    AddListener(new JournalModelOutsideModificationChecker<TModel>(log));
                    AddListener(new JournalModelCloningChecker<TModel>(log));
                    AddListener(new JournalModelChecksumChecker<TModel>(log));
                    AddListener(new JournalModelActionImmutabilityChecker<TModel>(log));
                    AddListener(new JournalModelRerunChecker<TModel>(log));
                    AddListener(new JournalModelModifyHistoryChecker<TModel>(log));
                }

                AddListener(new JournalModelCommitChecker<TModel>(log));
                AddListener(new FailingActionWarningListener<TModel>(log));

                Setup(model, JournalPosition.AfterTick(model.CurrentTick));
            }
        }

        protected override TimeSpan           SnapshotInterval => TimeSpan.FromMinutes(3);
        protected override AutoShutdownPolicy ShutdownPolicy   => AutoShutdownPolicy.ShutdownAfterSubscribersGone(lingerDuration: TimeSpan.FromSeconds(5));

        /// <summary> LogicVersion in use for this actor </summary>
        protected readonly int _logicVersion;
        /// <summary> Logging channel to route log events from Model into Akka logger </summary>
        protected readonly LogChannel _modelLogChannel;
        /// <summary> Currently active config. This is always set. </summary>
        protected ActiveGameConfig _activeGameConfig;

        /// <summary>
        /// Currently active baseline-version of the game config. This is always set.
        /// Note that Multiplayer Entities do not participate in Player Experiments and hence will not have
        /// specialized configs.
        /// </summary>
        protected FullGameConfig _baselineGameConfig;
        /// <summary>
        /// Resolver for <see cref="_baselineGameConfig"/>. This is always set.
        /// Note that Multiplayer Entities do not participate in Player Experiments and hence will not have
        /// specialized configs.
        /// </summary>
        protected IGameConfigDataResolver _baselineGameConfigResolver;

        /// <summary>
        /// Null until model state is set up.
        /// </summary>
        protected Journal _journal;

        /// <summary>
        /// The shared and persisted entity state. This should not be modified directly and changes should be done via ModelActions instead in most cases.
        /// See Documentation for more information.
        /// <para>
        /// This field may not be accessed until Model is set up and Entity Initialization is completed, i.e. earliest in and after <see cref="OnEntityInitialized"/>.
        /// </para>
        /// </summary>
        protected TModel Model => _journal.StagedModel;

        MetaTime                _previousTickUpdateAt;
        bool                    _startedTickTimer;
        bool                    _hadClientsForTickTimer;
        CancellationTokenSource _tickTimerCts;
        readonly TimeSpan       _tickIntervalWhenNoClientsConnected;
        readonly TimeSpan       _tickIntervalWithClientsConnected;

        public class TickRateSetting
        {
            public TimeSpan WhenNoClientsConnected;
            public TimeSpan WithClientsConnected;

            /// <param name="whenNoClientsConnected">Tick interval when there are no clients connected</param>
            /// <param name="withClientsConnected">Tick interval when there are clients connected. If zero (default), the tick rate is set to the Models tick rate.</param>
            public TickRateSetting(TimeSpan whenNoClientsConnected, TimeSpan withClientsConnected = default)
            {
                WhenNoClientsConnected = whenNoClientsConnected;
                WithClientsConnected = withClientsConnected;
            }
        }

        /// <summary>
        /// Time interval determining how often the model is updated to run any pending Ticks(). If TicksPerSecond is 10 and
        /// this is 1s, the model (if idle) will be woken once a second to run on average 10 ticks. Defaults to 5 seconds.
        /// </summary>
        protected virtual TickRateSetting TickRate => new TickRateSetting(whenNoClientsConnected: TimeSpan.FromSeconds(5), withClientsConnected: TimeSpan.Zero);

        /// <summary>
        /// Determines if the Model should be ticked forward automatically.
        /// <para>
        /// Generally, there are only two reasons to set this to <c>false</c>:
        /// <list type="bullet">
        /// <item>Keeping this <c>false</c> until first player joins this Multiplayer Entity. This removes the need to specially handle Model before it has any Participants.</item>
        /// <item>Setting this <c>false</c> after last player leaves the Multiplayer Entity, or Multiplayer game is concluded. This saves resources as ticking is no longer useful.</item>
        /// </list>
        /// Defaults to true.
        /// </para>
        /// <para>
        /// If the value is changed from <c>false</c> to <c>true</c> at runtime, <see cref="StartTickTimer"/> must be called in order for SDK
        /// to observe the change.
        /// </para>
        /// </summary>
        protected virtual bool IsTicking => true;

        public enum DesyncDebuggingMode
        {
            /// <summary>
            /// No additional desync debugging.
            /// </summary>
            None,

            /// <summary>
            /// Per batch desync debugging. This is able to detect and identify the desync in a batches of action/ticks but is
            /// not able to detect the precise action/ticks causing the mismatch.
            /// </summary>
            PerBatch,

            /// <summary>
            /// Per operation desync debugging. This is able to detect and identify the precise action/ticks causing the mismatch.
            /// </summary>
            PerOperation,
        }

        /// <summary>
        /// Sets the extra debugging for checksum mismatches (desyncs). Enabling extra debugging requires recoding a trace of the Model which is
        /// expensive in both CPU and memory usage. This should only be enabled if issues occurs.
        /// <b>Note: </b> This value cannot be changed at runtime.
        /// </summary>
        protected virtual DesyncDebuggingMode DesyncDebugging => DesyncDebuggingMode.None;

        /// <param name="logChannelName">Name tag for the log messages from the Actor and the Model. Defaults to the type name of the <typeparamref name="TModel"/>.</param>
        protected PersistedMultiplayerEntityActorBase(EntityId entityId, string logChannelName = null) : base(entityId)
        {
            _modelLogChannel = CreateModelLogChannel(logChannelName ?? typeof(TModel).ToGenericTypeString());

            // Fetch current LogicVersion & GameConfigs
            _logicVersion               = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;
            _activeGameConfig           = GlobalStateProxyActor.ActiveGameConfig.Get();
            _baselineGameConfig         = _activeGameConfig.BaselineGameConfig;
            _baselineGameConfigResolver = _baselineGameConfig.SharedConfig;

            TickRateSetting tickRate = TickRate;
            _tickIntervalWhenNoClientsConnected = tickRate.WhenNoClientsConnected;
            _tickIntervalWithClientsConnected  = tickRate.WithClientsConnected;
        }

        protected override sealed async Task Initialize()
        {
            // Fetch state from database (at least an empty state must exist)
            TPersisted persisted = await MetaDatabase.Get().TryGetAsync<TPersisted>(_entityId.ToString());
            if (persisted == null)
                throw new InvalidOperationException($"Trying to initialize {GetType().ToGenericTypeString()} for whom no state exists in database. At least an empty {typeof(TPersisted).ToGenericTypeString()} must exist in database. A new entity can be created with DatabaseEntityUtil.CreateNewEntityAsync<{typeof(TPersisted).ToGenericTypeString()}>()");

            await InitializePersisted(persisted);

            UpdateInActiveEntitiesList();

            if (DesyncDebugging != DesyncDebuggingMode.None)
                StartPeriodicTimer(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), PruneDesyncSnapshotsCommand.Instance);

            await base.Initialize();
        }

        protected override sealed Task<TModel> InitializeNew()
        {
            // Entity must be set up via explicit setup.
            return Task.FromResult<TModel>(null);
        }

        protected override sealed Task<TModel> RestoreFromPersisted(TPersisted persisted)
        {
            TModel model = DeserializePersistedPayload<TModel>(persisted.Payload, _baselineGameConfigResolver, _logicVersion);
            return Task.FromResult(model);
        }

        protected override sealed async Task PostLoad(TModel model, DateTime persistedAt, TimeSpan elapsedTime)
        {
            _log.Info("PostLoad() for {EntityId} logicVersion={LogicVersion}, away for {AwayDuration} (since {PersistedAt})", _entityId, _logicVersion, elapsedTime, persistedAt);

            if (model == null)
            {
                // Uninitialized Entity. Keep journal null.
                return;
            }

            AssignBasicRuntimePropertiesToModel(model, _modelLogChannel);

            // Fast forward any time this entity was suspended
            model.ResetTime(MetaTime.FromDateTime(persistedAt) + MetaDuration.FromTimeSpan(elapsedTime));
            model.OnFastForwardTime(MetaDuration.FromTimeSpan(elapsedTime));

            OnFastForwardedTime(model, MetaDuration.FromTimeSpan(elapsedTime));

            // Reset clock and start from this point
            OnSwitchedToModelCore(model);
            ResetJournalToModel(model);

            // Initialization via wakeup
            await OnEntityInitialized();

            // Start ticking
            if (IsTicking)
                StartTickTimer();
        }

        /// <summary>
        /// Switches immediately to the given model (by calling <see cref="OnSwitchedToModel"/>) and creates a new journal for it. The model
        /// clock will be reset to MetaTime.now, and CurrentTick to 0.
        /// </summary>
        protected void SwitchToNewModelImmediately(TModel model)
        {
            OnSwitchedToModelCore(model);
            ResetJournalToModel(model);

            // Start ticking
            if (IsTicking)
                StartTickTimer();
        }

        /// <summary>
        /// Don't call directly. Use <see cref="SwitchToNewModelImmediately"/>. (<see cref="PostLoad"/> is a special case).
        /// </summary>
        void ResetJournalToModel(TModel model)
        {
            // Reset from the current position in time
            model.ResetTime(MetaTime.Now);

            _journal = new Journal(
                _modelLogChannel,
                model,
                enableConsistencyChecks: true);

            _desyncDebugTrace.Clear();

            // The model is now the currently-active `Model`.
            // Trigger event log flushing in case new events were logged in the model before it became the currently-active `Model`.
            // \todo: TryEnqueueTriggerEventLogFlushing();
        }

        void OnSwitchedToModelCore(TModel model)
        {
            AssignBasicRuntimePropertiesToModel(model, _modelLogChannel);

            // Forward to game.
            OnSwitchedToModel(model);
        }

        protected override sealed async Task PersistStateImpl(bool isInitial, bool isFinal)
        {
            MetaTime now = MetaTime.Now;
            _log.Debug("Persisting state (isInitial={IsInitial}, isFinal={IsFinal}, schemaVersion={SchemaVersion})", isInitial, isFinal, _entityConfig.CurrentSchemaVersion);

            OnBeforePersist();

            byte[] persistedPayload;
            if (_journal == null)
            {
                // If entity was not set up, there is nothing to save. Save null payload.
                persistedPayload = null;
            }
            else
            {
                // Entity has been set up. Serialize and validate the contents.

                // On final persist, ensure Model is up-to-date
                if (isFinal)
                {
                    MetaDuration elapsedTime = now - ModelUtil.TimeAtTick(Model.CurrentTick, Model.TimeAtFirstTick, Model.TicksPerSecond);
                    if (elapsedTime > MetaDuration.Zero)
                    {
                        _log.Debug("Fast-forwarding Model {ElapsedTime} before final persist", elapsedTime);
                        Model.ResetTime(now);
                        Model.OnFastForwardTime(elapsedTime);
                    }
                }

                // Serialize and compress the payload
                persistedPayload = SerializeToPersistedPayload<TModel>(Model, resolver: _baselineGameConfigResolver, logicVersion: null);
            }

            // Persist in database
            TPersisted persisted = CreatePersisted(
                persistedAt:        now.ToDateTime(),
                payload:            persistedPayload,
                schemaVersion:      _entityConfig.CurrentSchemaVersion,
                isFinal:            isFinal
            );
            await MetaDatabase.Get().UpdateAsync(persisted);

            UpdateInActiveEntitiesList();
        }
        protected virtual void OnBeforePersist()
        {
        }

        LogChannel CreateModelLogChannel(string name)
        {
            return new LogChannel(name, _log, MetaLogger.MetaLogLevelSwitch);
        }

        /// <summary>
        /// Assign to the model runtime properties that should always be present, such as Game Configs and logic versions.
        /// </summary>
        void AssignBasicRuntimePropertiesToModel(TModel model, LogChannel logChannel)
        {
            // \note ServerListener is not assigned here. That's only done when the model becomes the actor's current execution model (i.e. in OnSwitchedToModel).
            //       Here should be assigned runtime properties that we want to be always available in the model, even during MigrateState.
            model.LogicVersion          = _logicVersion;
            model.GameConfig            = _activeGameConfig.BaselineGameConfig.SharedConfig;
            model.Log                   = logChannel; // \note Assigning the specified log channel, which is not necessarily _modelLogChannel.
            // \todo: model.AnalyticsEventHandler = _analyticsEventHandler;
            model.ResetTime(MetaTime.Now);
        }

        [EntityAskHandler]
        async Task<InternalEntitySetupResponse> HandleInternalEntitySetupRequest(InternalEntitySetupRequest request)
        {
            if (_journal != null)
                throw new InternalEntitySetupRefusal();

            TModel model = new TModel();
            model.EntityId = _entityId;
            model.CreatedAt = MetaTime.Now;
            AssignBasicRuntimePropertiesToModel(model, _modelLogChannel);

            OnSwitchedToModelCore(model);
            await SetUpModelAsync(model, request.SetupParams);
            ResetJournalToModel(model);
            await PersistStateIntermediate();

            // Initialization with setup request
            await OnEntityInitialized();

            // Start ticking
            if (IsTicking)
                StartTickTimer();

            return new InternalEntitySetupResponse();
        }

        /// <summary>
        /// Updates the entity in Dashboard Active Entities List.
        /// </summary>
        protected void UpdateInActiveEntitiesList()
        {
            if (_journal == null)
            {
                // Not set up yet. Don't announce
                return;
            }

            IActiveEntityInfo info = CreateActiveEntityInfo();
            if (info != null)
                Context.System.EventStream.Publish(new ActiveEntityInfoEnvelope(info));
        }

        [EntityAskHandler]
        InternalEntityStateResponse HandleInternalEntityStateRequest(InternalEntityStateRequest _)
        {
            MetaSerialized<IModel> serialized;
            if (_journal == null)
            {
                // If the entity is not set up, return no state.
                serialized = default;
            }
            else
            {
                serialized = MetaSerialization.ToMetaSerialized<IModel>(Model, MetaSerializationFlags.IncludeAll, _logicVersion);
            }

            return new InternalEntityStateResponse(
                model:                  serialized,
                logicVersion:           _logicVersion,
                staticGameConfigId:     _activeGameConfig.BaselineStaticGameConfigId,
                dynamicGameConfigId:    _activeGameConfig.BaselineDynamicGameConfigId,
                specializationKey:      null);  // by convention, null specialization key means no specialization.
        }

        [EntityAskHandler]
        async Task<EntityAskOk> HandleInternalEntityAssociatedEntityRefusedRequest(EntityId sessionId, InternalEntityAssociatedEntityRefusedRequest request)
        {
            _log.Debug("Handling InternalEntityAssociatedEntityRefusedRequest from {Entity} with {ReasonType}.", request.AssociationRef.AssociatedEntity, request.Refusal.GetType().ToGenericTypeString());

            EntityId playerId = SessionIdUtil.ToPlayerId(sessionId);

            // Filter out stale requests already
            foreach (AssociatedEntityRefBase currentAssociation in GetSessionStartAssociatedEntities())
            {
                if (currentAssociation.AssociatedEntity != request.AssociationRef.AssociatedEntity)
                    continue;

                // \todo: check MemberInstanceId. If request is less, request was stale. If more, desynced and need to remove. For equal need to remove.

                bool handled = await OnAssociatedEntityRefusalAsync(playerId, request.AssociationRef, request.Refusal);
                if (!handled)
                    _log.Warning("Unhandled association refusal from {Entity}. Actor.OnAssociatedEntityRefusalAsync", request.AssociationRef.AssociatedEntity);

                await PersistStateIntermediate();
                return EntityAskOk.Instance;
            }

            _log.Warning("Ignoring stale InternalEntityAssociatedEntityRefusedRequest from {Entity}. There was no such entity associated.", request.AssociationRef.AssociatedEntity);
            return EntityAskOk.Instance;
        }

        protected override async Task<MetaMessage> OnNewSubscriber(EntitySubscriber subscriber, MetaMessage message)
        {
            if (subscriber.Topic == EntityTopic.Participant && message is InternalEntitySubscribeRequestBase request)
            {
                if (_journal == null)
                {
                    _log.Warning("Entity {SourceEntity} attempted to subscribe to an Entity that was not set up yet (by sending InternalEntitySetupRequest). Refusing with TryAgain.", subscriber.EntityId);
                    throw new InternalEntitySubscribeRefusedBase.Builtins.NotAParticipant();
                }

                EntityId playerId = SessionIdUtil.ToPlayerId(subscriber.EntityId);
                await OnClientSessionHandshake(sessionId: subscriber.EntityId, playerId, request);

                List<AssociatedEntityRefBase> associatedEntities = GetSessionStartAssociatedEntities();

                // Check resource proposal, if such was given. Session start has a resource proposal but for mid-session joins
                // the client loads the config during session.
                if (request.ResourceProposal.HasValue)
                {
                    SessionProtocol.SessionResourceCorrection resourceCorrection = GetNewSessionResourceCorrection(request.AssociationRef, request.ResourceProposal.Value, request.SupportedArchiveCompressions);
                    if (resourceCorrection.HasAnyCorrection())
                        throw new InternalEntitySubscribeRefusedBase.Builtins.ResourceCorrection(resourceCorrection, associatedEntities);
                }
                if (request.IsDryRun)
                    throw new InternalEntitySubscribeRefusedBase.Builtins.DryRunSuccess(associatedEntities);

                InternalEntitySubscribeResponseBase response = await OnClientSessionStart(sessionId: subscriber.EntityId, playerId, request, associatedEntities);
                SetClientPeer(playerId, request, response);

                UpdateInActiveEntitiesList();

                // Other clients could have pending unflushed actions. To avoid having the clients in the different steps, flush pending ops of other clients.
                FlushActions();

                // Check if we are ticking after the session subscribed.
                _ = ExecuteOnActorContextAsync(() =>
                {
                    if (IsTicking)
                        StartTickTimer();
                });

                return response;
            }

            _log.Warning("Subscriber {EntityId} on unknown topic [{Topic}]", subscriber.EntityId, subscriber.Topic);
            throw new InvalidOperationException();
        }

        protected override void OnSubscriberLost(EntitySubscriber subscriber)
        {
            if (subscriber.Topic == EntityTopic.Participant)
                OnParticipantSessionEndedCore(subscriber);
        }

        protected override void OnSubscriberKicked(EntitySubscriber subscriber, MetaMessage message)
        {
            if (subscriber.Topic == EntityTopic.Participant)
                OnParticipantSessionEndedCore(subscriber);
        }

        void OnParticipantSessionEndedCore(EntitySubscriber session)
        {
            // \todo: move this "ended" callback into EntityActor
            OnParticipantSessionEnded(session);
            ClearClientState(SessionIdUtil.ToPlayerId(session.EntityId));
        }

        /// <summary>
        /// Creates a config correction if such is necessary.
        /// </summary>
        /// <param name="association">Information how a source entity is associated with this entity.</param>
        SessionProtocol.SessionResourceCorrection GetNewSessionResourceCorrection(AssociatedEntityRefBase association, SessionProtocol.SessionResourceProposal proposal, CompressionAlgorithmSet supportedArchiveCompressions)
        {
            ContentHash                                 proposalVersion = proposal.ConfigVersions.GetValueOrDefault(association.GetClientSlot());
            SessionProtocol.SessionResourceCorrection   correction      = new SessionProtocol.SessionResourceCorrection();

            if (proposalVersion != _activeGameConfig.ClientSharedGameConfigContentHash)
            {
                SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo updateInfo = _activeGameConfig.BaselineGameConfigSharedConfigDeliverySources.GetCorrection(supportedArchiveCompressions);
                correction.ConfigUpdates[association.GetClientSlot()] = updateInfo;
            }

            return correction;
        }

        /// <summary>
        /// Publishes the message to the clients that are online. The message is delivered to the corresponding Entity
        /// on a client. To handle the message, a listener needs to be attached to the EntityClient's MessageDispatcher.
        /// </summary>
        protected void SendToAllClientEntities(MetaMessage message, EntitySubscriber excludingSession = null)
        {
            MetaSerialized<MetaMessage> contents = default;
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (subscriber == excludingSession)
                    continue;
                if (subscriber.Topic != EntityTopic.Participant)
                    continue;

                ClientPeerState peer = TryGetClientPeer(SessionIdUtil.ToPlayerId(subscriber.EntityId));
                if (peer == null)
                    continue;

                // Serialize lazily.
                if (contents.IsEmpty)
                    contents = MetaSerialization.ToMetaSerialized<MetaMessage>(message, MetaSerializationFlags.SendOverNetwork, _logicVersion);

                SendMessage(subscriber, new EntityServerToClientEnvelope(peer.ClientChannelId, contents));
            }
        }

        /// <summary>
        /// Publishes the message to the specified client. The message is delivered to the corresponding Entity on a
        /// client. To handle the message, a listener needs to be attached to the EntityClient's MessageDispatcher.
        /// </summary>
        protected void SendToClientEntity(EntitySubscriber session, MetaMessage message)
        {
            ClientPeerState peer = TryGetClientPeer(SessionIdUtil.ToPlayerId(session.EntityId));
            if (peer != null)
                SendMessage(session, new EntityServerToClientEnvelope(peer.ClientChannelId, MetaSerialization.ToMetaSerialized<MetaMessage>(message, MetaSerializationFlags.SendOverNetwork, _logicVersion)));
        }

        /// <summary>
        /// Publishes the message to the active sessions (SessionActor of the clients that are online).
        /// </summary>
        protected void SendToAllSessions(MetaMessage message)
        {
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (subscriber.Topic != EntityTopic.Participant)
                    continue;

                ClientPeerState peer = TryGetClientPeer(SessionIdUtil.ToPlayerId(subscriber.EntityId));
                if (peer == null)
                    continue;

                SendMessage(subscriber, message);
            }
        }

        /// <summary>
        /// Publishes the message to the entities in the given ClientSlots of the active sessions (EntityActors of the given slots of the clients that are online).
        /// </summary>
        protected void SendToAllSessions(OrderedSet<ClientSlot> targetSlots, MetaMessage message)
        {
            SendToAllSessions(new InternalSessionEntityBroadcastMessage(targetSlots, message));
        }

        /// <inheritdoc cref="SendToAllSessions(OrderedSet{ClientSlot}, MetaMessage)"/>
        protected void SendToAllSessions(ClientSlot targetSlot, MetaMessage message) => SendToAllSessions(new OrderedSet<ClientSlot>() { targetSlot }, message);

        /// <inheritdoc cref="SendToAllSessions(OrderedSet{ClientSlot}, MetaMessage)"/>
        protected void SendToAllSessions(ClientSlot targetSlot1, ClientSlot targetSlot2, MetaMessage message) => SendToAllSessions(new OrderedSet<ClientSlot>() { targetSlot1, targetSlot2 }, message);

        /// <summary>
        /// Helper for creating <see cref="EntitySerializedState"/> of the current state. If <paramref name="memberId"/> is given,
        /// the state contains the Private state of the member, as given by <see cref="IMultiplayerModel.GetMemberPrivateState(EntityId)"/>.
        /// Note that while Member is usually the player, this does not need to be the case.
        /// </summary>
        protected EntitySerializedState CreateSerializedStateForSubscriber(EntityId? memberId = null)
        {
            // Snapshot of the state
            MetaSerialized<IMultiplayerModel> publicData = MetaSerialization.ToMetaSerialized<IMultiplayerModel>(Model, MetaSerializationFlags.SendOverNetwork, _logicVersion);
            MetaSerialized<MultiplayerMemberPrivateStateBase> memberData  = default;
            if (memberId != null)
            {
                MultiplayerMemberPrivateStateBase memberDataState = Model.GetMemberPrivateState(memberId.Value);
                if (memberDataState != null)
                    memberData = MetaSerialization.ToMetaSerialized(memberDataState, MetaSerializationFlags.SendOverNetwork, _logicVersion);
            }

            EntitySerializedState state = new EntitySerializedState(
                publicState:                publicData,
                memberPrivateState:         memberData,
                currentOperation:           _journal.StagedPosition.Operation,
                logicVersion:               _logicVersion,
                sharedGameConfigVersion:    _activeGameConfig.ClientSharedGameConfigContentHash,
                sharedConfigPatchesVersion: ContentHash.None,
                activeExperiments:          Array.Empty<EntityActiveExperiment>());
            return state;
        }

        /// <summary>
        /// Adds an association with an entity to all ongoing sessions. The associated entity will
        /// be subscribed-to by the session and the state and updates will be delivered to the client.
        /// If there is already a previous association on the <see cref="ClientSlot"/> set by this
        /// entity, the previous association is removed first. It is an error to replace an association
        /// on a slot set by some other Entity.
        /// </summary>
        protected void AddEntityAssociation(AssociatedEntityRefBase association)
        {
            SendToAllSessions(new InternalSessionEntityAssociationUpdate(association.GetClientSlot(), association));
        }

        /// <summary>
        /// Removes association on the given slot from the current sessions. The entity will no longer visible to
        /// clients. It is an error to remove an association set by some other entity.
        /// </summary>
        protected void RemoveEntityAssociation(ClientSlot slot)
        {
            SendToAllSessions(new InternalSessionEntityAssociationUpdate(slot, null));
        }

        #region Action & Tick Logic

        /// <summary>
        /// Starts tick timer if it wasn't started already. This must be called if <see cref="IsTicking"/> is set to true.
        /// </summary>
        protected void StartTickTimer()
        {
            if (!_startedTickTimer)
            {
                _startedTickTimer     = true;
                _previousTickUpdateAt = MetaTime.Now;
                _tickTimerCts         = new CancellationTokenSource();
            }
            else
            {
                // If there is already a timer, and conditions haven't changed, ignore.
                if (_hadClientsForTickTimer == HasClientsConnected())
                    return;

                // Conditions have changed. Cancel previous timer and schedule a new.
                _tickTimerCts.Cancel();
                _tickTimerCts = new CancellationTokenSource();
            }

            ReScheduleNextTickUpdate();
        }

        void ReScheduleNextTickUpdate()
        {
            Action scheduledTask = () =>
            {
                UpdateTicks();
                FlushActions();

                if (IsShutdownEnqueued)
                    return;

                ReScheduleNextTickUpdate();
            };

            bool hasClients = HasClientsConnected();
            TimeSpan interval = hasClients ? _tickIntervalWithClientsConnected : _tickIntervalWhenNoClientsConnected;
            DateTime nextUpdateAt;
            if (interval != TimeSpan.Zero)
                nextUpdateAt = DateTime.UtcNow + interval;
            else
                nextUpdateAt = ModelUtil.TimeAtTick(Model.CurrentTick + 1, Model.TimeAtFirstTick, Model.TicksPerSecond).ToDateTime() - MetaTime.DebugTimeOffset.ToTimeSpan();

            _hadClientsForTickTimer = hasClients;
            ScheduleExecuteOnActorContext(nextUpdateAt, scheduledTask, _tickTimerCts.Token);
        }

        bool HasClientsConnected()
        {
            // \todo: Replace this with EnumerateClients().Any() and remove this copy-pasta pattern everywhere.
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (subscriber.Topic != EntityTopic.Participant)
                    continue;
                if (subscriber.EntityId.Kind != EntityKindCore.Session)
                    continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Runs any pending ticks, i.e ticks accured from from last tick flush point to the current point in time,
        /// and then flush pending timeline updates to all clients.
        /// </summary>
        protected void UpdateTicksAndFlush()
        {
            UpdateTicks();
            FlushActions();
        }

        [CommandHandler]
        void HandleFlushActionsCommand(FlushActionsCommand _)
        {
            FlushActions();
        }

        void EnqueueFlush()
        {
            _self.Tell(FlushActionsCommand.Instance, null);
        }

        bool _isUpdatingTicks = false;

        /// <summary>
        /// Executes given action and informs clients.
        /// </summary>
        /// <param name="action">Action to execute.</param>
        /// <param name="runPendingTicksFirst">True, if any pending Ticks should be enqueued first. True by default.</param>
        protected void ExecuteAction(TAction action, bool runPendingTicksFirst = true)
        {
            if (runPendingTicksFirst)
                UpdateTicks();

            StageActionOnJournal(action);
            EnqueueFlush();
        }

        void StageActionOnJournal(TAction action)
        {
            _log.Verbose("Execute action (tick {Tick}): {Action}", Model.CurrentTick, PrettyPrint.Compact(action));
            JournalPosition positionBefore = _journal.StagedPosition;
            _journal.StageAction(action);
            AfterJournalModification(positionBefore);
        }

        void UpdateTicks()
        {
            // Catch recursive tick update (in case ExecuteAction was called during tick update)
            if (_isUpdatingTicks)
                throw new InvalidOperationException("UpdateTicks called recursively, likely through ExecuteAction(action, true).");

            if (!IsTicking)
                return;

            // Ticking was turned on during runtime (i.e. not during actor initialization).
            if (!_startedTickTimer)
                StartTickTimer();

            MetaTime lastTime = _previousTickUpdateAt;
            _previousTickUpdateAt = MetaTime.Now;

            long lastTotalTicks    = ModelUtil.TotalNumTicksElapsedAt(lastTime, Model.TimeAtFirstTick, Model.TicksPerSecond);
            long currentTotalTicks = ModelUtil.TotalNumTicksElapsedAt(_previousTickUpdateAt, Model.TimeAtFirstTick, Model.TicksPerSecond);
            long newTicks          = currentTotalTicks - lastTotalTicks;

            if (newTicks == 0)
                return;

            _log.Verbose("Simulating {NumTicks} ticks (from {FromTick} to {ToTick})", newTicks, lastTotalTicks, currentTotalTicks);

            _isUpdatingTicks = true;
            try
            {
                // Execute ticks
                for (int tick = 0; tick < newTicks; tick++)
                {
                    JournalPosition positionBefore = (_journal.StagedPosition.Operation == 0) ? _journal.StagedPosition : JournalPosition.NextTick(_journal.StagedPosition);
                    _journal.StageTick();
                    AfterJournalModification(positionBefore);
                    OnPostTick(positionBefore.Tick);
                }
            }
            finally
            {
                _isUpdatingTicks = false;
            }

            // Promote all post-tick pending markers to wait for the flush
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (subscriber.Topic != EntityTopic.Participant)
                    continue;
                if (subscriber.EntityId.Kind != EntityKindCore.Session)
                    continue;
                ClientPeerState peer = TryGetClientPeer(SessionIdUtil.ToPlayerId(subscriber.EntityId));
                if (peer == null)
                    continue;

                peer.PingMarkersWaitingForFlush.AddRange(peer.PingMarkersWaitingForTick);
                peer.PingMarkersWaitingForTick.Clear();
            }
        }

        void AfterJournalModification(JournalPosition positionBefore)
        {
            // \todo: This should be a timeline listener
            // In per-op debugging mode, keep a trace of model snapshots
            if (DesyncDebugging == DesyncDebuggingMode.PerOperation)
            {
                using (SegmentedIOBuffer tempBuffer = new SegmentedIOBuffer(segmentSize: 4096))
                {
                    _ = _journal.ForceComputeChecksum(_journal.StagedPosition, tempBuffer);
                    _desyncDebugTrace.Enqueue(new DesyncTraceEntry(MetaTime.Now, positionBefore, _journal.StagedPosition, tempBuffer.ToArray()));
                }
            }
        }

        void FlushActions()
        {
            EntityTimelineUpdateMessage update = TryGatherFlushActions();
            if (update != null)
            {
                SendToAllClientEntities(update);

                // Post flush markers
                foreach (EntitySubscriber subscriber in _subscribers.Values)
                {
                    if (subscriber.Topic != EntityTopic.Participant)
                        continue;
                    if (subscriber.EntityId.Kind != EntityKindCore.Session)
                        continue;
                    ClientPeerState peer = TryGetClientPeer(SessionIdUtil.ToPlayerId(subscriber.EntityId));
                    if (peer == null)
                        continue;
                    foreach (uint marker in peer.PingMarkersWaitingForFlush)
                        SendToClientEntity(subscriber, new EntityTimelinePingTraceMarker(marker, EntityTimelinePingTraceMarker.TracePosition.AfterNextTick));
                    peer.PingMarkersWaitingForFlush.Clear();
                }
            }
        }

        EntityTimelineUpdateMessage TryGatherFlushActions()
        {
            var               walker        = _journal.WalkJournal(from: _journal.CheckpointPosition);
            JournalPosition   gatherEnd     = JournalPosition.Epoch;
            List<ModelAction> operations    = new List<ModelAction>();
            List<uint>        opChecksums   = null;
            JournalPosition   startPosition = default;
            bool              isFirstOp     = true;

            // In per-operation mode, we have per operation checksums
            if (DesyncDebugging == DesyncDebuggingMode.PerOperation)
                opChecksums = new List<uint>();

            while (walker.MoveNext())
            {
                if (isFirstOp && (walker.IsTickFirstStep || walker.IsActionFirstStep))
                {
                    isFirstOp = false;
                    startPosition = walker.PositionBefore;
                }

                if (walker.IsTickFirstStep)
                {
                    operations.Add(null);
                    opChecksums?.Add(walker.ComputedChecksumAfter);

                    for (int i = 0; i < walker.NumStepsTotal - 1; ++i)
                    {
                        walker.MoveNext();
                        opChecksums?.Add(walker.ComputedChecksumAfter);
                    }
                }
                else if (walker.IsActionFirstStep)
                {
                    operations.Add(walker.Action);
                    opChecksums?.Add(walker.ComputedChecksumAfter);

                    for (int i = 0; i < walker.NumStepsTotal - 1; ++i)
                    {
                        walker.MoveNext();
                        opChecksums?.Add(walker.ComputedChecksumAfter);
                    }
                }

                gatherEnd = walker.PositionAfter;
            }

            if (operations.Count > 0)
            {
                uint finalChecksum;

                if (DesyncDebugging == DesyncDebuggingMode.PerBatch)
                {
                    // In batch debugging mode, keep a trace of model snapshots
                    using (SegmentedIOBuffer tempBuffer = new SegmentedIOBuffer(segmentSize: 4096))
                    {
                        finalChecksum = _journal.ForceComputeChecksum(gatherEnd, tempBuffer);
                        _desyncDebugTrace.Enqueue(new DesyncTraceEntry(MetaTime.Now, startPosition, gatherEnd, tempBuffer.ToArray()));
                    }
                }
                else if (DesyncDebugging == DesyncDebuggingMode.PerOperation)
                {
                    // In per-operation debugging mode, we already have checksums
                    finalChecksum = opChecksums[opChecksums.Count - 1];
                }
                else
                {
                    // In normal mode, compute (only) the checksum now.
                    finalChecksum = _journal.ForceComputeChecksum(gatherEnd);
                }

                _journal.CaptureStageSnapshot();
                _journal.Commit(gatherEnd);

                uint[] debugPerOperationChecksums = opChecksums == null ? null : opChecksums.ToArray();
                return new EntityTimelineUpdateMessage(operations, finalChecksum, debugPerOperationChecksums);
            }
            return null;
        }

        [PubSubMessageHandler]
        void HandleEntityEnqueueActionsRequest(EntitySubscriber session, EntityEnqueueActionsRequest request)
        {
            EntityId playerId = SessionIdUtil.ToPlayerId(session.EntityId);
            ClientPeerState client = TryGetClientPeer(playerId);
            if (client == null)
            {
                _log.Warning("Got EntityEnqueueActionsRequest from non-active player session {PlayerId}. Ignoring.", playerId);
                return;
            }

            UpdateTicks();

            foreach (ModelAction untypedAction in request.Actions)
            {
                // not allowed, but don't trust input
                if (untypedAction == null)
                {
                    _log.Warning("Got EntityEnqueueActionsRequest from {PlayerId} with null action. Ignoring.", playerId);
                    continue;
                }

                TAction typedAction = untypedAction as TAction;
                if (typedAction == null)
                {
                    _log.Warning("Got EntityEnqueueActionsRequest from {PlayerId} with unsupported action type {ActionType} (expected {ExpectedType}). Ignoring.", playerId, untypedAction.GetType().ToGenericTypeString(), typeof(TAction).ToGenericTypeString());
                    continue;
                }

                // Validation
                if (!ValidateClientOriginatingActionCore(client, typedAction))
                {
                    _log.Warning("Validation failed for action by {PlayerId}, ignored. Action: {Action}", playerId, PrettyPrint.Compact(typedAction));

                    // skip
                    continue;
                }

                // Dry run
                MetaActionResult dryRunResult = ModelUtil.DryRunAction(Model, typedAction);
                if (dryRunResult != MetaActionResult.Success)
                {
                    // only on debug level. This is expected due to races.
                    _log.Debug("Failed to execute action by {PlayerId}, ignored. Action: {Action}. Result: {Result}", playerId, PrettyPrint.Compact(typedAction), dryRunResult);

                    // skip
                    continue;
                }

                StageActionOnJournal(typedAction);
            }
            EnqueueFlush();
        }

        bool ValidateClientOriginatingActionCore(ClientPeerState client, TAction action)
        {
            ModelActionSpec actionSpec = ModelActionRepository.Instance.SpecFromType[action.GetType()];

            // Must be client-issuable and enqueable.
            if (!actionSpec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.FollowerSynchronized))
            {
                _log.Warning("Client tried to enqueue Action {Action} which does not have FollowerSynchronized mode. ExecuteFlags={ExecuteFlags}.", action.GetType().ToGenericTypeString(), actionSpec.ExecuteFlags);
                return false;
            }

            bool isDevelopmentOnlyAction = actionSpec.HasCustomAttribute<DevelopmentOnlyActionAttribute>();
            if (isDevelopmentOnlyAction)
            {
                _log.Info("Executing development-only action: {Action}", action.GetType().ToGenericTypeString());

                EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
                if (!envOpts.EnableDevelopmentFeatures && !GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(client.PlayerId))
                {
                    _log.Warning("Client tried to run development-only action {Action}, but Development-Only actions are not enabled.", action.GetType().ToGenericTypeString());
                    return false;
                }
            }

            return ValidateClientOriginatingAction(client, action);
        }

        #endregion

        #region Desync Debugging

        readonly struct DesyncTraceEntry
        {
            public readonly MetaTime        CreatedAt;
            public readonly JournalPosition StartPosition;
            public readonly JournalPosition EndPosition;
            public readonly byte[]          ChecksumBuffer;

            public DesyncTraceEntry(MetaTime createdAt, JournalPosition startPosition, JournalPosition endPosition, byte[] checksumBuffer)
            {
                CreatedAt = createdAt;
                StartPosition = startPosition;
                EndPosition = endPosition;
                ChecksumBuffer = checksumBuffer;
            }
        }
        Queue<DesyncTraceEntry> _desyncDebugTrace = new Queue<DesyncTraceEntry>();

        [CommandHandler]
        void HandlePruneDesyncSnapshotsCommand(PruneDesyncSnapshotsCommand _)
        {
            MetaTime pruneOlderThan = MetaTime.Now - MetaDuration.FromSeconds(5);
            for (;;)
            {
                if (!_desyncDebugTrace.TryPeek(out DesyncTraceEntry entry))
                    break;
                if (entry.CreatedAt >= pruneOlderThan)
                    break;
                _desyncDebugTrace.Dequeue();
            }
        }

        [MessageHandler]
        void HandleEntityChecksumMismatchDetails(EntityId sessionId, EntityChecksumMismatchDetails details)
        {
            EntityId playerId = SessionIdUtil.ToPlayerId(sessionId);
            if (DesyncDebugging == DesyncDebuggingMode.None)
            {
                _log.Debug("Client {PlayerId} reported checksum mismatch, but mismatch debugging is disabled. Ignored.", playerId);
                return;
            }

            JournalPosition detailsPosition = JournalPosition.FromTickOperationStep(details.Tick, details.Operation, 0);
            foreach (DesyncTraceEntry entry in _desyncDebugTrace)
            {
                if (entry.StartPosition > detailsPosition)
                    break;
                if (entry.EndPosition <= detailsPosition)
                    continue;

                _log.Warning("Client {PlayerId} reported checksum mismatch", playerId);
                if (entry.StartPosition != detailsPosition || entry.EndPosition.Operation != entry.StartPosition.Operation + 1)
                    _log.Warning("Warning. Match in debug trace is not exact and contains additional changes");

                SerializedObjectComparer comparer = new SerializedObjectComparer();
                comparer.FirstName = "Expected";
                comparer.SecondName = $"Client (desynced, {playerId})";
                comparer.Type = typeof(IModel);
                string result = comparer.Compare(entry.ChecksumBuffer, details.ChecksumBuffer).Description;
                _log.Warning("{0}", result);
                return;
            }
            _log.Debug("Client {PlayerId} reported checksum mismatch at position {Position} but could not find the position in debug trace. Trace could have been already pruned.", playerId, detailsPosition);
        }

        #endregion

        #region Session Client State

        /// <summary>
        /// Per-Session state to identify the client-side peer of the communications. Implementation may inherit this class to
        /// extend it and then override <see cref="CreateClientPeer"/> with custom implementation.
        /// </summary>
        protected class ClientPeerState
        {
            public readonly ClientSlot ClientSlot;
            public readonly int ClientChannelId;
            public readonly EntityId PlayerId;
            public readonly List<uint> PingMarkersWaitingForTick = new List<uint>();
            public readonly List<uint> PingMarkersWaitingForFlush = new List<uint>();

            public ClientPeerState(ClientSlot clientSlot, int clientChannelId, EntityId playerId)
            {
                ClientSlot = clientSlot;
                ClientChannelId = clientChannelId;
                PlayerId = playerId;
            }
        }

        Dictionary<EntityId, ClientPeerState> _defaultSessionClientStateStorage = new Dictionary<EntityId, ClientPeerState>();

        /// <summary>
        /// Gets the Client-side Session State for a subscriber client, or null if the subscriber is not an online session.
        /// <para>
        /// Note that the <paramref name="playerId"/> is a Player Id and not a Session Id. See <see cref="SessionIdUtil.ToPlayerId(EntityId)"/>.
        /// </para>
        /// </summary>
        protected ClientPeerState TryGetClientPeer(EntityId playerId)
        {
            if (playerId.Kind != EntityKindCore.Player)
                throw new ArgumentException($"Expected Player entity kind, got {playerId.Kind}", nameof(playerId));

            if (!_defaultSessionClientStateStorage.TryGetValue(playerId, out ClientPeerState sessionState))
                return null;
            return sessionState;
        }

        /// <summary>
        /// Create the Client Peer State for new Session. Implementation may override this to add
        /// custom data into session state by extending <see cref="ClientPeerState"/>.
        /// </summary>
        protected virtual ClientPeerState CreateClientPeer(EntityId playerId, InternalEntitySubscribeRequestBase requestBase, InternalEntitySubscribeResponseBase responseBase)
        {
            return new ClientPeerState(requestBase.AssociationRef.GetClientSlot(), requestBase.ClientChannelId, playerId);
        }

        void SetClientPeer(EntityId playerId, InternalEntitySubscribeRequestBase request, InternalEntitySubscribeResponseBase response)
        {
            _defaultSessionClientStateStorage.Add(playerId, CreateClientPeer(playerId, request, response));
        }

        void ClearClientState(EntityId playerId)
        {
            _ = _defaultSessionClientStateStorage.Remove(playerId);
        }

        #endregion

        #region Ping Measurement

        [PubSubMessageHandler]
        void HandleEntityTimelinePingTraceQuery(EntitySubscriber session, EntityTimelinePingTraceQuery request)
        {
            ClientPeerState peer = TryGetClientPeer(SessionIdUtil.ToPlayerId(session.EntityId));
            if (peer == null)
            {
                _log.Warning("Received EntityTimelinePingTraceQuery from a non-channel peer: {Source}", session);
                return;
            }

            // Reply immediately
            SendToClientEntity(session, new EntityTimelinePingTraceMarker(request.Id, EntityTimelinePingTraceMarker.TracePosition.MessageReceivedOnEntity));

            // Enqueue reply after next tick
            peer.PingMarkersWaitingForTick.Add(request.Id);
        }

        #endregion

        [PubSubMessageHandler]
        async Task HandleClientToEntityEnvelope(EntitySubscriber session, EntityClientToServerEnvelope envelope)
        {
            EntityId playerId = SessionIdUtil.ToPlayerId(session.EntityId);
            ClientPeerState client = TryGetClientPeer(playerId);
            if (client == null)
            {
                _log.Warning("Got ClientToEntityEnvelope from non-active player session {PlayerId}. Ignoring.", playerId);
                return;
            }

            // Unwrap envelopes and re-dispatch to handlers transparently.
            // \todo: Don't abuse Entity-dispatcher this way. Custom envelope handlers that could deliver ClientPeerState?
            MetaMessage contents = envelope.Message.Deserialize(_baselineGameConfigResolver, _logicVersion);
            Type msgType = contents.GetType();

            if (_dispatcher.TryGetPubSubSubscriberDispatchFunc(msgType, out var pubSubDispatchFunc))                await pubSubDispatchFunc(this, session, contents).ConfigureAwait(false);
            else if (_dispatcher.TryGetMessageDispatchFunc(msgType, out var msgDispatchFunc))                       await msgDispatchFunc(this, session.EntityId, contents).ConfigureAwait(false);
            else if (_dispatcher.TryGetPubSubSubscriberDispatchFunc(typeof(MetaMessage), out pubSubDispatchFunc))   await pubSubDispatchFunc(this, session, contents).ConfigureAwait(false);
            else                                                                                                    await HandleUnknownMessage(session.EntityId, contents).ConfigureAwait(false);
        }

        #region Callbacks to userland

        /// <summary>
        /// Called when entity is set up for the first time, or is woken up from a persisted state after being set up. During and after this call, the <c>Model</c>
        /// exists and has been set up with the <see cref="SetUpModelAsync"/>.
        /// </summary>
        protected virtual Task OnEntityInitialized() => Task.CompletedTask;

        /// <summary>
        /// Called when the entity is being set up the first time with <see cref="MultiplayerEntitySetUpRequest"/>. Implementation should use the <paramref name="setupParams"/>
        /// to set up the <paramref name="model"/>. The given model is just initialized and does not need to be cleaned up before setup.
        /// </summary>
        protected abstract Task SetUpModelAsync(TModel model, IMultiplayerEntitySetupParams setupParams);

        /// <summary>
        /// Creates a <typeparamref name="TPersisted"/> representation of the current Model with the given arguments. The current
        /// model is given in <paramref name="payload"/> and will be <c>null</c> if the entity is not set up yet.
        /// </summary>
        protected virtual TPersisted CreatePersisted(DateTime persistedAt, byte[] payload, int schemaVersion, bool isFinal)
        {
            TPersisted persisted = new TPersisted();
            persisted.EntityId      = _entityId.ToString();
            persisted.PersistedAt   = persistedAt;
            persisted.Payload       = payload;
            persisted.SchemaVersion = schemaVersion;
            persisted.IsFinal       = isFinal;
            return persisted;
        }

        /// <summary>
        /// Called when new model instance is becoming active. Callee should should set up listeners.
        /// After switching to the model is complete, the <see cref="_journal"/> will be
        /// set to track it and <see cref="Model"/> can be used to refer to it.
        ///
        /// This function is called during entity initialization, after state reset, and in
        /// <see cref="SwitchToNewModelImmediately(TModel)"/>.
        /// </summary>
        protected virtual void OnSwitchedToModel(TModel model) { }

        /// <summary>
        /// Called after each model tick.
        /// </summary>
        /// <param name="tick"></param>
        protected virtual void OnPostTick(int tick) { }

        /// <summary>
        /// Called after the model has been fast forwarded in PostLoad.
        /// </summary>
        protected virtual void OnFastForwardedTime(TModel model, MetaDuration elapsed) { }

        /// <summary>
        /// Creates the Active Entity info for the Entity. This information is shown in Dashboard in Recently Active Entities
        /// list. If the default values need to be extended, implement <see cref="IActiveEntityInfo"/> in a custom class and construct
        /// such class here. Returning <c>null</c> means the entity will not be added to the Recently Active list.
        /// </summary>
        protected virtual IActiveEntityInfo CreateActiveEntityInfo()
        {
            return new DefaultMultiplayerEntityActiveEntityInfo(
                entityId:       _entityId,
                activityAt:     MetaTime.Now,
                createdAt:      Model.CreatedAt,
                displayName:    Model.GetDisplayNameForDashboard());
        }

        /// <summary>
        /// Called when session (on Participant topic channel) is kicked, terminated, or unsubscribes from this entity.
        /// </summary>
        protected virtual void OnParticipantSessionEnded(EntitySubscriber session) { }

        /// <summary>
        /// Called when Session (i.e. client, i.e. user) subscribes to this Entity. Implementation should check the subscription requirements and throw
        /// an <see cref="InternalEntitySubscribeRefusedBase"/> if there if a failure. For example, entity should check the player is (still) a
        /// participant in this entity. This handshake may not lead into a successful session creation (via <see cref="OnClientSessionStart"/>.
        /// <para>
        /// SDK handles Resource Correction and DryRuns automatically.
        /// </para>
        /// </summary>
        /// <param name="sessionId">The EntityID of the session actor subscribing.</param>
        /// <param name="playerId">The PlayerId of the session subscribing.</param>
        /// <param name="subscribe">The Subscribe payload from session.</param>
        protected virtual Task OnClientSessionHandshake(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase) => Task.CompletedTask;

        /// <summary>
        /// Called when Session (i.e. client, i.e. user) subscribes into this Entity and the requirements have been checked (in <see cref="OnClientSessionHandshake"/>.
        /// Implementation should respond with the entity state. See also <see cref="InternalEntitySubscribeResponseBase.Default"/> for default type implementation and
        /// <see cref="CreateSerializedStateForSubscriber"/> helper. To override only the channel context data, see <see cref="CreateClientSessionStartChannelContext"/>.
        /// </summary>
        /// <param name="sessionId">The EntityId of the session actor subscribing.</param>
        /// <param name="playerId">The PlayerId of the session subscribing.</param>
        /// <param name="requestBase">The Subscribe payload from session.</param>
        /// <param name="associatedEntities">The set of associated entities as returned by <see cref="GetSessionStartAssociatedEntities"/></param>
        protected virtual Task<InternalEntitySubscribeResponseBase> OnClientSessionStart(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase, List<AssociatedEntityRefBase> associatedEntities)
        {
            EntitySerializedState serialized = CreateSerializedStateForSubscriber(memberId: playerId);
            return Task.FromResult<InternalEntitySubscribeResponseBase>(new InternalEntitySubscribeResponseBase.Default(
                new EntityInitialState(
                    state:          serialized,
                    channelId:      requestBase.ClientChannelId,
                    contextData:    CreateClientSessionStartChannelContext(sessionId, playerId, requestBase, associatedEntities)),
                associatedEntities));
        }

        /// <summary>
        /// Called by default <see cref="OnClientSessionStart"/> implementation to create entity channel context. Implementation may override this method to
        /// use custom context for additional custom payload.
        /// </summary>
        /// <param name="sessionId">The EntityId of the session actor subscribing.</param>
        /// <param name="playerId">The PlayerId of the session subscribing.</param>
        /// <param name="requestBase">The Subscribe payload from session.</param>
        /// <param name="associatedEntities">The set of associated entities as returned by <see cref="GetSessionStartAssociatedEntities"/></param>
        protected virtual ChannelContextDataBase CreateClientSessionStartChannelContext(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase, List<AssociatedEntityRefBase> associatedEntities)
        {
            return new ChannelContextDataBase.Default(clientSlot: requestBase.AssociationRef.GetClientSlot());
        }

        /// <summary>
        /// Validates the client submitted actions is legal for execution. If an action is not legal, the implementation should log a warning message and return false. Actions that fail the validation
        /// are not executed.
        ///
        /// <para>
        /// SDK checks for <see cref="ModelActionExecuteFlags"/> and <see cref="DevelopmentOnlyActionAttribute"/> requirements automatically.
        /// </para>
        /// </summary>
        protected virtual bool ValidateClientOriginatingAction(ClientPeerState client, TAction action) => true;

        /// <summary>
        /// Returns the associated entities used in session start. These entities and their associated entities are delivered to the client
        /// on session start.
        /// </summary>
        protected virtual List<AssociatedEntityRefBase> GetSessionStartAssociatedEntities()
        {
            List<AssociatedEntityRefBase> associatedEntities = new List<AssociatedEntityRefBase>();
            return associatedEntities;
        }

        /// <summary>
        /// Called when a declared associated entity refused the claimed association reference. Implementation should correct the situation such that
        /// the association data is no longer incorrect. Implementation may for example clear the association to the entity (leave the associated entity)
        /// or re-establish the association (re-join the associated entity).
        /// </summary>
        /// <returns>true if refusal was handled</returns>
        protected virtual Task<bool> OnAssociatedEntityRefusalAsync(EntityId playerId, AssociatedEntityRefBase association, InternalEntitySubscribeRefusedBase refusal) => Task.FromResult<bool>(false);

        #endregion
    }
}
