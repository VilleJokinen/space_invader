// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using static System.FormattableString;

namespace Metaplay.Server.Matchmaking
{
    public interface IAsyncMatchmakerPlayerModel
    {
        public EntityId PlayerId { get; }
        public int DefenseMmr { get; }

        /// <summary>
        /// A short summary of this player to show in the dashboard.
        /// </summary>
        string GetDashboardSummary() => Invariant($"MMR: {DefenseMmr}");
    }
}
