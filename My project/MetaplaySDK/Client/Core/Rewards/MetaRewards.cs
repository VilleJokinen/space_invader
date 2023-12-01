// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.InAppPurchase;
using Metaplay.Core.InGameMail;
using Metaplay.Core.Model;
using Metaplay.Core.League;

namespace Metaplay.Core.Rewards
{
    /// <summary>
    /// Base class for declaring things that can be rewarded to game entities.
    /// </summary>
    [MetaSerializable]
    public abstract class MetaReward
    {
    }

    /// <summary>
    /// Information about the source of a reward, for example for analytics purposes.
    /// </summary>
    public interface IRewardSource { }

    /// <summary>
    /// Integration entry point for extracting <see cref="IRewardSource"/> information from reward consume context.
    /// </summary>
    public class MetaRewardSourceProvider : IMetaIntegrationSingleton<MetaRewardSourceProvider>
    {
        /// <summary>
        /// Extracts custom <see cref="IRewardSource"/> source data for rewards delivered in Mails, or returns
        /// <c>null</c> if no data is extracted.
        /// </summary>
        public virtual IRewardSource DeclareMailRewardSource(MetaInGameMail mail) { return null; }

        /// <summary>
        /// Extracts custom <see cref="IRewardSource"/> source data for rewards that are part of In App Purchases, or returns
        /// <c>null</c> if no data is extracted.
        /// </summary>
        public virtual IRewardSource DeclareInAppRewardSource(InAppPurchaseEvent iap) { return null; }

        /// <summary>
        /// Extracts custom <see cref="IRewardSource"/> source data for rewards delivered in <see cref="IDivisionRewards"/>,
        /// i.e. competition rewards for playing in Leagues.
        /// </summary>
        public virtual IRewardSource DeclareLeagueRewardSource(IDivisionRewards divisionReward) { return null; }
    }
}
