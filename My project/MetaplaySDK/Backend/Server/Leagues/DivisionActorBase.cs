// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Analytics;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Guild;
using Metaplay.Core.League;
using Metaplay.Core.League.Actions;
using Metaplay.Core.League.Messages;
using Metaplay.Core.League.Player;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.EventLog;
using Metaplay.Server.League.InternalMessages;
using Metaplay.Server.League.Player.InternalMessages;
using Metaplay.Server.MaintenanceJob;
using Metaplay.Server.MultiplayerEntity;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace Metaplay.Server.League
{
    /// <summary>
    /// The core persisted fields of a division. Used when storing in a database.
    /// </summary>
    [Table("Divisions")]
    [MetaPersistedVirtualItem]
    public abstract class PersistedDivisionBase : PersistedMultiplayerEntityBase
    {
    }

    [EntityMaintenanceRefreshJob]
    [EntityMaintenanceSchemaMigratorJob]
    public abstract class DivisionEntityConfigBase : PersistedEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCore.Division;
        public override IShardingStrategy   ShardingStrategy        => new HashedShardingStrategy(Application.Instance.ClusterConfig, EntityKindCore.Division);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Base class for Division Actors. Division is a shared entity where division participants compete with each
    /// by accumulating their own score. A single Division is always tied to a single Season which is a predetermined
    /// period of time. After this season ends, the Division is Concluded and final rewards are given out.
    /// </summary>
    public abstract class DivisionActorBase<TModel, TServerModel, TPersisted>
        : PersistedMultiplayerEntityActorBase<TModel, DivisionActionBase, TPersisted>
        , IDivisionModelServerListenerCore
        , IServerActionDispatcher
        where TModel : class, IDivisionModel<TModel>, new()
        where TServerModel: class, IDivisionServerModel, new()
        where TPersisted : PersistedDivisionBase, new()
    {
        protected override TimeSpan           SnapshotInterval => TimeSpan.FromMinutes(3);
        protected override AutoShutdownPolicy ShutdownPolicy   => AutoShutdownPolicy.ShutdownAfterSubscribersGone(lingerDuration: TimeSpan.FromSeconds(5));

        // Stop ticking when division is concluded
        protected override bool IsTicking => !Model.IsConcluded;

        CancellationTokenSource _concludeCancellationTokenSource;

        // \todo Duplicated between PlayerActorBase and GuildActorBase
        private   RandomPCG                                                          _analyticsEventRandom;
        protected AnalyticsEventBatcher<DivisionEventBase, DivisionAnalyticsContext> _analyticsEventBatcher;
        protected AnalyticsEventHandler<IDivisionModel, DivisionEventBase>           _analyticsEventHandler; // Analytics event handler: queue up in _analyticsEventBatcher for sending down to AnalyticsDispatcher


        protected class SessionPeer : ClientPeerState
        {
            public EntityId ParticipantId;

            public SessionPeer(ClientSlot clientSlot, int clientChannelId, EntityId playerId, EntityId participantId) : base(clientSlot, clientChannelId, playerId)
            {
                ParticipantId = participantId;
            }
        }

        protected DivisionActorBase(EntityId entityId) : base(entityId, logChannelName: "division")
        {
            // Initialize analytics events collecting: batch events for sending to AnalyticsDispatcher
            // \todo Duplicated between PlayerActorBase, GuildActorBase and DivisionActorBase, move to some base class
            _analyticsEventRandom  = RandomPCG.CreateNew();
            _analyticsEventBatcher = new AnalyticsEventBatcher<DivisionEventBase, DivisionAnalyticsContext>(entityId, maxBatchSize: 100);
            _analyticsEventHandler = new AnalyticsEventHandler<IDivisionModel, DivisionEventBase>((model, payload) =>
            {
                // \note The given `model` is not necessarily the currently-active `Model`,
                //       for example when this event handler is invoked from MigrateState
                //       (which operates on an explicitly-provided model instead of the
                //       currently-active `Model`).
                //
                //       This event handler should be careful to not use `Model`, except
                //       after explicitly checking it is appropriate.

                // Metadata
                AnalyticsEventSpec      eventSpec     = AnalyticsEventRegistry.GetEventSpec(payload.GetType());
                string                  eventType     = eventSpec.EventType;
                MetaTime                collectedAt   = MetaTime.Now;
                MetaTime                modelTime     = model.CurrentTime;
                MetaUInt128             uniqueId      = new MetaUInt128((ulong)collectedAt.MillisecondsSinceEpoch, _analyticsEventRandom.NextULong());
                int                     schemaVersion = eventSpec.SchemaVersion;
                IGameConfigDataResolver resolver      = model.GetDataResolver();

                // Add to the model's event log (if enabled)
                if (eventSpec.IncludeInEventLog)
                {
                    // \todo [jarkko] Not implemented.
                }

                // Enqueue event for sending analytics (if enabled)
                if (eventSpec.SendToAnalytics)
                {
                    DivisionAnalyticsContext context = GetAnalyticsContext();
                    OrderedDictionary<AnalyticsLabel, string> labels = GetAnalyticsLabels((TModel)model);
                    _analyticsEventBatcher.Enqueue(_entityId, collectedAt, modelTime, uniqueId, eventType, schemaVersion, payload, context, labels, resolver, model.LogicVersion);
                }
            });
        }

        protected override Task SetUpModelAsync(TModel model, IMultiplayerEntitySetupParams setupParams)
        {
            DivisionSetupParams divisionSetup = (DivisionSetupParams)setupParams;

            model.StartsAt              = divisionSetup.ScheduledStartTime;
            model.EndsAt                = divisionSetup.ScheduledEndTime;
            model.EndingSoonStartsAt    = divisionSetup.ScheduledEndingSoonTime;
            model.DivisionIndex         = new DivisionIndex(divisionSetup.League, divisionSetup.Season, divisionSetup.Rank, divisionSetup.Division);
            model.AnalyticsEventHandler = _analyticsEventHandler;

            model.ServerModel = new TServerModel();
            InitializeServerModel(model, model.ServerModel as TServerModel);

            // Set CurrentTime here for analytics.
            model.ResetTime(MetaTime.Now);

            model.EventStream.Event(new DivisionEventCreated(model.DivisionIndex));

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override async Task OnEntityInitialized()
        {
            await ScheduleOrDoConclude(Model.EndsAt);
        }

        protected override async Task OnShutdown()
        {
            // Flush any pending events
            _analyticsEventBatcher.Flush();

            await base.OnShutdown();
        }

        protected override void OnBeforePersist()
        {
            base.OnBeforePersist();

            // Flush any pending events
            _analyticsEventBatcher.Flush();
        }

        Task ScheduleOrDoConclude(MetaTime concludeTime)
        {
            _concludeCancellationTokenSource?.Cancel();
            _concludeCancellationTokenSource = new CancellationTokenSource();

            if (MetaTime.Now > concludeTime)
            {
                // Conclude immediately
                return ConcludeSeason();
            }
            else
            {
                int maxDelayMilliseconds = RuntimeOptionsRegistry.Instance.GetCurrent<LeagueManagerOptions>().ConcludeSeasonMaxDelayMilliseconds;
                RandomPCG random = RandomPCG.CreateNew();
                TimeSpan  delay  = maxDelayMilliseconds > 0 ?
                    TimeSpan.FromMilliseconds(random.NextInt(maxDelayMilliseconds)) : TimeSpan.Zero;
                // Conclude later
                ScheduleExecuteOnActorContext(concludeTime.ToDateTime() + delay, ConcludeSeason, _concludeCancellationTokenSource.Token);
            }
            return Task.CompletedTask;
        }

        [EntityAskHandler]
        async Task<InternalDivisionForceSetupDebugResponse> HandleInternalDivisionSetupDebugRequest(InternalDivisionForceSetupDebugRequest request)
        {
            bool isSetUp = _journal != null;
            if (!isSetUp)
            {
                // If not set up, setup now. (Debug only. This only happens if entity initialization fails).
                TModel model = new TModel();
                model.EntityId = _entityId;
                model.CreatedAt = MetaTime.Now;
                await SetUpModelAsync(model, request.Args);
                SwitchToNewModelImmediately(model);
            }
            else
            {
                // Flush changes made to old state
                UpdateTicksAndFlush();

                // We are modifing state directly. Kick all subscribers since this would checksum mismatch otherwise. It is ok -- this is a debug feature.
                foreach (EntitySubscriber subscriber in _subscribers.Values.ToList())
                {
                    if (subscriber.Topic == EntityTopic.Participant)
                        KickSubscriber(subscriber, new InternalSessionDivisionDebugReset(creatorId: request.Args.CreatorId));
                }

                // Now create brand new state and switch to it to flush changes.
                TModel model = new TModel();
                model.EntityId  = _entityId;
                model.CreatedAt = MetaTime.Now;
                await SetUpModelAsync(model, request.Args);
                SwitchToNewModelImmediately(model);
            }

            await PersistStateIntermediate();
            return new InternalDivisionForceSetupDebugResponse(true);
        }

        [MessageHandler]
        async Task HandleInternalDivisionDebugSeasonScheduleUpdate(InternalDivisionDebugSeasonScheduleUpdate message)
        {
            if (Model.IsConcluded) // Already concluded. Cannot change season parameters.
                return;

            if(message.NewStartTime != Model.StartsAt)
                ExecuteAction(new DivisionSetSeasonStartsAtDebug(message.NewStartTime));

            if(message.NewEndTime != Model.EndsAt)
            {
                ExecuteAction(new DivisionSetSeasonEndsAtDebug(message.NewEndTime));

                await ScheduleOrDoConclude(message.NewEndTime);
            }
        }

        [MessageHandler]
        void HandleScoreEventMessage(InternalDivisionScoreEventMessage message)
        {
            if (Model.IsConcluded)
            {
                if(_log.IsInformationEnabled)
                    _log.Info("Received a score update event from {Participant} after season is already concluded!", message.ParticipantId);
                return; // Is already concluded.
            }
            if (!Model.TryGetParticipant(Model.GetParticipantIndexById(message.ParticipantId), out IDivisionParticipantState participant))
            {
                if (_log.IsWarningEnabled)
                    _log.Warning("Received a score update event from {Participant} that does not belong to this division!", message.ParticipantId);
                // \todo send reply or ignore?
                return; // Didn't have participant.
            }

            if (Model.ComputeSeasonPhaseAt(MetaTime.Now) != DivisionSeasonPhase.Ongoing)
            {
                _log.Info("Received a score update event from {Participant} when season is not ongoing!", message.ParticipantId);
                return;
            }

            int participantIndex = Model.GetParticipantIndexById(message.ParticipantId);

            // Try apply the event

            if (!TryApplyScoreEvent(participantIndex, message.PlayerId, message.ScoreEvent))
            {
                if(_log.IsInformationEnabled)
                    _log.Info("Score event from {Participant} was refused", message.ParticipantId);
                return;
            }

            // \todo Put event to event log
        }

        [EntityAskHandler]
        protected InternalDivisionParticipantHistoryResponse HandleParticipantHistoryRequest(InternalDivisionParticipantHistoryRequest request)
        {
            if (_journal == null)
                throw new InvalidEntityAsk("Cannot request history from a division that has not been initialized!");

            EntityId participantId = request.ParticipantId;
            bool     isParticipant = Model.TryGetParticipant(Model.GetParticipantIndexById(participantId), out IDivisionParticipantState participant);

            if (!isParticipant || !Model.IsConcluded)
                return new InternalDivisionParticipantHistoryResponse(isParticipant, Model.IsConcluded, null);

            IDivisionHistoryEntry history = HandleParticipantHistoryRequestInternal(participantId, participant);

            return new InternalDivisionParticipantHistoryResponse(true, true, history);
        }

        /// <summary>
        /// Called from <see cref="HandleParticipantHistoryRequest"/> if the participant has been found and model has concluded.
        /// The returned history entry will be sent back to to the asker.
        /// </summary>
        protected abstract IDivisionHistoryEntry HandleParticipantHistoryRequestInternal(EntityId participantId, IDivisionParticipantState participant);


        [EntityAskHandler]
        public InternalDivisionProgressStateResponse HandleInternalDivisionProgressStateRequest(InternalDivisionProgressStateRequest _)
        {
            if (_journal == null)
                throw new InvalidEntityAsk("Cannot ask for progress state if not set up!");

            return new InternalDivisionProgressStateResponse(
                isConcluded: Model.IsConcluded,
                startsAt: Model.StartsAt,
                endsAt: Model.EndsAt);
        }


        [EntityAskHandler]
        async Task<InternalDivisionParticipantResultResponse> HandleInternalDivisionParticipantResultRequest(InternalDivisionParticipantResultRequest request)
        {
            if (Model == null)
                throw new InvalidEntityAsk("Division not initialized yet.");

            // Force conclude if the division has not concluded already.
            if (!Model.IsConcluded)
                await ConcludeSeason();

            Dictionary<EntityId, IDivisionParticipantConclusionResult> results = new Dictionary<EntityId, IDivisionParticipantConclusionResult>();

            IEnumerable<EntityId> participantIds = request.ParticipantEntityIds;

            if(request.ParticipantEntityIds == null)
                participantIds = Model.EnumerateParticipants()
                    .Where(i => Model.ServerModel.ParticipantIndexToEntityId.ContainsKey(i))
                    .Select(i => Model.ServerModel.ParticipantIndexToEntityId[i]);

            foreach (EntityId participantId in participantIds)
            {
                int participantIndex = Model.GetParticipantIndexById(participantId);
                if (Model.TryGetParticipant(participantIndex, out _))
                    results.Add(participantId, GetParticipantResult(participantIndex));
            }

            return new InternalDivisionParticipantResultResponse(results);
        }

        [EntityAskHandler]
        async Task<EntityAskOk> HandleLeagueLeaveRequest(EntityId sender, InternalLeagueLeaveRequest request)
        {
            if(!sender.IsOfKind(EntityKindCloudCore.LeagueManager))
                throw new InvalidEntityAsk("Only league manager can ask to leave a league! Send to league manager instead.");

            if (Model == null)
                throw new InvalidEntityAsk("Division not initialized yet.");

            int participantIndex = Model.GetParticipantIndexById(request.ParticipantId);

            if (Model.TryGetParticipant(participantIndex, out _))
                ExecuteAction(new DivisionParticipantRemove(participantIndex));

            await PersistStateIntermediate();

            return EntityAskOk.Instance;
        }

        [EntityAskHandler]
        InternalDivisionParticipantIdResponse HandleInternalDivisionParticipantIdRequest(InternalDivisionParticipantIdRequest request)
        {
            if (Model == null)
                throw new InvalidEntityAsk("Division not initialized yet.");

            if(Model.ServerModel.ParticipantIndexToEntityId.TryGetValue(request.ParticipantIndex, out EntityId participantId))
                return new InternalDivisionParticipantIdResponse(participantId);
            else
                return new InternalDivisionParticipantIdResponse(EntityId.None);
        }

        protected override sealed Task OnClientSessionHandshake(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase request)
        {
            int participantIndex = Model.GetParticipantIndexById(request.AssociationRef.SourceEntity);
            if (!Model.TryGetParticipant(participantIndex, out IDivisionParticipantState participantBase))
                throw new InternalEntitySubscribeRefusedBase.Builtins.NotAParticipant();
            if (participantBase.AvatarDataEpoch != ((AssociatedDivisionRef)request.AssociationRef).DivisionAvatarEpoch)
                throw new InternalDivisionSubscribeRefusedParticipantAvatarDesync();

            return OnClientSessionHandshake(sessionId, playerId, request, participantBase);
        }

        protected override ChannelContextDataBase CreateClientSessionStartChannelContext(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase, List<AssociatedEntityRefBase> associatedEntities)
        {
            return new DivisionChannelContextData(requestBase.AssociationRef.GetClientSlot(), participantId: requestBase.AssociationRef.SourceEntity);
        }

        protected override void OnParticipantSessionEnded(EntitySubscriber session)
        {
        }

        protected override ClientPeerState CreateClientPeer(EntityId playerId, InternalEntitySubscribeRequestBase request, InternalEntitySubscribeResponseBase response)
        {
            return new SessionPeer(request.AssociationRef.GetClientSlot(), request.ClientChannelId, playerId, request.AssociationRef.SourceEntity);
        }

        /// <inheritdoc />
        protected override void OnBeforeSchemaMigration(TModel payload, int fromVersion, int toVersion)
        {
            // Add servermodel if missing
            if (payload.ServerModel == null)
            {
                payload.ServerModel = new TServerModel();
                InitializeServerModel(payload, payload.ServerModel as TServerModel);
            }
        }

        #region ServerModel ticking

        /// <summary>
        /// Used by <see cref="IDivisionServerModel.OnModelServerTick"/> to execute an action on the timeline.
        /// </summary>
        /// <param name="action"></param>
        void IServerActionDispatcher.ExecuteAction(ModelAction action)
        {
            ExecuteAction(action as DivisionActionBase, false);
        }

        /// <inheritdoc />
        protected override void OnPostTick(int tick)
        {
            base.OnPostTick(tick);

            // Execute server model tick
            Model.ServerModel.OnModelServerTick(Model, this);
        }

        /// <inheritdoc />
        protected override void OnFastForwardedTime(TModel model, MetaDuration elapsed)
        {
            base.OnFastForwardedTime(model, elapsed);
            model.ServerModel.OnFastForwardModel(model, elapsed);
        }

        #endregion

        // \todo remove? Just used for testing now.
        #region Debug Testing

        public void OnSeasonDebugConcluded()
        {
            // \todo Not this. HandleSeasonEnd should instead initiate setting to concluded
            Context.Self.Tell(InternalDebugHandleSeasonConcludeCommand.Instance, Context.Self);
        }

        public void OnSeasonDebugEnded()
        {
            // Inform sessions
            // SendToAllSessions(ClientSlotCore.Player, ClientSlotCore.Guild, InternalDivisionProgressStateChangedMessage.Instance);
        }

        // \todo remove
        public sealed class InternalDebugHandleSeasonConcludeCommand
        {
            public static InternalDebugHandleSeasonConcludeCommand Instance = new InternalDebugHandleSeasonConcludeCommand();
        }

        [CommandHandler]
        async Task HandleSeasonConcludeCommand(InternalDebugHandleSeasonConcludeCommand _)
        {
            await ConcludeSeason();
        }

        protected virtual Task ConcludeSeason()
        {
            if (Model.IsConcluded) // Already concluded
                return Task.CompletedTask;

            ExecuteAction(new DivisionConclude());

            ResolveParticipantRewards();

            // Inform sessions
            // \todo: Actually handle this message somehow?
            //SendToAllSessions(ClientSlotCore.Player, ClientSlotCore.Guild, InternalDivisionProgressStateChangedMessage.Instance);

            return Task.CompletedTask;
        }

        [EntityAskHandler]
        EntityAskOk HandleInternalDivisionMoveToNextSeasonPhaseDebugRequest(InternalDivisionMoveToNextSeasonPhaseDebugRequest request)
        {
            DivisionSeasonPhase currentPhase = Model.ComputeSeasonPhaseAt(ModelUtil.TimeAtTick(Model.CurrentTick, Model.TimeAtFirstTick, Model.TicksPerSecond));
            DivisionSeasonPhase nextPhase = (currentPhase == DivisionSeasonPhase.Concluded) ? (DivisionSeasonPhase.Preview) : (currentPhase + 1);

            if (request.RequestedNextPhase != nextPhase)
                throw new InvalidEntityAsk($"Cannot advance to {request.RequestedNextPhase}. Next phase is {nextPhase}");

            switch (currentPhase)
            {
                case DivisionSeasonPhase.Preview:
                {
                    ExecuteAction(new DivisionSetSeasonStartsAtDebug(MetaTime.Now));
                    break;
                }
                case DivisionSeasonPhase.Ongoing:
                {
                    ExecuteAction(new DivisionSetSeasonEndsAtDebug(MetaTime.Now));
                    break;
                }
                case DivisionSeasonPhase.Resolving:
                {
                    ExecuteAction(new DivisionConcludeSeasonDebug());
                    break;
                }
                case DivisionSeasonPhase.Concluded:
                {
                    // Restart
                    // \note: Copy-pasta from Setup logic

                    UpdateTicksAndFlush();

                    // We are modifing state directly. Kick all subscribers since this would checksum mismatch otherwise. It is ok -- this is a debug feature.
                    foreach (EntitySubscriber subscriber in _subscribers.Values.ToList())
                    {
                        if (subscriber.Topic == EntityTopic.Participant)
                            KickSubscriber(subscriber, new InternalSessionDivisionDebugReset(creatorId: EntityId.None));
                    }

                    // Now modify state directly and switch to itself to flush changes.
                    Model.StartsAt = MetaTime.Now + MetaDuration.FromMinutes(1);
                    Model.EndsAt = Model.StartsAt + MetaDuration.FromDays(10);

                    Model.IsConcluded = false;
                    SwitchToNewModelImmediately(Model);
                    break;
                }
            }

            return EntityAskOk.Instance;
        }

        #endregion

        #region Callbacks to userland

        protected override void OnSwitchedToModel(TModel model)
        {
            // Set analytics event handler
            model.AnalyticsEventHandler = _analyticsEventHandler;

            // Recompute transient values when model becomes active.
            model.RefreshScores();

            // Set listener.
            model.SetServerListenerCore(this);
        }

        /// <summary>
        /// Called when a division receives a new score event for an existing participant. Implementations should validate the score
        /// action is valid and has effect, and if so, execute an action updating the score and return <c>true</c>. If event has no
        /// effect, the methods should return <c>false</c> and issue no actions.
        /// </summary>
        /// <param name="playerId">Player Id of sending the event, or None if the event was not sent by the player.</param>
        protected abstract bool TryApplyScoreEvent(int participantIndex, EntityId playerId, IDivisionScoreEvent scoreEvent);

        /// <summary>
        /// Called when Session (i.e. client, i.e. user) subscribes into this Division. See <see cref="PersistedMultiplayerEntityActorBase{TModel, TAction, TPersisted}.OnClientSessionStart(EntityId, EntityId, InternalEntitySubscribeRequestBase)"/>
        /// </summary>
        /// <param name="sessionId">The EntityID of the session actor subscribing.</param>
        /// <param name="playerId">The PlayerId of the session subscribing.</param>
        /// <param name="requestBase">The Subscribe payload from session.</param>
        /// <param name="participantBase">The Participant of the Model state the subscriber represents</param>
        protected virtual Task OnClientSessionHandshake(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase, IDivisionParticipantState participantBase) => Task.CompletedTask;

        /// <summary>
        /// Computes and updates the <see cref="IDivisionParticipantState.ResolvedDivisionRewards"/> of all participants. This method
        /// is called when season is concluded and no more score update may happen.
        /// </summary>
        protected virtual void ResolveParticipantRewards()
        {
            foreach (int participantIndex in Model.EnumerateParticipants())
            {
                if (!Model.TryGetParticipant(participantIndex, out IDivisionParticipantState participant))
                    continue;
                // Skip participants that don't have an associated entity.
                if(!Model.ServerModel.ParticipantIndexToEntityId.TryGetValue(participantIndex, out _))
                    continue;

                participant.ResolvedDivisionRewards = CalculateDivisionRewardsForParticipant(participantIndex);
            }
        }

        /// <summary>
        /// Called when a division has concluded and is calculating rewards for all participants.
        /// Override this to define what rewards a given participant should receive.
        /// Default implementation gives no rewards.
        /// </summary>
        protected virtual IDivisionRewards CalculateDivisionRewardsForParticipant(int participantIndex) => null;

        /// <summary>
        /// <para>
        /// Called when a player is done with this division and is moving on to another one.
        /// This method should return a historical entry of this division that can be stored to the Player Model.
        /// The player's rewards should also be sent with this historical state, as the claiming happens on the Player actor.
        /// </para>
        /// <para>
        /// The passed in resolved rewards have been automatically set to an unclaimed state.
        /// </para>
        /// <para>
        /// Guild divisions have their own history entry for the guild, which this method does not cover.
        /// A player still gets their own historical entry if they participated through a guild.
        /// </para>
        /// </summary>
        /// <param name="participantIndex">The index of the participating player.</param>
        /// <param name="resolvedRewards">The previously resolved rewards when the division was concluded.</param>
        /// <returns></returns>
        protected abstract IDivisionHistoryEntry GetDivisionHistoryEntryForPlayer(int participantIndex, IDivisionRewards resolvedRewards);

        /// <summary>
        /// Returns a <see cref="IDivisionParticipantConclusionResult"/> for the given participantIndex.
        /// This should contain the participant's avatar and any information needed to apply promotion and demotion logic.
        /// End-of-season statistics should also be included.
        /// </summary>
        protected abstract IDivisionParticipantConclusionResult GetParticipantResult(int participantIndex);

        /// <summary>
        /// Override this to add custom initialization logic to the server model.
        /// </summary>
        /// <returns></returns>
        protected virtual void InitializeServerModel(TModel model, TServerModel serverModel) { }
        #endregion

        #region Analytics

        static DivisionAnalyticsContext GetAnalyticsContext()
        {
            return new DivisionAnalyticsContext();
        }

        /// <summary>
        /// Gets the analytics labels and their value which are added to emitted analytics events. Returning null means no labels. The default implementation returns null.
        /// </summary>
        protected virtual OrderedDictionary<AnalyticsLabel, string> GetAnalyticsLabels(TModel model)
        {
            return null;
        }
        #endregion
    }
}
