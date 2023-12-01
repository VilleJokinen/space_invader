// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;

namespace Metaplay.Server
{
    public interface IPlayerFilter
    {
        PlayerFilterCriteria PlayerFilter { get; }
    }

    public static class PlayerFilterExtensions
    {
        public static bool PassesFilter(this IPlayerModelBase playerModel, PlayerFilterCriteria filter, out bool evalError)
        {
            evalError = false;

            if (filter.IsEmpty)
                return true;

            // Check if targeted by player Id
            if (filter.PlayersToInclude != null && filter.PlayersToInclude.Contains(playerModel.PlayerId))
                return true;

            // Check if targeted by condition
            if (filter.Condition != null)
            {
                try
                {
                    if (filter.Condition.MatchesPlayer(playerModel))
                        return true;
                }
                catch(Exception)
                {
                    // Exception from player condition evaluation code: default to no match and report error
                    evalError = true;
                    return false;
                }
            }


            return false;
        }
    }
}
