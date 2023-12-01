// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Rewards;
using System.Collections.Generic;

namespace Metaplay.Core.League
{
    /// <summary>
    /// The container of Division rewards granted to a participant when season is concluded.
    /// </summary>
    [MetaSerializable]
    public interface IDivisionRewards
    {
        bool                    IsClaimed { get; set; }
        IEnumerable<MetaReward> Rewards   { get; }

        /// <summary>
        /// Applies the Division reward to the participant.
        /// </summary>
        void Apply(IModel model);
    }
}
