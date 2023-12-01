// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.League
{
    [MetaReservedMembers(100, 200)]
    [LeaguesEnabledCondition]
    public abstract class PlayerDivisionHistoryEntryBase : IDivisionHistoryEntry
    {
        [MetaMember(100)] public EntityId         DivisionId    { get; protected set; }
        [MetaMember(101)] public DivisionIndex    DivisionIndex { get; protected set; }
        [MetaMember(102)] public IDivisionRewards Rewards       { get; protected set; }

        protected PlayerDivisionHistoryEntryBase(EntityId divisionId, DivisionIndex divisionIndex, IDivisionRewards rewards)
        {
            DivisionId    = divisionId;
            DivisionIndex = divisionIndex;
            Rewards       = rewards;
        }
    }
}
