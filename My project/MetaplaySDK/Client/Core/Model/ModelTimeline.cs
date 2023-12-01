// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Serialization;
using System.Collections.Generic;
using System;

namespace Metaplay.Core.Model
{
    /// <summary>
    /// Like MetaSerialized, but only for internal use.
    /// </summary>
    readonly struct SerializedModel
    {
        readonly IOBuffer _buffer;
        readonly MetaSerializationFlags _flags;
        readonly IGameConfigDataResolver _resolver;
        readonly int _logicVersion;

        SerializedModel(IOBuffer buffer, MetaSerializationFlags flags, IGameConfigDataResolver resolver, int logicVersion)
        {
            _buffer = buffer;
            _flags = flags;
            _resolver = resolver;
            _logicVersion = logicVersion;
        }

        public static SerializedModel Create(IModel obj, MetaSerializationFlags flags, IGameConfigDataResolver resolver, int logicVersion)
        {
            FlatIOBuffer buffer = new FlatIOBuffer();
            JournalUtil.Serialize(buffer, obj, flags, logicVersion);
            return new SerializedModel(buffer, flags, resolver, logicVersion);
        }
        public IModel Deserialize()
        {
            return JournalUtil.Deserialize<IModel>(_buffer, _flags, _resolver, _logicVersion);
        }
        public void Dispose()
        {
            _buffer.Dispose();
        }
    };

    /// <summary>
    /// The data describing how a Model at a certain timeline position can be constructed.
    /// </summary>
    public struct TimelineCommitPlan
    {
        /// <summary>
        /// The number of ops from Checkpoint that will need to be run completed to reach the desired position.
        /// </summary>
        public int NumOperationsFromCheckpoint;

        /// <summary>
        /// If the commit position has a snapshot.
        /// </summary>
        public bool HasSnapshot;

        /// <summary>
        /// The position of the snapshot. The position is on the start of the next op, following the last completed op.
        /// </summary>
        public JournalPosition SnapshotPosition;

        public TimelineCommitPlan(int operationIndex, bool hasSnapshot, JournalPosition snapshotPosition)
        {
            NumOperationsFromCheckpoint = operationIndex;
            HasSnapshot = hasSnapshot;
            SnapshotPosition = snapshotPosition;
        }
    }

    public readonly struct TimelineSlot
    {
        public enum OpType
        {
            Tick,
            Action,
        }
        public readonly OpType          Type;
        public readonly JournalPosition StartPosition;
        public readonly int             NumSteps;

        public JournalPosition LastStepStartPosition => JournalPosition.FromTickOperationStep(StartPosition.Tick, StartPosition.Operation, (ushort)(StartPosition.Step + NumSteps - 1));
        public JournalPosition PositionAfter => JournalPosition.FromTickOperationStep(StartPosition.Tick, StartPosition.Operation, (ushort)(StartPosition.Step + NumSteps));

        private TimelineSlot(OpType type, JournalPosition startPosition, int numSteps)
        {
            Type = type;
            StartPosition = startPosition;
            NumSteps = numSteps;
        }

        public static TimelineSlot ForTick(JournalPosition startPosition, int numSteps) => new TimelineSlot(OpType.Tick, startPosition, numSteps);
        public static TimelineSlot ForAction(JournalPosition startPosition, int numSteps) => new TimelineSlot(OpType.Action, startPosition, numSteps);
    }

    public interface ITimelineOp : IDisposable
    {
        TimelineSlot GetSlot();
        ModelAction GetAction();
    }

    public class Timeline<TModel, TOp, TStep>
        where TModel : class, IModel<TModel>
        where TOp: ITimelineOp
    {
        uint                                                _version;

        readonly JournalChecksumHelper                      _checksumHelper;

        // Checkpoint
        SerializedModel                                     _checkpointBlob;
        TModel                                              _checkpointObj; // either _checkpointObj is null or Blob is disposed
        IModelRuntimeData<TModel>                           _checkpointRTData;
        public uint                                         _checkpointChecksum;
        JournalPosition                                     _checkpointPosition; // All actions on timeline are >=. All markers are >=.

        // Stage
        public TModel                                       _stagedModel;
        public List<TOp>                                    _stagedOps;
        public JournalPosition                              _stagedPosition; // All actions on timeline are <. All markers are <=.
        public OrderedDictionary<JournalPosition, TStep>    _stagedSteps;

        public JournalChecksumHelper                        ChecksumHelper => _checksumHelper;
        public uint                                         Version => _version;
        public uint                                         CheckpointChecksum => _checkpointChecksum;
        public JournalPosition                              CheckpointPosition => _checkpointPosition;

        struct Snapshot
        {
            public SerializedModel Model;
            public IModelRuntimeData<TModel> RTData;
            public uint Checksum;

            public Snapshot(SerializedModel model, IModelRuntimeData<TModel> rtData, uint checksum)
            {
                Model = model;
                RTData = rtData;
                Checksum = checksum;
            }
        }

        OrderedDictionary<JournalPosition, Snapshot> _snapshots;

        public Timeline(JournalChecksumHelper checksumHelper)
        {
            _version = 0;

            _checksumHelper = checksumHelper;

            _checkpointBlob = default;
            _checkpointObj = null;
            _checkpointRTData = null;
            _checkpointChecksum = 0;
            _checkpointPosition = default;

            _stagedModel = null;
            _stagedOps = new List<TOp>();
            _stagedPosition = default;
            _stagedSteps = new OrderedDictionary<JournalPosition, TStep>();

            _snapshots = new OrderedDictionary<JournalPosition, Snapshot>();
        }

        public void Setup(TModel model, JournalPosition position, IOBuffer checksumBuffer)
        {
            InvalidateOngoingWalks();

            _checkpointBlob = SerializedModel.Create(model, MetaSerializationFlags.IncludeAll, model.GetDataResolver(), model.LogicVersion);
            _checkpointObj = null;
            _checkpointRTData = model.GetRuntimeData();
            _checkpointChecksum = _checksumHelper.ComputeChecksum(checksumBuffer, model);
            _checkpointPosition = position;

            _stagedModel = model;
            _stagedOps.Clear();
            _stagedPosition = position;
            _stagedSteps.Clear();
        }

        public void InvalidateOngoingWalks()
        {
            _version++;
        }

        public void RemoveExpiredOps()
        {
            InvalidateOngoingWalks();

            // Remove too new ( >= Staged )
            int lastOpToRetain;
            for (lastOpToRetain = _stagedOps.Count - 1; lastOpToRetain >= 0; --lastOpToRetain)
            {
                TOp op = _stagedOps[lastOpToRetain];
                JournalPosition stepPosition = op.GetSlot().StartPosition;
                if (stepPosition < _stagedPosition)
                    break;
            }
            for (int ndx = lastOpToRetain + 1; ndx < _stagedOps.Count; ++ndx)
                DisposeOp(_stagedOps[ndx]);

            if (lastOpToRetain != _stagedOps.Count - 1)
            {
                int firstOpToRemove = lastOpToRetain + 1;
                _stagedOps.RemoveRange(firstOpToRemove, _stagedOps.Count - firstOpToRemove);
            }

            // Remove early ( < Checkpoint ).
            int firstOpToRetain;
            for (firstOpToRetain = 0; firstOpToRetain < _stagedOps.Count; ++firstOpToRetain)
            {
                TOp op = _stagedOps[firstOpToRetain];
                JournalPosition stepPosition = op.GetSlot().StartPosition;
                if (stepPosition >= _checkpointPosition)
                    break;
            }
            for (int ndx = 0; ndx < firstOpToRetain; ++ndx)
                DisposeOp(_stagedOps[ndx]);
            _stagedOps.RemoveRange(0, firstOpToRetain);

            RemoveExpiredSnapshots();
        }

        void DisposeOp(TOp op)
        {
            op.Dispose();
            JournalPosition stepPosition = op.GetSlot().StartPosition;
            for (int stepNdx = 0; stepNdx < op.GetSlot().NumSteps; ++stepNdx)
            {
                _stagedSteps.Remove(stepPosition);
                stepPosition = JournalPosition.AfterStep(stepPosition);
            }
        }

        /// <summary>
        /// If <paramref name="moveModelOwnershipToTimeline"/> is set, the caller may not modify the given <paramref name="model"/> instance after this call.
        /// </summary>
        public void UpdateCheckpoint(TModel model, uint checksum, JournalPosition position, bool moveModelOwnershipToTimeline)
        {
            bool checkpointIsStoredAsBlob = (_checkpointObj == null);
            if (checkpointIsStoredAsBlob)
                _checkpointBlob.Dispose();
            else
                _checkpointObj = null;

            if (moveModelOwnershipToTimeline)
                _checkpointObj = model;
            else
                _checkpointBlob = SerializedModel.Create(model, MetaSerializationFlags.IncludeAll, model.GetDataResolver(), model.LogicVersion);

            _checkpointRTData = model.GetRuntimeData();
            _checkpointChecksum = checksum;
            _checkpointPosition = position;
        }

        /// <summary>
        /// Consumes the <paramref name="commitPlan"/> and updates the checkpoint to the version.
        /// </summary>
        /// <param name="commitPlan"></param>
        public void UpdateCheckpointToSnapshotAndRemoveOps(TimelineCommitPlan commitPlan)
        {
            JournalPosition positionAfterCommit = commitPlan.SnapshotPosition;
            Snapshot snapshot = _snapshots[positionAfterCommit];
            _snapshots.Remove(positionAfterCommit);

            bool checkpointIsStoredAsBlob = (_checkpointObj == null);
            if (checkpointIsStoredAsBlob)
                _checkpointBlob.Dispose();
            else
                _checkpointObj = null;

            _checkpointBlob = snapshot.Model;
            _checkpointRTData = snapshot.RTData;
            _checkpointChecksum = snapshot.Checksum;
            _checkpointPosition = positionAfterCommit;

            RemoveExpiredOps();
        }

        public TModel GetCheckpointModel()
        {
            // need to materialize?
            if (_checkpointObj == null)
            {
                TModel checkpoint = (TModel)_checkpointBlob.Deserialize();
                _checkpointRTData.CopyResolversTo(checkpoint);

                _checkpointBlob.Dispose();
                _checkpointObj = checkpoint;
            }
            return _checkpointObj;
        }

        public TModel CreateCheckpointModelCopy()
        {
            return CopyCheckpoint();
        }

        /// <summary>
        /// Returns the plan to create a model at a certain position. If position would result in partial operation, will throw.
        /// </summary>
        public TimelineCommitPlan CreateCommitPlan(JournalPosition commitAllBefore)
        {
            int opNdx;
            JournalPosition followingModelStartPosition = _stagedPosition;
            for (opNdx = 0; opNdx < _stagedOps.Count; ++opNdx)
            {
                TOp op = _stagedOps[opNdx];
                if (op.GetSlot().StartPosition >= commitAllBefore)
                {
                    if (opNdx != 0 && _stagedOps[opNdx - 1].GetSlot().PositionAfter > commitAllBefore)
                        throw new InvalidOperationException("partial commit");
                    break;
                }
                // Align to next op
                followingModelStartPosition = op.GetSlot().PositionAfter;
                if (followingModelStartPosition.Operation == 0)
                    followingModelStartPosition = JournalPosition.AfterTick(followingModelStartPosition);
                else
                    followingModelStartPosition = JournalPosition.AfterAction(followingModelStartPosition);
            }

            if (_snapshots.ContainsKey(followingModelStartPosition))
                return new TimelineCommitPlan(opNdx, hasSnapshot: true, snapshotPosition: followingModelStartPosition);
            else
                return new TimelineCommitPlan(opNdx, hasSnapshot: false, snapshotPosition: default);
        }

        TModel CopyCheckpoint()
        {
            bool checkpointIsStoredAsBlob = (_checkpointObj == null);
            TModel model;

            if (!checkpointIsStoredAsBlob)
            {
                // Data is needed as blob (for copying). Promote to blob.
                _checkpointBlob = SerializedModel.Create(_checkpointObj, MetaSerializationFlags.IncludeAll, _checkpointObj.GetDataResolver(), _checkpointObj.LogicVersion);
                _checkpointObj = null;
            }

            model = (TModel)_checkpointBlob.Deserialize();
            _checkpointRTData.CopyResolversTo(model);
            return model;
        }

        /// <summary>
        /// Returns a model on the timeline at the given point in time. This method returns always a new
        /// model, i.e. a defensive copy is made.
        /// </summary>
        public TModel CreateHistoricModelCopy(TimelineCommitPlan plan)
        {
            TModel rerunModel = CopyCheckpoint();
            for (int ndx = 0; ndx < plan.NumOperationsFromCheckpoint; ++ndx)
            {
                TOp op = _stagedOps[ndx];
                ApplyOperation(rerunModel, ref op, NullChecksumEvaluator.Context);
            }
            return rerunModel;
        }

        /// <summary>
        /// Creates a new model as a copy at the given point in time and makes it suitable to become Staged model. This attaches
        /// side-effect listeners to the created model.
        /// </summary>
        public TModel CreateNewStagedModel(TimelineCommitPlan plan)
        {
            TModel model = CreateHistoricModelCopy(plan);
            _checkpointRTData.CopySideEffectListenersTo(model);
            return model;
        }

        public MetaActionResult ApplyOperation<TChecksumCtx>(TModel model, ref TOp op, TChecksumCtx ctx)
            where TChecksumCtx: IChecksumContext
        {
            switch (op.GetSlot().Type)
            {
                case TimelineSlot.OpType.Tick:
                    return ModelUtil.RunTick(model, ctx);

                case TimelineSlot.OpType.Action:
                    return ModelUtil.RunAction(model, op.GetAction(), ctx);
            }
            return default;
        }

        /// <summary>
        /// Takes a snapshot of the current Staged model. This snapshot may be used as a starting point when synthetizing a historic model state
        /// to speed up the process. Snapshots are automatically deleted when they fall beyond Checkpoint Position.
        /// </summary>
        public void CaptureStageSnapshot(uint stagedModelChecksum)
        {
            if (_snapshots.TryGetValue(_stagedPosition, out Snapshot oldSnapshot))
            {
                oldSnapshot.Model.Dispose();
                _snapshots.Remove(_stagedPosition);
            }

            _snapshots[_stagedPosition] = new Snapshot(
                model: SerializedModel.Create(_stagedModel, MetaSerializationFlags.IncludeAll, _stagedModel.GetDataResolver(), _stagedModel.LogicVersion),
                rtData: _stagedModel.GetRuntimeData(),
                checksum: stagedModelChecksum);
        }

        public void RemoveAllSnapshots()
        {
            foreach (Snapshot snapshot in _snapshots.Values)
                snapshot.Model.Dispose();
            _snapshots.Clear();
        }

        void RemoveExpiredSnapshots()
        {
            List<JournalPosition> toBeDeleted = null;

            foreach ((JournalPosition position, Snapshot snapshot) in _snapshots)
            {
                // Any positions before checkpoint are unreachable. No point storing them.
                // Additionally, the checkpoint position itself is not useful. The checkpoint itself covers it.
                // Similarly, any positions after stage are orphan, this would only happen in the case of a rollback.
                // The position on stage is still valid, and can be retained.
                if (position <= _checkpointPosition || position > _stagedPosition)
                {
                    if (toBeDeleted == null)
                        toBeDeleted = new List<JournalPosition>();
                    toBeDeleted.Add(position);

                    snapshot.Model.Dispose();
                }
            }

            if (toBeDeleted != null)
            {
                foreach (var position in toBeDeleted)
                    _snapshots.Remove(position);
            }
        }
    };

    struct TimelineWalker<TModel, TOp, TStep>
        where TModel : class, IModel<TModel>
        where TOp: ITimelineOp
    {
        Timeline<TModel, TOp, TStep>    _timeline;
        uint                            _version;
        JournalPosition?                _pendingStart;
        int                             _opIndex;
        int                             _opStepIndex; // -1 on EOF
        JournalPosition                 _position;

        public JournalPosition PositionBefore => _position;
        public JournalPosition PositionAfter => JournalPosition.NextAfter(PositionBefore);
        public bool IsTickFirstStep => IsTickStep && PositionBefore.Step == 0;
        public bool IsTickStep => PositionBefore.Operation == 0;
        public bool IsActionFirstStep => IsActionStep && PositionBefore.Step == 0;
        public bool IsActionStep => PositionBefore.Operation > 0;
        public ModelAction Action => _timeline._stagedOps[_opIndex].GetAction();
        public Timeline<TModel, TOp, TStep> Timeline => _timeline;
        public int OpIndex => _opIndex;
        public int OpStepIndex => _opStepIndex;
        public int NumStepsTotal => _timeline._stagedOps[_opIndex].GetSlot().NumSteps;

        public TimelineWalker(Timeline<TModel, TOp, TStep> timeline, JournalPosition fromPosition)
        {
            _timeline = timeline;
            _version = timeline.Version;
            _pendingStart = fromPosition;
            _opIndex = -1;
            _opStepIndex = -1;
            _position = JournalPosition.Epoch;
        }

        void CheckVersion()
        {
            if (_version != _timeline.Version)
                throw new InvalidOperationException("journal was modified during walk");
        }

        void Start(JournalPosition fromPosition)
        {
            for (int ndx = 0; ndx < _timeline._stagedOps.Count; ++ndx)
            {
                TimelineSlot slot = _timeline._stagedOps[ndx].GetSlot();
                if (slot.PositionAfter <= fromPosition)
                    continue;

                if (slot.StartPosition >= fromPosition)
                {
                    // before action
                    _opIndex = ndx;
                    _opStepIndex = 0;
                    _position = slot.StartPosition;
                    return;
                }
                else
                {
                    // mid action
                    _opIndex = ndx;
                    _opStepIndex = fromPosition.Step - slot.StartPosition.Step;
                    _position = fromPosition;
                    return;
                }
            }

            // EOF
            _opIndex = _timeline._stagedOps.Count;
            _opStepIndex = -1;
        }

        public bool MoveNext()
        {
            CheckVersion();

            // first update setups
            if (_pendingStart.HasValue)
            {
                Start(_pendingStart.Value);
                _pendingStart = null;
                return (_opStepIndex > -1);
            }

            if (_opStepIndex == -1)
                return false;

            _opStepIndex++;
            if (_opStepIndex < _timeline._stagedOps[_opIndex].GetSlot().NumSteps)
            {
                _position = JournalPosition.AfterStep(_position);
                return true;
            }

            _opStepIndex = 0;
            _opIndex++;
            if (_opIndex < _timeline._stagedOps.Count)
            {
                _position = _timeline._stagedOps[_opIndex].GetSlot().StartPosition;
                return true;
            }

            _opStepIndex = -1;
            return false;
        }
    }
}
