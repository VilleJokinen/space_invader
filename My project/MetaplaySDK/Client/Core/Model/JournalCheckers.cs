// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Serialization;
using System;
using static System.FormattableString;

namespace Metaplay.Core.Model.JournalCheckers
{
    public class ModelJournalListenerBase<TModel>
        : ModelJournal<TModel>.Leader.IListener
        , ModelJournal<TModel>.Follower.IListener
        where TModel : class, IModel<TModel>
    {
        protected LogChannel Log { get; private set; }

        ModelJournal<TModel>.Leader _leaderJournal;
        ModelJournal<TModel>.Follower _followerJournal;

        protected bool IsLeaderJournal => _leaderJournal != null;
        protected bool IsFollowerJournal => _followerJournal != null;

        protected IModel StagedModel
        {
            get
            {
                if (_leaderJournal != null)
                    return _leaderJournal.StagedModel;
                if (_followerJournal != null)
                    return _followerJournal.StagedModel;
                return null;
            }
        }
        protected IModel CheckpointModel
        {
            get
            {
                if (_leaderJournal != null)
                    return _leaderJournal.CheckpointModel;
                if (_followerJournal != null)
                    return _followerJournal.CheckpointModel;
                return null;
            }
        }

        protected uint StagedChecksumInJournal
        {
            get
            {
                if (_leaderJournal != null)
                    return _leaderJournal.StagedChecksum;
                // follower has no staged checksum.
                throw new InvalidOperationException();
            }
        }
        protected uint CheckpointChecksumInJournal
        {
            get
            {
                if (_leaderJournal != null)
                    return _leaderJournal.CheckpointChecksum;
                if (_followerJournal != null)
                    return _followerJournal.CheckpointChecksum;
                throw new InvalidOperationException();
            }
        }
        protected JournalPosition StagedPosition
        {
            get
            {
                if (_leaderJournal != null)
                    return _leaderJournal.StagedPosition;
                if (_followerJournal != null)
                    return _followerJournal.StagedPosition;
                return JournalPosition.Epoch;
            }
        }

        IOBuffer _leasedChecksumSerializationBuffer;
        IOBuffer _ownedChecksumSerializationBuffer;
        uint? _leasedChecksum;
        uint? _ownedChecksum;

        /// <summary>
        /// Setting this to true disables the checker until it is unpaused.
        /// </summary>
        public bool IsPaused
        {
            get
            {
                return _isPaused;
            }
            set
            {
                bool wasPaused = _isPaused;
                _isPaused = value;
                if (wasPaused && !_isPaused)
                    OnUnpaused();
            }
        }

        bool _isDisabled;
        bool _isPaused;
        bool IsPausedOrDisabled => _isPaused || _isDisabled;

        public ModelJournalListenerBase(LogChannel log)
        {
            Log = log;
            _isDisabled = false;
            _isPaused = false;
        }

        protected IOBuffer GetStagedModelChecksumSerializationBuffer(bool computeIfNotSuppliedByJournal = true)
        {
            if (_leasedChecksumSerializationBuffer != null && !_leasedChecksumSerializationBuffer.IsEmpty)
                return _leasedChecksumSerializationBuffer;
            if (!computeIfNotSuppliedByJournal)
                return null;
            UpdateStagedChecksum();
            return _ownedChecksumSerializationBuffer;
        }
        protected uint GetStagedModelChecksum(bool computeIfNotSuppliedByJournal = true)
        {
            if (_leasedChecksum != null && _leasedChecksum.Value != 0)
                return _leasedChecksum.Value;
            if (!computeIfNotSuppliedByJournal)
                return 0;
            UpdateStagedChecksum();
            return _ownedChecksum.Value;
        }
        void InvalidateCachedChecksum()
        {
            _ownedChecksum = null;
        }
        void UpdateStagedChecksum()
        {
            if (_ownedChecksum != null)
                return;
            if (_ownedChecksumSerializationBuffer == null)
                _ownedChecksumSerializationBuffer = new FlatIOBuffer();
            _ownedChecksum = JournalUtil.ComputeChecksum(_ownedChecksumSerializationBuffer, StagedModel);
        }
        protected void DisableAfterError()
        {
            Log.Info("Further error reports from {Name} disabled for this context.", GetType().ToGenericTypeString());
            _isDisabled = true;
        }

        protected virtual void AfterSetup() { }
        protected virtual void BeforeTick(int tick) { }

        /// <summary>
        /// Called just after a Tick() operation is applied.
        /// </summary>
        protected virtual void AfterTick(int tick, MetaActionResult result) { }

        /// <summary>
        /// Called just before an Action() operation is applied.
        /// </summary>
        protected virtual void BeforeAction(ModelAction action) { }

        /// <summary>
        /// Called just after an Action() operation is applied.
        /// </summary>
        protected virtual void AfterAction(ModelAction action, MetaActionResult result) { }
        protected virtual void AfterCommit() { }
        protected virtual void AfterRollback() { }
        protected virtual void OnCommitCheckpointDrift(IModel stagedModel, uint stagedModelChecksum, IModel rerunModel, uint rerunModelChecksum) { }
        protected virtual void BeforeModifyHistory(IModel model) { }
        protected virtual void AfterModifyHistory(IModel model) { }
        protected virtual void BeforeExecuteUnsynchronizedServerActionBlock(IModel model) { }
        protected virtual void AfterExecuteUnsynchronizedServerActionBlock(IModel model) { }
        protected virtual void OnUnpaused() { }

        #region ModelJournal<TModel>.Leader.IListener
        void ModelJournal<TModel>.Leader.IListener.OnAttach(ModelJournal<TModel>.Leader journal)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            _leaderJournal = journal;
        }

        void ModelJournal<TModel>.Leader.IListener.AfterSetup(uint checksumAfter, IOBuffer checksumSerializationBuffer)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            _leasedChecksumSerializationBuffer = checksumSerializationBuffer;
            _leasedChecksum = checksumAfter;

            try
            {
                AfterSetup();
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }

            _leasedChecksumSerializationBuffer = null;
            _leasedChecksum = null;
        }

        void ModelJournal<TModel>.Leader.IListener.BeforeTick(int tick)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                BeforeTick(tick);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Leader.IListener.AfterTick(int tick, MetaActionResult result, uint checksumAfter, IOBuffer checksumSerializationBuffer)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            _leasedChecksumSerializationBuffer = checksumSerializationBuffer;
            _leasedChecksum = checksumAfter;

            try
            {
                AfterTick(tick, result);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }

            _leasedChecksumSerializationBuffer = null;
            _leasedChecksum = null;
        }

        void ModelJournal<TModel>.Leader.IListener.BeforeAction(ModelAction action)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                BeforeAction(action);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Leader.IListener.AfterAction(ModelAction action, MetaActionResult result, uint checksumAfter, IOBuffer checksumSerializationBuffer)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            _leasedChecksumSerializationBuffer = checksumSerializationBuffer;
            _leasedChecksum = checksumAfter;

            try
            {
                AfterAction(action, result);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
            _leasedChecksumSerializationBuffer = null;
            _leasedChecksum = null;
        }

        void ModelJournal<TModel>.Leader.IListener.BeforeCommit()
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;
        }

        void ModelJournal<TModel>.Leader.IListener.AfterCommit()
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                AfterCommit();
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Leader.IListener.BeforeRollback()
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;
        }

        void ModelJournal<TModel>.Leader.IListener.AfterRollback()
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                AfterRollback();
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Leader.IListener.BeforeModifyHistory(IModel model)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                BeforeModifyHistory(model);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Leader.IListener.AfterModifyHistory(IModel model)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                AfterModifyHistory(model);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Leader.IListener.BeforeExecuteUnsynchronizedServerActionBlock(IModel model)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                BeforeExecuteUnsynchronizedServerActionBlock(model);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Leader.IListener.AfterExecuteUnsynchronizedServerActionBlock(IModel model)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                AfterExecuteUnsynchronizedServerActionBlock(model);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        #endregion

        #region ModelJournal<TModel>.Follower
        void ModelJournal<TModel>.Follower.IListener.OnAttach(ModelJournal<TModel>.Follower journal)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            _followerJournal = journal;
        }

        void ModelJournal<TModel>.Follower.IListener.AfterSetup(uint checksumAfter, IOBuffer checksumSerializationBuffer)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            _leasedChecksumSerializationBuffer = checksumSerializationBuffer;
            _leasedChecksum = checksumAfter;

            try
            {
                AfterSetup();
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }

            _leasedChecksumSerializationBuffer = null;
            _leasedChecksum = null;
        }

        void ModelJournal<TModel>.Follower.IListener.BeforeTick(int tick)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                BeforeTick(tick);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Follower.IListener.AfterTick(int tick, MetaActionResult result)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                AfterTick(tick, result);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Follower.IListener.BeforeAction(ModelAction action)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                BeforeAction(action);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Follower.IListener.AfterAction(ModelAction action, MetaActionResult result)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                AfterAction(action, result);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Follower.IListener.BeforeCommit()
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;
        }

        void ModelJournal<TModel>.Follower.IListener.AfterCommit()
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                AfterCommit();
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Follower.IListener.OnCommitCheckpointDrift(IModel stagedModel, uint stagedModelChecksum, IModel rerunModel, uint rerunModelChecksum)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                OnCommitCheckpointDrift(stagedModel, stagedModelChecksum, rerunModel, rerunModelChecksum);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Follower.IListener.BeforeRollback()
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;
        }

        void ModelJournal<TModel>.Follower.IListener.AfterRollback()
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                AfterRollback();
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Follower.IListener.BeforeModifyHistory(IModel model)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                BeforeModifyHistory(model);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Follower.IListener.AfterModifyHistory(IModel model)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                AfterModifyHistory(model);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Follower.IListener.BeforeExecuteUnsynchronizedServerActionBlock(IModel model)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                BeforeExecuteUnsynchronizedServerActionBlock(model);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }

        void ModelJournal<TModel>.Follower.IListener.AfterExecuteUnsynchronizedServerActionBlock(IModel model)
        {
            InvalidateCachedChecksum();
            if (IsPausedOrDisabled)
                return;

            try
            {
                AfterExecuteUnsynchronizedServerActionBlock(model);
            }
            catch (Exception ex)
            {
                Log.Warning("Unhandled exception from Journal Checker: {Error}", ex);
                DisableAfterError();
            }
        }
        #endregion
    };

    /// <summary>
    /// Checks the Model has no clonable state, i.e. such that cannot be round-tripped through serialization.
    /// </summary>
    public class JournalModelCloningChecker<TModel> : ModelJournalListenerBase<TModel>
        where TModel : class, IModel<TModel>
    {
        FlatIOBuffer _checkCloningBuffer;

        public JournalModelCloningChecker(LogChannel log) : base(log)
        {
        }

        /// <summary> Check that cloning using decode/encode produces original result </summary>
        void CheckCloning(IOBuffer checksumSerializationBuffer, string opName)
        {
            if (_checkCloningBuffer == null)
                _checkCloningBuffer = new FlatIOBuffer();

            var modelRT = ((TModel)StagedModel).GetRuntimeData();
            TModel clonedState = (TModel)JournalUtil.Deserialize<IModel>(checksumSerializationBuffer, MetaSerializationFlags.ComputeChecksum, StagedModel.GetDataResolver(), StagedModel.LogicVersion);
            modelRT.CopyResolversTo(clonedState);
            JournalUtil.ComputeChecksum(_checkCloningBuffer, clonedState);

            if (!IOBufferUtil.ContentsEqual(checksumSerializationBuffer, _checkCloningBuffer))
            {
                SerializedObjectComparer comparer = new SerializedObjectComparer();
                comparer.FirstName = "Original";
                comparer.SecondName = "Cloned";
                comparer.Type = typeof(IModel);
                string report = comparer.Compare(checksumSerializationBuffer.ToArray(), _checkCloningBuffer.ToArray()).Description;

                Log.Error("Failed {Op} clone consistency check for {Type}.\n{DiffReport}", opName, typeof(TModel).Name, report);

                DisableAfterError();
            }
        }

        protected override void AfterTick(int tick, MetaActionResult result)
        {
            CheckCloning(GetStagedModelChecksumSerializationBuffer(), Invariant($"{typeof(TModel).Name} tick {tick}"));
        }
        protected override void AfterAction(ModelAction action, MetaActionResult result)
        {
            CheckCloning(GetStagedModelChecksumSerializationBuffer(), action.GetType().ToGenericTypeString());
        }
    }

    /// <summary>
    /// Checks the Model is not modified outside Ticks() and Action.Executes().
    /// </summary>
    public class JournalModelOutsideModificationChecker<TModel> : ModelJournalListenerBase<TModel>
        where TModel : class, IModel<TModel>
    {
        FlatIOBuffer _outsideModificationBeforeBuffer;
        FlatIOBuffer _outsideModificationAfterBuffer;
        IGameConfigDataResolver _outsideModificationBeforeResolver;
        int _outsideModificationBeforeLogicVersion;

        public JournalModelOutsideModificationChecker(LogChannel log) : base(log)
        {
        }

        /// <summary> Check whether model has been changed after previous Tick/Action. </summary>
        void CheckOutsideModification()
        {
            // Previous buffer is invalidated. Skip this time.
            if (_outsideModificationBeforeBuffer.IsEmpty)
                return;

            if (_outsideModificationAfterBuffer == null)
                _outsideModificationAfterBuffer = new FlatIOBuffer();
            JournalUtil.ComputeChecksum(_outsideModificationAfterBuffer, StagedModel);

            if (!IOBufferUtil.ContentsEqual(_outsideModificationAfterBuffer, _outsideModificationBeforeBuffer))
            {
                SerializedObjectComparer comparer = new SerializedObjectComparer();
                comparer.FirstName = "Original";
                comparer.SecondName = "Modified";
                comparer.Type = typeof(IModel);
                string report = comparer.Compare(_outsideModificationBeforeBuffer.ToArray(), _outsideModificationAfterBuffer.ToArray()).Description;

                Log.Error("{ModelName} has been modified outside of Ticks/Actions!.\n{DiffReport}", typeof(TModel).Name, report);

                DisableAfterError();
            }
        }

        /// <summary> Update last outside-modification check </summary>
        void UpdateOutsideModification()
        {
            if (_outsideModificationBeforeBuffer == null)
                _outsideModificationBeforeBuffer = new FlatIOBuffer();

            _outsideModificationBeforeBuffer.Clear();
            IOBufferUtil.AppendTo(src: GetStagedModelChecksumSerializationBuffer(), dst: _outsideModificationBeforeBuffer);
            _outsideModificationBeforeResolver = StagedModel.GetDataResolver();
            _outsideModificationBeforeLogicVersion = StagedModel.LogicVersion;
        }

        protected override void BeforeTick(int tick)
        {
            CheckOutsideModification();
        }
        protected override void BeforeAction(ModelAction action)
        {
            CheckOutsideModification();
        }

        protected override void AfterSetup()
        {
            UpdateOutsideModification();
        }
        protected override void AfterTick(int tick, MetaActionResult result)
        {
            UpdateOutsideModification();
        }
        protected override void AfterAction(ModelAction action, MetaActionResult result)
        {
            UpdateOutsideModification();
        }
        protected override void AfterRollback()
        {
            UpdateOutsideModification();
        }

        protected override void OnUnpaused()
        {
            // Checker was (paused and) unpaused. This checker only works when it can
            // observe changes between two sequent operations. Skip next operation as
            // previously checked operation may not be the previous operation.
            _outsideModificationBeforeBuffer.Clear();
        }
    }

    /// <summary>
    /// Checks the Checksum of both Staged and Checkpoint model is correct.
    /// </summary>
    public class JournalModelChecksumChecker<TModel> : ModelJournalListenerBase<TModel>
        where TModel : class, IModel<TModel>
    {
        FlatIOBuffer _consistencyBuffer;

        public JournalModelChecksumChecker(LogChannel log) : base(log)
        {
        }

        FlatIOBuffer GetBuffer()
        {
            if (_consistencyBuffer == null)
                _consistencyBuffer = new FlatIOBuffer();
            return _consistencyBuffer;
        }

        void CheckCheckpoint(string eventName)
        {
            // If no checksum is available, nothing can be done.
            if (CheckpointChecksumInJournal == 0)
                return;

            uint actualChecksum = JournalUtil.ComputeChecksum(GetBuffer(), CheckpointModel);
            if (actualChecksum == CheckpointChecksumInJournal)
                return;

            Log.Error("{Event}. {TypeName} Journal checksum is not correct: Checksum of Journal.CheckpointModel is recorded as {Claimed}, but recomputed checksum is {Actual}. This is most likely caused by a modification to the model outside a tick or action.", eventName, typeof(TModel).Name, CheckpointChecksumInJournal, actualChecksum);
            DisableAfterError();
        }

        void CheckStagedModel(string eventName)
        {
            // If no checksum is available, nothing can be done.
            uint stagedModelChecksum = GetStagedModelChecksum(computeIfNotSuppliedByJournal: false);
            if (stagedModelChecksum == 0)
                return;

            uint actualChecksum = JournalUtil.ComputeChecksum(GetBuffer(), StagedModel);
            if (actualChecksum == stagedModelChecksum)
                return;

            Log.Error("{Event}. {TypeName} Journal checksum is not correct: Checksum of Journal.StagedModel is recorded as {Claimed}, but recomputed checksum is {Actual}. This is most likely caused by a modification to the model outside a tick or action.", eventName, typeof(TModel).Name, StagedChecksumInJournal, actualChecksum);
            DisableAfterError();
        }

        protected override void AfterSetup()
        {
            CheckCheckpoint("AfterSetup()");
            if (IsLeaderJournal)
                CheckStagedModel("AfterSetup()");
        }
        protected override void AfterTick(int tick, MetaActionResult result)
        {
            if (IsLeaderJournal)
                CheckStagedModel(Invariant($"AfterTick({tick})"));
        }
        protected override void AfterAction(ModelAction action, MetaActionResult result)
        {
            if (IsLeaderJournal)
                CheckStagedModel($"AfterAction({action.GetType().Name})");
        }
        protected override void AfterCommit()
        {
            CheckCheckpoint("AfterCommit()");
        }
        protected override void AfterRollback()
        {
            if (IsLeaderJournal)
                CheckStagedModel("AfterRollback()");
        }
    }

    /// <summary>
    /// Checks the Tick and Actions produce identical changes to a model if run again. In re-run, the model
    /// is a copy of the original model before the initial operation was run. This essentially checks the
    /// Tick and Actions are deterministic and have no State.
    /// </summary>
    public class JournalModelRerunChecker<TModel> : ModelJournalListenerBase<TModel>
        where TModel : class, IModel<TModel>
    {
        FlatIOBuffer _beforeBuffer;
        FlatIOBuffer _afterBuffer;
        IGameConfigDataResolver _beforeResolver;
        int _beforeLogicVersion;
        IModelRuntimeData<TModel> _beforeRT;

        public JournalModelRerunChecker(LogChannel log) : base(log)
        {
        }

        /// <summary> Check whether model has been changed after previous Tick/Action. </summary>
        void CheckRerun(Action<TModel> action, Func<string> getOpName)
        {
            TModel rerunStateAll = (TModel)JournalUtil.Deserialize<IModel>(_beforeBuffer, MetaSerializationFlags.IncludeAll, _beforeResolver, _beforeLogicVersion);
            _beforeRT.CopyResolversTo(rerunStateAll);

            // Detect dependency from time by re-running near at totally different local time. We can only do this
            // on unity client. On server or botclient, this would cause races.
            //
            // Test various invalid times to detect different use cases. Exhaustive testing is expensive so test
            // a single strategy a time.
            #if UNITY_EDITOR
            MetaDuration originalSkip = MetaTime.DebugTimeOffset;
            switch (RandomPCG.CreateNew().NextInt(4))
            {
                case 0: MetaTime.DebugTimeOffset = -MetaDuration.FromMilliseconds(MetaTime.FromDateTime(DateTime.UtcNow).MillisecondsSinceEpoch); break;
                case 1: MetaTime.DebugTimeOffset = MetaDuration.FromDays(20_000); break;
                case 2: MetaTime.DebugTimeOffset = -MetaDuration.FromSeconds(2); break;
                case 3: MetaTime.DebugTimeOffset = MetaDuration.FromSeconds(2); break;
            }
            #endif

            try
            {
                action(rerunStateAll);
            }
            finally
            {
                #if UNITY_EDITOR
                MetaTime.DebugTimeOffset = originalSkip;
                #endif
            }

            if (_afterBuffer == null)
                _afterBuffer = new FlatIOBuffer();
            JournalUtil.ComputeChecksum(_afterBuffer, rerunStateAll);

            if (!IOBufferUtil.ContentsEqual(GetStagedModelChecksumSerializationBuffer(), _afterBuffer))
            {
                SerializedObjectComparer comparer = new SerializedObjectComparer();
                comparer.FirstName = "Original";
                comparer.SecondName = "Re-execution";
                comparer.Type = typeof(IModel);
                string report = comparer.Compare(GetStagedModelChecksumSerializationBuffer().ToArray(), _afterBuffer.ToArray()).Description;

                Log.Error("Re-executing {ModelName} operation {OpName} did not produce deterministic results!.\n{DiffReport}", typeof(TModel).Name, getOpName(), report);

                DisableAfterError();
            }
        }

        void CopyState()
        {
            if (_beforeBuffer == null)
                _beforeBuffer = new FlatIOBuffer();

            _beforeBuffer.Clear();
            JournalUtil.Serialize(_beforeBuffer, StagedModel, MetaSerializationFlags.IncludeAll, StagedModel.LogicVersion);
            _beforeResolver = StagedModel.GetDataResolver();
            _beforeRT = ((TModel)StagedModel).GetRuntimeData();
            _beforeLogicVersion = StagedModel.LogicVersion;
        }

        protected override void BeforeTick(int tick)
        {
            CopyState();
        }
        protected override void BeforeAction(ModelAction action)
        {
            CopyState();
        }
        protected override void AfterTick(int tick, MetaActionResult result)
        {
            CheckRerun(model => ModelUtil.RunTick(model, NullChecksumEvaluator.Context), () => Invariant($"{typeof(TModel).Name} tick {tick}"));
        }
        protected override void AfterAction(ModelAction action, MetaActionResult result)
        {
            CheckRerun(model => ModelUtil.RunAction(model, action, NullChecksumEvaluator.Context), () => $"{action.GetType().Name}: {PrettyPrint.Compact(action)}");
        }
    }

    /// <summary>
    /// Warns if re-execution during commit does not produce pre-determined results. This suggests there is
    /// state leak, such as an class instance shared over multiple models or actions.
    /// Unlike <see cref="JournalModelRerunChecker"/>, this is run after commit and cannot determine the cause.
    /// </summary>
    public class JournalModelCommitChecker<TModel> : ModelJournalListenerBase<TModel>
        where TModel : class, IModel<TModel>
    {
        public JournalModelCommitChecker(LogChannel log) : base(log)
        {
        }

        protected override void OnCommitCheckpointDrift(IModel stagedModel, uint stagedModelChecksum, IModel rerunModel, uint rerunModelChecksum)
        {
            string diffReport;

            using (FlatIOBuffer stagedBuffer = new FlatIOBuffer())
            using (FlatIOBuffer rerunBuffer = new FlatIOBuffer())
            {
                _ = JournalUtil.ComputeChecksum(stagedBuffer, stagedModel);
                _ = JournalUtil.ComputeChecksum(rerunBuffer, rerunModel);

                SerializedObjectComparer comparer = new SerializedObjectComparer();
                comparer.FirstName = "First execution Model";
                comparer.SecondName = "Deterministic re-execution Model";
                comparer.Type = typeof(IModel);
                diffReport = comparer.Compare(stagedBuffer.ToArray(), rerunBuffer.ToArray()).Description;
            }

            Log.Error("Model on journal does not match a re-executed model. "
                + "The execution is either nondeterministic or model was modified outside a tick or action. "
                + "Checksum {StageChecksum} vs {RerunChecksum}."
                + "\n{DiffReport}", stagedModelChecksum, rerunModelChecksum, diffReport);

            DisableAfterError();
        }
    }

    /// <summary>
    /// Checks any serializeable fields in Actions do not change during execution, i.e. they are immutable from the serialization perspective.
    /// </summary>
    public class JournalModelActionImmutabilityChecker<TModel> : ModelJournalListenerBase<TModel>
        where TModel : class, IModel<TModel>
    {
        FlatIOBuffer _beforeBuffer;
        FlatIOBuffer _afterBuffer;

        public JournalModelActionImmutabilityChecker(LogChannel log) : base(log)
        {
        }

        protected override void BeforeAction(ModelAction action)
        {
            if (_beforeBuffer == null)
                _beforeBuffer = new FlatIOBuffer();

            JournalUtil.Serialize(_beforeBuffer, action, MetaSerializationFlags.IncludeAll, StagedModel.LogicVersion);
        }

        protected override void AfterAction(ModelAction action, MetaActionResult result)
        {
            if (_afterBuffer == null)
                _afterBuffer = new FlatIOBuffer();

            int logicVersion = StagedModel.LogicVersion;
            JournalUtil.Serialize(_afterBuffer, action, MetaSerializationFlags.IncludeAll, logicVersion);

            if (!IOBufferUtil.ContentsEqual(_afterBuffer, _beforeBuffer))
            {
                SerializedObjectComparer comparer = new SerializedObjectComparer();
                comparer.FirstName = "Before execution";
                comparer.SecondName = "After execution";
                comparer.Type = typeof(ModelAction);
                string report = comparer.Compare(_beforeBuffer.ToArray(), _afterBuffer.ToArray()).Description;

                Log.Error(
                    "Action {Action} mutated internal state during execution. Internal mutation of Actions is not allowed. "
                    + "Actions must be pure in order to ensure deterministic execution. "
                    + "If the action uses temporary fields to pass data around, consider marking them with [IgnoreDataMember]."
                    + "\n{DiffReport}", action.GetType().Name, report);

                DisableAfterError();
            }
        }
    }

    /// <summary>
    /// Checks ModifyHistory calls do not modify final checksum.
    /// </summary>
    public class JournalModelModifyHistoryChecker<TModel> : ModelJournalListenerBase<TModel>
        where TModel : class, IModel<TModel>
    {
        FlatIOBuffer _beforeBuffer;
        FlatIOBuffer _afterBuffer;
        IGameConfigDataResolver _beforeResolver;
        int _beforeLogicVersion;

        public JournalModelModifyHistoryChecker(LogChannel log) : base(log)
        {
        }

        protected override void BeforeModifyHistory(IModel model)
        {
            Before(model);
        }

        protected override void BeforeExecuteUnsynchronizedServerActionBlock(IModel model)
        {
            Before(model);
        }

        protected override void AfterModifyHistory(IModel model)
        {
            After(model, callsite: "ModifyHistory");
        }

        protected override void AfterExecuteUnsynchronizedServerActionBlock(IModel model)
        {
            After(model, callsite: "ExecuteServerActionBlock");
        }

        void Before(IModel model)
        {
            if (_beforeBuffer == null)
                _beforeBuffer = new FlatIOBuffer();

            _beforeResolver = model.GetDataResolver();
            _beforeLogicVersion = model.LogicVersion;
            JournalUtil.Serialize(_beforeBuffer, model, MetaSerializationFlags.ComputeChecksum, _beforeLogicVersion);
        }

        void After(IModel model, string callsite)
        {
            if (_afterBuffer == null)
                _afterBuffer = new FlatIOBuffer();

            int afterLogicVersion = StagedModel.LogicVersion;
            JournalUtil.Serialize(_afterBuffer, model, MetaSerializationFlags.ComputeChecksum, afterLogicVersion);

            if (!IOBufferUtil.ContentsEqual(_afterBuffer, _beforeBuffer))
            {
                SerializedObjectComparer comparer = new SerializedObjectComparer();
                comparer.FirstName = "Before";
                comparer.SecondName = "After";
                comparer.Type = typeof(IModel);
                string report = comparer.Compare(_beforeBuffer.ToArray(), _afterBuffer.ToArray()).Description;

                Log.Error("Illegal state modification detected in {Callsite}. Operation may only modify [NoChecksum] fields.\n{DiffReport}", callsite, report);

                DisableAfterError();
            }
        }
    }

    /// <summary>
    /// Logs warnings for actions that result in other than <see cref="MetaActionResult.Success"/>.
    /// </summary>
    public class FailingActionWarningListener<TModel> : ModelJournalListenerBase<TModel>
        where TModel : class, IModel<TModel>
    {
        public FailingActionWarningListener(LogChannel log) : base(log)
        {
        }

        protected override void AfterTick(int tick, MetaActionResult result)
        {
            if (result != MetaActionResult.Success)
                Log.Warning("Failed to execute tick {Tick}: {Result}", tick, result.ToString());
        }

        protected override void AfterAction(ModelAction action, MetaActionResult result)
        {
            if (result != MetaActionResult.Success)
                Log.Warning("Failed to execute action {Action}: {Result}", PrettyPrint.Compact(action), result.ToString());
        }
    }
}
