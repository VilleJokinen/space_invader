// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model.JournalCheckers;
using System;
using static System.FormattableString;

namespace Metaplay.Core.Model
{
    public interface ITimelineHistory : IDisposable
    {
        void AddEntry<TModel>(string name, int tick, ModelAction action, TModel afterState) where TModel : class, IModel;
    }

    public class TimelineHistoryListener<TModel> : ModelJournalListenerBase<TModel>
        where TModel : class, IModel<TModel>
    {
        ITimelineHistory    _timelineHistory;

        public TimelineHistoryListener(LogChannel log, ITimelineHistory timelineHistory) : base(log)
        {
            _timelineHistory = timelineHistory;
        }

        void SetupTimelineHistory()
        {
#if UNITY_EDITOR
            _timelineHistory.AddEntry($"{StagedModel.GetType().Name} init", tick: StagedPosition.Tick, action: null, (TModel)StagedModel);
#endif
        }

        /// <summary> Add operation to history buffer (if present) </summary>
        void UpdateTimelineHistory(string name, ModelAction action)
        {
#if UNITY_EDITOR
            _timelineHistory.AddEntry(name, StagedPosition.Tick, action, (TModel)StagedModel);
#endif
        }

        protected override void AfterSetup()
        {
            SetupTimelineHistory();
        }
        protected override void AfterTick(int tick, MetaActionResult result)
        {
            UpdateTimelineHistory(Invariant($"{StagedModel.GetType().Name} tick {tick}"), null);
        }
        protected override void AfterAction(ModelAction action, MetaActionResult result)
        {
            UpdateTimelineHistory(action.GetType().ToGenericTypeString(), action);
        }
    }
}
