// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using static System.FormattableString;

namespace Metaplay.Core.Client
{
    public class DefaultPlayerClientContext : IPlayerClientContext
    {
        protected virtual MetaDuration              ActionFlushInterval => MetaDuration.FromSeconds(5);  // Interval how often flushes happen when there's no actions

        protected LogChannel                        _log;
        EntityId                                    _playerId;
        Func<MetaMessage, bool>                     _sendMessageToServer;
        ChecksumGranularity                         _checksumGranularity;

        MetaTime                                    _startTime;                                         // Time when ticking is considered to have started
        MetaTime                                    _currentTime;                                       // Time when ticking was last updated
        MetaDuration                                _timeSinceLastTick  = MetaDuration.Zero;            // Sub-tick time

        MetaTime                                    _actionsLastFlushedAt;                              // Time when actions were last flushed
        JournalPosition                             _lastFlushedOperationsAt;                           // Position on staged journal where the last flush occured

        /// <summary>
        /// Marker on a timeline position to indicate some off-journal payload.
        /// </summary>
        struct ActionMarker
        {
            public enum MarkerType
            {
                /// <summary>
                /// Marker marks that the action on this position was an Unsynchronized server action.
                /// </summary>
                Unsynchronized,

                /// <summary>
                /// Marker marks that the action on this position was a Synchronized server action.
                /// </summary>
                Synchronized,
            }
            public readonly MarkerType Type;
            public readonly int TrackingId;

            public ActionMarker(MarkerType type, int trackingId)
            {
                Type = type;
                TrackingId = trackingId;
            }
        }

        /// <summary>
        /// Timeline markers since last flush.
        /// </summary>
        OrderedDictionary<JournalPosition, ActionMarker> _markers = new OrderedDictionary<JournalPosition, ActionMarker>();

        int                                         _runningActionId;
        int                                         _logicVersion;
        ClientPlayerModelJournal                    _playerJournal;
        bool                                        _isDisconnected;

        List<PlayerFlushActions.Operation>          _operationsBuffer = new List<PlayerFlushActions.Operation>();
        List<uint>                                  _checksumsBuffer = new List<uint>();

        public ClientPlayerModelJournal Journal => _playerJournal;
        IClientPlayerModelJournal IPlayerClientContext.Journal => _playerJournal;
        public LogChannel Log => _log;
        IModel IEntityClientContext.Model => _playerJournal.StagedModel;

        public MetaDuration GetAccumulatedTimeSinceLastTick()
        {
            return _timeSinceLastTick;
        }

        public MetaTime LastUpdateTimeDebug => _currentTime;

        public DefaultPlayerClientContext(LogChannel log, IPlayerModelBase playerModel, int currentOperation, EntityId playerId, int logicVersion, ITimelineHistory timelineHistory, Func<MetaMessage, bool> sendMessageToServer, bool enableConsistencyChecks, ChecksumGranularity checksumGranularity, MetaTime startTime)
        {
            if (playerModel == null)
                throw new ArgumentNullException(nameof(playerModel));

            _log                        = log;
            _playerId                   = playerId;
            _sendMessageToServer        = sendMessageToServer;
            _checksumGranularity        = checksumGranularity;
            _startTime                  = startTime;
            _currentTime                = startTime;
            _logicVersion               = logicVersion;
            _playerJournal              = new ClientPlayerModelJournal(
                log:                        log,
                model:                      playerModel,
                currentOperation:           currentOperation,
                timelineHistory:            timelineHistory,
                enableConsistencyChecks:    enableConsistencyChecks,
                computeChecksums:           checksumGranularity == ChecksumGranularity.PerOperation
            );
            _lastFlushedOperationsAt    = _playerJournal.CheckpointPosition;
            _isDisconnected             = false;

            // Reset state
            _runningActionId = 0; // \todo [petri] get from server?
        }

        /// <summary>
        /// Immediately execute a <see cref="PlayerAction"/> against the current <see cref="PlayerModel"/>.
        /// Any subsequent code can rely on the effects of the action to be resolved against the PlayerModel.
        ///
        /// The Action is also given a running Id for the purposes of identifying it later. The Action
        /// is also stored in the <see cref="_playerJournal"/>.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        public virtual MetaActionResult ExecuteAction(PlayerActionBase action, out int actionId)
        {
            // Allocate id for action
            _runningActionId++;
            action.Id = _runningActionId;

            _log.Debug("Execute action on tick {CurrentTick}: {ActionTypeName}", _playerJournal.StagedPosition.Tick, PrettyPrint.Compact(action));

            // Run in on journal
            MetaActionResult actionResult = _playerJournal.StageAction(action);
            if (_checksumGranularity == ChecksumGranularity.PerActionSingleTickPerFrame)
                _ = _playerJournal.ForceComputeChecksum(_playerJournal.StagedPosition);

            actionId = action.Id;
            return actionResult;
        }

        /// <inheritdoc cref="ExecuteAction(PlayerActionBase, out int)"/>
        public MetaActionResult ExecuteAction(PlayerActionBase action)
        {
            return ExecuteAction(action, out int actionId);
        }

        /// <summary>
        /// Dry-execute a <see cref="PlayerAction"/>. Returns the <see cref="MetaActionResult"/> from executing
        /// the Action, but without modifying the <see cref="PlayerModel"/> state. Can be useful in updating
        /// the state of the UI based on whether the player has enough resources to perform an action, etc.
        /// </summary>
        /// <param name="action">The action to dry-execute.</param>
        /// <returns></returns>
        public virtual MetaActionResult DryExecuteAction(PlayerActionBase action)
        {
            return ModelUtil.DryRunAction(_playerJournal.StagedModel, action);
        }

        public PlayerActionBase ExecuteServerAction(PlayerExecuteUnsynchronizedServerAction execute)
        {
            IGameConfigDataResolver resolver = _playerJournal.StagedModel.GetDataResolver();
            PlayerActionBase action = execute.Action.Deserialize(resolver, _logicVersion);
            _log.Debug("Execute unsynchronized server action on tick {CurrentTick}: {Action}", _playerJournal.StagedPosition.Tick, PrettyPrint.Compact(action));

            _playerJournal.ExecuteUnsynchronizedServerActionBlock(() =>
            {
                _playerJournal.StageAction(action);
                if (_checksumGranularity == ChecksumGranularity.PerActionSingleTickPerFrame)
                    _ = _playerJournal.ForceComputeChecksum(_playerJournal.StagedPosition);
            });

            // Set unsyncronized action tracking data on a marker that is placed on the action position.
            JournalPosition actionStartPosition = JournalPosition.FromTickOperationStep(_playerJournal.StagedPosition.Tick, _playerJournal.StagedPosition.Operation - 1, 0);
            _markers.Add(actionStartPosition, new ActionMarker(ActionMarker.MarkerType.Unsynchronized, execute.TrackingId));

            return action;
        }

        public PlayerActionBase ExecuteServerAction(PlayerEnqueueSynchronizedServerAction enqueued)
        {
            IGameConfigDataResolver resolver = _playerJournal.StagedModel.GetDataResolver();
            PlayerActionBase action = enqueued.Action.Deserialize(resolver, _logicVersion);
            _log.Debug("Execute synchronized server action on tick {CurrentTick}: {Action}", _playerJournal.StagedPosition.Tick, PrettyPrint.Compact(action));

            _playerJournal.StageAction(action);
            if (_checksumGranularity == ChecksumGranularity.PerActionSingleTickPerFrame)
                _ = _playerJournal.ForceComputeChecksum(_playerJournal.StagedPosition);

            // Set syncronized action tracking data on a marker that is placed on the action position.
            JournalPosition actionStartPosition = JournalPosition.FromTickOperationStep(_playerJournal.StagedPosition.Tick, _playerJournal.StagedPosition.Operation - 1, 0);
            _markers.Add(actionStartPosition, new ActionMarker(ActionMarker.MarkerType.Synchronized, enqueued.TrackingId));

            return action;
        }

        /// <summary>
        /// Execute a finalizing action on the timeline.
        /// </summary>
        public void HandleGuildTransactionResponse(PlayerTransactionFinalizingActionBase action, int trackingId)
        {
            _log.Debug("Execute finalizing action on tick {CurrentTick}: {Action}", _playerJournal.StagedPosition.Tick, PrettyPrint.Compact(action));
            _playerJournal.StageAction(action);
            if (_checksumGranularity == ChecksumGranularity.PerActionSingleTickPerFrame)
                _ = _playerJournal.ForceComputeChecksum(_playerJournal.StagedPosition);

            // Set syncronized action tracking data on a marker that is placed on the action position.
            JournalPosition actionStartPosition = JournalPosition.FromTickOperationStep(_playerJournal.StagedPosition.Tick, _playerJournal.StagedPosition.Operation - 1, 0);
            _markers.Add(actionStartPosition, new ActionMarker(ActionMarker.MarkerType.Synchronized, trackingId));
        }

        public void Update(MetaTime currentTime)
        {
            MetaTime lastTime = _currentTime;
            _currentTime = currentTime;

            long lastTotalTicks     = TotalNumTicksElapsedAt(lastTime);
            long currentTotalTicks  = TotalNumTicksElapsedAt(_currentTime);
            long newTicks           = currentTotalTicks - lastTotalTicks;

            _timeSinceLastTick = _currentTime - TimeAtTick(currentTotalTicks);

            // Execute ticks
            if (newTicks > 0)
            {
                // Safety limits: If there would more than 1 second worth of work AND the session
                // is already lost, run only the first 1 second and drop the rest. This shouldn't
                // matter as without session the game is just speculating while showing the error
                // message. This is very aggressive but should not matter as there is no session.
                if (_isDisconnected && newTicks > FloorTicksPerDuration(MetaDuration.FromSeconds(1)))
                {
                    long clampedTicks = FloorTicksPerDuration(MetaDuration.FromSeconds(1));
                    _log.Warning("Session is lost and PlayerClient is behind {NumTicksBehind} ticks. Silently skipping {NumSkipped} ticks (only running {NumRun} ticks).", newTicks, newTicks - clampedTicks, clampedTicks);
                    newTicks = clampedTicks;
                }

                bool lastTickExplicitlyChecksummed = false;
                for (int tick = 0; tick < newTicks; tick++)
                {
                    // If we have many Ticks, disable debug checks for all but the first and the last ticks.
                    // This improves the performance in the edge case where we have a lot of ticks, but reduces
                    // consistency check coverage a little.
                    if (tick == 0)
                    {
                        foreach (var checker in _playerJournal.DebugCheckers)
                            checker.IsPaused = false;
                    }
                    else if (newTicks > 2 && tick == 1)
                    {
                        foreach (var checker in _playerJournal.DebugCheckers)
                            checker.IsPaused = true;
                    }
                    else if (newTicks > 2 && tick == newTicks - 1)
                    {
                        foreach (var checker in _playerJournal.DebugCheckers)
                            checker.IsPaused = false;
                    }

                    _playerJournal.StageTick();
                    lastTickExplicitlyChecksummed = false;

                    int numPendingTicks = _playerJournal.StagedPosition.Tick - _lastFlushedOperationsAt.Tick;
                    if (numPendingTicks >= PlayerFlushActions.MaxTicksPerFlush)
                    {
                        // Last tick in the batch, but not necessarily of the frame. Need to add checksum nevertheless.
                        if (_checksumGranularity == ChecksumGranularity.PerActionSingleTickPerFrame)
                        {
                            _ = _playerJournal.ForceComputeChecksum(_playerJournal.StagedPosition);
                            lastTickExplicitlyChecksummed = true;
                        }

                        InternalFlushActions(forceFlush: true);
                    }
                }

                // Last tick of the frame
                if (_checksumGranularity == ChecksumGranularity.PerActionSingleTickPerFrame && !lastTickExplicitlyChecksummed)
                {
                    _ = _playerJournal.ForceComputeChecksum(_playerJournal.StagedPosition);
                }
            }

            // Flush operations to server
            InternalFlushActions(forceFlush: false);
        }

        long TotalNumTicksElapsedAt(MetaTime time) => ModelUtil.FloorTicksPerDuration(time - _startTime, _playerJournal.StagedModel.TicksPerSecond);

        MetaTime TimeAtTick(long tick) => ModelUtil.TimeAtTick(tick, _startTime, _playerJournal.StagedModel.TicksPerSecond);

        long FloorTicksPerDuration(MetaDuration duration) => ModelUtil.FloorTicksPerDuration(duration, _playerJournal.StagedModel.TicksPerSecond);

        public void FlushActions()
        {
            InternalFlushActions(forceFlush: true);
        }

        /// <summary>
        /// Flush any pending Actions to the server. Also sends a flush with no Actions periodically,
        /// if no Actions are executed. If <paramref name="forceFlush"/> is given, all pending Ticks
        /// are flushed immediately even if the send interval hasn't been reached.
        /// </summary>
        void InternalFlushActions(bool forceFlush)
        {
            var                                 walker              = _playerJournal.WalkJournal(from: _lastFlushedOperationsAt);
            JournalPosition                     gatherEnd           = JournalPosition.Epoch;
            MetaDuration                        timeSinceLastFlush  = _currentTime - _actionsLastFlushedAt;
            int                                 numPendingTicks     = 0;
            int                                 numPendingActions   = 0;
            List<PlayerFlushActions.Operation>  operations          = _operationsBuffer;
            List<uint>                          checksums           = _checksumsBuffer;

            operations.Clear();
            checksums.Clear();

            while (walker.MoveNext())
            {
                if (walker.IsTickFirstStep)
                {
                    numPendingTicks++;
                    operations.Add(new PlayerFlushActions.Operation(walker.PositionBefore, null, walker.NumStepsTotal));
                    checksums.Add(walker.ComputedChecksumAfter);

                    for (int i = 0; i < walker.NumStepsTotal - 1; ++i)
                    {
                        walker.MoveNext();
                        checksums.Add(walker.ComputedChecksumAfter);
                    }
                }
                else if (walker.IsActionFirstStep)
                {
                    numPendingActions++;

                    // substitute server-supplied actions with the invocation-by-ids
                    PlayerActionBase action;
                    if (_markers.TryGetValue(walker.PositionBefore, out ActionMarker marker))
                    {
                        _markers.Remove(walker.PositionBefore);

                        if (marker.Type == ActionMarker.MarkerType.Unsynchronized)
                            action = new PlayerUnsynchronizedServerActionMarker(marker.TrackingId);
                        else
                            action = new PlayerSynchronizedServerActionMarker(marker.TrackingId);
                    }
                    else
                        action = (PlayerActionBase)walker.Action;

                    operations.Add(new PlayerFlushActions.Operation(walker.PositionBefore, action, walker.NumStepsTotal));
                    checksums.Add(walker.ComputedChecksumAfter);

                    for (int i = 0; i < walker.NumStepsTotal - 1; ++i)
                    {
                        walker.MoveNext();
                        checksums.Add(walker.ComputedChecksumAfter);
                    }
                }
                gatherEnd = walker.PositionAfter;
            }

            bool shouldFlush = numPendingActions > 0                                                // Any actions to flush?
                            || (timeSinceLastFlush >= ActionFlushInterval && numPendingTicks > 0)   // Enough time passed since last flush, and there is something to flush?
                            || numPendingTicks > 2 * FloorTicksPerDuration(ActionFlushInterval)     // Significantly more ticks have passed than worth a normal ActionFlushInterval?
                            || (forceFlush && (numPendingActions > 0 || numPendingTicks > 0));      // Must flush (and there is something to flush
            if (shouldFlush)
            {
                _actionsLastFlushedAt = _currentTime;
                _lastFlushedOperationsAt = gatherEnd;

                // Inject correct checksum if in per-flush mode
                if (_checksumGranularity == ChecksumGranularity.PerBatch)
                {
                    checksums[checksums.Count - 1] = _playerJournal.ForceComputeChecksum(_lastFlushedOperationsAt);
                }

                // If disconnected, don't enqueue messages.
                if (!_isDisconnected)
                {
                    var serializedOperation = new MetaSerialized<List<PlayerFlushActions.Operation>>(operations, MetaSerializationFlags.SendOverNetwork, _logicVersion);
                    _sendMessageToServer(new PlayerFlushActions(serializedOperation, checksums.ToArray()));
                }

                // If we flushed all the way to the Stage, let's take a snapshot of the state. Snapshots allow
                // following commits (when we get ACK from the server), to use the checkpoint directly rather than needing to recompute.
                if (!_isDisconnected)
                {
                    Journal.CaptureStageSnapshot();
                }
            }

            _operationsBuffer.Clear();
            _checksumsBuffer.Clear();
        }

        /// <summary>
        /// Marks any actions (and tick) pending flush as already flushed. They will not be sent to server with the normal Flush message.
        /// </summary>
        public void MarkPendingActionsAsFlushed()
        {
            _lastFlushedOperationsAt = _playerJournal.StagedPosition;
        }

        public PlayerChecksumMismatchDetails ResolveChecksumMismatch(PlayerChecksumMismatch mismatch)
        {
            JournalPosition beforeConflict;
            JournalPosition afterConflict;

            if (mismatch.ActionIndex == -1)
            {
                beforeConflict = JournalPosition.BeforeTick(mismatch.Tick);
                afterConflict = JournalPosition.AfterTick(mismatch.Tick);
            }
            else
            {
                beforeConflict = JournalPosition.BeforeAction(mismatch.Tick, mismatch.ActionIndex);
                afterConflict = JournalPosition.AfterAction(mismatch.Tick, mismatch.ActionIndex);
            }

            // Mismatch at point X means that all actions preceeding X were valid.
            _playerJournal.Commit(beforeConflict);

            // Inspect the next (the conflicting) operation on journal
            PlayerActionBase    mismatchAction      = null;
            uint                clientChecksumAfter = 0;
            bool                foundOpOnJournal    = false;
            var                 walker              = _playerJournal.WalkJournal(from: beforeConflict);

            if (walker.MoveNext() && walker.PositionBefore == beforeConflict)
            {
                if (walker.IsActionFirstStep)
                {
                    mismatchAction = (PlayerActionBase)walker.Action;
                    clientChecksumAfter = walker.ComputedChecksumAfter;
                    foundOpOnJournal = true;
                }
                else if (walker.IsTickFirstStep)
                {
                    clientChecksumAfter = walker.ComputedChecksumAfter;
                    foundOpOnJournal = true;
                }
            }

            IPlayerModelBase justAfterConflictFullModel = null;
            if (foundOpOnJournal)
            {
                justAfterConflictFullModel = _playerJournal.TryCopyModelAtPosition(afterConflict);
                if (justAfterConflictFullModel == null)
                {
                    foundOpOnJournal = false;
                    _log.Warning("Failed to materialize model when resolving checksum mismatch.", mismatch.Tick, _logicVersion);
                }
            }

            PlayerChecksumMismatchDetails details;
            if (foundOpOnJournal)
            {
                // We have info on the conflicting operation. Peek just after the conflict and take a ComputeChecksum dump of the state
                byte[] serializedClientState;
                using (FlatIOBuffer buffer = new FlatIOBuffer())
                {
                    JournalUtil.Serialize(buffer, justAfterConflictFullModel, MetaSerializationFlags.ComputeChecksum, _logicVersion);
                    serializedClientState = IOBufferUtil.ToArray(buffer);
                }

                if (clientChecksumAfter != 0)
                {
                    // Compare what we got as the checksum when we first computed and what we have
                    // now with the re-evaluation. This is indended to catch internal un-stable/non-deterministic
                    // computations on client.
                    uint clientChecksumOnRecomputeAfter = MurmurHash.MurmurHash2(serializedClientState);
                    uint serverChecksumAfter = MurmurHash.MurmurHash2(mismatch.AfterState);
                    if (clientChecksumOnRecomputeAfter != clientChecksumAfter)
                    {
                        if (clientChecksumOnRecomputeAfter == serverChecksumAfter)
                            _log.Warning("When re-evaluating the timeline on client, model checksum was different than on first evaluation, and now on second evaluation matches the server timeline. "
                                + "This suggests an Action or Tick changes are not properly encapsulated or are not deterministic. Perhaps an Action or Tick client listener is modifying the model?");
                        else
                            _log.Warning("When re-evaluating the timeline on client, model checksum was different than on first evaluation, and neither matches server's checksum. "
                                + "This suggests an Action or Tick changes are not properly encapsulated or are not deterministic.");

                        _log.Warning("Re-evaluating the timeline on client is not stable. State diffs may not be correct.");
                    }

                    // Compare the server's expected state to what we submitted. This is indended to catch internal
                    // un-stable/non-deterministic computations on server.
                    if (clientChecksumAfter == serverChecksumAfter)
                    {
                        _log.Warning("When re-evaluating the timeline on server, the model checksum matches checksum from client's first evaluation. "
                            + "This suggests an Action or Tick changes are not properly encapsulated or are not deterministic on server. Perhaps an Action or Tick server listener is modifying the model?");
                    }
                }

                if (mismatch.ActionIndex == -1)
                    _log.Error("Checksum mismatch when executing tick {Tick} (logicVersion={LogicVersion})", mismatch.Tick, _logicVersion);
                else
                    _log.Error("Checksum mismatch on tick {Tick} executing {Action} (logicVersion={LogicVersion})", mismatch.Tick, PrettyPrint.Verbose(mismatchAction), _logicVersion);

                SerializedObjectComparer comparer = new SerializedObjectComparer();
                comparer.FirstName = "Client";
                comparer.SecondName = "Server";
                comparer.Type = typeof(IModel);
                SerializedObjectComparer.Report diffReport = comparer.Compare(serializedClientState, mismatch.AfterState);
                Log.Warning("{DiffReport}", diffReport.Description);

                MetaSerialized<PlayerActionBase> serializedAction = new MetaSerialized<PlayerActionBase>(mismatchAction, MetaSerializationFlags.IncludeAll, _logicVersion);
                details = new PlayerChecksumMismatchDetails(mismatch.Tick, serializedAction, diffReport.Description, diffReport.VagueDifferencePathsMaybe);
            }
            else
            {
                _log.Error("No client state found for mismatch on tick={Tick} actionNdx={ActionIndex}, unable to do state comparison!", mismatch.Tick, mismatch.ActionIndex);
                details = new PlayerChecksumMismatchDetails(mismatch.Tick, MetaSerialized<PlayerActionBase>.Empty, Invariant($"Unable to compute diff, no client-side state found for: tick={mismatch.Tick}, actionNdx={mismatch.ActionIndex}"), null);
            }

            // \note: ok to leave the journal in a wonky state as we lose connection anyway. No point to rollback if we throw results away anyway.

            return details;
        }

        public void PurgeSnapshotsUntil(JournalPosition untilPosition)
        {
            _playerJournal.Commit(untilPosition);
        }

        public void OnDisconnected()
        {
            // \todo [jarkko]: allow PlayerClientContext to listen to messages automatically.
            _isDisconnected = true;
        }
    }
}
