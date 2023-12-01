// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using static System.FormattableString;

namespace Metaplay.Core.Player
{
    [MetaSerializableDerived(100)]
    public class PlayerPropertyPlayerbaseSubsetNumber : TypedPlayerPropertyId<int>
    {
        [MetaMember(1)] public int  NumSubsets  = 1;
        [MetaMember(2)] public uint Modifier    = 0;

        PlayerPropertyPlayerbaseSubsetNumber(){ }
        public PlayerPropertyPlayerbaseSubsetNumber(int numSubsets, uint modifier)
        {
            if (numSubsets < 1)
                throw new ArgumentOutOfRangeException(nameof(numSubsets), numSubsets, "Must have at least 1 playerbase subset");

            NumSubsets = numSubsets;
            Modifier = modifier;
        }

        public override string DisplayName => Invariant($"Playerbase subset number out of total {NumSubsets} (with hash modifier {Modifier})");

        public override int GetTypedValueForPlayer(IPlayerModelBase player)
        {
            return GetSubsetNumberForPlayerId(player.PlayerId);
        }

        int GetSubsetNumberForPlayerId(EntityId playerId)
        {
            ulong   playerIdValue   = playerId.Value;
            uint    hashId          = (uint)(playerIdValue >> 32) ^ (uint)(playerIdValue & 0xffff_ffff);
            uint    hash            = Hash(key: Modifier, id: hashId);
            int     subsetIndex     = (int)(hash % (uint)NumSubsets);
            int     subsetNumber    = subsetIndex + 1;

            return subsetNumber;
        }

        static uint Hash(uint key, uint id)
        {
            // Modified copypaste from KeyedStableWeightedCoinflip
            uint k;
            k = key ^ (id * 2654435761u);
            k ^= k >> 16;
            k *= 0x5bd1e995;
            k ^= k >> 16;
            k *= 0x5bd1e995;
            return k;
        }
    }
}
