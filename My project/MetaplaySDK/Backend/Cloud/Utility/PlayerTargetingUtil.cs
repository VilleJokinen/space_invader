// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Player;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Cloud.Utility
{
    /// <summary>
    /// Utilities relating to player targeting, by segmentation and otherwise.
    /// </summary>
    public static class PlayerTargetingUtil
    {
        /// <summary>
        /// Estimate the player audience size for the targeting consisting of
        /// <paramref name="includeTargetPlayers"/> and <paramref name="includeTargetSegments"/>.
        /// Each targeting parameter can be null, in which case that targeting
        /// is not included.
        /// As a special case, if all targeting parameters are null, then
        /// all players are targeted.
        /// </summary>
        /// <param name="totalPlayerCount">Total player count, upon which to base the segmentation audience size estimates</param>
        /// <param name="includeTargetPlayers">Explicit list of players to target, or null</param>
        /// <param name="includeTargetSegments">List of player segments to target, or null</param>
        /// <param name="playerSegmentSizeEstimates">Player segment size estimates, or null if not available</param>
        /// <returns>Estimated size of targeted audience, or null if not enough information is available to estimate</returns>
        public static long? TryEstimateAudienceSize(int totalPlayerCount, List<EntityId> includeTargetPlayers, List<PlayerSegmentId> includeTargetSegments, Dictionary<PlayerSegmentId, float> playerSegmentSizeEstimates)
        {
            if (includeTargetPlayers != null || includeTargetSegments != null)
            {
                // At least some targeting is specified - try to estimate.

                long numTargetPlayers = includeTargetPlayers?.Count ?? 0;

                if (includeTargetSegments != null)
                {
                    if (playerSegmentSizeEstimates != null)
                    {
                        // Estimate segmentation audience, and add to number of explicitly targeted players (if any).
                        long segmentationAudienceSizeEstimate = includeTargetSegments.Sum(segmentId => TryEstimateSegmentAudienceSize(totalPlayerCount, segmentId, playerSegmentSizeEstimates) ?? 0);
                        return numTargetPlayers + segmentationAudienceSizeEstimate;
                    }
                    else
                    {
                        // No segment size estimates available - cannot estimate total audience size.
                        return null;
                    }
                }
                else
                {
                    // No segment targeting - targets exactly the explicitly-targeted players.
                    return numTargetPlayers;
                }
            }
            else
            {
                // No targeting - targets all players.
                return totalPlayerCount;
            }
        }

        public static long? TryEstimateAudienceSize(int totalPlayerCount, PlayerFilterCriteria Filter, Dictionary<PlayerSegmentId, float> playerSegmentSizeEstimates)
        {
            List<PlayerSegmentId> segmentAnyList = null;

            if (Filter.Condition is PlayerSegmentBasicCondition basicCondition)
            {
                if (basicCondition.PropertyRequirements != null)
                {
                    // Can't handle property requirements
                    return null;
                }
                else if (basicCondition.RequireAllSegments != null)
                {
                    if (basicCondition.RequireAnySegment != null || basicCondition.RequireAllSegments.Count > 1)
                        return null;
                    segmentAnyList = basicCondition.RequireAllSegments;
                }
                else if (basicCondition.RequireAnySegment != null)
                {
                    segmentAnyList = basicCondition.RequireAnySegment;
                }
            }
            else if (Filter.Condition != null)
            {
                // Can't handle other conditions
                return null;
            }
            return TryEstimateAudienceSize(totalPlayerCount, Filter.PlayersToInclude, segmentAnyList, playerSegmentSizeEstimates);
        }


        public static long? TryEstimateSegmentAudienceSize(int totalPlayerCount, PlayerSegmentId segmentId, Dictionary<PlayerSegmentId, float> playerSegmentSizeEstimates)
        {
            if (playerSegmentSizeEstimates != null && playerSegmentSizeEstimates.TryGetValue(segmentId, out float ratio))
                return (long)(ratio * totalPlayerCount);
            else
                return null;
        }
    }
}
