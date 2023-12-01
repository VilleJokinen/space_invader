// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Client
{
    [MetaSerializable]
    public class ClientSlotCore : ClientSlot
    {
        public static readonly ClientSlot Player            = new ClientSlotCore(1, nameof(Player));
        public static readonly ClientSlot Guild             = new ClientSlotCore(2, nameof(Guild));
        public static readonly ClientSlot Nft               = new ClientSlotCore(3, nameof(Nft));
        public static readonly ClientSlot PlayerDivision    = new ClientSlotCore(4, nameof(PlayerDivision));
        public static readonly ClientSlot GuildDivision     = new ClientSlotCore(5, nameof(GuildDivision));

        public ClientSlotCore(int id, string name) : base(id, name) { }
    }
}
