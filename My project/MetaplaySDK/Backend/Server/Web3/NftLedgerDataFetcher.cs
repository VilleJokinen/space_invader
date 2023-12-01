// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Web3;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Web3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server.Web3
{
    /// <summary>
    /// Helper class for querying data from an NFT ledger (e.g. Immutable X)
    /// such that the caller doesn't need to care about the different ledgers.
    /// </summary>
    public class NftLedgerDataFetcher
    {
        IMetaLogger _log;

        public NftLedgerDataFetcher(IMetaLogger log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public async Task<NftCollectionLedgerInfo> TryGetCollectionLedgerInfoAsync(Web3Options web3Options, NftCollectionId collectionId)
        {
            Web3Options.NftCollectionSpec collection = web3Options.GetNftCollection(collectionId);

            switch (collection.Ledger)
            {
                case Web3Options.NftCollectionLedger.LocalTesting:
                    return null;

                case Web3Options.NftCollectionLedger.ImmutableX:
                    return await TryGetCollectionImmutableXInfoAsync(web3Options, collection);

                default:
                    _log.Error($"Unhandled {nameof(collection.Ledger)}={{Ledger}} in collection {{CollectionId}}, ignoring", collection.Ledger, collectionId);
                    return null;
            }
        }

        async Task<NftCollectionLedgerInfo> TryGetCollectionImmutableXInfoAsync(Web3Options web3Options, Web3Options.NftCollectionSpec collection)
        {
            ImmutableXApi.CollectionInfo imxCollection = await ImmutableXApi.TryGetCollectionInfoAsync(web3Options, collection.ContractAddress);

            if (imxCollection == null)
                return null;

            return new NftCollectionLedgerInfo(
                name:               imxCollection.Name,
                description:        imxCollection.Description,
                iconUrl:            imxCollection.IconUrl,
                collectionImageUrl: imxCollection.CollectionImageUrl,
                metadataApiUrl:     imxCollection.MetadataApiUrl);
        }

        public async Task<NftOwnerAddress> TryGetNftOwnerAddressAsync(Web3Options web3Options, NftKey nftKey)
        {
            Web3Options.NftCollectionSpec collection = web3Options.GetNftCollection(nftKey.CollectionId);

            switch (collection.Ledger)
            {
                case Web3Options.NftCollectionLedger.LocalTesting:
                    return NftOwnerAddress.None;

                case Web3Options.NftCollectionLedger.ImmutableX:
                    return await TryGetNftImmutableXOwnerAddressAsync(web3Options, nftKey, collection);

                default:
                    _log.Error($"Unhandled {nameof(collection.Ledger)}={{Ledger}} in collection {{CollectionId}}, ignoring", collection.Ledger, collection.CollectionId);
                    return NftOwnerAddress.None;
            }
        }

        async Task<NftOwnerAddress> TryGetNftImmutableXOwnerAddressAsync(Web3Options web3Options, NftKey nftKey, Web3Options.NftCollectionSpec collection)
        {
            EthereumAddress? owner = await ImmutableXApi.TryGetNftTokenOwnerAddressAsync(web3Options, collection.ContractAddress, Erc721TokenId.FromDecimalString(nftKey.TokenId.ToString()));

            if (!owner.HasValue)
                return NftOwnerAddress.None;

            return NftOwnerAddress.CreateEthereum(owner.Value);
        }

        public async Task<OrderedDictionary<NftKey, NftOwnerAddress>> GetNftsOwnedByAddressesAsync(Web3Options web3Options, List<NftOwnerAddress> ownerAddresses)
        {
            OrderedDictionary<NftKey, NftOwnerAddress> ownedNfts = new();

            foreach (Web3Options.NftCollectionSpec collection in web3Options.NftCollections.Values)
            {
                OrderedDictionary<NftKey, NftOwnerAddress> ownedInCollection = await GetNftsInCollectionOwnedByAddressesAsync(web3Options, collection, ownerAddresses);

                foreach ((NftKey key, NftOwnerAddress address) in ownedInCollection)
                    ownedNfts[key] = address;
            }

            return ownedNfts;
        }

        async Task<OrderedDictionary<NftKey, NftOwnerAddress>> GetNftsInCollectionOwnedByAddressesAsync(Web3Options web3Options, Web3Options.NftCollectionSpec collection, List<NftOwnerAddress> ownerAddresses)
        {
            switch (collection.Ledger)
            {
                case Web3Options.NftCollectionLedger.LocalTesting:
                    return new OrderedDictionary<NftKey, NftOwnerAddress>();

                case Web3Options.NftCollectionLedger.ImmutableX:
                    return await GetImmutableXNftsInCollectionOwnedByAddressesAsync(web3Options, collection, ownerAddresses);

                default:
                    _log.Error($"Unhandled {nameof(collection.Ledger)}={{Ledger}} in collection {{CollectionId}}, ignoring", collection.Ledger, collection.CollectionId);
                    return new OrderedDictionary<NftKey, NftOwnerAddress>();
            }
        }

        async Task<OrderedDictionary<NftKey, NftOwnerAddress>> GetImmutableXNftsInCollectionOwnedByAddressesAsync(Web3Options web3Options, Web3Options.NftCollectionSpec collection, List<NftOwnerAddress> ownerAddresses)
        {
            OrderedDictionary<NftKey, NftOwnerAddress> ownedNfts = new();

            foreach (NftOwnerAddress ownerAddress in ownerAddresses.Where(address => address.Type == NftOwnerAddress.AddressType.Ethereum))
            {
                EthereumAddress ownerEthereumAddress = EthereumAddress.FromString(ownerAddress.AddressString);

                Erc721TokenId[] tokenIds = await ImmutableXApi.GetNftTokensInWalletAsync(web3Options, nftContract: collection.ContractAddress, userAddress: ownerEthereumAddress);

                foreach (Erc721TokenId tokenId in tokenIds)
                {
                    NftKey nftKey = new NftKey(collection.CollectionId, NftId.ParseFromString(tokenId.GetTokenIdString()));
                    ownedNfts.Add(nftKey, ownerAddress);
                }
            }

            return ownedNfts;
        }
    }

    [MetaSerializable]
    public class NftCollectionLedgerInfo
    {
        [MetaMember(1)] public string Name;
        [MetaMember(2)] public string Description;
        [MetaMember(3)] public string IconUrl;
        [MetaMember(4)] public string CollectionImageUrl;
        [MetaMember(5)] public string MetadataApiUrl;

        NftCollectionLedgerInfo() { }
        public NftCollectionLedgerInfo(string name, string description, string iconUrl, string collectionImageUrl, string metadataApiUrl)
        {
            Name = name;
            Description = description;
            IconUrl = iconUrl;
            CollectionImageUrl = collectionImageUrl;
            MetadataApiUrl = metadataApiUrl;
        }
    }
}
