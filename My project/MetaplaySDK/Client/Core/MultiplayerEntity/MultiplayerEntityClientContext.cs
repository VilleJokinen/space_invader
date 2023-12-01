// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Client;
using Metaplay.Core.Model;
using Metaplay.Core.Model.JournalCheckers;
using Metaplay.Core.MultiplayerEntity.Messages;
using System;
using System.Collections.Generic;

namespace Metaplay.Core.MultiplayerEntity
{
    /// <summary>
    /// Common constructor arguments for entity client contextes. Commonly, this is created this with <see cref="MultiplayerEntityClientBase.DefaultInitArgs(IMultiplayerModel, EntityInitialState)"/>.
    /// </summary>
    public class ClientMultiplayerEntityContextInitArgs
    {
        public readonly LogChannel                  Log;
        public readonly IMultiplayerModel           Model;
        public readonly int                         CurrentOperation;
        public readonly EntityId                    PlayerId;
        public readonly EntityMessageDispatcher     MessageDispatcher;
        public readonly bool                        EnableConsistencyChecks;
        public readonly IMetaplaySubClientServices  Services;
        public readonly MultiplayerEntityClientBase Client;

        public MetaplayClientStore ClientStore => Services.ClientStore;
        public ITimelineHistory TimelineHistory => Services.TimelineHistory;

        public ClientMultiplayerEntityContextInitArgs(LogChannel log, IMultiplayerModel model, int currentOperation, EntityId playerId, EntityMessageDispatcher messageDispatcher, bool enableConsistencyChecks, IMetaplaySubClientServices services, MultiplayerEntityClientBase client)
        {
            Log = log;
            Model = model;
            CurrentOperation = currentOperation;
            PlayerId = playerId;
            MessageDispatcher = messageDispatcher;
            EnableConsistencyChecks = enableConsistencyChecks;
            Services = services;
            Client = client;
        }
    }

    public class ClientMultiplayerEntityModelJournal<TModel> : ModelJournal<TModel>.Follower
        where TModel : class, IMultiplayerModel<TModel>
    {
        public ClientMultiplayerEntityModelJournal(ClientMultiplayerEntityContextInitArgs initArgs)
            : base(initArgs.Log)
        {
            if (initArgs.TimelineHistory != null)
                AddListener(new TimelineHistoryListener<TModel>(initArgs.Log, initArgs.TimelineHistory));

            if (initArgs.EnableConsistencyChecks)
            {
                AddListener(new JournalModelOutsideModificationChecker<TModel>(initArgs.Log));
                AddListener(new JournalModelCloningChecker<TModel>(initArgs.Log));
                AddListener(new JournalModelChecksumChecker<TModel>(initArgs.Log));
                AddListener(new JournalModelRerunChecker<TModel>(initArgs.Log));
                AddListener(new JournalModelActionImmutabilityChecker<TModel>(initArgs.Log));
                AddListener(new JournalModelModifyHistoryChecker<TModel>(initArgs.Log));
            }

            AddListener(new FailingActionWarningListener<TModel>(initArgs.Log));

            // Model is currently at the given position.
            JournalPosition currentPosition = JournalPosition.FromTickOperationStep(tick: initArgs.Model.CurrentTick, operation: initArgs.CurrentOperation, 0);
            Setup((TModel)initArgs.Model, currentPosition);
        }
    }

    /// <summary>
    /// The playback mode on a Multiplayer entity client.
    /// <para>
    /// Playback controls the pacing of when to present on client actions and ticks completed by the server.
    /// </para>
    /// </summary>
    public enum ClientPlaybackMode
    {
        /// <summary>
        /// Eagerly execute all updates when they become available. This may result in
        /// choppy updates as the interval between when Ticks are presented are not guaranteed to be equal.
        /// </summary>
        Instant,

        /// <summary>
        /// Execute all updates with a controlled pacing such that the interval between the Ticks remains the expected.
        /// This results in smoother updates but will introduce latency. The latency is adaptive to the network conditions
        /// and is minimal when the network quality good.
        /// </summary>
        Smooth,
    }

    struct PlaybackOp
    {
        public ModelAction Action;
        public ArraySegment<uint> Checksums;

        public static PlaybackOp ForAction(ModelAction action, ArraySegment<uint> checksums) => new PlaybackOp() { Action = action, Checksums = checksums };
        public static PlaybackOp ForTick(ArraySegment<uint> checksums) => new PlaybackOp() { Action = null, Checksums = checksums };
    }

    /// <summary>
    /// Untyped <see cref="MultiplayerEntityClientContext{TModel}"/>.
    /// </summary>
    public interface IMultiplayerEntityClientContext : IEntityClientContext
    {
        IMultiplayerModel CommittedModel { get; }

        void EnqueueAction(ModelAction action);

        /// <summary>
        /// <inheritdoc cref="IMetaplaySubClient.OnDisconnected"/>
        /// </summary>
        void OnDisconnected();

        /// <summary>
        /// Sets client listener setter. The <paramref name="applyFn"/> may be <c>null</c>, in which
        /// case there are no listeners.
        /// </summary>
        void SetClientListeners(Action<IMultiplayerModel> applyFn);
    }

    /// <summary>
    /// The base class for the multiplayer entity client Context. This maintains the Model state and the context
    /// state required for executing server updates and state required for client to enqueue actions for execution.
    /// </summary>
    public partial class MultiplayerEntityClientContext<TModel> : IMultiplayerEntityClientContext
        where TModel : class, IMultiplayerModel<TModel>
    {
        protected readonly MetaplayClientStore          _clientStore;
        protected readonly LogChannel                   _log;
        protected readonly int                          _logicVersion;
        protected readonly EntityMessageDispatcher      _messageDispatcher;
        protected readonly MultiplayerEntityClientBase  _client;
        List<ModelAction>                               _pendingActions = new List<ModelAction>();
        MetaTime                                        _previousUpdateAt;

        public ClientMultiplayerEntityModelJournal<TModel> Journal { get; private set; }

        public TModel CommittedModel => Journal.StagedModel; // \note: we update staged model only on commit. This is a hack to get Committed/CheckpointModel to have have side effects.

        /// <summary>
        /// The timestamp when current (i.e. latest) tick was presented, i.e. visually executed by the client. This is not the
        /// timestamp of the frame when the tick executed, but rather the scheduled presentation time when the tick should have been
        /// presented had we infinite frame rate.
        ///
        /// This can be used to approximate how far we have executed the current tick. This "subtick" time is is useful for visually predicting movement.
        ///
        /// For example, suppose a tick is supposed to be presented at time t=5, but frames are rendered at t=0 and t=10. The tick is not yet
        /// executed on t=0 frame, but on the t=10 frame. However, now on the t=10 frame, the visual tick should have happened 5 time units ago.
        /// On t=10 frame, this timestamp contains the presentation time, t=5, allowing continuous subtick visualizations.
        /// </summary>
        public MetaTime CurrentTickPresentationAt { get; private set; }

        /// <summary>
        /// The relative duration how far the current Tick has been executed in presentation time. Ratio of 0.0 means the model has been presented exactly now, and 0.5
        /// would mean the we are half way into presenting this frame.
        ///
        /// This can be used to approximate how far we have executed the current tick. This "subtick" time is is useful for visually predicting movement.
        /// </summary>
        public float CurrentTickPresentationSubframeRatio { get; private set; }

        public LogChannel Log => _log;
        IModel IEntityClientContext.Model  => CommittedModel;

        /// <summary>
        /// True when connection to server has been irrecoverably lost and the current session has become stale.
        /// See <see cref="OnDisconnected"/>.
        /// </summary>
        protected bool IsDisconnected { get; private set; }

        /// <inheritdoc cref="ClientPlaybackMode"/>
        protected virtual ClientPlaybackMode PlaybackMode => ClientPlaybackMode.Instant;

        /// <summary>
        /// The delta between Model current time and the wallclock wall time. Since Model's clock is set by server,
        /// this includes BOTH the client-server clock drift AND the amount of time client delays the playback. This must only
        /// be used for converting time from Model time to presentation time.
        /// </summary>
        MetaDuration                                _playbackDelta;
        Queue<EntityTimelineUpdateMessage>          _playbackBuffer = new Queue<EntityTimelineUpdateMessage>();
        int                                         _playbackBufferOpNdx;
        List<PlaybackOp>                            _playbackOpBuffer = new List<PlaybackOp>();
        float                                       _playbackFfCarry;
        bool                                        _playbackFfReported;
        bool                                        _playbackStallOngoing;
        DateTime                                    _playbackStallStartedAt;

        DateTime                                    _nextUpdateExpectedAt;
        int                                         _nextUpdateTick;
        TimeSpanHistory                             _updateTimestamps;

        /// <summary>
        /// If enabled, context will measure latency periodically. Measurements are reported via <see cref="OnLatencySample"/>.
        /// </summary>
        protected virtual bool EnableLatencyMeasurement => false;
        DateTime        _latencyMeasurementLastPingAt;
        uint            _latencyMeasurementNextId = 1;
        uint            _latencyMeasurementIdInFlight; // 0 if none
        bool            _latencyMeasurementSampleComplete;
        LatencySample   _latencyMeasurementSample;

        public struct LatencySample
        {
            /// <summary>
            /// Latency (RTT) to perform any operation the Multiplayer Entity and get visual acknowledgment. This includes:
            /// * Latency to prepare encapsulate message to network format send it.
            /// * Network latency
            /// * Load balancer processing
            /// * Packet routing within cluster
            /// * Message processing on receiver server node
            /// * Message routing to target entity
            /// * Message processing latency on entity
            /// * Routing data back to device same way back
            /// * Reply processing on client
            /// * VSync latency until next frame
            /// </summary>
            public TimeSpan EntityMessagingLatency;

            /// <summary>
            /// Latency (RTT) to perform any Action the Multiplayer Entity and get the action replied on received timeline. This includes
            /// the same steps as <see cref="EntityMessagingLatency"/> and also includes Tick scheduling, processing and delivery latency
            /// on server.
            /// </summary>
            public TimeSpan ActionSubmitLatency;
        }

        public MultiplayerEntityClientContext(ClientMultiplayerEntityContextInitArgs initArgs)
        {
            _clientStore        = initArgs.ClientStore;
            _log                = initArgs.Log;
            _client             = initArgs.Client;
            _logicVersion       = initArgs.Model.LogicVersion;
            _messageDispatcher  = initArgs.MessageDispatcher;
            _previousUpdateAt   = MetaTime.Now;
            Journal             = new ClientMultiplayerEntityModelJournal<TModel>(initArgs);

            // Initial delta such that the current tick was just executed.
            int         currentTick             = Journal.StagedModel.CurrentTick;
            MetaTime    currentTickAtModelTime  = ModelUtil.TimeAtTick(currentTick, Journal.StagedModel.TimeAtFirstTick, Journal.StagedModel.TicksPerSecond);
            _playbackDelta              = (MetaTime.Now - currentTickAtModelTime);
            CurrentTickPresentationAt   = MetaTime.Now;

            MetaTime modelTimeForThisUpdate = ModelUtil.TimeAtTick(currentTick, CommittedModel.TimeAtFirstTick, CommittedModel.TicksPerSecond);
            MetaTime modelTimeForNextUpdate = ModelUtil.TimeAtTick(currentTick + 1, CommittedModel.TimeAtFirstTick, CommittedModel.TicksPerSecond);
            _nextUpdateTick             = currentTick + 1;
            _nextUpdateExpectedAt       = DateTime.UtcNow + (modelTimeForNextUpdate - modelTimeForThisUpdate).ToTimeSpan();
            _updateTimestamps           = new TimeSpanHistory(Journal.StagedModel.TicksPerSecond * 5);

            _messageDispatcher.AddListener<EntityTimelineUpdateMessage>(OnTimelineUpdateMessage);
            _messageDispatcher.AddListener<EntityTimelinePingTraceMarker>(OnEntityTimelinePingTraceMarker);
        }

        /// <summary>
        /// Enqueues the Action for execution. This method may be overriden for example to add
        /// action invoker information to the messages.
        /// </summary>
        public virtual void EnqueueAction(ModelAction action)
        {
            ModelActionSpec actionSpec = ModelActionRepository.Instance.SpecFromType[action.GetType()];

            // Must be client-issuable and enqueable.
            if (!actionSpec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.FollowerSynchronized))
                throw new InvalidOperationException($"Tried to enqueue Action {action.GetType().ToGenericTypeString()} which does not have FollowerSynchronized mode. ExecuteFlags=${actionSpec.ExecuteFlags}.");

            _pendingActions.Add(action);
        }

        public virtual void Update(MetaTime currentTime)
        {
            FlushActions();
            bool modelWasUpdated = UpdatePlayback(currentTime);
            _previousUpdateAt = currentTime;

            if (modelWasUpdated)
                _client.OnAdvancedOnTimeline();

            // Complete in pending samples
            if (_latencyMeasurementSampleComplete)
            {
                _latencyMeasurementSampleComplete = false;
                _latencyMeasurementIdInFlight = 0;
                OnLatencySample(_latencyMeasurementSample);
            }
        }

        public virtual void OnDisconnected()
        {
            IsDisconnected = true;
        }

        public void FlushActions()
        {
            if (!IsDisconnected)
            {
                // For ping measurements, try to send them just before the action. This way we can measure the time
                // for action to be replied on timeline.
                if (EnableLatencyMeasurement && _latencyMeasurementIdInFlight == 0)
                {
                    DateTime now = DateTime.UtcNow;
                    TimeSpan interval = (_pendingActions.Count > 0) ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(4);
                    if (_latencyMeasurementLastPingAt + interval <= now)
                    {
                        uint id = _latencyMeasurementNextId;
                        ++_latencyMeasurementNextId;

                        _latencyMeasurementLastPingAt = now;
                        _latencyMeasurementIdInFlight = id;
                        _messageDispatcher.SendMessage(new EntityTimelinePingTraceQuery(id));
                    }
                }

                // Send actions
                if (_pendingActions.Count > 0)
                {
                    _messageDispatcher.SendMessage(new EntityEnqueueActionsRequest(_pendingActions));
                }
            }
            _pendingActions.Clear();
        }

        void OnTimelineUpdateMessage(EntityTimelineUpdateMessage msg)
        {
            _playbackBuffer.Enqueue(msg);

            // Keep track of time when the ticks arrive
            int updateTick = GetLatestServerKnownTick();
            if (updateTick >= _nextUpdateTick)
            {
                DateTime now = DateTime.UtcNow;
                MetaTime modelTimeForThisUpdate = ModelUtil.TimeAtTick(updateTick, CommittedModel.TimeAtFirstTick, CommittedModel.TicksPerSecond);
                MetaTime modelTimeForNextUpdate = ModelUtil.TimeAtTick(updateTick + 1, CommittedModel.TimeAtFirstTick, CommittedModel.TicksPerSecond);

                TimeSpan tickAheadOfTime = _nextUpdateExpectedAt - now;
                _nextUpdateExpectedAt = now + (modelTimeForNextUpdate - modelTimeForThisUpdate).ToTimeSpan();
                _nextUpdateTick = updateTick + 1;

                _updateTimestamps.Add(tickAheadOfTime);
            }
        }

        void OnEntityTimelinePingTraceMarker(EntityTimelinePingTraceMarker msg)
        {
            if (_latencyMeasurementIdInFlight == 0 || msg.Id != _latencyMeasurementIdInFlight)
                return;
            switch (msg.Position)
            {
                case EntityTimelinePingTraceMarker.TracePosition.MessageReceivedOnEntity:
                {
                    _latencyMeasurementSample.EntityMessagingLatency = DateTime.UtcNow - _latencyMeasurementLastPingAt;
                    break;
                }
                case EntityTimelinePingTraceMarker.TracePosition.AfterNextTick:
                {
                    _latencyMeasurementSample.ActionSubmitLatency = DateTime.UtcNow - _latencyMeasurementLastPingAt;

                    // Complete callback in Update()
                    _latencyMeasurementSampleComplete = true;
                    break;
                }
            }
        }

        bool TryPeekNextPlaybackOp(out ArraySegment<uint> checksums, out ModelAction action)
        {
            if (_playbackBuffer.Count == 0)
            {
                checksums = default;
                action = null;
                return false;
            }

            EntityTimelineUpdateMessage message = _playbackBuffer.Peek();
            action = message.Operations[_playbackBufferOpNdx];
            if (message.DebugChecksums != null)
            {
                checksums = new ArraySegment<uint>(message.DebugChecksums, offset: _playbackBufferOpNdx, count: 1);
            }
            else
            {
                // Only final step gets the checksum
                bool isLastOp = _playbackBufferOpNdx == message.Operations.Count - 1;
                if (isLastOp)
                    checksums = new ArraySegment<uint>(new uint[] { message.FinalChecksum });
                else
                    checksums = new ArraySegment<uint>();
            }
            return true;
        }

        void PopNextPlaybackOp()
        {
            _playbackBufferOpNdx++;
            if (_playbackBufferOpNdx >= _playbackBuffer.Peek().Operations.Count)
            {
                _playbackBufferOpNdx = 0;
                _ = _playbackBuffer.Dequeue();
            }
        }

        MetaTime GetPresentationTimeOfTick(int tick)
        {
            MetaTime tickAtModelTime        = ModelUtil.TimeAtTick(tick, Journal.StagedModel.TimeAtFirstTick, Journal.StagedModel.TicksPerSecond);
            MetaTime tickAtPresentationTime = tickAtModelTime + _playbackDelta;
            return tickAtPresentationTime;
        }

        void ExtractOpsForPlayback(MetaTime currentTime, List<PlaybackOp> outOps, out bool outAdvancingWasThrottledByFrameLimit)
        {
            // Limit playback to at most one second worth of ticks. If there is a pileup of timeline, this will
            // limit the amount of per-frame work to keep the Player responsive. However, to account for very rarely
            // updating entities, we always allow 10 ticks regardless of tickrate.
            TModel stagedModel = Journal.StagedModel;
            int maxNumTicksPerFrame = System.Math.Max(10, stagedModel.TicksPerSecond);
            int numTicksGathered = 0;
            for (;;)
            {
                if (!TryPeekNextPlaybackOp(out ArraySegment<uint> checksums, out ModelAction action))
                    break;

                // Run any pending actions on (past == current) tick
                if (action != null)
                {
                    outOps.Add(PlaybackOp.ForAction(action, checksums));
                    PopNextPlaybackOp();
                    continue;
                }

                // Run tick only if the it is at or happened before the current playback time.
                // \note: We do this in all presentation modes. Instant mode just updates clock according
                //        to the server's updates.
                int         nextTick                    = stagedModel.CurrentTick + numTicksGathered + 1;
                MetaTime    nextTickAtPresentationTime  = GetPresentationTimeOfTick(nextTick);

                // Presentation is in the future?
                if (currentTime < nextTickAtPresentationTime)
                    break;

                // Limit the amount of work per frame
                if (numTicksGathered >= maxNumTicksPerFrame)
                {
                    _log.Warning("Attempted playback of too many ticks per frame. Limiting to {NumTicks}.", maxNumTicksPerFrame);
                    outAdvancingWasThrottledByFrameLimit = true;
                    return;
                }

                numTicksGathered++;
                outOps.Add(PlaybackOp.ForTick(checksums));
                PopNextPlaybackOp();
            }

            outAdvancingWasThrottledByFrameLimit = false;
        }

        bool AdvanceTimeline(MetaTime currentTime, out bool advancingWasThrottledByFrameLimit)
        {
            _playbackOpBuffer.Clear();
            ExtractOpsForPlayback(currentTime, _playbackOpBuffer, out advancingWasThrottledByFrameLimit);

            // For the current playback chunk, find the last possible place to commit. A place to commit
            // is place with a checksum. There might not be any.
            int commitPointNdx;
            for (commitPointNdx = _playbackOpBuffer.Count - 1; commitPointNdx >= 0; --commitPointNdx)
            {
                if (_playbackOpBuffer[commitPointNdx].Checksums.Count > 0)
                    break;
            }

            // Run all operations from [0, commitPointNdx] (inclusive),
            // Commit the model,
            // And run all operations from (commitPointNdx, end) (exclusive)
            for (int ndx = 0; ndx < _playbackOpBuffer.Count; ++ndx)
            {
                StagePlaybackOpAt(ndx);

                if (ndx == commitPointNdx)
                {
                    using (var result = Journal.Commit(Journal.StagedPosition, ModelJournal<TModel>.Follower.ConflictResolutionMode.ContinueEvenIfConflicts))
                    {
                        if (result.HasConflict)
                        {
                            HandleChecksumMismatch(result);
                            return true;
                        }
                    }
                }
            }

            bool modelWasUpdated = _playbackOpBuffer.Count > 0;
            _playbackOpBuffer.Clear();

            // Update subtick ratio
            float ratio = (currentTime - CurrentTickPresentationAt).Milliseconds * Journal.StagedModel.TicksPerSecond / 1000.0f;
            CurrentTickPresentationSubframeRatio = System.Math.Max(0.0f, System.Math.Min(1.0f, ratio));

            return modelWasUpdated;
        }

        void StagePlaybackOpAt(int ndx)
        {
            ModelAction action = _playbackOpBuffer[ndx].Action;
            if (action != null)
            {
                Journal.StageAction(action, _playbackOpBuffer[ndx].Checksums);
                _log.Debug("Action on tick {Tick}: {Op}", Journal.StagedModel.CurrentTick, action.GetType().GetNestedClassName());
            }
            else
            {
                Journal.StageTick(_playbackOpBuffer[ndx].Checksums);
                UpdateCurrentTickPresentationTime();
            }
        }

        void UpdateCurrentTickPresentationTime()
        {
            int             currentTick                     = Journal.StagedModel.CurrentTick;
            MetaTime        currentTickAtPresentationTime  = GetPresentationTimeOfTick(currentTick);
            CurrentTickPresentationAt = currentTickAtPresentationTime;
        }

        bool UpdatePlayback(MetaTime currentTime)
        {
            // Run playback as normally.
            bool modelWasUpdated = AdvanceTimeline(currentTime, out bool advancingWasThrottledByFrameLimit);

            // If AdvanceTimeline reports it couldn't run all the work it should have (to keep the app resposive)
            // then the Actual presentation time is behind the expected Playback time. Rather than take this into
            // the account, we instead wait for the pending work to be done before resuming the normal pacing logic.
            if (advancingWasThrottledByFrameLimit)
                return modelWasUpdated;

            // If playback is ahead of the server-supplied timeline, stall playback by increasing delay.
            // Specifically, if we should already present the next tick or beyond, set duration such that it appears we stall at the next tick
            int             nextTick                    = Journal.StagedModel.CurrentTick + 1;
            MetaTime        nextTickAtModelTime         = ModelUtil.TimeAtTick(nextTick, Journal.StagedModel.TimeAtFirstTick, Journal.StagedModel.TicksPerSecond);
            MetaTime        nextTickAtPresentationTime  = nextTickAtModelTime + _playbackDelta;

            if (currentTime >= nextTickAtPresentationTime)
            {
                _playbackDelta = currentTime - nextTickAtModelTime;
                if (!_playbackStallOngoing && PlaybackMode == ClientPlaybackMode.Smooth)
                {
                    _playbackStallOngoing = true;
                    _playbackStallStartedAt = DateTime.UtcNow;
                }
            }
            else if (_playbackStallOngoing)
            {
                _playbackStallOngoing = false;
                _log.Info("Playback of {Name} stalled. Timeline update from server wasn't received timely for {StallTime}ms.", typeof(TModel).Name, (int)(DateTime.UtcNow - _playbackStallStartedAt).TotalMilliseconds);
            }

            // If playback is too far back from server-supplied timeline head (plus normal delivery latency), catch up by decreasing delay.
            int catchupThisFrameMs;
            if (PlaybackMode == ClientPlaybackMode.Smooth)
            {
                // For playback, we can advance up to ...LastReceivedTick + 1 - epsilon. When we reach it, we should ideally receive
                // the next update from server.

                int         nextUpdateContainsTick          = GetLatestServerKnownTick() + 1;
                DateTime    nextUpdateArrivesLatestAt       = _nextUpdateExpectedAt + GetSmoothPlaybackDelay();
                MetaTime    nextUpdateAtModelTime           = ModelUtil.TimeAtTick(nextUpdateContainsTick, Journal.StagedModel.TimeAtFirstTick, Journal.StagedModel.TicksPerSecond);
                MetaTime    nextUpdateAtPresentationTime    = nextUpdateAtModelTime + _playbackDelta;

                // The amount of excess buffer is the difference of when we can expect the update, vs. when we need it to present it.
                TimeSpan    excessBuffer = nextUpdateAtPresentationTime.ToDateTime() - nextUpdateArrivesLatestAt;

                if (excessBuffer > TimeSpan.FromSeconds(1))
                {
                    // If too late, immediately run up to the the latest known position.
                    catchupThisFrameMs = (int)excessBuffer.TotalMilliseconds;
                }
                else if (excessBuffer >= TimeSpan.Zero)
                {
                    // Catch up at most 20% faster than real time
                    // We are dealing with submilliseconds, so keep a special carry around.
                    MetaDuration    frameDelta          = (currentTime - _previousUpdateAt);
                    float           catchupThisFrame    = System.Math.Min((float)excessBuffer.TotalMilliseconds, (float)frameDelta.Milliseconds * 0.2f + _playbackFfCarry);

                    catchupThisFrameMs  = (int)catchupThisFrame;
                    _playbackFfCarry = catchupThisFrame - (float)catchupThisFrameMs;

                    if (catchupThisFrameMs > 0)
                    {
                        if (!_playbackFfReported)
                        {
                            _log.Debug("Playback of {Name} stated fast forward. Attempt to reduce delay buffer by {Excess}ms", typeof(TModel).Name, excessBuffer.TotalMilliseconds);
                            _playbackFfReported = true;
                        }
                    }
                }
                else
                {
                    _playbackFfCarry = 0.0f;
                    catchupThisFrameMs = 0;
                    if (_playbackFfReported)
                    {
                        _playbackFfReported = false;
                        _log.Verbose("Playback of {Name} ended fast forward.", typeof(TModel).Name);
                    }
                }
            }
            else
            {
                // Always catch up with server
                int             furthestTick                    = GetLatestServerKnownTick();
                MetaTime        furthestTickAtModelTime         = ModelUtil.TimeAtTick(furthestTick, Journal.StagedModel.TimeAtFirstTick, Journal.StagedModel.TicksPerSecond);
                MetaTime        furthestTickAtPresentationTime  = furthestTickAtModelTime + _playbackDelta;

                catchupThisFrameMs = (int)(furthestTickAtPresentationTime - currentTime).Milliseconds;
            }

            if (catchupThisFrameMs > 0)
            {
                _playbackDelta -= MetaDuration.FromMilliseconds(catchupThisFrameMs);

                // And due to the jump on presentation timeline, update presentation timestamps.
                // If AdvanceTimeline runs any new tick, UpdateCurrentTickPresentationTime() is already called but it doesn't matter.
                UpdateCurrentTickPresentationTime();

                // Since we moved formward in time, playback again.
                if (AdvanceTimeline(currentTime, out bool _))
                    modelWasUpdated = true;
            }

            return modelWasUpdated;
        }

        /// <summary>
        /// Get the latest tick server is known to have executed. This is the Model's current tick unless
        /// client has some buffered updates is has not yet exeuted.
        /// </summary>
        public int GetLatestServerKnownTick()
        {
            int tick = Journal.StagedModel.CurrentTick;
            foreach (EntityTimelineUpdateMessage msg in _playbackBuffer)
            {
                foreach (ModelAction op in msg.Operations)
                {
                    if (op == null)
                        tick++;
                }
            }
            return tick;
        }

        /// <summary>
        /// Return the amount of time the playback is behind the updates in attempt to keep playback smooth despite jitter.
        /// </summary>
        public TimeSpan GetSmoothPlaybackDelay()
        {
            if (PlaybackMode != ClientPlaybackMode.Smooth)
                return TimeSpan.Zero;

            // We have been measuring when Update messages arrive and comparing it to when we expected it to arrive. We use the
            // most delayed message (of the recent history) as the scale how much time we should keep in the buffer as safety
            // extra. This means we increase buffer when jitter increses.

            TimeSpan bufferNeededForJitter = -2 * _updateTimestamps.MinOrDefault(TimeSpan.Zero);
            if (bufferNeededForJitter > TimeSpan.FromMilliseconds(500))
                bufferNeededForJitter = TimeSpan.FromMilliseconds(500);
            if (bufferNeededForJitter < TimeSpan.Zero)
                bufferNeededForJitter = TimeSpan.Zero;
            return bufferNeededForJitter;
        }

        void HandleChecksumMismatch(ModelJournal<TModel>.Follower.CommitResult result)
        {
            // No point checking this after disconnect. Disconnect could have been caused by error or checker failure. There
            // is good chance this mismatch is useless.
            if (IsDisconnected)
                return;

            _log.Warning("Expected checksum {ExpectedChecksum} but got {ActualChecksum} when applying update from {From} to {End} with operations {OpList}.",
                result.ExpectedChecksum,
                result.ActualChecksum,
                result.FirstSuspectOpAt,
                result.LastSuspectOpAt,
                result.GetSuspectOpListDisplayString());
            _log.Error("Checksum mismatch while applying updates from server.");
            _messageDispatcher.SendMessage(new EntityChecksumMismatchDetails(result.ChecksumSerializedAfter, result.ConflictAfterPosition.Tick, result.ConflictAfterPosition.Operation));

            _client.OnTimelineUpdateFailed();
        }

        public void SetClientListeners(Action<IMultiplayerModel> applyFn)
        {
            applyFn?.Invoke(CommittedModel);
        }

        IMultiplayerModel IMultiplayerEntityClientContext.CommittedModel => CommittedModel;

        /// <summary>
        /// Called when next latency sample becomes available. <see cref="EnableLatencyMeasurement"/> must be
        /// enabled for measurements to be taken.
        /// </summary>
        protected virtual void OnLatencySample(LatencySample sample) { }
    }
}
