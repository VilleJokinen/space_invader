// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using System.Collections.Generic;

namespace Metaplay.Core.League
{
    /// <summary>
    /// The container of Division rewards granted to a Player when season is concluded.
    ///
    /// You may inherit this class for custom reward types, or use <see cref="Default"/>
    /// for the default implementation based on <see cref="MetaPlayerRewardBase"/>s.
    /// </summary>
    [MetaReservedMembers(100, 200)]
    public abstract class DivisionPlayerRewardsBase : IDivisionRewards
    {
        [MetaMember(100)] public bool              IsClaimed { get; set; }
        IEnumerable<MetaReward> IDivisionRewards.  Rewards   => Rewards;
        public abstract List<MetaPlayerRewardBase> Rewards   { get; set; }

        void IDivisionRewards.Apply(IModel model) => Apply((IPlayerModelBase)model);

        public virtual void Apply(IPlayerModelBase playerModel)
        {
            MetaRewardSourceProvider rewardSourceProvider = IntegrationRegistry.Get<MetaRewardSourceProvider>();
            IRewardSource source = rewardSourceProvider.DeclareLeagueRewardSource(this);
            foreach (MetaPlayerRewardBase reward in Rewards)
                reward.InvokeConsume(playerModel, source);
        }

        /// <summary>
        /// Default convenience implementation of <see cref="DivisionPlayerRewardsBase"/> that grants
        /// player <see cref="MetaPlayerRewardBase"/>s.
        /// </summary>
        [MetaSerializableDerived(100)]
        public class Default : DivisionPlayerRewardsBase
        {
            [MetaMember(1)] public override sealed List<MetaPlayerRewardBase> Rewards { get; set; }

            Default() { }
            public Default(List<MetaPlayerRewardBase> rewards)
            {
                Rewards = rewards;
            }
        }
    }
}
