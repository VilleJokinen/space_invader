// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Model.JournalCheckers;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using System.Collections.Generic;

namespace Metaplay.Core.Client
{
    public interface IClientPlayerModelJournal
    {
        IPlayerModelBase    StagedModel         { get; }
        IPlayerModelBase    CheckpointModel     { get; }
        JournalPosition     StagedPosition      { get; }

        void UpdateSharedGameConfig(ISharedGameConfig gameConfig);
    }

    public class ClientPlayerModelJournal : ModelJournal<IPlayerModelBase>.Leader, IClientPlayerModelJournal
    {
        IPlayerModelBase IClientPlayerModelJournal.StagedModel => StagedModel;
        IPlayerModelBase IClientPlayerModelJournal.CheckpointModel => CheckpointModel;
        public List<ModelJournalListenerBase<IPlayerModelBase>> DebugCheckers = new List<ModelJournalListenerBase<IPlayerModelBase>>();

        public ClientPlayerModelJournal(
            LogChannel log,
            IPlayerModelBase model,
            int currentOperation,
            ITimelineHistory timelineHistory,
            bool enableConsistencyChecks,
            bool computeChecksums)
            : base(
                log,
                enableConsistencyChecks: enableConsistencyChecks,
                computeChecksums: computeChecksums)
        {
            if (timelineHistory != null)
                AddListener(new TimelineHistoryListener<IPlayerModelBase>(log, timelineHistory));

            if (enableConsistencyChecks)
            {
                AddDebugListener(new JournalModelOutsideModificationChecker<IPlayerModelBase>(log));
                AddDebugListener(new JournalModelCloningChecker<IPlayerModelBase>(log));
                AddDebugListener(new JournalModelChecksumChecker<IPlayerModelBase>(log));
                AddDebugListener(new JournalModelRerunChecker<IPlayerModelBase>(log));
                AddDebugListener(new JournalModelActionImmutabilityChecker<IPlayerModelBase>(log));
                AddDebugListener(new JournalModelModifyHistoryChecker<IPlayerModelBase>(log));
            }

            AddListener(new FailingActionWarningListener<IPlayerModelBase>(log));

            JournalPosition currentPosition = JournalPosition.FromTickOperationStep(tick: model.CurrentTick, operation: currentOperation, 0);
            Setup(model, currentPosition);
        }

        protected void AddDebugListener(ModelJournalListenerBase<IPlayerModelBase> listener)
        {
            AddListener(listener);
            DebugCheckers.Add(listener);
        }

        /// <summary>
        /// Update the active PlayerModel.SharedGameConfig to a new version. The references
        /// to GameConfigs within the PlayerModel are updated by cloning the model using the
        /// new SharedGameConfig as the resolver.
        /// </summary>
        /// <param name="newGameConfig">The new ShareGameConfig to switch to</param>
        void IClientPlayerModelJournal.UpdateSharedGameConfig(ISharedGameConfig newGameConfig)
        {
            ModifyHistory((JournalPosition position, IPlayerModelBase oldModel) =>
            {
                IPlayerModelBase newModel = MetaSerializationUtil.CloneModel(oldModel, resolver: newGameConfig);

                // Copy old resolvers, except overwrite GameConfig with new one
                IModelRuntimeData<IPlayerModelBase> oldRT = oldModel.GetRuntimeData();
                oldRT.CopyResolversTo(newModel);
                newModel.GameConfig = newGameConfig;

                return newModel;
            });
        }
    }
}
