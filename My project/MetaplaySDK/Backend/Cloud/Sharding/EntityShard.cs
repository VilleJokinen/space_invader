// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Entity.EntityStatusMessages;
using Metaplay.Cloud.Entity.PubSub;
using Metaplay.Cloud.Entity.Synchronize.Messages;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Sharding
{
    // UpdateEntityShardStats

    [MetaMessage(MessageCodesCore.UpdateShardLiveEntityCount, MessageDirection.ServerInternal)]
    public class UpdateShardLiveEntityCount : MetaMessage
    {
        public string      ShardName        { get; private set; }
        public EntityKind  EntityKind       { get; private set; }
        public int         LiveEntityCount  { get; private set; }

        UpdateShardLiveEntityCount() { }
        public UpdateShardLiveEntityCount(string shardName, EntityKind entityKind, int liveEntityCount)
        {
            ShardName       = shardName;
            EntityKind      = entityKind;
            LiveEntityCount = liveEntityCount;
        }
    }

    // EntityShardConfig

    public class EntityShardConfig
    {
        public readonly EntityKind      EntityKind;
        public readonly ClusterConfig   ClusterConfig;
        public readonly int             SelfIndex;

        public EntityShardConfig(EntityKind entityKind, ClusterConfig clusterConfig, int selfIndex)
        {
            EntityKind      = entityKind;
            ClusterConfig   = clusterConfig;
            SelfIndex       = selfIndex;
        }
    }

    class EntityShardStartException : Exception
    {
        public Exception[] Exceptions;
        public EntityShardStartException(EntityShardId shardId, Exception[] exceptions)
            : base($"Failed to start shard {shardId}", innerException: new AggregateException(exceptions))
        {
            Exceptions = exceptions;
        }
    }

    // EntityShard

    public class EntityShard : MetaReceiveActor
    {
        public class InitializeShard
        {
            public static readonly InitializeShard Instance = new InitializeShard();
        }

        public class ShardGetInitPhaseRequest { public static readonly ShardGetInitPhaseRequest Instance = new ShardGetInitPhaseRequest(); }
        public class ShardGetInitPhaseResponse
        {
            public EntityShardId ShardId { get; private set; }
            public ShardLifecyclePhase Phase { get; private set; }
            public Exception[] InitErrors { get; private set; }
            public ShardGetInitPhaseResponse(EntityShardId shardId, ShardLifecyclePhase phase, Exception[] initErrors)
            {
                ShardId = shardId;
                Phase = phase;
                InitErrors = initErrors;
            }
        }

        // \todo [petri] enable attribute when supported on interfaces
        //[MetaSerializable]
        public interface IRoutedMessage
        {
            EntityId                    TargetEntityId  { get; }
            MetaSerialized<MetaMessage> Message         { get; }
            EntityId                    FromEntityId    { get; }
        }

        [MetaSerializable]
        public class CastMessage : IRoutedMessage
        {
            [MetaMember(1)] public EntityId                     TargetEntityId  { get; private set; }
            [MetaMember(2)] public MetaSerialized<MetaMessage>  Message         { get; private set; }
            [MetaMember(3)] public EntityId                     FromEntityId    { get; private set; }

            public CastMessage() { }
            public CastMessage(EntityId targetEntityId, MetaSerialized<MetaMessage> message, EntityId fromEntityId)
            {
                TargetEntityId  = targetEntityId;
                Message         = message;
                FromEntityId    = fromEntityId;
            }
        }

        [MetaSerializable(MetaSerializableFlags.ImplicitMembers)]
        [MetaImplicitMembersRange(1, 100)]
        public class EntityAsk : IRoutedMessage
        {
            public int                                                  Id              { get; set; } // Unique identifier (in context of sending entity)
            public EntityId                                             TargetEntityId  { get; private set; }
            public MetaSerialized<MetaMessage>                          Message         { get; private set; }
            public EntityId                                             FromEntityId    { get; private set; }
            [IgnoreDataMember]
            public TaskCompletionSource<MetaSerialized<MetaMessage>>    Promise         { get; set; } // \note only kept on local node
            [IgnoreDataMember]
            public IActorRef                                            OwnerShard      { get; set; } // Owning ShardEntity actor (for routing reply directly to)

            public EntityAsk() { }
            public EntityAsk(EntityId targetEntityId, MetaSerialized<MetaMessage> message, EntityId fromEntityId, TaskCompletionSource<MetaSerialized<MetaMessage>> promise)
            {
                TargetEntityId  = targetEntityId;
                Message         = message;
                FromEntityId    = fromEntityId;
                Promise         = promise;
            }
        }

        /// <summary>
        /// Details of any exception that happens during the handling of an EntityAsk, so the error can be propagated back to the caller.
        /// </summary>
        [MetaMessage(MessageCodesCore.RemoteEntityError, MessageDirection.ServerInternal)]
        public class RemoteEntityError : MetaMessage
        {
            public EntityAskExceptionBase Payload { get; private set; }
            private RemoteEntityError() { }
            public RemoteEntityError(EntityAskExceptionBase payload)
            {
                Payload = payload;
            }
        }

        /// <summary>
        /// A stringly-typed container for propagating information about unexpected errors in entity ask handlers.
        /// <para>
        /// When an exception not of type EntityAskError is thrown by an entity ask handler, the corresponding actor is terminated and
        /// the exception information is propagated to the caller via extracting information into an UnexpectedEntityAskError about
        /// the exception for debugging needs.
        /// </para>
        /// </summary>
        [MetaSerializableDerived(100)]
        public class UnexpectedEntityAskError : EntityAskExceptionBase
        {
            [MetaMember(1)] public string ExceptionType       { get; private set; }   // Type of the exception thrown in the handler
            [MetaMember(2)] public string HandlerStackTrace   { get; private set; }   // Stack trace of the exception thrown in the handler
            [MetaMember(3)] public string HandlerErrorMessage { get; private set; }   // Message of the exception thrown in the handler

            UnexpectedEntityAskError() {}
            public UnexpectedEntityAskError(string exceptionType, string handlerStackTrace, string handlerErrorMessage)
            {
                ExceptionType       = exceptionType;
                HandlerStackTrace   = handlerStackTrace;
                HandlerErrorMessage = handlerErrorMessage;
            }

            public override string Message => $"EntityAsk threw unexpected exception {ExceptionType}: {HandlerErrorMessage}\n{HandlerStackTrace}";
        }

        [MetaSerializable]
        public class EntityAskReply : IRoutedMessage
        {
            [MetaMember(1)] public int                          AskId           { get; private set; } // Unique identifier (in context of sending entity), same as EntityAsk.Id
            [MetaMember(2)] public EntityId                     TargetEntityId  { get; private set; } // EntityId where to send this message (the original EntityAsk caller)
            [MetaMember(3)] public MetaSerialized<MetaMessage>  Message         { get; private set; } // Payload message of the reply (contains an EntityAskReplyException if ask failed at target)
            [MetaMember(4)] public EntityId                     FromEntityId    { get; private set; } // Source EntityId of the reply (ie, the EntityAsk's target)

            private EntityAskReply() { }
            private EntityAskReply(int askId, EntityId targetEntityId, MetaSerialized<MetaMessage> message, EntityId fromEntityId)
            {
                AskId           = askId;
                TargetEntityId  = targetEntityId;
                Message         = message;
                FromEntityId    = fromEntityId;
            }

            public static EntityAskReply Success(int askId, EntityId targetEntityId, EntityId fromEntityId, MetaSerialized<MetaMessage> message) =>
                new EntityAskReply(askId, targetEntityId, message, fromEntityId);

            public static EntityAskReply FailWithUnexpectedError(int askId, EntityId targetEntityId, EntityId fromEntityId, Exception ex) =>
                FailWithError(askId, targetEntityId, fromEntityId, new UnexpectedEntityAskError(ex.GetType().ToGenericTypeString(), ex.StackTrace, ex.Message));

            public static EntityAskReply FailWithError(int askId, EntityId targetEntityId, EntityId fromEntityId, EntityAskExceptionBase ex) =>
                new EntityAskReply(askId, targetEntityId, message: new MetaSerialized<MetaMessage>(new RemoteEntityError(ex), MetaSerializationFlags.IncludeAll, logicVersion: null), fromEntityId);
        }

        /// <summary>
        /// The phase of the shard lifecycle. Follows the following state machine:
        /// <code>
        /// Starting
        ///    |
        ///    |'----.
        ///    |     |
        ///    |     V
        ///    |   ( Error during init )
        ///    |     |
        ///    |     V
        ///    |   StartingFailed
        ///    |           |
        ///    |'-----.    |
        ///    |      |    |
        ///    V      |    |
        /// Running   |    |
        ///    |      |    |
        ///    V      V    V
        /// ( Shutdown message )
        ///    |
        ///    V
        /// Stopping
        ///    |
        ///    V
        /// Stopped
        /// </code>
        /// </summary>
        public enum ShardLifecyclePhase
        {
            /// <summary>
            /// Shard is starting up. It is in the process of ensuring all "AlwaysRunningEntities" are up.
            /// </summary>
            Starting,

            /// <summary>
            /// Shard could not ensure initial AlwaysRunningEntities.
            /// </summary>
            StartingFailed,

            /// <summary>
            /// Shard is running. The AlwaysRunningEntities have been up at least for an instant during the initialization.
            /// </summary>
            Running,

            /// <summary>
            /// Shard is shutting down. Running entities are being shut down, and no new entities are started.
            /// </summary>
            Stopping,

            /// <summary>
            /// Shard has shut down. No entities are running.
            /// </summary>
            Stopped,
        }

        public class EntityAskState
        {
            public TaskCompletionSource<MetaSerialized<MetaMessage>> Promise { get; private set; }

            public EntityAskState(TaskCompletionSource<MetaSerialized<MetaMessage>> promise)
            {
                Promise = promise;
            }
        }

        protected class EntitySyncState
        {
            public readonly int                                 LocalChannelId;
            public int                                          RemoteChannelId; // -1 until remote replies with the channel
            public readonly EntityId                            LocalEntityId;
            public readonly EntityId                            RemoteEntityId;
            public TaskCompletionSource<int>                    OpeningPromise; // delivers channel id to entity
            public ChannelWriter<MetaSerialized<MetaMessage>>   WriterPeer;
            public bool                                         IsRemoteClosed;

            public EntitySyncState(int localChannelId, int remoteChannelId, EntityId localEntityId, EntityId remoteEntityId, TaskCompletionSource<int> openingPromise, ChannelWriter<MetaSerialized<MetaMessage>> writerPeer)
            {
                LocalChannelId = localChannelId;
                RemoteChannelId = remoteChannelId;
                LocalEntityId = localEntityId;
                RemoteEntityId = remoteEntityId;
                OpeningPromise = openingPromise;
                WriterPeer = writerPeer;
                IsRemoteClosed = false;
            }
        }

        protected class EntityState
        {
            public EntityId                         EntityId            = EntityId.None;
            public EntityStatus                     Status              = EntityStatus.Starting;
            public IActorRef                        Actor               = null;
            public List<IRoutedMessage>             PendingMessages     = new List<IRoutedMessage>();
            public int                              EntityAskRunningId  = 1;
            public Dictionary<int, EntityAskState>  EntityAsks          = new Dictionary<int, EntityAskState>(); // \todo [petri] asks only removed when response is received
            public int                              EntitySyncRunningId = 1;
            public Dictionary<int, EntitySyncState> EntitySyncs         = new Dictionary<int, EntitySyncState>(); // local channel id -> Sync.

            /// <summary>
            /// The exception that caused entity to crash.
            /// </summary>
            public Exception TerminationReason = null;

            /// <summary>
            /// The point in time when the entity was requested to shut down. If the shutdown request
            /// hasn't been sent yet, <see cref="DateTime.MinValue"/>.
            /// </summary>
            public DateTime EntityShutdownStartedAt = DateTime.MinValue;

            public EntityState(EntityId entityId, IActorRef actor)
            {
                EntityId    = entityId;
                Actor       = actor;
            }
        }

        // Metrics
        static readonly Prometheus.Counter  c_entitySpawned     = Prometheus.Metrics.CreateCounter("game_entity_started_total", "Cumulative number of entities started (by type)", "entity");
        static readonly Prometheus.Counter  c_entityTerminated  = Prometheus.Metrics.CreateCounter("game_entity_terminated_total", "Cumulative number of entities terminated (by type)", "entity");
        static readonly Prometheus.Gauge    c_entityActive      = Prometheus.Metrics.CreateGauge("game_entity_active", "Cumulative number of entities active currently (by type)", "entity");

        // Members
        protected readonly string                       _nodeName;
        protected readonly EntityShardConfig            _shardConfig;
        protected readonly EntityShardId                _selfShardId;   // Value is -1 for proxy shards (not part of sharding) and [0..N) for shard nodes.
        protected readonly Func<EntityId, Props>        _entityProps;
        protected readonly EntitySharding               _entitySharding;

        protected EntityKind                            EntityKind  => _selfShardId.Kind;

        protected HashSet<EntityId>                     _alwaysRunningEntities = new HashSet<EntityId>();   // Entities that should always be running

        protected ShardLifecyclePhase                   _lifecyclePhase;
        protected IActorRef                             _shutdownSender;
        protected Dictionary<EntityId, EntityState>     _entityStates   = new Dictionary<EntityId, EntityState>();
        protected Dictionary<IActorRef, EntityState>    _entityByActor  = new Dictionary<IActorRef, EntityState>();

        Dictionary<EntityId, HashSet<EntityId>>         _entityWatches  = new Dictionary<EntityId, HashSet<EntityId>>();

        static readonly TimeSpan                        UpdateTickInterval  = TimeSpan.FromMilliseconds(2000);
        ICancelable                                     _cancelUpdateTimer  = new Cancelable(Context.System.Scheduler);
        List<Exception>                                 _shardStartupErrors = new List<Exception>();

        // Entity shutdown throttling

        /// <inheritdoc cref="EntityConfigBase.MaxConcurrentEntityShutdownsPerShard"/>
        readonly int                                    _maxConcurrentEntityShutdowns;
        int                                             _numOngoingShutdowns;
        OrderedSet<EntityId>                            _deferredShutdowns = new OrderedSet<EntityId>();

        public EntityShard(EntityShardConfig shardConfig)
        {
            ClusteringOptions   clusterOpts     = RuntimeOptionsRegistry.Instance.GetCurrent<ClusteringOptions>();
            EntityConfigBase    entityConfig    = EntityConfigRegistry.Instance.GetConfig(shardConfig.EntityKind);

            _nodeName       = clusterOpts.SelfAddress.ToString();
            _shardConfig    = shardConfig;
            _selfShardId    = new EntityShardId(shardConfig.EntityKind, shardConfig.SelfIndex);
            _entityProps    = entityConfig.AllowEntitySpawn ? (EntityId entityId) => Props.Create(entityConfig.EntityActorType, entityId) : null;
            _entitySharding = EntitySharding.Get(Context.System);

            // Register EntityKind and self as local actor
            _log.Info("Register local shard {ShardId}: {ActorRef}", _selfShardId, _self);
            _entitySharding.RegisterLocalShard(_selfShardId, _self);

            if (entityConfig.ShardingStrategy is ServiceShardingStrategy serviceStrategy && _selfShardId.Value != -1)
            {
                _log.Info("Starting owned service shard {ShardId}", _selfShardId);
                EntityId localEntityId = EntityId.Create(EntityKind, (uint)_selfShardId.Value);
                _alwaysRunningEntities.Add(localEntityId);
            }

            // Register message handlers
            RegisterHandlers();

            _lifecyclePhase = ShardLifecyclePhase.Starting;
            _maxConcurrentEntityShutdowns = entityConfig.MaxConcurrentEntityShutdownsPerShard;

            Tell(_self, InitializeShard.Instance);
        }

        bool IsShuttingDownEntities => _lifecyclePhase == ShardLifecyclePhase.StartingFailed || _lifecyclePhase == ShardLifecyclePhase.Stopping || _lifecyclePhase == ShardLifecyclePhase.Stopped;

        bool AreAlwaysRunningEntitiesRunning()
        {
            foreach (EntityId entityId in _alwaysRunningEntities)
            {
                if (!_entityStates.TryGetValue(entityId, out EntityState entity))
                    return false;
                if (entity.Status != EntityStatus.Running)
                    return false;
            }
            return true;
        }

        void RegisterHandlers()
        {
            ReceiveAsync<InitializeShard>(ReceiveInitializeShard);
            Receive<ShardGetInitPhaseRequest>(ReceiveShardGetInitPhaseRequest);
            Receive<ActorTick>(ReceiveActorTick);
            Receive<EntityReady>(ReceiveEntityReady);
            Receive<EntityShutdownRequest>(ReceiveEntityShutdownRequest);
            Receive<EntitySuspendRequest>(ReceiveEntitySuspendRequest);
            Receive<EntityResumeRequest>(ReceiveEntityResumeRequest);
            Receive<CastMessage>(ReceiveCastMessage);
            Receive<EntityAsk>(ReceiveEntityAsk);
            Receive<EntityAskReply>(ReceiveEntityAskReply);
            Receive<WatchedEntityTerminated>(ReceiveWatchedEntityTerminated);
            Receive<Terminated>(ReceiveTerminated);
            Receive<ClusterNodeLostEvent>(ReceiveClusterNodeLostEvent);
            Receive<ShutdownSync>(ReceiveShutdownSync);
            Receive<EntitySynchronizationE2SBeginRequest>(ReceiveEntitySynchronizationE2SBeginRequest);
            Receive<EntitySynchronizationS2SBeginRequest>(ReceiveEntitySynchronizationS2SBeginRequest);
            Receive<EntitySynchronizationS2EBeginResponse>(ReceiveEntitySynchronizationS2EBeginResponse);
            Receive<EntitySynchronizationS2SBeginResponse>(ReceiveEntitySynchronizationS2SBeginResponse);
            Receive<EntitySynchronizationE2SChannelMessage>(ReceiveEntitySynchronizationE2SChannelMessage);
            Receive<EntitySynchronizationS2SChannelMessage>(ReceiveEntitySynchronizationS2SChannelMessage);
        }

        protected virtual Task InitializeAsync()
        {
            // Spawn local always-running entities
            foreach (EntityId entityId in _alwaysRunningEntities)
            {
                EntityState entity = GetOrSpawnEntity(entityId, SpawnEntityActor);
                if (entity == null)
                    throw new InvalidOperationException($"Failed to start service entity {entityId} (on {_selfShardId})");
            }

            // If there are no init steps, the shard is Running immediately
            if (_alwaysRunningEntities.Count == 0)
                _lifecyclePhase = ShardLifecyclePhase.Running;

            return Task.CompletedTask;
        }

        async Task ReceiveInitializeShard(InitializeShard initialize)
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                // any Failure terminates the shard. No point in continuing.
                _lifecyclePhase = ShardLifecyclePhase.StartingFailed;
                AppendStartingFailure(ex);

                RequestShutdownAllEntities();
            }
        }

        /// <summary>
        /// Completes with true when shard is running.
        /// Completes with false if reached internal timeout while waiting for shard. Shard status is still starting.
        /// Completes with <see cref="EntityShardStartException"/> error if shard failed to initialize.
        /// </summary>
        public static async Task<bool> TryWaitUntilRunning(IActorRef actor)
        {
            try
            {
                ShardGetInitPhaseResponse response = await actor.Ask<ShardGetInitPhaseResponse>(ShardGetInitPhaseRequest.Instance);

                switch (response.Phase)
                {
                    case ShardLifecyclePhase.Starting:
                        return false;
                    case ShardLifecyclePhase.StartingFailed:
                        throw new EntityShardStartException(response.ShardId, response.InitErrors);
                    case ShardLifecyclePhase.Running:
                        return true;

                    case ShardLifecyclePhase.Stopping:
                    case ShardLifecyclePhase.Stopped:
                    default:
                        throw new InvalidOperationException($"Entity shard {response.ShardId} state {response.Phase} was not initial state");
                }
            }
            catch(TimeoutException)
            {
                return false;
            }
        }

        void ReceiveShardGetInitPhaseRequest(ShardGetInitPhaseRequest req)
        {
            Sender.Tell(new ShardGetInitPhaseResponse(_selfShardId, _lifecyclePhase, _shardStartupErrors.ToArray()));
        }

        void ReceiveActorTick(ActorTick tick)
        {
            // Publish shard entity counts (handled by StatsCollectorProxy)
            Context.System.EventStream.Publish(new UpdateShardLiveEntityCount(_nodeName, EntityKind, _entityStates.Count));

            // DEBUG DEBUG: Dump all watch states
            //if (_entityWatches.Count > 0)
            //{
            //    StringBuilder sb = new StringBuilder();
            //    foreach ((EntityId fromEntityId, HashSet<EntityId> watchers) in _entityWatches)
            //        sb.AppendLine($"  {fromEntityId}: {string.Join(", ", watchers)}");
            //    sb.Length -= 1;
            //    _log.Info("Watchers:\n{0}", sb.ToString());
            //}

            // Metrics
            c_entityActive.WithLabels(EntityKind.ToString()).Set(_entityStates.Count);
        }

        void ReceiveEntityReady(EntityReady ready)
        {
            if (!_entityByActor.TryGetValue(Sender, out EntityState entity))
            {
                _log.Warning("Got EntityReady for unknown child: {Actor}", Sender);
                return;
            }

            if (entity.Status == EntityStatus.Starting)
            {
                // Set status to Running & store sequence number
                //_log.Debug("Entity {0} ready for action, {1} pending messages", ready.EntityId, entity.PendingMessages.Count);
                entity.Status = EntityStatus.Running;

                // Flush all pending messages
                FlushPendingMessagesToLocalEntity(entity);

                // If shard is shutting down, ask child to shut down immediately
                // \note: This should not normally happen since entities are put to Stopping status when we transition to Stopping phase.
                if (IsShuttingDownEntities)
                    RequestEntityShutdown(entity);

                // If this is one of the initial entities, the shard may be ready now. Check it.
                if (_lifecyclePhase == ShardLifecyclePhase.Starting && _alwaysRunningEntities.Contains(entity.EntityId))
                {
                    if (AreAlwaysRunningEntitiesRunning())
                        _lifecyclePhase = ShardLifecyclePhase.Running;
                }
            }
            else if (entity.Status == EntityStatus.Stopping)
            {
                // ignored: can end up here, if entity is immediately terminated upon starting (eg, short-lived connections from LB)
                //          or if shard shutdown is started after entity init is started but not yet completed.
            }
            else
                _log.Warning("Got EntityReady for {EntityId} in invalid state {Status}", entity.EntityId, entity.Status);
        }

        void ReceiveEntityShutdownRequest(EntityShutdownRequest shutdown)
        {
            if (!_entityByActor.TryGetValue(Sender, out EntityState entity))
            {
                _log.Warning("Got EntityRequestShutdown for unknown child: {Actor}", Sender);
                return;
            }

            // \note allow shutdown during any state (including Starting) & allow double-stopping
            RequestEntityShutdown(entity);
        }

        void ReceiveEntitySuspendRequest(EntitySuspendRequest suspend)
        {
            if (!_entityByActor.TryGetValue(Sender, out EntityState entity))
            {
                _log.Warning("Got EntitySuspendRequest for unknown child: {Actor}", Sender);
                return;
            }

            if (IsShuttingDownEntities || entity.Status == EntityStatus.Stopping)
            {
                // If shard or the entity is shutting down, the shutdown request is already being delivered.
                // No point replying.
            }
            else if (entity.Status == EntityStatus.Running)
            {
                // suspend, and let the entity know
                entity.Status = EntityStatus.Suspended;
                entity.Actor.Tell(EntitySuspendEvent.Instance);
            }
            else
                _log.Warning("Got EntitySuspendRequest for {EntityId} in invalid state {Status}", entity.EntityId, entity.Status);
        }

        void ReceiveEntityResumeRequest(EntityResumeRequest resume)
        {
            if (!_entityByActor.TryGetValue(Sender, out EntityState entity))
            {
                _log.Warning("Got EntityResumeRequest for unknown child: {Actor}", Sender);
                return;
            }

            if (IsShuttingDownEntities || entity.Status == EntityStatus.Stopping)
            {
                // If shard or the entity is shutting down, the shutdown request is already being delivered.
            }
            else if (entity.Status == EntityStatus.Suspended)
            {
                // Mark entity as Running
                entity.Status = EntityStatus.Running;

                // Flush all pending messages
                FlushPendingMessagesToLocalEntity(entity);
            }
            else
                _log.Warning("Got EntityResumeRequest for {EntityId} in invalid state {Status}", entity.EntityId, entity.Status);
        }

        /// <summary>
        /// Handle an outgoing <see cref="CastMessage"/>. First handled by the source Entity's parent EntityShard,
        /// then routed onwards to target entity's parent EntityShard, which routes it locally to the target Entity.
        /// </summary>
        /// <param name="cast"></param>
        void ReceiveCastMessage(CastMessage cast)
        {
            //_log.Debug("Handle CastMessage from {0} to {1}: {2}", Sender, cast.TargetEntityId, MetaSerializationUtil.PeekMessageName(cast.Message));

            // Check that source is valid
            if (!cast.FromEntityId.IsValid)
            {
                _log.Error("Received CastMessage from invalid entity {FromEntityId}: {MessageName}", cast.FromEntityId, MetaSerializationUtil.PeekMessageName(cast.Message));
                return;
            }

            // Check that targetEntityId is valid
            if (!cast.TargetEntityId.IsValid)
            {
                _log.Error("Trying to send an CastMessage message to invalid entity {TargetEntityId} (from {FromEntityId}): {MessageName}", cast.TargetEntityId, cast.FromEntityId, MetaSerializationUtil.PeekMessageName(cast.Message));
                return;
            }

            // Special case for SubscriberKicked: Kicked is delivered with CastMessage. Since it ends subscription and its
            // entity watch, we update the bookkeeping here.
            int typeCode = MetaSerializationUtil.PeekMessageTypeCode(cast.Message);
            if (typeCode == MessageCodesCore.EntitySubscriberKicked)
            {
                _log.Verbose("Unregister watch due to SubscriberKicked: {FromEntityId} <-> {ToEntityId}", cast.FromEntityId, cast.TargetEntityId);
                UnregisterTwoWayEntityWatch(cast.FromEntityId, cast.TargetEntityId);
            }

            // Route message onwards (either locally or to remote shard)
            RouteMessage(cast);
        }

        /// <summary>
        /// Handle an <see cref="EntityAsk"/>. First handled when an Entity sends this to its
        /// owning EntityShard (ask.Promise != null), where the state of the EntityAsk is stored in Entity's
        /// metadata, for handling the response. After this, the EntityShard can forward the EntityAsk
        /// forward to the target Entity's EntityShard.
        /// The target Entity's parent EntityShard will finally route the message to the Entity itself.
        /// </summary>
        /// <param name="ask"></param>
        void ReceiveEntityAsk(EntityAsk ask)
        {
            //_log.Debug("Handle EntityAsk<{0}> #{1} to {2}: {3} promise", MetaSerializationUtil.PeekMessageName(ask.Message), ask.Id, ask.TargetEntityId, ask.Promise != null ? "has" : "no");

            // Check that targetEntityId is valid
            if (!ask.TargetEntityId.IsValid)
            {
                _log.Error("Trying to send an EntityAsk message to invalid entity {TargetEntityId} (from {FromEntityId}): {MessageName}", ask.TargetEntityId, ask.FromEntityId, MetaSerializationUtil.PeekMessageName(ask.Message));
                return;
            }

            // If ask.Promise != null, we just received the EntityAsk from the originating Entity (our child)
            if (ask.Promise != null)
            {
                // Check that the Entity is a known child of ours
                if (!_entityByActor.TryGetValue(Sender, out EntityState askingEntity))
                {
                    // Source Entity unknown, ignore message
                    _log.Warning("Received EntityAsk from unknown entity {Actor} (entity {FromEntityId}) for {TargetEntityId}: {MessageType}, ignoring it", Sender, ask.FromEntityId, ask.TargetEntityId, MetaSerializationUtil.PeekMessageName(ask.Message));
                    return;
                }

                // Store the promise for handling the response
                ask.Id = askingEntity.EntityAskRunningId++;
                askingEntity.EntityAsks.Add(ask.Id, new EntityAskState(ask.Promise));

                // Clear the promise (we've captured it already) and mark ourselves as the source
                ask.Promise = null;
                ask.OwnerShard = _self;

                // Route message onwards
                RouteMessage(ask);
            }
            else
            {
                // Received from remote shard, use Sender as Owner
                ask.OwnerShard = Sender;

                // Route message onwards
                RouteMessage(ask);
            }
        }

        /// <summary>
        /// Handle a reply to an <see cref="EntityAsk"/>. The source Entity sends it directly to the asking
        /// Entity's parent EntityShard, so we know it always gets delivered locally. The delivery is done
        /// by resolving EntityAsk's promise, thus no more messaging is done and the replies are not ordered
        /// with other messaging.
        /// </summary>
        /// <param name="reply"></param>
        void ReceiveEntityAskReply(EntityAskReply reply)
        {
            //_log.Debug("Routing EntityAskReply from {FromEntityId} to {TargetEntityId}: {MessageName}", reply.FromEntityId, reply.TargetEntityId, MetaSerializationUtil.PeekMessageName(reply.Message));

            // Check that source is valid
            if (!reply.FromEntityId.IsValid)
            {
                _log.Error("Received EntityAskReply from invalid entity {FromEntityId}: {MessageName}", reply.FromEntityId, MetaSerializationUtil.PeekMessageName(reply.Message));
                return;
            }

            // Check that target entity's kind matches this shard's kind
            if (reply.TargetEntityId.Kind != EntityKind)
            {
                _log.Error("Received EntityAskReply for entity {TargetEntityId} of invalid kind, this shard expects {ExpectedEntityKind}", reply.TargetEntityId, EntityKind);
                return;
            }

            // Check that destination is valid
            // \note: AskReply is sent directly to the destination shard so this should always be local
            if (!_entityStates.TryGetValue(reply.TargetEntityId, out EntityState entity))
            {
                _log.Warning("Received EntityAskReply for unknown entity {TargetEntityId} (from {FromEntityId}): {MessageName}", reply.TargetEntityId, reply.FromEntityId, MetaSerializationUtil.PeekMessageName(reply.Message));
                return;
            }

            // SubscribeAck returned to local entity: register entity watch in both directions
            int typeCode = MetaSerializationUtil.PeekMessageTypeCode(reply.Message);
            if (typeCode == MessageCodesCore.EntitySubscribeAck)
            {
                _log.Verbose("Register watch due to {MessageName}: {FromEntityId} <-> {ToEntityId} (typeCode={TypeCode})", "SubscribeAck", reply.FromEntityId, reply.TargetEntityId, typeCode);
                RegisterTwoWayEntityWatch(reply.FromEntityId, reply.TargetEntityId);
            }
            else if (typeCode == MessageCodesCore.EntityUnsubscribeAck)
            {
                _log.Verbose("Unregister watch due to {MessageName}: {FromEntityId} <-> {ToEntityId} (typeCode={TypeCode})", "UnsubscribeAck", reply.FromEntityId, reply.TargetEntityId, typeCode);
                UnregisterTwoWayEntityWatch(reply.FromEntityId, reply.TargetEntityId);
            }

            // Route the response (or error) to the awaiting Task
            if (entity.EntityAsks.Remove(reply.AskId, out EntityAskState askState))
            {
                if (typeCode == MessageCodesCore.RemoteEntityError)
                {
                    RemoteEntityError error = (RemoteEntityError)reply.Message.Deserialize(resolver: null, logicVersion: null);
                    //_log.Debug("Delivering failed EntityAskReply to {CallerEntityId} (#{AskId}) with {ExceptionType}: {ErrorMessage}\n{StackTrace}", reply.TargetEntityId, reply.AskId, error.ExceptionType, error.Message, error.StackTrace);
                    askState.Promise.SetException(error.Payload);
                }
                else
                {
                    //_log.Debug("Delivering successful EntityAskReply to {CallerEntityId} (#{AskId}): {ReplyMessageType}", reply.TargetEntityId, reply.AskId, MetaSerializationUtil.PeekMessageName(reply.Message));
                    askState.Promise.SetResult(reply.Message); // \note passing on serialized version of message
                }
            }
            else
            {
                _log.Warning("Received response to unknown EntityAsk #{AskId}: {MessageType}", reply.AskId, MetaSerializationUtil.PeekMessageName(reply.Message));
            }
        }

        void ReceiveWatchedEntityTerminated(WatchedEntityTerminated terminated)
        {
            // Handle all (local) watchers
            if (_entityWatches.Remove(terminated.EntityId, out HashSet<EntityId> watchers))
            {
                _log.Debug("Watched entity {EntityId} terminated, watched by: {Watchers}", terminated.EntityId, string.Join(", ", watchers));

                foreach (EntityId watcherId in watchers)
                {
                    if (_entityStates.TryGetValue(watcherId, out EntityState entityState))
                    {
                        _log.Verbose("Informing local watcher {WatcherEntityId} (status={EntityStatus}) of death of {TerminatedEntityId}", watcherId, entityState.Status, terminated.EntityId);
                        entityState.Actor.Tell(terminated);
                    }
                    else
                        _log.Warning("Entity for local watcher {WatcherEntityId} not found!", watcherId);

                    // Remove reverse watcher
                    UnregisterDirectedEntityWatch(watcherId, terminated.EntityId);
                }
            }
            else
                _log.Verbose("Watched entity {EntityId} terminated, no watchers", terminated.EntityId);
        }

        void ReceiveTerminated(Terminated terminated)
        {
            // Handle terminated child entity actors
            if (!_entityByActor.Remove(terminated.ActorRef, out EntityState entity))
            {
                _log.Warning("Received Terminated for unknown actor {Actor}", terminated.ActorRef);
                return;
            }

            EntityId entityId = entity.EntityId;
            bool shutdownIsExpected = entity.EntityShutdownStartedAt != DateTime.MinValue;

            // Entity crash, unexpected shutdown, or expected?
            if (entity.TerminationReason != null)
            {
                _log.Error("Child entity {EntityId} (actor={Actor}, status={EntityStatus}, numPendingMessages={NumPendingMessages}) crashed due to {ExceptionType} '{ExceptionMessage}':\n{ExceptionStackTrace}",
                    entityId, entity.Actor, entity.Status, entity.PendingMessages.Count, entity.TerminationReason.GetType().Name, entity.TerminationReason.Message, entity.TerminationReason.StackTrace);

                // If entity crashes in Starting state, it means the entity failed to Initialize(). Clear the pending messages to prevent it from restarting.
                // Otherwise, we could get into infinite retry loop with no throtting, spamming the logs.
                //
                // \todo: Why terminate pending asks only in Starting?
                if (entity.Status == EntityStatus.Starting)
                {
                    // Reply to all pending EntityAsks with the error (they might not actually have caused it, but it's better that they are informed regardless of why the actor failed)
                    foreach (IRoutedMessage routed in entity.PendingMessages)
                    {
                        if (routed is EntityAsk ask)
                            Tell(ask.OwnerShard, EntityAskReply.FailWithUnexpectedError(ask.Id, ask.FromEntityId, entity.EntityId, entity.TerminationReason));
                    }

                    // Remove any pending messages
                    entity.PendingMessages.Clear();
                }
            }
            else if (!shutdownIsExpected)
            {
                _log.Error("Entity {EntityId} unexpectedly terminated (status={Status}, actor={Actor}, numPendingMessages={NumPendingMessages})", entityId, entity.Status, terminated.ActorRef, entity.PendingMessages.Count);
            }
            else
            {
                // Expected shutdown
                // \todo [jarkko]: keep metrics on shutdowns
                //    TimeSpan shutdownDuration = (DateTime.UtcNow - entity.EntityShutdownStartedAt);
                //    observe()
            }

            // Does this terminate the shard init? If required initial entities fail to start, shard fails to start.
            bool terminateShardInit = _lifecyclePhase == ShardLifecyclePhase.Starting && _alwaysRunningEntities.Contains(entityId);
            if (terminateShardInit)
            {
                // \note: this sets IsShuttingDownEntities to False, and the entity will not be auto restarted below
                _lifecyclePhase = ShardLifecyclePhase.StartingFailed;
                AppendStartingFailure(entity.TerminationReason ?? new InvalidOperationException($"Entity {entityId} terminated unexpectedly"));
            }

            // Inform all watches
            InformTerminatedEntityWatchers(entityId);

            // Update shutdown queue status
            // * Any (expected or not) shutdown removes the entity from the shutdown queue.
            // * Any expected shutdown releases one back to the semaphore
            _deferredShutdowns.Remove(entity.EntityId);
            if (shutdownIsExpected)
                _numOngoingShutdowns--;
            TryRunNextEnqueuedEntityShutdown();

            // Entities with pending messages (unless entity is transient) or always-running entities are re-started immediately
            bool shouldRestart;
            if (IsShuttingDownEntities)
                shouldRestart = false;
            else if (_alwaysRunningEntities.Contains(entity.EntityId))
                shouldRestart = true;
            else if (_entityProps == null) // entity is transient
                shouldRestart = false;
            else if (entity.PendingMessages.Count > 0)
                shouldRestart = true;
            else
                shouldRestart = false;

            if (shouldRestart)
            {
                IActorRef actor = SpawnEntityActor(entityId);
                entity.Status = EntityStatus.Starting;
                entity.Actor = actor;
                _entityByActor.Add(actor, entity);
            }
            else
            {
                // Forget about entity
                c_entityTerminated.WithLabels(entityId.Kind.ToString()).Inc();
                _entityStates.Remove(entityId);

                // If shard became a failed shard, start cleaning up already
                if (terminateShardInit)
                    RequestShutdownAllEntities();

                // Check whether ready to shutdown immediately
                UpdateShardShutdown();
            }
        }

        void ReceiveClusterNodeLostEvent(ClusterNodeLostEvent nodeLost)
        {
            // \todo [petri] SHARD: is this robust? move into some utility
            Dictionary<EntityKind, EntityShardId> terminatedShardIds = _shardConfig.ClusterConfig.GetNodeShardIds(nodeLost.Address);

            _log.Warning("Lost node {NodeAddress} with shards: {EntityShards}", nodeLost.Address, string.Join(", ", terminatedShardIds.Values));

            // Resolve all watched entityIds affected
            List<EntityId> terminatedEntityIds = _entityWatches
                .Keys
                .Where(entityId =>
                {
                    EntityKindShardState targetShardStates = _entitySharding.GetShardStatesForKind(entityId.Kind);
                    if (terminatedShardIds.TryGetValue(entityId.Kind, out EntityShardId lostShardId))
                    {
                        EntityShardId onShardId = targetShardStates.Strategy.ResolveShardId(entityId);
                        return onShardId == lostShardId;
                    }
                    else
                        return false;
                })
                .ToList();

            if (terminatedEntityIds.Count > 0)
            {
                _log.Debug("Entities terminated on {NodeAddress}: {TerminatedEntityIds}", nodeLost.Address, string.Join(", ", terminatedEntityIds));

                // Handle all terminated entities
                foreach (EntityId entityId in terminatedEntityIds)
                    InformTerminatedEntityWatchers(entityId);
            }
        }

        void ReceiveShutdownSync(ShutdownSync s)
        {
            _log.Info("Shutting down ({NumChildren} children)..", _entityStates.Count);

            // Mark as shutting down
            _lifecyclePhase = ShardLifecyclePhase.Stopping;
            _shutdownSender = Sender;

            RequestShutdownAllEntities();

            // Check if should terminated immediately (checks for no children)
            UpdateShardShutdown();
        }

        void UpdateShardShutdown()
        {
            // If shutdown requested and all entities are terminated, terminate shard itself
            if (_lifecyclePhase == ShardLifecyclePhase.Stopping && _entityStates.Count == 0)
            {
                _log.Info("Shard is ready to terminate");
                _lifecyclePhase = ShardLifecyclePhase.Stopped;

                // Inform requester and terminate self
                _shutdownSender.Tell(ShutdownComplete.Instance);
                _self.Tell(PoisonPill.Instance);
            }
        }

        void AppendStartingFailure(Exception ex)
        {
            _shardStartupErrors.Add(ex);
        }

        protected override void PreStart()
        {
            base.PreStart();

            // Subscribe to cluster node loss events (so can break all subscriptions)
            Context.System.EventStream.Subscribe(_self, typeof(ClusterNodeLostEvent));

            // Start update timer
            Context.System.Scheduler.ScheduleTellRepeatedly(UpdateTickInterval, UpdateTickInterval, _self, ActorTick.Instance, ActorRefs.NoSender, _cancelUpdateTimer);
        }

        protected override void PostStop()
        {
            // Cancel timer
            _cancelUpdateTimer.Cancel();

            // Unsubscribe node loss events
            Context.System.EventStream.Unsubscribe(_self, typeof(ClusterNodeLostEvent));

            base.PostStop();
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(wrappedExeption =>
            {
                // For convenience, unwrap trivial AggregateExceptions.
                Exception ex;
                if (wrappedExeption is AggregateException aggregateException && aggregateException.InnerExceptions.Count == 1)
                    ex = aggregateException.InnerException;
                else
                    ex = wrappedExeption;

                // Handle terminated child entity actors
                // \note We don't remove the entity here as the Terminated handler will do that
                if (_entityByActor.TryGetValue(Sender, out EntityState entity))
                {
                    entity.TerminationReason = ex;
                }
                else
                    _log.Error("Exception occurred in unknown child {Actor}, {ExceptionType} {ExceptionMessage}:\n{ExceptionStackTrace}", Sender, ex.GetType().Name, ex.Message, ex.StackTrace);

                // Don't auto-restart any actors
                return Directive.Stop;
            }, loggingEnabled: false);
        }

        /// <summary>
        /// Requests entity to shut down. If there are too many ongoing shutdowns, the entity is put into a shutdown queue.
        /// During shutdown or on the queue the entity receives no further messages, and the messages for it are buffered.
        /// </summary>
        protected void RequestEntityShutdown(EntityState entityState)
        {
            // Nothing to do if already done
            if (entityState.Status == EntityStatus.Stopping)
                return;

            // Log a warning if this is the one that exceeds the capacity
            if (_maxConcurrentEntityShutdowns > 0 && _numOngoingShutdowns == _maxConcurrentEntityShutdowns && _deferredShutdowns.Count == 0)
            {
                _log.Warning("Entity shutdown concurrency limit of {Limit} exceeded, throtting.", _maxConcurrentEntityShutdowns);
            }

            // Marks as stopping and try to schedule stop
            entityState.Status = EntityStatus.Stopping;
            _deferredShutdowns.Add(entityState.EntityId);
            TryRunNextEnqueuedEntityShutdown();
        }

        /// <summary>
        /// If there is shutdown capacity, pops the next entity from the shutdown list and begins its shutdown.
        /// </summary>
        void TryRunNextEnqueuedEntityShutdown()
        {
            for (;;)
            {
                // Capacity reached?
                if (_maxConcurrentEntityShutdowns > 0 && _numOngoingShutdowns >= _maxConcurrentEntityShutdowns)
                    return;

                // Pop next from the queue
                if (_deferredShutdowns.Count == 0)
                    return;
                EntityId nextEntityToShutdown = _deferredShutdowns.First();
                _deferredShutdowns.Remove(nextEntityToShutdown);

                if (!_entityStates.TryGetValue(nextEntityToShutdown, out EntityState entityState))
                {
                    _log.Warning("Unknown entity in shutdown list: {EntityId}", nextEntityToShutdown);
                    continue;
                }

                _numOngoingShutdowns++;
                entityState.EntityShutdownStartedAt = DateTime.UtcNow;
                Tell(entityState.Actor, ShutdownEntity.Instance);
            }
        }

        void RequestShutdownAllEntities()
        {
            // Send shutdown request to all children
            foreach ((EntityId entityId, EntityState entityState) in _entityStates)
                RequestEntityShutdown(entityState);
        }

        /// <summary>
        /// Route a message to a local or remote Entity.
        /// </summary>
        void RouteMessage(IRoutedMessage routed)
        {
            //_log.Debug("Routing message from {FromEntityId} to {TargetEntityId} with message {MessageName}", routed.FromEntityId, routed.TargetEntityId, MetaSerializationUtil.PeekMessageName(routed.Message));

            // Check that target is valid
            EntityId targetEntityId = routed.TargetEntityId;
            if (!targetEntityId.IsValid)
            {
                _log.Error("Trying to send message to invalid target {ToEntityId}: {MessageName}", targetEntityId, MetaSerializationUtil.PeekMessageName(routed.Message));
                return;
            }

            // Check that source is valid
            if (!routed.FromEntityId.IsValid)
            {
                _log.Error("Received message from invalid entity {FromEntityId}: {MessageName}", routed.FromEntityId, MetaSerializationUtil.PeekMessageName(routed.Message));
                return;
            }

            // Resolve shard index based on target
            // \note for services, targetEntityId is mapped to service's entityId
            EntityKindShardState targetShardStates = _entitySharding.TryGetShardStatesForKind(targetEntityId.Kind);
            if (targetShardStates == null)
            {
                _log.Error("Trying to send message to target {ToEntityId} with no registered shards: {MessageName}", targetEntityId, MetaSerializationUtil.PeekMessageName(routed.Message));
                return;
            }

            // Resolve target shardId and handle local vs remote cases
            EntityShardId shardId = targetShardStates.Strategy.ResolveShardId(targetEntityId);
            //_log.Debug("HandleRoutedMessage {0}: target={1} / {2} (local={3})", MetaSerializationUtil.PeekMessageName(routed.Message), shardId, targetEntityId, shardId == _selfShardId);
            if (shardId == _selfShardId)
            {
                // Ensure entity actor is woken up
                EntityState entity = GetOrSpawnEntity(routed.TargetEntityId, SpawnEntityActor);

                // Route the triggering message
                if (entity != null)
                    RouteMessageToLocalEntity(entity, routed);
                else if (_entityProps != null) // only warn if entities can be spawned
                    _log.Warning("Failed to spawn entity {EntityId} to receive message: {MessageType}", routed.TargetEntityId, routed.GetType().Name);
            }
            else // message target not on this shard, forward to receiver shard
            {
                int shardNdx = shardId.Value;
                if (shardNdx < 0 || shardNdx >= targetShardStates.RemoteActors.Length)
                    _log.Error("Invalid shard {ShardId} for target {TargetEntityId} ({NumShards} shards for entity)", shardId, targetEntityId, targetShardStates.RemoteActors.Length);
                else
                {
                    IActorRef shardActorRef = targetShardStates.RemoteActors[shardNdx];
                    //_log.Debug("Routing message {Message} to shard {ShardId} / {ShardActor}", MetaSerializationUtil.PeekMessageName(routed.Message), shardId, shardActorRef);
                    shardActorRef.Tell(routed);
                }
            }
        }

        /// <summary>
        /// Immediately deliver a message to a local Entity. Entity must be in Running state.
        /// </summary>
        void DeliverMessageToLocalEntity(EntityState entity, IRoutedMessage routed, bool isReplyLike = false)
        {
            // Log error about invalid status (but try to deliver in any case)
            if (entity.Status != EntityStatus.Running && !(isReplyLike && entity.Status == EntityStatus.Starting))
                _log.Error("Trying to flush message to Entity {EntityId} in invalid state ({EntityStatus})", entity.EntityId, entity.Status);

            // Subscribe delivered to local entity: register entity watch in both directions
            int typeCode = MetaSerializationUtil.PeekMessageTypeCode(routed.Message);
            if (typeCode == MessageCodesCore.EntitySubscribe)
            {
                _log.Verbose("Register watch due to {MessageName}: {FromEntityId} <-> {ToEntityId} (typeCode={TypeCode})", "Subscribe", routed.FromEntityId, routed.TargetEntityId, typeCode);
                RegisterTwoWayEntityWatch(routed.FromEntityId, routed.TargetEntityId);
            }
            else if (typeCode == MessageCodesCore.EntityUnsubscribe)
            {
                _log.Verbose("Unregister watch due to {MessageName}: {FromEntityId} <-> {ToEntityId} (typeCode={TypeCode})", "Unsubscribe", routed.FromEntityId, routed.TargetEntityId, typeCode);
                UnregisterTwoWayEntityWatch(routed.FromEntityId, routed.TargetEntityId);
            }

            // Forward the message to the child actor
            entity.Actor.Tell(routed);
        }

        /// <summary>
        /// Flush all pending messages to a given local Entity.
        /// </summary>
        /// <param name="entity"></param>
        void FlushPendingMessagesToLocalEntity(EntityState entity)
        {
            foreach (IRoutedMessage routed in entity.PendingMessages)
                DeliverMessageToLocalEntity(entity, routed);
            entity.PendingMessages.Clear();
        }

        /// <summary>
        /// Route a <see cref="IRoutedMessage"/> to an entity that is owned by this shard. The message is
        /// delivered immediately if the Entity is in Running state, or put into the pending messages queue
        /// otherwise.
        /// </summary>
        /// <param name="entity">Local entity to deliver message to</param>
        /// <param name="routed">Message to deliver</param>
        void RouteMessageToLocalEntity(EntityState entity, IRoutedMessage routed)
        {
            // Check that EntityAskReplies are not delivered here
            if (routed is EntityAskReply)
            {
                _log.Error($"Should never get EntityAskReplies in RouteMessageToLocalEntity()!");
                return;
            }

            int typeCode = MetaSerializationUtil.PeekMessageTypeCode(routed.Message);
            bool isReplyLike = (typeCode == MessageCodesCore.EntitySubscriberKicked);

            // Deliver if Entity is in Running state
            // Reply-like messages are also deliveded in Starting phase
            if (entity.Status == EntityStatus.Running || (isReplyLike && entity.Status == EntityStatus.Starting))
            {
                //_log.Debug("Delivering {ContainerType} / {MessageType} to local {EntityId} (status={EntityStatus})", routed.GetType().Name, MetaSerializationUtil.PeekMessageName(routed.Message), entity.EntityId, entity.Status);
                DeliverMessageToLocalEntity(entity, routed, isReplyLike);
            }
            else // not in Running state, so just buffer up the message
            {
                //_log.Debug("Buffering message {MessageType} to local {EntityId} (status={EntityStatus})", routed.GetType().Name, entity.EntityId, entity.Status);
                entity.PendingMessages.Add(routed);
            }
        }

        protected IActorRef SpawnEntityActor(EntityId entityId)
        {
            // If no entity props func, don't event try to spawn
            if (_entityProps == null)
                return null;

            // Spawn actor for entity & start watching
            IActorRef actor = Context.ActorOf(_entityProps(entityId), entityId.ToString());
            Context.Watch(actor);

            // Send InitializeEntity to ensure it's the first received command
            actor.Tell(InitializeEntity.Instance);

            return actor;
        }

        protected EntityState GetOrSpawnEntity(EntityId entityId, Func<EntityId, IActorRef> spawnFunc)
        {
            if (!_entityStates.TryGetValue(entityId, out EntityState entity))
            {
                // Spawn entity & create state (only if spawned successfully)
                // \note spawning can fail when ClientConnections receive messages after getting destroyed
                IActorRef actor = spawnFunc(entityId);
                if (actor != null)
                {
                    c_entitySpawned.WithLabels(EntityKind.ToString()).Inc();
                    entity = new EntityState(entityId, actor);
                    _entityStates.Add(entityId, entity);
                    _entityByActor.Add(actor, entity);
                }
            }
            return entity;
        }

        void RegisterDirectedEntityWatch(EntityId fromEntityId, EntityId toEntityId)
        {
            // Ensure watch set exists
            if (!_entityWatches.TryGetValue(fromEntityId, out HashSet<EntityId> watchSet))
            {
                watchSet = new HashSet<EntityId>();
                _entityWatches.Add(fromEntityId, watchSet);
            }

            // Add to watch set
            // \todo [petri] how to handle dupes? keep track of sub counts instead?
            if (!watchSet.Add(toEntityId))
                _log.Verbose("Entity watch {FromEntityId} -> {ToEntityId} already exists!", fromEntityId, toEntityId);
        }

        void RegisterTwoWayEntityWatch(EntityId fromEntityId, EntityId toEntityId)
        {
            if (fromEntityId == toEntityId)
            {
                _log.Warning("Entity trying to watch self!");
                return;
            }

            RegisterDirectedEntityWatch(fromEntityId, toEntityId);
            RegisterDirectedEntityWatch(toEntityId, fromEntityId);
        }

        void UnregisterDirectedEntityWatch(EntityId fromEntityId, EntityId toEntityId)
        {
            // \note Duplicate removals may happen when a subscriber gets kicked while doing an unsubscribe
            if (_entityWatches.TryGetValue(fromEntityId, out HashSet<EntityId> watchSet))
            {
                if (!watchSet.Remove(toEntityId))
                    _log.Debug("Removing non-existent entity watch {FromEntityId} -> {ToEntityId} (target not found in watchSet)", fromEntityId, toEntityId);

                // Remove empty HashSets to avoid leaking memory (for remote entities)
                if (watchSet.Count == 0)
                    _entityWatches.Remove(fromEntityId);
            }
            else
                _log.Debug("Removing non-existent entity watch {FromEntityId} -> {ToEntityId} (no watchSet found for source)", fromEntityId, toEntityId);
        }

        void UnregisterTwoWayEntityWatch(EntityId fromEntityId, EntityId toEntityId)
        {
            if (fromEntityId == toEntityId)
            {
                _log.Warning("Entity {EntityId} trying to unwatch self!", fromEntityId);
                return;
            }

            UnregisterDirectedEntityWatch(fromEntityId, toEntityId);
            UnregisterDirectedEntityWatch(toEntityId, fromEntityId);
        }

        void InformTerminatedEntityWatchers(EntityId deadEntityId)
        {
            if (_entityWatches.Remove(deadEntityId, out HashSet<EntityId> watchSet) && watchSet.Count > 0)
            {
                _log.Debug("Inform watchers of {EntityId} of entity death: {EntitySet}", deadEntityId, string.Join(", ", watchSet));

                // Remove all watches of entity
                foreach (EntityId toEntityId in watchSet)
                    UnregisterDirectedEntityWatch(toEntityId, deadEntityId);

                // Collect per-shard list of entities that should notify about
                HashSet<EntityShardId> notifyShards = new HashSet<EntityShardId>();
                foreach (EntityId watcherId in watchSet)
                {
                    EntityKind              watcherKind         = watcherId.Kind;
                    EntityKindShardState    targetShardStates   = _entitySharding.GetShardStatesForKind(watcherKind);
                    EntityShardId           shardId             = targetShardStates.Strategy.ResolveShardId(watcherId);

                    if (shardId != _selfShardId)
                    {
                        notifyShards.Add(shardId);
                    }
                    else
                    {
                        if (_entityStates.TryGetValue(watcherId, out EntityState entityState))
                        {
                            _log.Verbose("Inform local watcher {WatcherId} (status={WatcherState}) of entity {EntityId} termination", watcherId, entityState.Status, deadEntityId);
                            entityState.Actor.Tell(new WatchedEntityTerminated(deadEntityId));
                        }
                        else
                            _log.Warning("Local watcher {WatcherId} of entity {EntityId} not found!", watcherId, deadEntityId);
                    }
                }

                // Send notification to all watching remote shards (if any)
                if (notifyShards.Count > 0)
                {
                    WatchedEntityTerminated msg = new WatchedEntityTerminated(deadEntityId);

                    // Inform all shards of lost entity
                    foreach (EntityShardId shardId in notifyShards)
                    {
                        EntityKindShardState targetShardStates = _entitySharding.GetShardStatesForKind(shardId.Kind);
                        if (shardId.Value >= 0 && shardId.Value < targetShardStates.RemoteActors.Length)
                        {
                            IActorRef shardActor = targetShardStates.RemoteActors[shardId.Value];
                            shardActor.Tell(msg);
                        }
                        else
                            _log.Warning("Invalid ShardId: {ShardId}", shardId);
                    }
                }
            }
            //else
            //    _log.Debug("No watchers for terminated {EntityId}, nothing to inform", entityId);
        }

        #region EntitySynchronize

        IActorRef TryGetShardForEntity(EntityId entityId)
        {
            EntityId                targetEntityId      = entityId;
            EntityKindShardState    targetShardStates   = _entitySharding.TryGetShardStatesForKind(targetEntityId.Kind);
            if (targetShardStates == null)
            {
                _log.Error("No shards registered for target {TargetEntityId} kind", entityId);
                return null;
            }

            EntityShardId           shardId             = targetShardStates.Strategy.ResolveShardId(targetEntityId);
            int                     shardNdx            = shardId.Value;

            if (shardNdx < 0 || shardNdx >= targetShardStates.RemoteActors.Length)
            {
                _log.Error("Invalid shard {ShardId} for target {TargetEntityId} ({NumShards} shards for entity)", shardId, targetEntityId, targetShardStates.RemoteActors.Length);
                return null;
            }

            if (shardId == _selfShardId)
            {
                return _self;
            }

            return targetShardStates.RemoteActors[shardNdx];
        }

        void ReceiveEntitySynchronizationE2SBeginRequest(EntitySynchronizationE2SBeginRequest req)
        {
            if (!_entityStates.TryGetValue(req.FromEntityId, out EntityState entity))
            {
                _log.Warning("Got EntitySynchronizationE2SBeginRequest for unknown entity: {EntityId}", req.FromEntityId);
                return;
            }

            // Create id from Source entity <-> Source shard communication

            int localChannelId = entity.EntitySyncRunningId++;

            EntitySyncState syncState = new EntitySyncState(
                localChannelId:     localChannelId,
                remoteChannelId:    -1,
                localEntityId:      req.FromEntityId,
                remoteEntityId:     req.TargetEntityId,
                openingPromise:     req.OpeningPromise,
                writerPeer:         req.WriterPeer
                );
            entity.EntitySyncs.Add(localChannelId, syncState);

            // Send to the destination shard.
            // \todo: if target is on the same node, could use more optimized strategy. We could skip
            //        messaging, and fill sync promise and inbox directly. Even maybe from the entity.
            //        Note that this really means same node, not the same shard, which is unlikely due
            //        to the deadlock risk.

            IActorRef shardActorRef = TryGetShardForEntity(req.TargetEntityId);
            if (shardActorRef == null)
            {
                _log.Error("Could not process EntitySynchronizationE2SBeginRequest targeting {TargetEntityId}", req.TargetEntityId);
                return;
            }

            EntitySynchronizationS2SBeginRequest shardToShardRequest = new EntitySynchronizationS2SBeginRequest(
                sourceChannelId:    localChannelId,
                targetEntityId:     req.TargetEntityId,
                fromEntityId:       req.FromEntityId,
                message:            req.Message
                );
            shardActorRef.Tell(shardToShardRequest);
        }

        void ReceiveEntitySynchronizationS2SBeginRequest(EntitySynchronizationS2SBeginRequest req)
        {
            EntityId fromEntityId = req.FromEntityId;
            if (!fromEntityId.IsValid)
            {
                _log.Error("Invalid EntitySynchronizationS2SBeginRequest, source {FromEntityId} is not valid", fromEntityId);
                return;
            }

            EntityId                targetEntityId      = req.TargetEntityId;
            EntityKindShardState    targetShardStates   = _entitySharding.TryGetShardStatesForKind(targetEntityId.Kind);
            if (targetShardStates == null || targetShardStates.Strategy.ResolveShardId(targetEntityId) != _selfShardId)
            {
                _log.Error("Invalid shard for EntitySynchronizationS2SBeginRequest, target {TargetEntityId} not allocated for this shard", targetEntityId);
                return;
            }

            EntityState entity = GetOrSpawnEntity(targetEntityId, SpawnEntityActor);
            if (entity == null)
            {
                if (_entityProps != null)
                    _log.Warning("Failed to spawn entity {EntityId} to receive EntitySynchronize", targetEntityId);
                else
                    _log.Warning("Cannot spawn entity {EntityId} to receive EntitySynchronize", targetEntityId);
                return;
            }

            // Create id from Target Entity <-> Target shard communication

            int localChannelId = entity.EntitySyncRunningId++;

            EntitySyncState syncState = new EntitySyncState(
                localChannelId:     localChannelId,
                remoteChannelId:    req.SourceChannelId,
                localEntityId:      targetEntityId,
                remoteEntityId:     fromEntityId,
                openingPromise:     null,
                writerPeer:         null
                );
            entity.EntitySyncs.Add(localChannelId, syncState);

            // Forward to the entity
            EntitySynchronizationS2EBeginRequest entityRequest = new EntitySynchronizationS2EBeginRequest(
                channelId:      localChannelId,
                targetEntityId: targetEntityId,
                fromEntityId:   fromEntityId,
                message:        req.Message
                );

            // Deliver message to the entity (may also buffer)
            RouteMessageToLocalEntity(entity, entityRequest);
        }

        void ReceiveEntitySynchronizationS2EBeginResponse(EntitySynchronizationS2EBeginResponse response)
        {
            if (!_entityStates.TryGetValue(response.FromEntityId, out EntityState entity))
            {
                _log.Warning("Got EntitySynchronizationS2EBeginResponse for unknown entity: {EntityId}", response.FromEntityId);
                return;
            }
            if (!entity.EntitySyncs.TryGetValue(response.ChannelId, out EntitySyncState syncState))
            {
                _log.Warning("Got EntitySynchronizationS2EBeginResponse for unknown channel: Channel {ChannelId}", response.ChannelId);
                return;
            }
            if (syncState.WriterPeer != null)
            {
                _log.Warning("Got EntitySynchronizationS2EBeginResponse at unexpected time: Channel {ChannelId}", response.ChannelId);
                return;
            }

            // set writer once.
            syncState.WriterPeer = response.WriterPeer;

            // Reply to remote shard
            IActorRef shardActorRef = TryGetShardForEntity(syncState.RemoteEntityId);
            if (shardActorRef == null)
            {
                _log.Error("Could not process EntitySynchronizationS2EBeginResponse targeting {TargetEntityId}", syncState.RemoteEntityId);
                return;
            }

            EntitySynchronizationS2SBeginResponse shardResponse = new EntitySynchronizationS2SBeginResponse(
                targetEntityId:     syncState.RemoteEntityId,
                sourceChannelId:    syncState.LocalChannelId,
                targetChannelId:    syncState.RemoteChannelId
                );

            shardActorRef.Tell(shardResponse);

            // sync channel is now open
        }

        void ReceiveEntitySynchronizationS2SBeginResponse(EntitySynchronizationS2SBeginResponse response)
        {
            if (!_entityStates.TryGetValue(response.TargetEntityId, out EntityState entity))
            {
                _log.Warning("Got EntitySynchronizationS2SBeginResponse for unknown entity: {EntityId}", response.TargetEntityId);
                return;
            }
            if (!entity.EntitySyncs.TryGetValue(response.TargetChannelId, out EntitySyncState syncState))
            {
                _log.Warning("Got EntitySynchronizationS2SBeginResponse for unknown channel: Channel {ChannelId}", response.TargetChannelId);
                return;
            }
            if (syncState.RemoteChannelId != -1)
            {
                _log.Warning("Got EntitySynchronizationS2SBeginResponse at unexpected time: Channel {ChannelId}", response.TargetChannelId);
                return;
            }

            // set channel once and trigger open promise
            syncState.RemoteChannelId = response.SourceChannelId;
            syncState.OpeningPromise.SetResult(syncState.LocalChannelId);
            syncState.OpeningPromise = null;

            // sync channel is now open
        }

        void ReceiveEntitySynchronizationE2SChannelMessage(EntitySynchronizationE2SChannelMessage channelMessage)
        {
            bool isEndOfStream = channelMessage.Message.IsEmpty;

            if (!_entityStates.TryGetValue(channelMessage.FromEntityId, out EntityState entity))
            {
                _log.Warning("Got EntitySynchronizationE2SChannelMessage for unknown entity: {EntityId}", channelMessage.FromEntityId);
                return;
            }
            if (!entity.EntitySyncs.TryGetValue(channelMessage.ChannelId, out EntitySyncState syncState))
            {
                _log.Warning("Got EntitySynchronizationE2SChannelMessage for unknown channel: Channel {ChannelId}", channelMessage.ChannelId);
                return;
            }

            if (isEndOfStream)
                entity.EntitySyncs.Remove(channelMessage.ChannelId);

            // Deliver to the target shard, unless already closed there.

            if (syncState.IsRemoteClosed)
            {
                if (!isEndOfStream)
                    _log.Warning("Entity {EntityId} attempted to write to a closed sync channel {ChannelId} to {TargetEntityId}", syncState.LocalEntityId,  channelMessage.ChannelId, syncState.RemoteEntityId);
                return;
            }

            IActorRef shardActorRef = TryGetShardForEntity(syncState.RemoteEntityId);
            if (shardActorRef == null)
            {
                _log.Error("Could not process EntitySynchronizationE2SChannelMessage targeting {TargetEntityId}", syncState.RemoteEntityId);
                return;
            }

            EntitySynchronizationS2SChannelMessage shardToShardRequest = new EntitySynchronizationS2SChannelMessage(
                targetEntityId: syncState.RemoteEntityId,
                channelId:      syncState.RemoteChannelId,
                message:        channelMessage.Message
                );
            shardActorRef.Tell(shardToShardRequest);
        }

        void ReceiveEntitySynchronizationS2SChannelMessage(EntitySynchronizationS2SChannelMessage channelMessage)
        {
            bool isEof = channelMessage.Message.IsEmpty;

            if (!_entityStates.TryGetValue(channelMessage.TargetEntityId, out EntityState entity))
            {
                _log.Warning("Got EntitySynchronizationS2SChannelMessage for unknown entity: {EntityId}", channelMessage.TargetEntityId);
                return;
            }
            if (!entity.EntitySyncs.TryGetValue(channelMessage.ChannelId, out EntitySyncState syncState))
            {
                // Acknowlegding channel close? If so, ignore. We most likely just closed the channel from this end. Otherwise want
                if (!isEof)
                    _log.Warning("Got EntitySynchronizationS2SChannelMessage for unknown channel: Channel {ChannelId}", channelMessage.ChannelId);
                return;
            }

            if (!isEof)
            {
                bool success = syncState.WriterPeer.TryWrite(channelMessage.Message);
                if (!success)
                    _log.Error("While processing EntitySynchronization message unbounded queue refused write");
            }
            else
            {
                syncState.WriterPeer.Complete();
                syncState.IsRemoteClosed = true;
                // \note: we don't remove half-closed connection from entity.EntitySyncs. We wait for the local entity to clear it.
            }
        }

        #endregion
    }
}
