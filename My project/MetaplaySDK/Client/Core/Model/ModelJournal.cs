// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Metaplay.Core.Model
{
    /// <summary>
    /// Exposes the Journal flavor of serialization (Tagged) used in checkpoints and checksumming.
    /// </summary>
    public static class JournalUtil
    {
        public static void Serialize<T>(IOBuffer buffer, T obj, MetaSerializationFlags flags, int logicVersion)
        {
            using (IOWriter writer = new IOWriter(buffer, IOWriter.Mode.Truncate))
                MetaSerialization.SerializeTagged<T>(writer, obj, flags, logicVersion);
        }

        public static T Deserialize<T>(IOBuffer buffer, MetaSerializationFlags flags, IGameConfigDataResolver resolver, int logicVersion)
        {
            using (IOReader reader = new IOReader(buffer))
                return MetaSerialization.DeserializeTagged<T>(reader, flags, resolver, logicVersion);
        }

        public static uint ComputeChecksum(IOBuffer buffer, IModel model)
        {
            // \note: checksum is always computed as an IModel
            using (IOWriter writer = new IOWriter(buffer, IOWriter.Mode.Truncate))
                MetaSerialization.SerializeTagged<IModel>(writer, model, MetaSerializationFlags.ComputeChecksum, model.LogicVersion);
            return MurmurHash.MurmurHash2(buffer);
        }
    }

    /// <summary>
    /// Helper to allow optionally omitting checksum computation in ModelJournal.
    /// </summary>
    public struct JournalChecksumHelper
    {
        bool _computeChecksums;

        public JournalChecksumHelper(bool computeChecksums)
        {
            _computeChecksums = computeChecksums;
        }

        public uint ComputeChecksum(IOBuffer buffer, IModel model)
        {
            if (_computeChecksums)
                return JournalUtil.ComputeChecksum(buffer, model);
            else
            {
                buffer.Clear();
                return 0;
            }
        }
    }

    /// <summary>
    /// Represents the runtime data (i.e. non-serialized portion, e.g. Log channel, listeners, additional state) of a model instance.
    /// </summary>
    public interface IModelRuntimeData<TModel> where TModel : IModel
    {
        /// <summary>
        /// Sets runtime data on instance. Called on all models - internal, staged or otherwise.
        /// </summary>
        void CopyResolversTo(TModel model);

        /// <summary>
        /// Sets runtime data on instance. Called on a model when a model becomes StagedModel. Use this to attach side-effect listeners.
        /// </summary>
        void CopySideEffectListenersTo(TModel model);
    }

    /// <summary>
    /// <c>ModelJournal</c> represents model with a bounded history for the purpose of rollbacks.
    ///
    /// <para>
    /// ModelJournal consists of three parts: A known good checkpoint, a timeline of staged actions and ticks not yet committed, and the speculated (staged) model.
    /// The speculated model has observed all staged actions and ticks. It is operated by 1) staging actions 2) committing staged actions and 3) executing a rollbacks.
    /// Staging an action simply appends the action to the timeline, and updates the speculated model. Committing actions updates the checkpoint and removes the staged actions,
    /// and the rollback restores state from the checkpoint, and reruns actions upto the certain point, and drops the rest.
    /// </para>
    /// <para>
    /// The journals come in two flavors - leaders and followers. Leaders can stage ticks and actions at any time and will compute expected checksums for these operations. The
    /// followers take a list of ticks and actions and their claimed checksums, run them, and eventually verify them. If the verification fails, it is up to the caller to resolve
    /// the situation in the desired manner.
    /// </para>
    /// </summary>
    public static class ModelJournal<TModel>
        where TModel : class, IModel<TModel>
    {
        public abstract class Leader
        {
            internal readonly struct StagedOp : ITimelineOp
            {
                public readonly TimelineSlot        Slot;
                public readonly ModelAction         Action;

                StagedOp(TimelineSlot slot, ModelAction action)
                {
                    Slot = slot;
                    Action = action;
                }

                public static StagedOp ForTick(JournalPosition position, int numSteps)
                {
                    return new StagedOp(TimelineSlot.ForTick(position, numSteps), default);
                }
                public static StagedOp ForAction(JournalPosition position, ModelAction action, int numSteps)
                {
                    return new StagedOp(TimelineSlot.ForAction(position, numSteps), action);
                }

                public TimelineSlot GetSlot()
                {
                    return Slot;
                }
                public ModelAction GetAction()
                {
                    return Action;
                }
                public void Dispose()
                {
                }
            };
            internal readonly struct StagedStep
            {
                public readonly uint    ComputedChecksumAfter;
                public readonly string  StepName;

                public StagedStep(uint computedChecksumAfter, string stepName)
                {
                    ComputedChecksumAfter = computedChecksumAfter;
                    StepName = stepName;
                }
            };
            class JournalChecksumEvaluator : IChecksumContext
            {
                TModel                                          _model;
                JournalChecksumHelper                           _checksumHelper;
                FlatIOBuffer                                    _recycledChecksumBuffer;
                bool                                            _bufferIsLocked;
                uint                                            _finalChecksum;
                int                                             _tick;
                int                                             _operation;
                int                                             _stepIndex;
                OrderedDictionary<JournalPosition, StagedStep>  _dstSteps;

                /// <summary>
                /// Buffer containing model after all steps, serialized with ComputeChecksum flag. Only valid between EndEvaluation() -- Reset().
                /// </summary>
                public FlatIOBuffer FinalChecksumBuffer => _recycledChecksumBuffer;

                /// <summary>
                /// The checksum after the final step. Only valid between EndEvaluation() -- Reset().
                /// </summary>
                public uint FinalChecksum => _finalChecksum;

                /// <summary>
                /// Number of executed steps. Contains the end step so always >= 1. Only valid between EndEvaluation() -- Reset().
                /// </summary>
                public int NumSteps => _stepIndex;

                public JournalChecksumEvaluator(JournalChecksumHelper checksumHelper, FlatIOBuffer recycledChecksumBuffer, ref OrderedDictionary<JournalPosition, StagedStep> dstSteps)
                {
                    _checksumHelper = checksumHelper;
                    _recycledChecksumBuffer = recycledChecksumBuffer;
                    _dstSteps = dstSteps;
                }

                public void BeginEvaluation(TModel model, JournalPosition position)
                {
                    if (position.Step != 0)
                        throw new InvalidOperationException("invalid journal position");

                    _tick = position.Tick;
                    _operation = position.Operation;
                    _stepIndex = position.Step;
                    _model = model;
                }

                public void EndEvaluation()
                {
                    uint checksum = _checksumHelper.ComputeChecksum(_recycledChecksumBuffer, _model);
                    _finalChecksum = checksum;
                    AdvanceStep("end", checksum);
                    _model = null;

                    _bufferIsLocked = true;
                    _recycledChecksumBuffer.BeginRead(); // lock until reset
                }

                public void Reset()
                {
                    _model = null;
                    if (_bufferIsLocked)
                    {
                        _recycledChecksumBuffer.EndRead();
                        _bufferIsLocked = false;
                    }
                }

                void AdvanceStep(string name, uint checksum)
                {
                    StagedStep step = new StagedStep(checksum, name);
                    _dstSteps.Add(JournalPosition.FromTickOperationStep(_tick, _operation, _stepIndex), step);
                    _stepIndex++;
                }

                void IChecksumContext.Step(string name)
                {
                    uint checksum = _checksumHelper.ComputeChecksum(_recycledChecksumBuffer, _model);
                    AdvanceStep(name, checksum);
                }
            };

            public interface IListener
            {
                /// <summary> Called when listener is attached to the journal. Useful for initial setup </summary>
                void OnAttach(Leader self);

                /// <summary>
                /// Called just after Setup() is applied.
                ///
                /// <para>
                /// <c>checksumSerializationBuffer</c> contains the model serialized with ComputeChecksum flag. The method may inspect this
                /// buffer, but should not modify it, keep a reference, or release it. The contents of the memory will be undefined after
                /// the return of this call.
                /// </para>
                /// </summary>
                void AfterSetup(uint checksumAfter, IOBuffer checksumSerializationBuffer);

                /// <summary>
                /// Called just before a Tick() operation is applied.
                /// </summary>
                void BeforeTick(int tick);

                /// <summary>
                /// Called just after a Tick() operation is applied.
                ///
                /// <para>
                /// <c>checksumSerializationBuffer</c> contains the model serialized with ComputeChecksum flag. The method may inspect this
                /// buffer, but should not modify it, keep a reference, or release it. The contents of the memory will be undefined after
                /// the return of this call.
                /// </para>
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void AfterTick(int tick, MetaActionResult result, uint checksumAfter, IOBuffer checksumSerializationBuffer);

                /// <summary>
                /// Called just before an Action() operation is applied.
                /// </summary>
                void BeforeAction(ModelAction action);

                /// <summary>
                /// Called just after an Action() operation is applied.
                ///
                /// <para>
                /// <c>checksumSerializationBuffer</c> contains the model serialized with ComputeChecksum flag. The method may inspect this
                /// buffer, but should not modify it, keep a reference, or release it. The contents of the memory will be undefined after
                /// the return of this call.
                /// </para>
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void AfterAction(ModelAction action, MetaActionResult result, uint checksumAfter, IOBuffer checksumSerializationBuffer);

                /// <summary>
                /// Called just before Commit() operation is applied.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void BeforeCommit();

                /// <summary>
                /// Called just after Commit() operation is applied.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void AfterCommit();

                /// <summary>
                /// Called just before Rollback() operation is applied.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void BeforeRollback();

                /// <summary>
                /// Called just after Rollback() operation is applied.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void AfterRollback();

                /// <summary>
                /// Called just before ModifyHistory() operation is applied to a model.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void BeforeModifyHistory(IModel model);

                /// <summary>
                /// Called just after ModifyHistory() operation is applied to a model.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void AfterModifyHistory(IModel model);

                /// <summary>
                /// Called just before ExecuteUnsynchronizedServerActionBlock() operation is executed.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void BeforeExecuteUnsynchronizedServerActionBlock(IModel model);

                /// <summary>
                /// Called just after ExecuteUnsynchronizedServerActionBlock() operation is complete.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void AfterExecuteUnsynchronizedServerActionBlock(IModel model);
            }

            LogChannel                              _log;
            Timeline<TModel, StagedOp, StagedStep>  _timeline;
            FlatIOBuffer                            _recycledChecksumBuffer;
            uint                                    _stagedChecksum;
            JournalChecksumEvaluator                _checksumEvaluator;
            List<IListener>                         _listeners;
            int                                     _executingTick;
            ModelAction                             _executingAction;
            readonly bool                           _enableConsistencyChecks;
            bool                                    _suppressTimelineDivergedErrors;
            bool                                    _suppressCheckpointOutsideModificationErrors;

            public LogChannel Log => _log;
            public JournalChecksumHelper ChecksumHelper => _timeline.ChecksumHelper;
            public TModel StagedModel => _timeline._stagedModel;
            public TModel CheckpointModel => _timeline.CreateCheckpointModelCopy();
            public JournalPosition StagedPosition => _timeline._stagedPosition;
            public JournalPosition CheckpointPosition => _timeline.CheckpointPosition;
            public uint StagedChecksum => _stagedChecksum;
            public uint CheckpointChecksum => _timeline.CheckpointChecksum;
            public bool ConsistencyChecksEnabled => _enableConsistencyChecks;

            public Leader(LogChannel log, bool enableConsistencyChecks, bool computeChecksums)
            {
                JournalChecksumHelper checksumHelper = new JournalChecksumHelper(computeChecksums: computeChecksums);

                _log = log;
                _enableConsistencyChecks = enableConsistencyChecks;
                _timeline = new Timeline<TModel, StagedOp, StagedStep>(checksumHelper);
                _recycledChecksumBuffer = new FlatIOBuffer();
                _stagedChecksum = _timeline.CheckpointChecksum;
                _checksumEvaluator = new JournalChecksumEvaluator(checksumHelper, _recycledChecksumBuffer, ref _timeline._stagedSteps);
                _listeners = new List<IListener>();

                _executingTick = -1;
                _executingAction = null;
                _suppressTimelineDivergedErrors = false;
                _suppressCheckpointOutsideModificationErrors = false;
            }

            /// <summary>
            /// Sets up the timeline on the given checkpoint at the given time.
            /// </summary>
            public void Setup(TModel model, JournalPosition position)
            {
                _timeline.Setup(model, position, _recycledChecksumBuffer);
                _stagedChecksum = _timeline.CheckpointChecksum;
                foreach (IListener listener in _listeners)
                    listener.AfterSetup(_timeline.CheckpointChecksum, _recycledChecksumBuffer);

                _executingTick = -1;
                _executingAction = null;
            }

            /// <summary>
            /// Stages a Tick() action.
            /// </summary>
            public void StageTick()
            {
                _timeline.InvalidateOngoingWalks();

                // Check that we're not already executing a tick or action
                if (_executingTick != -1)
                    throw new InvalidOperationException($"Trying to execute tick {StagedPosition.Tick} when already exeuting tick {_executingTick}");
                else if (_executingAction != null)
                    throw new InvalidOperationException($"Trying to execute tick {StagedPosition.Tick} within another action ({PrettyPrint.Compact(_executingAction)})");

                try
                {
                    // Mark as executing action
                    _executingTick = (int)StagedPosition.Tick + 1;

                    foreach (IListener listener in _listeners)
                        listener.BeforeTick(_executingTick);

                    // Align to next Tick
                    if (StagedPosition.Operation != 0 || StagedPosition.Step != 0)
                        _timeline._stagedPosition = JournalPosition.NextTick(StagedPosition);

                    // Run tick and add journal entries
                    _checksumEvaluator.BeginEvaluation(StagedModel, StagedPosition);
                    MetaActionResult result = ModelUtil.RunTick(StagedModel, _checksumEvaluator);
                    _checksumEvaluator.EndEvaluation();

                    _timeline._stagedOps.Add(StagedOp.ForTick(_timeline._stagedPosition, _checksumEvaluator.NumSteps));
                    _timeline._stagedPosition = JournalPosition.AfterTick(_timeline._stagedPosition);
                    _stagedChecksum = _checksumEvaluator.FinalChecksum;

                    foreach (IListener listener in _listeners)
                        listener.AfterTick(_executingTick, result, _checksumEvaluator.FinalChecksum, _checksumEvaluator.FinalChecksumBuffer);
                }
                finally
                {
                    // Mark that no longer executing tick
                    _executingTick = -1;
                    _checksumEvaluator.Reset();
                }
            }

            /// <summary>
            /// Stages an action.
            /// </summary>
            public MetaActionResult StageAction(ModelAction action)
            {
                _timeline.InvalidateOngoingWalks();

                // Check that we're not already executing a tick or action
                if (_executingTick != -1)
                    throw new InvalidOperationException($"Trying to execute action {PrettyPrint.Compact(action)} when already exeuting tick {_executingTick}");
                else if (_executingAction != null)
                    throw new InvalidOperationException($"Trying to execute action {PrettyPrint.Compact(action)} within another action ({PrettyPrint.Compact(_executingAction)})");
                else if (StagedPosition.Operation == 0 && StagedPosition.Step == 0)
                    throw new InvalidOperationException("Trying to execute action before Tick() has been executed");

                MetaActionResult actionResult;

                try
                {
                    // Mark as executing action
                    _executingAction = action;

                    foreach (IListener listener in _listeners)
                        listener.BeforeAction(action);

                    // Align to next (non-tick)Action
                    if (StagedPosition.Operation == 0 || StagedPosition.Step != 0)
                        _timeline._stagedPosition = JournalPosition.NextAction(StagedPosition);

                    // Run action and add journal entries
                    _checksumEvaluator.BeginEvaluation(StagedModel, StagedPosition);
                    actionResult = ModelUtil.RunAction(StagedModel, action, _checksumEvaluator);
                    _checksumEvaluator.EndEvaluation();

                    // Actions are serialized with SendOverNetwork.
                    _timeline._stagedOps.Add(StagedOp.ForAction(_timeline._stagedPosition, action, _checksumEvaluator.NumSteps));
                    _timeline._stagedPosition = JournalPosition.AfterAction(_timeline._stagedPosition);
                    _stagedChecksum = _checksumEvaluator.FinalChecksum;

                    foreach (IListener listener in _listeners)
                        listener.AfterAction(action, actionResult, _checksumEvaluator.FinalChecksum, _checksumEvaluator.FinalChecksumBuffer);
                }
                finally
                {
                    // Mark that execution is finished
                    _executingAction = null;
                    _checksumEvaluator.Reset();
                }

                return actionResult;
            }

            /// <summary>
            /// Commits all staged actions before <paramref name="commitAllBefore"/> into the checkpoint.
            /// </summary>
            public void Commit(JournalPosition commitAllBefore)
            {
                _timeline.InvalidateOngoingWalks();

                if (commitAllBefore > StagedPosition)
                    throw new InvalidOperationException("cannot commit journal farther into the future than staged");
                if (commitAllBefore < CheckpointPosition)
                    throw new InvalidOperationException("cannot commit into a time before CheckpointPosition");

                TimelineCommitPlan commitPlan = _timeline.CreateCommitPlan(commitAllBefore);
                foreach (IListener listener in _listeners)
                    listener.BeforeCommit();
                CommitOperationRange(commitPlan, commitAllBefore);
                foreach (IListener listener in _listeners)
                    listener.AfterCommit();
            }

            void CommitOperationRange(TimelineCommitPlan commitPlan, JournalPosition checkpointPosition)
            {
                if (commitPlan.NumOperationsFromCheckpoint == 0)
                    return;

                // Move Checkpoint forward by NumOperationsFromCheckpoint operations. We do this by applying the past operations to the
                // checkpoint. But if the destination just happens to be a Snapshot, we copy the model directly.

                if (commitPlan.HasSnapshot)
                {
                    // \note: Since this is a direct checkpoint copy we don't need to verify checksums. The checkpoints aren't exposed
                    //        externally, so they cannot be accidentally mutated.
                    _timeline.UpdateCheckpointToSnapshotAndRemoveOps(commitPlan);
                    return;
                }

                // Note that if the destination just happens to be the Staged model, we could also copy it and use it as
                // the new checkpoint. However, since the caller has not marked that position as a Checkpoint (above check)
                // we will not use it.

                // Run Checkpoint model forward by rerunning the actions.
                // \note: we modify the actual checkpoint instance, not a copy

                TModel checkpointModel = _timeline.GetCheckpointModel();

                // If there is record of the past checksum, we should get the same results now
                if (ConsistencyChecksEnabled && CheckpointChecksum != 0 && !_suppressCheckpointOutsideModificationErrors)
                {
                    uint actualChecksum = JournalUtil.ComputeChecksum(_recycledChecksumBuffer, checkpointModel);
                    if (actualChecksum != CheckpointChecksum)
                    {
                        Log.Error(
                            "{TypeName} Journal model has been changed outside a Tick or Action. "
                            + "Checksum of Journal.CheckpointModel is recorded as {Claimed}, but recomputed checksum is {Actual}.",
                            typeof(TModel).Name, CheckpointChecksum, actualChecksum);
                        // \todo: print difference (outside modification)

                        Log.Info("Further error reports from {Name} timeline checkpoint are disabled.", typeof(TModel).Name);
                        _suppressCheckpointOutsideModificationErrors = true;
                    }
                }

                for (int ndx = 0; ndx < commitPlan.NumOperationsFromCheckpoint; ++ndx)
                {
                    StagedOp op = _timeline._stagedOps[ndx];
                    _timeline.ApplyOperation(checkpointModel, ref op, NullChecksumEvaluator.Context);

                    if (ConsistencyChecksEnabled && !_suppressTimelineDivergedErrors)
                    {
                        uint expectedChecksum;
                        uint actualChecksum;

                        // If there is a reference checksum, compute current and check equality.
                        expectedChecksum = _timeline._stagedSteps[op.Slot.LastStepStartPosition].ComputedChecksumAfter;
                        if (expectedChecksum != 0)
                            actualChecksum = JournalUtil.ComputeChecksum(_recycledChecksumBuffer, checkpointModel);
                        else
                            actualChecksum = 0;

                        if (actualChecksum != expectedChecksum)
                        {
                            switch(op.Slot.Type)
                            {
                                case TimelineSlot.OpType.Tick:
                                    Log.Error(
                                        "{TypeName} Journal timeline has diverged at Tick on {Position}. "
                                        + "When Tick was staged to journal (run the first time), the checksum was recorded as {Claimed} but re-executing yielded {Actual}. "
                                        + "This is likely caused by 1) non-deterministic Tick() 2) modifying CheckpointModel state outside Tick or Action (for example by sharing a mutable class instance).",
                                        typeof(TModel).Name, op.Slot.StartPosition, expectedChecksum, actualChecksum);
                                    break;
                                case TimelineSlot.OpType.Action:
                                    ModelAction action = op.GetAction();
                                    Log.Error(
                                        "{TypeName} Journal timeline has diverged at Action {Action} on {Position}. "
                                        + "When Action was staged to journal (run the first time), the checksum was recorded as {Claimed} but re-executing yielded {Actual}. "
                                        + "This is likely caused by 1) non-deterministic Action 2) modifying CheckpointModel state outside Tick or Action (for example by sharing a mutable class instance).\n"
                                        + "Full action: {Action}",
                                        typeof(TModel).Name, action.GetType().Name, op.Slot.StartPosition, expectedChecksum, actualChecksum, PrettyPrint.Compact(action));
                                    break;
                            }

                            Log.Info("Further error reports from {Name} timeline are disabled.", typeof(TModel).Name);
                            _suppressTimelineDivergedErrors = true;
                        }
                    }
                }

                uint checkpointChecksum = _timeline._stagedSteps[_timeline._stagedOps[commitPlan.NumOperationsFromCheckpoint - 1].Slot.LastStepStartPosition].ComputedChecksumAfter;

                _timeline.UpdateCheckpoint(checkpointModel, checkpointChecksum, checkpointPosition, moveModelOwnershipToTimeline: true);
                _timeline.RemoveExpiredOps();
            }

            /// <summary>
            /// Restores StagedModel back to the state it was just before <paramref name="position"/>.
            ///
            /// <para>
            /// This is completed by throwing away all staged operations starting on or after <paramref name="position"/>, and then rerunning operations on checkpoint.
            /// </para>
            /// </summary>
            public void Rollback(JournalPosition position)
            {
                _timeline.InvalidateOngoingWalks();

                if (position > StagedPosition)
                    throw new InvalidOperationException("cannot revert into the future");
                if (position < CheckpointPosition)
                    throw new InvalidOperationException("cannot rever beyond the Checkpoint");

                TimelineCommitPlan rollbackPlan = _timeline.CreateCommitPlan(position);
                foreach (IListener listener in _listeners)
                    listener.BeforeRollback();
                RollbackOperationRange(rollbackPlan, position);
                foreach (IListener listener in _listeners)
                    listener.AfterRollback();
            }

            void RollbackOperationRange(TimelineCommitPlan rollbackPlan, JournalPosition rollbackPosition)
            {
                // No point in rerunning
                if (rollbackPlan.NumOperationsFromCheckpoint == _timeline._stagedOps.Count)
                    return;

                // Rerun from checkpoint up to the desired point, and attach side-effect listeners
                TModel rerunModel = _timeline.CreateNewStagedModel(rollbackPlan);

                uint newChecksum;
                if (rollbackPlan.NumOperationsFromCheckpoint == 0)
                    newChecksum = _timeline.CheckpointChecksum;
                else
                {
                    JournalPosition lastStep = _timeline._stagedOps[rollbackPlan.NumOperationsFromCheckpoint-1].Slot.LastStepStartPosition;
                    newChecksum = _timeline._stagedSteps[lastStep].ComputedChecksumAfter;
                }

                _timeline._stagedModel = rerunModel;
                _timeline._stagedPosition = rollbackPosition;
                _stagedChecksum = newChecksum;

                _timeline.RemoveExpiredOps();
            }

            /// <summary>
            /// Directly edits internal snapshots in the journal to allow for changing the history.
            ///
            /// <para>
            /// Note that no consistency validation of the staged action will be done. If this method is used to modify
            /// snapshot in such manner that already staged actions change behavior, the journal will become internally
            /// inconsistent.
            /// </para>
            /// </summary>
            public void ModifyHistory(Action<JournalPosition, TModel> inPlaceOperation)
            {
                DoModifyHistory(isInPlace: true, (JournalPosition position, TModel model) =>
                {
                    inPlaceOperation(position, model);
                    return model;
                });
            }

            /// <summary>
            /// Directly replaces internal snapshots in the journal to allow for changing the history.
            ///
            /// <para>
            /// Note that no consistency validation of the staged action will be done. If this method is used to modify
            /// snapshot in such manner that already staged actions change behavior, the journal will become internally
            /// inconsistent.
            /// </para>
            ///
            /// <para>
            /// The created <see cref="TModel"/>s must have their resolvers set properly. See <see cref="IModel{TModel}.GetRuntimeData"/>
            /// and <see cref="IModelRuntimeData{TModel}.CopyResolversTo(TModel)"/> for details.
            /// Side-effect listeners (<see cref="IModelRuntimeData{TModel}.CopySideEffectListenersTo(TModel)"/>"
            /// will be set automatically after the user-specified factory is run.
            /// </para>
            /// </summary>
            public void ModifyHistory(Func<JournalPosition, TModel, TModel> operation)
            {
                DoModifyHistory(isInPlace: false, operation);
            }

            void DoModifyHistory(bool isInPlace, Func<JournalPosition, TModel, TModel> operation)
            {
                _timeline.InvalidateOngoingWalks();
                _timeline.RemoveAllSnapshots();

                // stage
                foreach (IListener listener in _listeners)
                    listener.BeforeModifyHistory(StagedModel);

                if (isInPlace)
                {
                    operation(StagedPosition, StagedModel);
                }
                else
                {
                    var     stageRT     = StagedModel.GetRuntimeData();
                    TModel  newStage    = operation(StagedPosition, StagedModel);
                    stageRT.CopySideEffectListenersTo(newStage);
                    _timeline._stagedModel = newStage;
                }

                foreach (IListener listener in _listeners)
                    listener.AfterModifyHistory(StagedModel);

                // checkpoints
                {
                    TModel checkpoint;
                    if (isInPlace)
                        checkpoint = _timeline.GetCheckpointModel();
                    else
                        checkpoint = _timeline.CreateCheckpointModelCopy();

                    foreach (IListener listener in _listeners)
                        listener.BeforeModifyHistory(checkpoint);

                    if (isInPlace)
                        operation(CheckpointPosition, checkpoint);
                    else
                        checkpoint = operation(CheckpointPosition, checkpoint);

                    foreach (IListener listener in _listeners)
                        listener.AfterModifyHistory(checkpoint);

                    _timeline.UpdateCheckpoint(checkpoint, CheckpointChecksum, CheckpointPosition, moveModelOwnershipToTimeline: true);
                }
            }

            /// <summary>
            /// Runs given operation and, if journal checkers are enabled, checks that no changes have
            /// been made into no [NoChecksum] fields.
            /// </summary>
            public void ExecuteUnsynchronizedServerActionBlock(Action operation)
            {
                // stage
                foreach (IListener listener in _listeners)
                    listener.BeforeExecuteUnsynchronizedServerActionBlock(StagedModel);

                operation();

                foreach (IListener listener in _listeners)
                    listener.AfterExecuteUnsynchronizedServerActionBlock(StagedModel);
            }

            public struct JournalWalker
            {
                TimelineWalker<TModel, StagedOp, StagedStep> _walker;

                public JournalPosition PositionBefore => _walker.PositionBefore;
                public JournalPosition PositionAfter => _walker.PositionAfter;
                public bool IsTickFirstStep => _walker.IsTickFirstStep;
                public bool IsTickStep => _walker.IsTickStep;
                public bool IsActionFirstStep => _walker.IsActionFirstStep;
                public bool IsActionStep => _walker.IsActionStep;
                public ModelAction Action => _walker.Action;
                public int NumStepsTotal => _walker.NumStepsTotal;

                public uint ComputedChecksumAfter => _walker.Timeline._stagedSteps[PositionBefore].ComputedChecksumAfter;

                internal JournalWalker(Timeline<TModel, StagedOp, StagedStep> timeline, JournalPosition fromPosition)
                {
                    _walker = new TimelineWalker<TModel, StagedOp, StagedStep>(timeline, fromPosition);
                }

                public bool MoveNext() => _walker.MoveNext();
            }

            public JournalWalker WalkJournal(JournalPosition from)
            {
                return new JournalWalker(_timeline, from);
            }

            protected void AddListener(IListener listener)
            {
                listener.OnAttach(this);
                _listeners.Add(listener);
            }

            /// <summary>
            /// Returns a model on the timeline at the given point in time. This method returns always a new
            /// model, i.e. a defensive copy is made. Copying fails is position if before last committed position
            /// or after last staged position. Returned Model will NOT have resolvers or any other RTData attached.
            /// </summary>
            public TModel TryCopyModelAtPosition(JournalPosition position)
            {
                if (position > StagedPosition)
                    return null;
                if (position < CheckpointPosition)
                    return null;

                TimelineCommitPlan applyPlan = _timeline.CreateCommitPlan(commitAllBefore: position);
                return _timeline.CreateHistoricModelCopy(applyPlan);
            }

            /// <summary>
            /// Computes and updates the checksum of the model at desired postion. Checksum is computed with
            /// <see cref="JournalUtil.ComputeChecksum" />. If there were already a checksum computed and this
            /// computation differs, a warning is logged. This method is useful for manually controlling
            /// checksumming if default checksum generation disabled.
            /// Note that modifying StagedPosition is cheaper than Checkpoint or any intermediate step.
            /// </summary>
            /// <param name="checksummingBuffer">If given, a copy of the Model encoded in checksumming mode is written into this buffer</param>
            public uint ForceComputeChecksum(JournalPosition checksumAt, IOBuffer checksummingBuffer = null)
            {
                TimelineCommitPlan applyPlan;
                try
                {
                    applyPlan = _timeline.CreateCommitPlan(commitAllBefore: checksumAt);
                }
                catch(InvalidOperationException)
                {
                    return 0;
                }

                IOBuffer workBuffer = (checksummingBuffer != null) ?  checksummingBuffer : _recycledChecksumBuffer;
                if (applyPlan.NumOperationsFromCheckpoint == _timeline._stagedOps.Count)
                {
                    uint actualChecksum = JournalUtil.ComputeChecksum(workBuffer, StagedModel);
                    if (_stagedChecksum != actualChecksum)
                    {
                        if (_stagedChecksum != 0)
                        {
                            Log.Warning(
                                "{TypeName} Journal stage model checksum has changed between computation and manual force-recomputation. "
                                + "Checksum of Journal.StagedModel is recorded as {Claimed}, but recomputed checksum is {Actual}.", typeof(TModel).Name, _stagedChecksum, actualChecksum);
                        }

                        _stagedChecksum = actualChecksum;
                        if (_timeline._stagedOps.Count > 0)
                        {
                            JournalPosition lastStep = _timeline._stagedOps[_timeline._stagedOps.Count - 1].Slot.LastStepStartPosition;
                            _timeline._stagedSteps[lastStep] = new StagedStep(computedChecksumAfter: actualChecksum, _timeline._stagedSteps[lastStep].StepName);
                        }
                    }
                    return actualChecksum;
                }
                else if (applyPlan.NumOperationsFromCheckpoint == 0)
                {
                    uint actualChecksum = JournalUtil.ComputeChecksum(workBuffer, _timeline.GetCheckpointModel());
                    if (_timeline._checkpointChecksum != actualChecksum)
                    {
                        if (_timeline._checkpointChecksum != 0)
                        {
                            Log.Warning(
                                "{TypeName} Journal checkpoint model checksum has changed between computation and manual force-recomputation. "
                                + "Checksum of Journal.CheckpointModel is recorded as {Claimed}, but recomputed checksum is {Actual}.", typeof(TModel).Name, _stagedChecksum, actualChecksum);
                        }

                        _timeline._checkpointChecksum = actualChecksum;
                    }
                    return actualChecksum;
                }
                else
                {
                    JournalPosition lastStep = _timeline._stagedOps[applyPlan.NumOperationsFromCheckpoint - 1].Slot.LastStepStartPosition;
                    TModel model = _timeline.CreateHistoricModelCopy(applyPlan);
                    uint actualChecksum = JournalUtil.ComputeChecksum(workBuffer, model);

                    if (_timeline._stagedSteps[lastStep].ComputedChecksumAfter != 0 && _timeline._stagedSteps[lastStep].ComputedChecksumAfter != actualChecksum)
                    {
                        Log.Warning(
                            "{TypeName} Journal intermediate model checksum has changed between computation and manual force-recomputation. "
                            + "Checksum of model is recorded as {Claimed}, but recomputed checksum is {Actual}.", typeof(TModel).Name, _stagedChecksum, actualChecksum);
                    }
                    _timeline._stagedSteps[lastStep] = new StagedStep(computedChecksumAfter: actualChecksum, _timeline._stagedSteps[lastStep].StepName);
                    return actualChecksum;
                }
            }

            /// <inheritdoc cref="Timeline{TModel, TOp, TStep}.CaptureStageSnapshot"/>
            public void CaptureStageSnapshot()
            {
                _timeline.CaptureStageSnapshot(_stagedChecksum);
            }
        };

        public abstract class Follower
        {
            internal readonly struct StagedOp : ITimelineOp
            {
                public readonly TimelineSlot        Slot;
                public readonly ModelAction         Action;
                public readonly int                 NumStepsExpected;

                // \note: In case numStepsExecuted and numStepsExpected differ, use numStepsExecuted for the slot to
                //        keep staged-steps datastructure consistent with us. It doesn't matter too much, this will
                //        fail validation anyway.
                StagedOp(TimelineSlot slot, ModelAction action, ArraySegment<uint> expectedStepChecksums)
                {
                    Slot = slot;
                    Action = action;
                    NumStepsExpected = expectedStepChecksums.Count;
                }

                public static StagedOp ForTick(JournalPosition position, int numStepsExecuted, ArraySegment<uint> expectedStepChecksums)
                {
                    return new StagedOp(TimelineSlot.ForTick(position, numStepsExecuted), default, expectedStepChecksums);
                }
                public static StagedOp ForAction(JournalPosition position, ModelAction action, int numStepsExecuted, ArraySegment<uint> expectedStepChecksums)
                {
                    return new StagedOp(TimelineSlot.ForAction(position, numStepsExecuted), action, expectedStepChecksums);
                }

                public TimelineSlot GetSlot()
                {
                    return Slot;
                }
                public void Dispose()
                {
                }
                public ModelAction GetAction()
                {
                    return Action;
                }
            };
            internal readonly struct StagedStep
            {
                public readonly uint    ExpectedChecksumAfter;
                public readonly string  StepName;

                public StagedStep(uint expectedChecksumAfter, string stepName)
                {
                    ExpectedChecksumAfter = expectedChecksumAfter;
                    StepName = stepName;
                }
            };

            /// <summary> "Evaluates" checksums by returning the expected value </summary>
            class ExpectedStepEvaluator : IChecksumContext
            {
                ArraySegment<uint>                              _expectedChecksums;
                int                                             _tick;
                int                                             _operation;
                int                                             _stepIndex;
                uint                                            _finalChecksum;
                OrderedDictionary<JournalPosition, StagedStep>  _dstSteps;

                /// <summary>
                /// Number of executed steps. Contains the end step so always >= 1. Only valid between EndEvaluation() -- Reset().
                /// </summary>
                public int NumSteps => _stepIndex;

                /// <summary>
                /// The checksum after the final step. Only valid between EndEvaluation() -- Reset().
                /// </summary>
                public uint FinalChecksum => _finalChecksum;

                public ExpectedStepEvaluator(ref OrderedDictionary<JournalPosition, StagedStep>  dstSteps)
                {
                    _dstSteps = dstSteps;
                }

                public void BeginEvaluation(JournalPosition position, ArraySegment<uint> expectedChecksums)
                {
                    if (position.Step != 0)
                        throw new InvalidOperationException("invalid journal position");

                    _tick = position.Tick;
                    _operation = position.Operation;
                    _stepIndex = position.Step;
                    _expectedChecksums = expectedChecksums;
                }

                public void EndEvaluation()
                {
                    AdvanceStep("end");
                }

                public void Reset()
                {
                    _expectedChecksums = default;
                }

                void AdvanceStep(string name)
                {
                    // if mismatch here, fill with zeros. The checksums don't matter, the count mismatch will be caught anyway.
                    uint expectedChecksum = 0;
                    if (_stepIndex < _expectedChecksums.Count)
                        expectedChecksum = _expectedChecksums.Array[_expectedChecksums.Offset + _stepIndex];

                    StagedStep step = new StagedStep(expectedChecksum, name);
                    _dstSteps.Add(JournalPosition.FromTickOperationStep(_tick, _operation, _stepIndex), step);
                    _finalChecksum = expectedChecksum;
                    _stepIndex++;
                }

                void IChecksumContext.Step(string name)
                {
                    AdvanceStep(name);
                }
            };

            class ExpectedStepValidator : IChecksumContext
            {
                TModel                                          _model;
                FlatIOBuffer                                    _checksumBufferOdd;
                FlatIOBuffer                                    _checksumBufferEven;
                bool                                            _bufferIsLocked;
                OrderedDictionary<JournalPosition, StagedStep>  _srcSteps;
                int                                             _tick;
                int                                             _operation;
                int                                             _stepIndex;
                bool                                            _hasConflict;
                JournalPosition                                 _conflictingStepStartPosition;
                string                                          _conflictingStepName;
                uint                                            _conflictingExpectedChecksum;
                uint                                            _conflictingActualChecksum;
                int                                             _numExpectedSteps;
                uint                                            _finalChecksum;

                /// <summary>
                /// Buffer containing model after all steps (until first conflict), serialized with ComputeChecksum flag. Only valid between EndEvaluation() -- Reset().
                /// </summary>
                public FlatIOBuffer ChecksumBufferAfter => ((_stepIndex % 2) == 0) ? _checksumBufferOdd : _checksumBufferEven;

                /// <summary>
                /// Buffer containing model after all steps (before first conflict), serialized with ComputeChecksum flag. Only valid between EndEvaluation() -- Reset().
                /// </summary>
                public FlatIOBuffer ChecksumBufferBefore => ((_stepIndex % 2) == 0) ? _checksumBufferEven : _checksumBufferOdd;

                /// <summary>
                /// The checksum after the final step. Only valid between EndEvaluation() -- Reset().
                /// </summary>
                public uint FinalChecksum => _finalChecksum;

                public ExpectedStepValidator(OrderedDictionary<JournalPosition, StagedStep> srcSteps)
                {
                    _checksumBufferOdd = new FlatIOBuffer();
                    _checksumBufferEven = new FlatIOBuffer();
                    _srcSteps = srcSteps;
                }

                public void SetInitialModel(TModel model)
                {
                    JournalUtil.ComputeChecksum(_checksumBufferOdd, model);
                }

                public void BeginEvaluation(TModel model, JournalPosition position, int numExpectedSteps)
                {
                    if (position.Step != 0)
                        throw new InvalidOperationException("invalid journal position");

                    _model = model;
                    _tick = position.Tick;
                    _operation = position.Operation;
                    _stepIndex = position.Step;
                    _numExpectedSteps = numExpectedSteps;
                    _hasConflict = false;
                    _conflictingStepName = null;
                }

                public void EndEvaluation()
                {
                    _finalChecksum = InternalStep("end");

                    // We consumed all steps?
                    if (!_hasConflict && _stepIndex < _numExpectedSteps)
                    {
                        // We run less steps than originator.
                        // We don't have the expected checksum here, so just use clearly placeholder values 0 and 1.
                        // This shouldn't be a problem as the unique "past_end" step name should be greppable.
                        _hasConflict = true;
                        _conflictingStepStartPosition = JournalPosition.FromTickOperationStep(_tick, _operation, _stepIndex);
                        _conflictingStepName = "past_end";
                        _conflictingExpectedChecksum = 1;
                        _conflictingActualChecksum = 0;
                    }

                    _model = null;

                    _bufferIsLocked = true;
                    _checksumBufferOdd.BeginRead(); // lock until reset
                    _checksumBufferEven.BeginRead(); // lock until reset
                }

                public void Reset()
                {
                    _model = null;

                    _hasConflict = false;
                    _conflictingStepName = null;

                    if (_bufferIsLocked)
                    {
                        _checksumBufferOdd.EndRead();
                        _checksumBufferEven.EndRead();
                        _bufferIsLocked = false;
                    }
                }

                public bool TryGetConflict(out JournalPosition afterStepStartingAt, out string stepName, out uint expectedChecksum, out uint actualChecksum)
                {
                    if (!_hasConflict)
                    {
                        afterStepStartingAt = default;
                        stepName = default;
                        expectedChecksum = default;
                        actualChecksum = default;
                        return false;
                    }

                    afterStepStartingAt = _conflictingStepStartPosition;
                    stepName = _conflictingStepName;
                    expectedChecksum = _conflictingExpectedChecksum;
                    actualChecksum = _conflictingActualChecksum;
                    return true;
                }

                void AdvanceStep(string name, uint actualChecksumAfter)
                {
                    JournalPosition stepStartsAt = JournalPosition.FromTickOperationStep(_tick, _operation, _stepIndex);
                    _stepIndex++;

                    uint expectedChecksumAfter;
                    if (_stepIndex > _numExpectedSteps)
                    {
                        // We run more steps than originator
                        expectedChecksumAfter = 0;
                    }
                    else if (_srcSteps.TryGetValue(stepStartsAt, out StagedStep expectedStep))
                    {
                        if (expectedStep.ExpectedChecksumAfter == actualChecksumAfter)
                            return;
                        expectedChecksumAfter = expectedStep.ExpectedChecksumAfter;
                    }
                    else
                    {
                        expectedChecksumAfter = 0;
                    }

                    _hasConflict = true;
                    _conflictingStepStartPosition = stepStartsAt;
                    _conflictingStepName = name;
                    _conflictingExpectedChecksum = expectedChecksumAfter;
                    _conflictingActualChecksum = actualChecksumAfter;
                }
                void IChecksumContext.Step(string name)
                {
                    InternalStep(name);
                }
                uint InternalStep(string name)
                {
                    if (_hasConflict)
                        return 0;
                    uint checksum = JournalUtil.ComputeChecksum(((_stepIndex % 2) == 0) ? (_checksumBufferEven) : (_checksumBufferOdd), _model);
                    AdvanceStep(name, checksum);
                    return checksum;
                }
            };

            public struct CommitResult : IDisposable
            {
                public readonly bool HasConflict;
                public readonly JournalPosition ConflictAfterPosition;
                public readonly string StepName;
                public readonly uint ExpectedChecksum;
                public readonly uint ActualChecksum;
                public readonly byte[] ChecksumSerializedBefore;
                public readonly byte[] ChecksumSerializedAfter;
                public readonly ModelAction ConflictAction;
                public readonly bool HasCheckpointDrift;

                public readonly JournalPosition             FirstSuspectOpAt;
                public readonly JournalPosition             LastSuspectOpAt;
                public readonly IEnumerable<ModelAction>    SuspectOps;

                public JournalPosition BeforeConflictOperation
                {
                    get
                    {
                        if (ConflictAfterPosition.Operation == 0)
                            return JournalPosition.BeforeTick(ConflictAfterPosition.Tick);
                        else
                            return JournalPosition.BeforeAction(ConflictAfterPosition.Tick, ConflictAfterPosition.Operation - 1);
                    }
                }

                CommitResult(bool hasConflict, JournalPosition conflictAfterPosition, string stepName, uint expectedChecksum, uint actualChecksum, byte[] checksumSerializedBefore, byte[] checksumSerializedAfter, ModelAction conflictAction, bool hasCheckpointDrift, JournalPosition firstSuspectOpAt, JournalPosition lastSuspectOpAt, IEnumerable<ModelAction> suspectOps)
                {
                    HasConflict = hasConflict;
                    ConflictAfterPosition = conflictAfterPosition;
                    StepName = stepName;
                    ExpectedChecksum = expectedChecksum;
                    ActualChecksum = actualChecksum;
                    ChecksumSerializedBefore = checksumSerializedBefore;
                    ChecksumSerializedAfter = checksumSerializedAfter;
                    ConflictAction = conflictAction;
                    HasCheckpointDrift = hasCheckpointDrift;
                    FirstSuspectOpAt = firstSuspectOpAt;
                    LastSuspectOpAt = lastSuspectOpAt;
                    SuspectOps = suspectOps;
                }

                public static CommitResult Ok()
                {
                    return new CommitResult(false, default, default, default, default, default, default, default, default, default, default, Array.Empty<ModelAction>());
                }
                public static CommitResult Conflict(JournalPosition conflictAfter, string stepName, uint expectedChecksum, uint actualChecksum, byte[] checksumSerializedBefore, byte[] checksumSerializedAfter, ModelAction conflictAction, JournalPosition firstSuspectOpAt, JournalPosition lastSuspectOpAt, List<ModelAction> suspectOps)
                {
                    return new CommitResult(true, conflictAfter, stepName, expectedChecksum, actualChecksum, checksumSerializedBefore, checksumSerializedAfter, conflictAction, hasCheckpointDrift: false, firstSuspectOpAt, lastSuspectOpAt, suspectOps);
                }
                public static CommitResult CheckpointDrift(JournalPosition conflictAfter, string stepName, uint expectedChecksum, uint actualChecksum, byte[] checksumSerializedBefore, byte[] checksumSerializedAfter, ModelAction conflictAction)
                {
                    return new CommitResult(true, conflictAfter, stepName, expectedChecksum, actualChecksum, checksumSerializedBefore, checksumSerializedAfter, conflictAction, hasCheckpointDrift: true, default, default, Array.Empty<ModelAction>());
                }

                public string GetSuspectOpListDisplayString()
                {
                    int nextSuspectTick;
                    if (FirstSuspectOpAt.Operation == 0)
                        nextSuspectTick = FirstSuspectOpAt.Tick;
                    else
                        nextSuspectTick = FirstSuspectOpAt.Tick + 1;

                    StringBuilder opList = new StringBuilder();
                    foreach (ModelAction op in SuspectOps)
                    {
                        if (opList.Length > 0)
                            opList.Append(",");
                        if (op == null)
                        {
                            opList.Append(FormattableString.Invariant($"Tick {nextSuspectTick}"));
                            nextSuspectTick++;
                        }
                        else
                            opList.Append(PrettyPrint.Compact(op));
                    }
                    if (opList.Length == 0)
                        opList.Append("<unknown>");
                    return opList.ToString();
                }

                void IDisposable.Dispose()
                {
                }
            }
            public interface IListener
            {
                /// <summary> Called when listener is attached to the journal. Useful for initial setup </summary>
                void OnAttach(Follower self);

                /// <summary>
                /// Called just after Setup() is applied.
                ///
                /// <para>
                /// <c>checksumSerializationBuffer</c> contains the model serialized with ComputeChecksum flag. The method may inspect this
                /// buffer, but should not modify it, keep a reference, or release it. The contents of the memory will be undefined after
                /// the return of this call.
                /// </para>
                /// </summary>
                void AfterSetup(uint checksumAfter, IOBuffer checksumSerializationBuffer);

                /// <summary>
                /// Called just before a Tick() operation is applied.
                /// </summary>
                void BeforeTick(int tick);

                /// <summary>
                /// Called just after a Tick() operation is applied.
                /// </summary>
                void AfterTick(int tick, MetaActionResult result);

                /// <summary>
                /// Called just before an Action() operation is applied.
                /// </summary>
                void BeforeAction(ModelAction action);

                /// <summary>
                /// Called just after an Action() operation is applied.
                /// </summary>
                void AfterAction(ModelAction action, MetaActionResult result);

                /// <summary>
                /// Called just before Commit() operation is applied.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void BeforeCommit();

                /// <summary>
                /// Called just after Commit() operation is applied.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void AfterCommit();

                /// <summary>
                /// Called during Commit() if rerunning staged operation on checkpoint does not match the staged model.
                /// This can be caused by non-determinism in actions or ticks, or by modifying staged model directly
                /// changing its checksum. In either case, this is most likely a bug.
                ///
                /// <para>
                /// The callee does not need to handle this case in any way. The journal will resume its state on the
                /// recomputed model. However, if this was caused intentionally by modifying the model directly, such
                /// changes will be lost and the callee should handle the case as appropriate.
                /// </para>
                /// </summary>
                void OnCommitCheckpointDrift(IModel stagedModel, uint stagedModelChecksum, IModel rerunModel, uint rerunModelChecksum);

                /// <summary>
                /// Called just before Rollback() operation is applied.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void BeforeRollback();

                /// <summary>
                /// Called just after Rollback() operation is applied.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void AfterRollback();

                /// <summary>
                /// Called just before ModifyHistory() operation is applied to a model.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void BeforeModifyHistory(IModel model);

                /// <summary>
                /// Called just after ModifyHistory() operation is applied to a model.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void AfterModifyHistory(IModel model);

                /// <summary>
                /// Called just before ExecuteUnsynchronizedServerActionBlock() operation is executed.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void BeforeExecuteUnsynchronizedServerActionBlock(IModel model);

                /// <summary>
                /// Called just after ExecuteUnsynchronizedServerActionBlock() operation is complete.
                ///
                /// <para>
                /// The method may also inspect journal state, but should not call any Journal modifying method.
                /// </para>
                /// </summary>
                void AfterExecuteUnsynchronizedServerActionBlock(IModel model);
            }

            LogChannel                              _log;
            Timeline<TModel, StagedOp, StagedStep>  _timeline;
            FlatIOBuffer                            _recycledChecksumBuffer;
            uint                                    _expectedChecksum;
            ExpectedStepEvaluator                   _expectedStepEvaluator;
            ExpectedStepValidator                   _expectedStepValidator;
            List<IListener>                         _listeners;
            int                                     _executingTick;
            ModelAction                             _executingAction;

            public LogChannel Log => _log;
            public TModel StagedModel => _timeline._stagedModel;
            public TModel CheckpointModel => _timeline.CreateCheckpointModelCopy();
            public JournalPosition StagedPosition => _timeline._stagedPosition;
            public JournalPosition CheckpointPosition => _timeline.CheckpointPosition;
            public uint ExpectedStagedChecksum => _expectedChecksum;
            public uint CheckpointChecksum => _timeline.CheckpointChecksum;

            public Follower(LogChannel log)
            {
                _log = log;
                _timeline = new Timeline<TModel, StagedOp, StagedStep>(new JournalChecksumHelper(computeChecksums: true));
                _recycledChecksumBuffer = new FlatIOBuffer();
                _expectedChecksum = _timeline.CheckpointChecksum;
                _expectedStepEvaluator = new ExpectedStepEvaluator(ref _timeline._stagedSteps);
                _expectedStepValidator = new ExpectedStepValidator(_timeline._stagedSteps);
                _listeners = new List<IListener>();

                _executingTick = -1;
                _executingAction = null;
            }

            /// <summary>
            /// Sets up the timeline on the given checkpoint at the given time.
            /// </summary>
            public void Setup(TModel model, JournalPosition position)
            {
                _timeline.Setup(model, position, _recycledChecksumBuffer);
                _expectedChecksum = _timeline.CheckpointChecksum;
                foreach (IListener listener in _listeners)
                    listener.AfterSetup(_timeline.CheckpointChecksum, _recycledChecksumBuffer);

                _executingTick = -1;
                _executingAction = null;
            }

            /// <summary>
            /// Stages a Tick() action.
            /// </summary>
            public void StageTick(ArraySegment<uint> expectedStepChecksums)
            {
                _timeline.InvalidateOngoingWalks();

                // Check that we're not already executing a tick or action
                if (_executingTick != -1)
                    throw new InvalidOperationException($"Trying to execute tick {StagedPosition.Tick} when already exeuting tick {_executingTick}");
                else if (_executingAction != null)
                    throw new InvalidOperationException($"Trying to execute tick {StagedPosition.Tick} within another action ({PrettyPrint.Compact(_executingAction)})");

                try
                {
                    // Mark as executing action
                    _executingTick = (int)StagedPosition.Tick + 1;

                    foreach (IListener listener in _listeners)
                        listener.BeforeTick(_executingTick);

                    // Align to next Tick
                    if (StagedPosition.Operation != 0 || StagedPosition.Step != 0)
                        _timeline._stagedPosition = JournalPosition.NextTick(StagedPosition);

                    // Run tick and add journal entries
                    _expectedStepEvaluator.BeginEvaluation(StagedPosition, expectedStepChecksums);
                    MetaActionResult result = ModelUtil.RunTick(StagedModel, _expectedStepEvaluator);
                    _expectedStepEvaluator.EndEvaluation();

                    _timeline._stagedOps.Add(StagedOp.ForTick(_timeline._stagedPosition, _expectedStepEvaluator.NumSteps, expectedStepChecksums));
                    _timeline._stagedPosition = JournalPosition.AfterTick(_timeline._stagedPosition);
                    _expectedChecksum = _expectedStepEvaluator.FinalChecksum;

                    foreach (IListener listener in _listeners)
                        listener.AfterTick(_executingTick, result);
                }
                finally
                {
                    // Mark that no longer executing tick
                    _executingTick = -1;
                    _expectedStepEvaluator.Reset();
                }
            }

            /// <summary>
            /// Stages an action.
            /// </summary>
            public void StageAction(ModelAction action, ArraySegment<uint> expectedStepChecksums)
            {
                _timeline.InvalidateOngoingWalks();

                // Check that we're not already executing a tick or action
                if (_executingTick != -1)
                    throw new InvalidOperationException($"Trying to execute action {PrettyPrint.Compact(action)} when already exeuting tick {_executingTick}");
                else if (_executingAction != null)
                    throw new InvalidOperationException($"Trying to execute action {PrettyPrint.Compact(action)} within another action ({PrettyPrint.Compact(_executingAction)})");
                else if (StagedPosition.Operation == 0 && StagedPosition.Step == 0)
                    throw new InvalidOperationException("Trying to execute action before Tick() has been executed");

                try
                {
                    // Mark as executing action
                    _executingAction = action;

                    foreach (IListener listener in _listeners)
                        listener.BeforeAction(action);

                    // Align to next (non-tick)Action
                    if (StagedPosition.Operation == 0 || StagedPosition.Step != 0)
                        _timeline._stagedPosition = JournalPosition.NextAction(StagedPosition);

                    // Run action and add journal entries
                    _expectedStepEvaluator.BeginEvaluation(StagedPosition, expectedStepChecksums);
                    MetaActionResult result = ModelUtil.RunAction(StagedModel, action, _expectedStepEvaluator);
                    _expectedStepEvaluator.EndEvaluation();

                    // \todo[jarkko]: abstract, pluggable action storage mechanism
                    _timeline._stagedOps.Add(StagedOp.ForAction(_timeline._stagedPosition, action, _expectedStepEvaluator.NumSteps, expectedStepChecksums));
                    _timeline._stagedPosition = JournalPosition.AfterAction(_timeline._stagedPosition);
                    _expectedChecksum = _expectedStepEvaluator.FinalChecksum;

                    foreach (IListener listener in _listeners)
                        listener.AfterAction(action, result);
                }
                finally
                {
                    // Mark that execution is finished
                    _executingAction = null;
                    _expectedStepEvaluator.Reset();
                }
            }

            public enum ConflictResolutionMode
            {
                /// <summary>
                /// The Commit is either accepted as a whole or not at all. When a conflict is detected, the commit is aborted and the staged
                /// (conflicting) operations are kept in the journal. The only way to recover is to rollback. On failure, reports the first
                /// conflict.
                /// </summary>
                AllOrNothing,

                /// <summary>
                /// When a conflict is detected, continue commit to the end, and report the first conflict.
                /// </summary>
                ContinueEvenIfConflicts,
            };

            /// <summary>
            /// Commits all staged actions before <paramref name="commitAllBefore"/> into the checkpoint. <paramref name="resolutionMode"/>
            /// controls the behavior on conflict.
            /// </summary>
            public CommitResult Commit(JournalPosition commitAllBefore, ConflictResolutionMode resolutionMode)
            {
                _timeline.InvalidateOngoingWalks();

                if (commitAllBefore > StagedPosition)
                    throw new InvalidOperationException("cannot commit journal farther into the future than staged");
                if (commitAllBefore < CheckpointPosition)
                    throw new InvalidOperationException("cannot commit into a time before CheckpointPosition");

                TimelineCommitPlan commitPlan = _timeline.CreateCommitPlan(commitAllBefore);
                foreach (IListener listener in _listeners)
                    listener.BeforeCommit();
                CommitResult result = CommitOperationRange(commitPlan, commitAllBefore, resolutionMode);
                foreach (IListener listener in _listeners)
                    listener.AfterCommit();
                return result;
            }

            CommitResult CommitOperationRange(TimelineCommitPlan commitPlan, JournalPosition commitPosition, ConflictResolutionMode resolutionMode)
            {
                if (commitPlan.NumOperationsFromCheckpoint == 0)
                    return CommitResult.Ok();

                TModel  commitModel;
                uint    expectedCommitModelChecksum;
                if (commitPlan.NumOperationsFromCheckpoint == _timeline._stagedOps.Count)
                {
                    commitModel = StagedModel;
                    expectedCommitModelChecksum = ExpectedStagedChecksum;
                }
                else
                {
                    commitModel = _timeline.CreateHistoricModelCopy(commitPlan);
                    expectedCommitModelChecksum = _timeline._stagedSteps[_timeline._stagedOps[commitPlan.NumOperationsFromCheckpoint - 1].GetSlot().LastStepStartPosition].ExpectedChecksumAfter;
                }

                CommitResult result;
                uint actualChecksum = JournalUtil.ComputeChecksum(_recycledChecksumBuffer, commitModel);
                bool forceSnapshotCommitToCheckpoint = false;

                if (expectedCommitModelChecksum != actualChecksum)
                {
                    // walk till conflict
                    CommitResult conflict = FindCommitConflict(commitPlan.NumOperationsFromCheckpoint, out TModel conflictSearchRerunModel, out uint conflictSearchRerunModelChecksum);
                    if (conflict.HasConflict)
                    {
                        switch (resolutionMode)
                        {
                            case ConflictResolutionMode.AllOrNothing:
                            {
                                // got conflict, return the conflict and abort
                                return conflict;
                            }
                            case ConflictResolutionMode.ContinueEvenIfConflicts:
                            {
                                // got conflict, return the conflict and continue commit
                                result = conflict;
                                break;
                            }
                            default:
                                throw new NotImplementedException(nameof(resolutionMode));
                        }
                    }
                    else
                    {
                        // On re-evaluation the violation is gone?
                        // Report error. Recover from the state by re-snapshotting
                        // the checkpoint (only possible if trying to commit whole history).
                        // Note that we cannot give a valid error report in this case so we
                        // use a non-existent position within the commit range.
                        foreach (IListener listener in _listeners)
                            listener.OnCommitCheckpointDrift(commitModel, actualChecksum, conflictSearchRerunModel, conflictSearchRerunModelChecksum);

                        _ = JournalUtil.ComputeChecksum(_recycledChecksumBuffer, commitModel);
                        byte[] serializedModel = _recycledChecksumBuffer.ToArray();
                        result = CommitResult.CheckpointDrift(
                            JournalPosition.FromTickOperationStep(_timeline.CheckpointPosition.Tick, 9999, 0),
                            stepName: "fake",
                            expectedChecksum: expectedCommitModelChecksum,
                            actualChecksum: actualChecksum,
                            checksumSerializedBefore: serializedModel,
                            checksumSerializedAfter: serializedModel,
                            conflictAction: null);

                        if (commitPlan.NumOperationsFromCheckpoint == _timeline._stagedOps.Count)
                            forceSnapshotCommitToCheckpoint = true;
                    }
                }
                else
                {
                    result = CommitResult.Ok();
                }

                // commitModel is either StagedModel, or a temporary copy. If it is a temporary copy, we can move it to
                // the timeline. If it is not, we can either copy it to the timeline or replay actions on checkpoint to
                // let is catch up with commit position.
                // \todo[jarkko]: some heuristic for when to copy and when to re-evaluate
                bool commitModelIsATemporaryCopy        = Object.ReferenceEquals(commitModel, StagedModel) == false;
                bool allowReplayCheckpointToCommitPoint = true;
                if (commitModelIsATemporaryCopy || !allowReplayCheckpointToCommitPoint || forceSnapshotCommitToCheckpoint)
                {
                    _timeline.UpdateCheckpoint(commitModel, actualChecksum, commitPosition, moveModelOwnershipToTimeline: commitModelIsATemporaryCopy);
                    _timeline.RemoveExpiredOps();
                }
                else
                {
                    // Make old checkpoint valid by rerunning the actions.
                    TModel oldChecksumModel = _timeline.GetCheckpointModel();
                    for (int ndx = 0; ndx < commitPlan.NumOperationsFromCheckpoint; ++ndx)
                    {
                        StagedOp op = _timeline._stagedOps[ndx];
                        _timeline.ApplyOperation(oldChecksumModel, ref op, NullChecksumEvaluator.Context);
                    }

                    _timeline.UpdateCheckpoint(oldChecksumModel, actualChecksum, commitPosition, moveModelOwnershipToTimeline: true);
                    _timeline.RemoveExpiredOps();
                }
                return result;
            }

            CommitResult FindCommitConflict(int commitOpCount, out TModel walkModel, out uint walkChecksum)
            {
                walkModel = _timeline.CreateCheckpointModelCopy();

                _expectedStepValidator.SetInitialModel(walkModel);

                bool gotConflict = false;
                byte[] checksumBufferBefore = null;
                byte[] checksumBufferAfter = null;
                ModelAction conflictActionOrNull = null;
                JournalPosition conflictAfterStepStartingAt = default;
                string conflictStepName = null;
                uint conflictExpectedChecksum = 0;
                uint conflictActualChecksum = 0;

                JournalPosition firstSuspectOpAt = default;
                JournalPosition lastSuspectOpAt = default;
                List<ModelAction> suspectOps = new List<ModelAction>();

                walkChecksum = 0;
                for (int opNdx = 0; opNdx < commitOpCount; ++opNdx)
                {
                    StagedOp op = _timeline._stagedOps[opNdx];

                    try
                    {
                        ModelAction action = (op.GetSlot().Type == TimelineSlot.OpType.Action) ? (op.GetAction()) : (null);
                        _expectedStepValidator.BeginEvaluation(walkModel, op.GetSlot().StartPosition, op.NumStepsExpected);
                        _timeline.ApplyOperation(walkModel, ref op, _expectedStepValidator);
                        _expectedStepValidator.EndEvaluation();

                        walkChecksum = _expectedStepValidator.FinalChecksum;

                        if (_expectedStepValidator.TryGetConflict(out JournalPosition afterStepStartingAt, out string stepName, out uint expectedChecksum, out uint actualChecksum))
                        {
                            if (gotConflict == false)
                                firstSuspectOpAt = op.GetSlot().StartPosition;

                            // If expectedChecksum == 0, the step sent by leader most likely did not contain the checksum.
                            // Try to find first error for which we have a checksum on both sides. This will happen on on error
                            // granularity (otherwise we wouldn't have detected the conflict in the first place). If no such case,
                            // return the first error.
                            if (expectedChecksum != 0)
                            {
                                return CommitResult.Conflict(
                                    afterStepStartingAt,
                                    stepName,
                                    expectedChecksum,
                                    actualChecksum,
                                    _expectedStepValidator.ChecksumBufferBefore.ToArray(),
                                    _expectedStepValidator.ChecksumBufferAfter.ToArray(),
                                    action,
                                    firstSuspectOpAt,
                                    lastSuspectOpAt: op.GetSlot().StartPosition,
                                    suspectOps);
                            }

                            // Remember only the first error
                            if (gotConflict == false)
                            {
                                gotConflict = true;
                                checksumBufferBefore = _expectedStepValidator.ChecksumBufferBefore.ToArray();
                                checksumBufferAfter = _expectedStepValidator.ChecksumBufferAfter.ToArray();
                                conflictActionOrNull = action;
                                conflictAfterStepStartingAt = afterStepStartingAt;
                                conflictStepName = stepName;
                                conflictExpectedChecksum = expectedChecksum;
                                conflictActualChecksum = actualChecksum;
                            }
                        }

                        // If we have already a conflict (but without checksum), keep track of all ops
                        if (gotConflict)
                        {
                            suspectOps.Add(action);
                            lastSuspectOpAt = op.GetSlot().StartPosition;
                        }
                    }
                    finally
                    {
                        _expectedStepValidator.Reset();
                    }
                }

                // There was a conflict, but couldn't identify the root operation.
                if (gotConflict)
                    return CommitResult.Conflict(conflictAfterStepStartingAt, conflictStepName, conflictExpectedChecksum, conflictActualChecksum, checksumBufferBefore, checksumBufferAfter, conflictActionOrNull, firstSuspectOpAt, lastSuspectOpAt, suspectOps);

                // Cannot find conflict? Caller must handle.
                return CommitResult.Ok();
            }

            /// <summary>
            /// Restores StagedModel back to the state it was just before <paramref name="position"/>.
            ///
            /// <para>
            /// This is completed by throwing away all staged operations starting on or after <paramref name="position"/>, and then rerunning operations on checkpoint.
            /// </para>
            /// </summary>
            public void Rollback(JournalPosition position)
            {
                _timeline.InvalidateOngoingWalks();

                if (position > StagedPosition)
                    throw new InvalidOperationException("cannot revert into the future");
                if (position < CheckpointPosition)
                    throw new InvalidOperationException("cannot rever beyond the Checkpoint");

                TimelineCommitPlan rollbackPlan = _timeline.CreateCommitPlan(commitAllBefore: position);
                foreach (IListener listener in _listeners)
                    listener.BeforeRollback();
                RollbackOperationRange(rollbackPlan, position);
                foreach (IListener listener in _listeners)
                    listener.AfterRollback();
            }

            void RollbackOperationRange(TimelineCommitPlan rollbackPlan, JournalPosition rollbackPosition)
            {
                // No point in rerunning
                if (rollbackPlan.NumOperationsFromCheckpoint == _timeline._stagedOps.Count)
                    return;

                // Rerun from checkpoint up to the desired point, and attach side-effect listeners
                TModel rerunModel = _timeline.CreateNewStagedModel(rollbackPlan);

                uint newChecksum;
                if (rollbackPlan.NumOperationsFromCheckpoint == 0)
                    newChecksum = _timeline.CheckpointChecksum;
                else
                {
                    JournalPosition lastStep = _timeline._stagedOps[rollbackPlan.NumOperationsFromCheckpoint-1].Slot.LastStepStartPosition;
                    newChecksum = _timeline._stagedSteps[lastStep].ExpectedChecksumAfter;
                }

                _timeline._stagedModel = rerunModel;
                _timeline._stagedPosition = rollbackPosition;
                _expectedChecksum = newChecksum;

                _timeline.RemoveExpiredOps();
            }

            /// <summary>
            /// <inheritdoc cref="ModelJournal{TModel}.Leader.ModifyHistory(Action{JournalPosition, TModel})"/>
            /// </summary>
            public void ModifyHistory(Action<JournalPosition, TModel> inPlaceOperation)
            {
                DoModifyHistory(isInPlace: true, (JournalPosition position, TModel model) =>
                {
                    inPlaceOperation(position, model);
                    return model;
                });
            }

            /// <summary>
            /// <inheritdoc cref="ModelJournal{TModel}.Leader.ModifyHistory(Func{JournalPosition, TModel, TModel})"/>
            /// </summary>
            public void ModifyHistory(Func<JournalPosition, TModel, TModel> operation)
            {
                DoModifyHistory(isInPlace: false, operation);
            }

            void DoModifyHistory(bool isInPlace, Func<JournalPosition, TModel, TModel> operation)
            {
                _timeline.InvalidateOngoingWalks();
                _timeline.RemoveAllSnapshots();

                // stage
                foreach (IListener listener in _listeners)
                    listener.BeforeModifyHistory(StagedModel);

                if (isInPlace)
                {
                    operation(StagedPosition, StagedModel);
                }
                else
                {
                    var     stageRT     = StagedModel.GetRuntimeData();
                    TModel  newStage    = operation(StagedPosition, StagedModel);
                    stageRT.CopySideEffectListenersTo(newStage);
                    _timeline._stagedModel = newStage;
                }

                foreach (IListener listener in _listeners)
                    listener.AfterModifyHistory(StagedModel);

                // checkpoints
                {
                    TModel checkpoint;
                    if (isInPlace)
                        checkpoint = _timeline.GetCheckpointModel();
                    else
                        checkpoint = _timeline.CreateCheckpointModelCopy();

                    foreach (IListener listener in _listeners)
                        listener.BeforeModifyHistory(checkpoint);

                    if (isInPlace)
                        operation(CheckpointPosition, checkpoint);
                    else
                        checkpoint = operation(CheckpointPosition, checkpoint);

                    foreach (IListener listener in _listeners)
                        listener.AfterModifyHistory(checkpoint);

                    _timeline.UpdateCheckpoint(checkpoint, CheckpointChecksum, CheckpointPosition, moveModelOwnershipToTimeline: true);
                }
            }

            /// <summary>
            /// Runs given operation and, if journal checkers are enabled, checks that no changes have
            /// been made into no [NoChecksum] fields.
            /// </summary>
            public void ExecuteUnsynchronizedServerActionBlock(Action operation)
            {
                // stage
                foreach (IListener listener in _listeners)
                    listener.BeforeExecuteUnsynchronizedServerActionBlock(StagedModel);

                operation();

                foreach (IListener listener in _listeners)
                    listener.AfterExecuteUnsynchronizedServerActionBlock(StagedModel);
            }

            public struct JournalWalker
            {
                TimelineWalker<TModel, StagedOp, StagedStep> _walker;

                public JournalPosition PositionBefore => _walker.PositionBefore;
                public JournalPosition PositionAfter => _walker.PositionAfter;
                public bool IsTickFirstStep => _walker.IsTickFirstStep;
                public bool IsTickStep => _walker.IsTickStep;
                public bool IsActionFirstStep => _walker.IsActionFirstStep;
                public bool IsActionStep => _walker.IsActionStep;
                public ModelAction Action => _walker.Action;
                public int NumStepsTotal => _walker.NumStepsTotal;

                public uint ExpectedChecksumAfter => _walker.Timeline._stagedSteps[PositionBefore].ExpectedChecksumAfter;

                internal JournalWalker(Timeline<TModel, StagedOp, StagedStep> timeline, JournalPosition fromPosition)
                {
                    _walker = new TimelineWalker<TModel, StagedOp, StagedStep>(timeline, fromPosition);
                }

                public bool MoveNext() => _walker.MoveNext();
            }

            public JournalWalker WalkJournal(JournalPosition from)
            {
                return new JournalWalker(_timeline, from);
            }

            protected void AddListener(IListener listener)
            {
                listener.OnAttach(this);
                _listeners.Add(listener);
            }
        };
    };
}
