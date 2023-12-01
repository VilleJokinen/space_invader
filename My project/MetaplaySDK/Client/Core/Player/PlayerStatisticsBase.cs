// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Player
{
    /// <summary>
    /// Base class for statistics related to a player.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(1, 5), MetaReservedMembers(6, 100)] // \note [1, 100) but skip 5 for compatibility reasons
    public abstract class PlayerStatisticsBase
    {
        [MetaMember(1)] public MetaTime     CreatedAt               { get; set; }   // Creation time of the player.
        [MetaMember(6)] public string       InitialClientVersion    { get; set; }   // Game client version (Application.version) on player's first login. \note Null (unknown) if player was created before this field was added here.
        [MetaMember(2)] public MetaTime     LastLoginAt             { get; set; }   // Time of latest successful login.
        [MetaMember(3)] public int          TotalLogins             { get; set; }   // Number of times the player has logged in.
        [MetaMember(4)] public int          TotalDesyncs            { get; set; }   // Total number of Desyncs happened in the PlayerModel.
        // \note [MetaMember(5)] skipped for compatibility
        [MetaMember(7)] public MetaTime     LastImportAt            { get; set; }   // Time of the last (admin) import, used for invalidating caches when player state is forcefully reset

        public void InitializeForNewPlayer(MetaTime createTime)
        {
            CreatedAt       = createTime;
            LastLoginAt     = createTime;
            TotalLogins     = 0;
            LastImportAt    = MetaTime.Epoch;
        }
    }

    /// <summary>
    /// Convenience <see cref="PlayerStatisticsBase"/> implementation for when you don't need to add any game-specific members.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class PlayerStatisticsCore : PlayerStatisticsBase
    {
    }
}
