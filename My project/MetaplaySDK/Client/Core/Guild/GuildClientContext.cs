// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Guild.Messages.Core;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Model.JournalCheckers;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Metaplay.Core.Guild
{
    public class ClientGuildModelJournal : ModelJournal<IGuildModelBase>.Follower
    {
        public ClientGuildModelJournal(LogChannel log,
                                        IGuildModelBase model,
                                        int currentOperation,
                                        ITimelineHistory timelineHistory,
                                        bool enableConsistencyChecks)
            : base(log)
        {
            if (timelineHistory != null)
                AddListener(new TimelineHistoryListener<IGuildModelBase>(log, timelineHistory));

            if (enableConsistencyChecks)
            {
                AddListener(new JournalModelOutsideModificationChecker<IGuildModelBase>(log));
                AddListener(new JournalModelCloningChecker<IGuildModelBase>(log));
                AddListener(new JournalModelChecksumChecker<IGuildModelBase>(log));
                AddListener(new JournalModelRerunChecker<IGuildModelBase>(log));
                AddListener(new JournalModelActionImmutabilityChecker<IGuildModelBase>(log));
                AddListener(new JournalModelModifyHistoryChecker<IGuildModelBase>(log));
            }

            AddListener(new FailingActionWarningListener<IGuildModelBase>(log));

            // Model is currently at the given position.
            JournalPosition currentPosition = JournalPosition.FromTickOperationStep(tick: model.CurrentTick, operation: currentOperation, 0);
            Setup(model, currentPosition);
        }
    }

    /// <summary>
    /// The Client Context for the client's current Guild. This maintains the GuildModel state and the context
    /// state required for executing server updates and state required for client to enqueue actions for execution.
    /// See also convenience helper <see cref="GuildClientContext{TGuildModel}"/>
    /// </summary>
    public class GuildClientContext : IGuildClientContext
    {
        LogChannel                                  _log;
        Func<MetaMessage, bool>                     _sendMessageToServer;
        EntityId                                    _playerId;
        int                                         _channelId;
        int                                         _logicVersion;
        IGameConfigDataResolver                     _gameConfigResolver;
        List<GuildActionBase>                       _pendingActions = new List<GuildActionBase>();

        ClientGuildModelJournal                     _journal;

        // \note: we update staged model only on commit. This is a hack to get Committed/CheckpointModel to have have side effects.
        public IGuildModelBase                      CommittedModel => _journal.StagedModel;
        public ClientGuildModelJournal              Journal => _journal;
        public EntityId                             PlayerId => _playerId;
        public LogChannel                           Log => _log;
        IModel                                      IEntityClientContext.Model => CommittedModel;

        public GuildClientContext(LogChannel log, EntityId playerId, IGuildModelBase model, int channelId, int currentOperation, ITimelineHistory timelineHistory, Func<MetaMessage, bool> sendMessageToServer, bool enableConsistencyChecks)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            _log                        = log;
            _sendMessageToServer        = sendMessageToServer;
            _playerId                   = playerId;
            _channelId                  = channelId;
            _logicVersion               = model.LogicVersion;
            _gameConfigResolver         = model.GameConfig;
            _journal                    = new ClientGuildModelJournal
            (
                log:                        log,
                model:                      model,
                currentOperation:           currentOperation,
                timelineHistory:            timelineHistory,
                enableConsistencyChecks:    enableConsistencyChecks
            );
        }

        /// <summary>
        /// Enqueues the Action for execution.
        /// </summary>
        public void EnqueueAction(GuildActionBase action)
        {
            ModelActionSpec actionSpec = ModelActionRepository.Instance.SpecFromType[action.GetType()];

            // Must be client-issuable and enqueable.
            if (!actionSpec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.FollowerSynchronized))
                throw new InvalidOperationException($"Tried to enqueue Action {action.GetType().ToGenericTypeString()} which does not have FollowerSynchronized mode. ExecuteFlags=${actionSpec.ExecuteFlags}.");

            //action.Id = _runningActionId++;
            action.InvokingPlayerId = _playerId;

            _pendingActions.Add(action);
        }

        public void Update(MetaTime currentTime)
        {
            // \todo: speculation

            FlushActions();
        }

        public void FlushActions()
        {
            if (_pendingActions.Count == 0)
                return;

            _sendMessageToServer(new GuildEnqueueActionsRequest(_channelId, new MetaSerialized<List<GuildActionBase>>(_pendingActions, MetaSerializationFlags.SendOverNetwork, _logicVersion)));
            _pendingActions.Clear();
        }

        public bool HandleGuildTimelineUpdateMessage(GuildTimelineUpdateMessage msg)
        {
            List<GuildTimelineUpdateMessage.Operation>  operations      = msg.Operations.Deserialize(_gameConfigResolver, _logicVersion);
            bool                                        isFirstOp       = true;

            // \note: Journal does not support only-final-checksum mode yet, so let's emulate it by
            //        claiming all the ops have the final checksum.
            ArraySegment<uint>                          hackChecksum    = new ArraySegment<uint>(new uint[] { msg.FinalChecksum });

            foreach (GuildTimelineUpdateMessage.Operation op in operations)
            {
                bool isTick = op.Action == null;

                // Check Start position is correct
                if (isFirstOp)
                {
                    isFirstOp = false;
                    JournalPosition alignedPosition;

                    if (isTick)
                    {
                        if (_journal.StagedPosition.Operation == 0 && _journal.StagedPosition.Step == 0)
                            alignedPosition = _journal.StagedPosition;
                        else
                            alignedPosition = JournalPosition.NextTick(_journal.StagedPosition);
                    }
                    else
                    {
                        if (_journal.StagedPosition.Operation > 0 && _journal.StagedPosition.Step == 0)
                            alignedPosition = _journal.StagedPosition;
                        else
                            alignedPosition = JournalPosition.NextAction(_journal.StagedPosition);
                    }

                    JournalPosition expectedPosition = JournalPosition.FromTickOperationStep(msg.StartTick, msg.StartOperation, 0);
                    if (alignedPosition != expectedPosition)
                    {
                        _log.Warning("GuildTimelineUpdateMessage is out of sync. Journal was at {Position}, Update was for {UpdatePosition}", alignedPosition, expectedPosition);
                        return false;
                    }
                }

                if (isTick)
                    _journal.StageTick(hackChecksum);
                else
                {
                    op.Action.InvokingPlayerId = op.InvokingPlayerId;
                    _journal.StageAction(op.Action, hackChecksum);

                    _log.Debug("Guild action on tick {Tick}: {Op}", _journal.StagedPosition.Tick, op.Action.GetType().GetNestedClassName());
                }
            }

            // because we mangled the checksums above, we cant report anything more specific than "something went wrong".
            using (var result = _journal.Commit(_journal.StagedPosition, ModelJournal<IGuildModelBase>.Follower.ConflictResolutionMode.ContinueEvenIfConflicts))
            {
                if (!result.HasConflict)
                    return true;

                _log.Warning("Expected checksum {ExpectedChecksum} but got {ActualChecksum} when applying update from {From} to {End} with operations {OpList}. Note that transaction finalizing actions are not shown here.",
                    result.ExpectedChecksum,
                    result.ActualChecksum,
                    result.FirstSuspectOpAt,
                    result.LastSuspectOpAt,
                    result.GetSuspectOpListDisplayString());
                _log.Error("Checksum mismatch while applying guild updates from server.");

                return false;
            }
        }

        public void HandleGuildTransactionResponse(GuildActionBase action)
        {
            action.InvokingPlayerId = _playerId;

            _journal.StageAction(action, new ArraySegment<uint>()); // no checksum yet
            _journal.Commit(_journal.StagedPosition, ModelJournal<IGuildModelBase>.Follower.ConflictResolutionMode.ContinueEvenIfConflicts);

            _log.Debug("Guild finalizing action on tick {Tick}: {Op}", _journal.StagedPosition.Tick, action.GetType().GetNestedClassName());
        }

        public void SetClientListeners(Action<IGuildModelBase> applyFn)
        {
            applyFn?.Invoke(_journal.StagedModel);
        }
    }

    /// <summary>
    /// The Client Context for the client's current Guild. Typed convenience wrapper of <see cref="GuildClientContext"/>.
    /// </summary>
    public class GuildClientContext<TGuildModel> : GuildClientContext, IGuildClientContext<TGuildModel>
        where TGuildModel : IGuildModelBase
    {
        public GuildClientContext(LogChannel log, EntityId playerId, TGuildModel model, int channelId, int currentOperation, ITimelineHistory timelineHistory, Func<MetaMessage, bool> sendMessageToServer, bool enableConsistencyChecks)
            : base(log, playerId, model, channelId, currentOperation, timelineHistory, sendMessageToServer, enableConsistencyChecks)
        {
        }

        public new TGuildModel CommittedModel => (TGuildModel)base.CommittedModel;
    }
}

#endif
