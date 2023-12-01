// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;

namespace Metaplay.Core.Web3
{
    /// <summary>
    /// State related to a player's NFTs.
    /// </summary>
    [MetaSerializable]
    public class PlayerNftSubModel
    {
        /// <summary>
        /// A replica of the set of NFTs (which are known to the game) owned by the player.
        ///
        /// User code should treat this as read-only: modifications to NFTs should instead
        /// be done by defining a <see cref="PlayerNftTransaction"/> and invoking it with
        /// <see cref="NftClient.ExecuteNftTransaction"/> (typically via MetaplayClient.NftClient).
        /// </summary>
        [MetaMember(1)] public OrderedDictionary<NftKey, MetaNft> OwnedNfts { get; set; } = new OrderedDictionary<NftKey, MetaNft>();

        /// <summary>
        /// SDK-internal, not intended to be accessed directly by user code.
        /// State held during an ongoing transaction.
        /// </summary>
        [MetaMember(2)] public PlayerNftTransactionState TransactionState { get; set; } = null;
    }

    [MetaSerializable]
    public class PlayerNftTransactionState
    {
        [MetaMember(1)] public OrderedDictionary<NftKey, MetaNft>   Nfts        { get; set; }
        [MetaMember(2)] public PlayerNftTransaction.ContextBase     UserContext { get; set; }
    }
}
