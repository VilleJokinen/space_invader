// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.Model;

namespace Metaplay.Server.League
{
    [MetaReservedMembers(100, 200)]
    public abstract class DivisionServerModelBase<TModel> : IDivisionServerModel
        where TModel: IDivisionModel<TModel>
    {
        /// <inheritdoc />
        [MetaMember(101)]
        public OrderedDictionary<int, EntityId> ParticipantIndexToEntityId { get; set; } = new OrderedDictionary<int, EntityId>();

        /// <inheritdoc />
        void IDivisionServerModel.OnModelServerTick(IDivisionModel readOnlyModel, IServerActionDispatcher actionDispatcher)
            => OnModelServerTick((TModel)readOnlyModel, actionDispatcher);

        /// <inheritdoc />
        void IDivisionServerModel.OnFastForwardModel(IDivisionModel model, MetaDuration elapsedTime)
            => OnFastForwardModel((TModel)model, elapsedTime);

        /// <inheritdoc cref="IDivisionServerModel.OnModelServerTick" />
        public abstract void OnModelServerTick(TModel readOnlyModel, IServerActionDispatcher actionDispatcher);

        /// <inheritdoc cref="IDivisionServerModel.OnFastForwardModel" />
        public abstract void OnFastForwardModel(TModel model, MetaDuration elapsedTime);
    }

    [MetaSerializableDerived(101)]
    [LeaguesEnabledCondition]
    public class DefaultDivisionServerModel : IDivisionServerModel
    {
        /// <inheritdoc />
        [MetaMember(101)]
        public OrderedDictionary<int, EntityId> ParticipantIndexToEntityId { get; set; } = new OrderedDictionary<int, EntityId>();

        /// <inheritdoc />
        public void OnModelServerTick(IDivisionModel readOnlyModel, IServerActionDispatcher actionDispatcher) { }

        /// <inheritdoc />
        public void OnFastForwardModel(IDivisionModel model, MetaDuration elapsedTime) { }
    }
}
