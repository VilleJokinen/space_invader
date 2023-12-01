// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Forms;
using Metaplay.Core.Model;

namespace Metaplay.Server.Matchmaking
{
    public interface IAsyncMatchmakerQuery
    {
        public EntityId AttackerId { get; }
        public int      AttackMmr  { get; }
    }

    [MetaSerializable]
    [MetaReservedMembers(100, 200)]
    public abstract class AsyncMatchmakerQueryBase : IAsyncMatchmakerQuery
    {
        [MetaFormNotEditable]
        [MetaMember(101)] public EntityId AttackerId { get; set; }
        [MetaMember(102)] public int      AttackMmr  { get; set; }

        protected AsyncMatchmakerQueryBase() { }

        protected AsyncMatchmakerQueryBase(EntityId attackerId, int attackMmr)
        {
            AttackerId = attackerId;
            AttackMmr  = attackMmr;
        }
    }
}
