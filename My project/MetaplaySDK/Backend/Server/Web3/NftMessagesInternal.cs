// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Web3;
using Metaplay.Core.TypeCodes;
using System.Collections.Generic;
using Metaplay.Core.Model;
using Metaplay.Cloud.Entity;
using System;

namespace Metaplay.Server.Web3
{
    [MetaSerializable]
    public struct NftQueryItem
    {
        [MetaMember(1)] public NftKey NftKey;
        [MetaMember(2)] public bool IsSuccess;
        [MetaMember(3)] public MetaNft Nft;
        [MetaMember(4)] public string Error;

        public NftQueryItem(NftKey nftKey, bool isSuccess, MetaNft nft, string error)
        {
            NftKey = nftKey;
            IsSuccess = isSuccess;
            Nft = nft;
            Error = error;
        }
    }

    [MetaMessage(MessageCodesCore.GetNftRequest, MessageDirection.ServerInternal)]
    public class GetNftRequest : MetaMessage
    {
        public NftKey NftKey;

        GetNftRequest() { }
        public GetNftRequest(NftKey nftKey)
        {
            NftKey = nftKey;
        }
    }
    [MetaMessage(MessageCodesCore.GetNftResponse, MessageDirection.ServerInternal)]
    public class GetNftResponse : MetaMessage
    {
        public NftQueryItem Item;
        public bool HasPendingMetadataWrite;

        GetNftResponse() { }
        public GetNftResponse(NftQueryItem item, bool hasPendingMetadataWrite)
        {
            Item = item;
            HasPendingMetadataWrite = hasPendingMetadataWrite;
        }
    }

    [MetaMessage(MessageCodesCore.RefreshNftRequest, MessageDirection.ServerInternal)]
    public class RefreshNftRequest : MetaMessage
    {
        public NftKey NftKey;

        RefreshNftRequest() { }
        public RefreshNftRequest(NftKey nftKey)
        {
            NftKey = nftKey;
        }
    }
    [MetaMessage(MessageCodesCore.RefreshNftResponse, MessageDirection.ServerInternal)]
    public class RefreshNftResponse : MetaMessage
    {
    }

    [MetaMessage(MessageCodesCore.RepublishNftMetadataRequest, MessageDirection.ServerInternal)]
    public class RepublishNftMetadataRequest : MetaMessage
    {
        public NftKey NftKey;

        RepublishNftMetadataRequest() { }
        public RepublishNftMetadataRequest(NftKey nftKey)
        {
            NftKey = nftKey;
        }
    }
    [MetaMessage(MessageCodesCore.RepublishNftMetadataResponse, MessageDirection.ServerInternal)]
    public class RepublishNftMetadataResponse : MetaMessage
    {
    }

    /// <summary>
    /// Sent to <see cref="NftManager"/>, query NFTs matching filters specified by
    /// the fields in this message.
    /// </summary>
    [MetaMessage(MessageCodesCore.QueryNftsRequest, MessageDirection.ServerInternal)]
    public class QueryNftsRequest : MetaMessage
    {
        /// <summary>
        /// If non-null, query only NFTs belonging to this collection.
        /// If null, all collections are considered.
        /// </summary>
        public NftCollectionId Collection;
        /// <summary>
        /// If non-null, query only NFTs currently owned by this entity.
        /// If null, NFTs of all owners are considered.
        /// </summary>
        public EntityId? Owner;

        QueryNftsRequest() { }
        public QueryNftsRequest(NftCollectionId collection = null, EntityId? owner = null)
        {
            Collection = collection;
            Owner = owner;
        }
    }
    [MetaMessage(MessageCodesCore.QueryNftsResponse, MessageDirection.ServerInternal)]
    public class QueryNftsResponse : MetaMessage
    {
        public List<NftQueryItem> Items;

        QueryNftsResponse() { }
        public QueryNftsResponse(List<NftQueryItem> items)
        {
            Items = items;
        }
    }

    /// <summary>
    /// Sent to <see cref="NftManager"/>, trigger it to query the NFTs owned by the
    /// specified addresses, which are assumed to be owned by the specified entity,
    /// and adjust game-side ownership info accordingly.
    /// </summary>
    [MetaMessage(MessageCodesCore.RefreshNftsOwnedByEntityRequest, MessageDirection.ServerInternal)]
    public class RefreshNftsOwnedByEntityRequest : MetaMessage
    {
        public EntityId Owner;
        public List<NftOwnerAddress> OwnedAddresses;

        RefreshNftsOwnedByEntityRequest() { }
        public RefreshNftsOwnedByEntityRequest(EntityId owner, List<NftOwnerAddress> ownedAddresses)
        {
            Owner = owner;
            OwnedAddresses = ownedAddresses;
        }
    }
    [MetaMessage(MessageCodesCore.RefreshNftsOwnedByEntityResponse, MessageDirection.ServerInternal)]
    public class RefreshNftsOwnedByEntityResponse : MetaMessage
    {
    }

    /// <summary>
    /// Sent to <see cref="NftManager"/>, updates the states of the given NFTs.
    /// <see cref="NftManager"/> checks that the NFTs are owned by <see cref="AssertedOwner"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.OwnerUpdateNftStatesRequest, MessageDirection.ServerInternal)]
    public class OwnerUpdateNftStatesRequest : MetaMessage
    {
        public EntityId AssertedOwner;
        public List<MetaNft> Nfts;

        OwnerUpdateNftStatesRequest() { }
        public OwnerUpdateNftStatesRequest(EntityId assertedOwner, List<MetaNft> nfts)
        {
            AssertedOwner = assertedOwner;
            Nfts = nfts;
        }
    }
    [MetaMessage(MessageCodesCore.OwnerUpdateNftStatesResponse, MessageDirection.ServerInternal)]
    public class OwnerUpdateNftStatesResponse : MetaMessage
    {
    }

    [MetaMessage(MessageCodesCore.BatchInitializeNftsRequest, MessageDirection.ServerInternal)]
    public class BatchInitializeNftsRequest : MetaMessage
    {
        public List<NftInitSpec> NftInitSpecs;
        public NftCollectionId CollectionId;
        public bool ShouldWriteMetadata;
        public bool ShouldQueryOwnersFromLedger;
        public bool AllowOverwrite;
        public bool ValidateOnly;

        [MetaSerializable]
        public struct NftInitSpec
        {
            [MetaMember(1)] public NftId? TokenId;
            [MetaMember(2)] public MetaNft Nft;
            [MetaMember(3)] public string SourceInfo;

            public NftInitSpec(NftId? tokenId, MetaNft nft, string sourceInfo)
            {
                TokenId = tokenId;
                Nft = nft;
                SourceInfo = sourceInfo;
            }
        }

        BatchInitializeNftsRequest() { }
        public BatchInitializeNftsRequest(List<NftInitSpec> nftInitSpecs, NftCollectionId collectionId, bool shouldWriteMetadata, bool shouldQueryOwnersFromLedger, bool allowOverwrite, bool validateOnly)
        {
            NftInitSpecs = nftInitSpecs;
            CollectionId = collectionId;
            ShouldWriteMetadata = shouldWriteMetadata;
            ShouldQueryOwnersFromLedger = shouldQueryOwnersFromLedger;
            AllowOverwrite = allowOverwrite;
            ValidateOnly = validateOnly;
        }
    }
    [MetaMessage(MessageCodesCore.BatchInitializeNftsResponse, MessageDirection.ServerInternal)]
    public class BatchInitializeNftsResponse : MetaMessage
    {
        [MetaSerializable]
        public struct NftResponse
        {
            [MetaMember(1)] public MetaNft Nft;
            [MetaMember(2)] public MetaNft OverwrittenNftMaybe;

            public NftResponse(MetaNft nft, MetaNft overwrittenNftMaybe)
            {
                Nft = nft;
                OverwrittenNftMaybe = overwrittenNftMaybe;
            }
        }

        public List<NftResponse> Nfts;

        BatchInitializeNftsResponse() { }
        public BatchInitializeNftsResponse(List<NftResponse> nfts)
        {
            Nfts = nfts;
        }
    }
    [MetaSerializableDerived(MessageCodesCore.BatchInitializeNftsRefusal)]
    public class BatchInitializeNftsRefusal : EntityAskRefusal
    {
        [MetaMember(1)] public string Error;
        [MetaMember(2)] public string Details;

        public override string Message => Error;

        BatchInitializeNftsRefusal() { }
        public BatchInitializeNftsRefusal(string message, string details)
        {
            Error = message;
            Details = details;
        }

        public BatchInitializeNftsRefusal(string message, Exception ex)
            : this(message, ex.ToString())
        {
        }
    }

    [MetaMessage(MessageCodesCore.TryGetExistingNftIdInRangeRequest, MessageDirection.ServerInternal)]
    public class TryGetExistingNftIdInRangeRequest : MetaMessage
    {
        public NftCollectionId CollectionId;
        public NftId FirstTokenId;
        public int NumTokens;

        TryGetExistingNftIdInRangeRequest() { }
        public TryGetExistingNftIdInRangeRequest(NftCollectionId collectionId, NftId firstTokenId, int numTokens)
        {
            CollectionId = collectionId;
            FirstTokenId = firstTokenId;
            NumTokens = numTokens;
        }
    }
    [MetaMessage(MessageCodesCore.TryGetExistingNftIdInRangeResponse, MessageDirection.ServerInternal)]
    public class TryGetExistingNftIdInRangeResponse : MetaMessage
    {
        public NftId? Id;

        TryGetExistingNftIdInRangeResponse() { }
        public TryGetExistingNftIdInRangeResponse(NftId? id)
        {
            Id = id;
        }
    }

    /// <summary>
    /// Sent as an admin message to <see cref="NftManager"/>, marks <see cref="NewOwner"/>
    /// as the owner of the NFT identified by <see cref="NftKey"/>. <see cref="NewOwner"/>
    /// can also be <see cref="EntityId.None"/>, in which case the NFT will have no owning
    /// entity in the game.
    /// </summary>
    [MetaMessage(MessageCodesCore.SetNftOwnershipDebugRequest, MessageDirection.ServerInternal)]
    public class SetNftOwnershipDebugRequest : MetaMessage
    {
        public NftKey NftKey;
        public EntityId NewOwner;
        public NftOwnerAddress NewOwnerAddress;

        SetNftOwnershipDebugRequest() { }
        public SetNftOwnershipDebugRequest(NftKey nftKey, EntityId newOwner, NftOwnerAddress newOwnerAddress)
        {
            NftKey = nftKey;
            NewOwner = newOwner;
            NewOwnerAddress = newOwnerAddress;
        }
    }
    [MetaMessage(MessageCodesCore.SetNftOwnershipDebugResponse, MessageDirection.ServerInternal)]
    public class SetNftOwnershipDebugResponse : MetaMessage
    {
    }

    /// <summary>
    /// Sent from <see cref="NftManager"/> to an entity to inform the entity that it
    /// no longer owns the specified NFT.
    /// </summary>
    [MetaMessage(MessageCodesCore.NftOwnershipRemoved, MessageDirection.ServerInternal)]
    public class NftOwnershipRemoved : MetaMessage
    {
        public NftKey NftKey;

        NftOwnershipRemoved() { }
        public NftOwnershipRemoved(NftKey nftKey)
        {
            NftKey = nftKey;
        }
    }

    /// <summary>
    /// Sent from <see cref="NftManager"/> to an entity to inform the entity that it
    /// now owns the specified NFT.
    /// </summary>
    [MetaMessage(MessageCodesCore.NftOwnershipGained, MessageDirection.ServerInternal)]
    public class NftOwnershipGained : MetaMessage
    {
        public MetaNft Nft;

        NftOwnershipGained() { }
        public NftOwnershipGained(MetaNft nft)
        {
            Nft = nft;
        }
    }

    /// <summary>
    /// Sent from <see cref="NftManager"/> to an entity to inform the entity that
    /// an NFT owned by it has had its state updated.
    /// </summary>
    [MetaMessage(MessageCodesCore.NftStateUpdated, MessageDirection.ServerInternal)]
    public class NftStateUpdated : MetaMessage
    {
        public MetaNft Nft;

        NftStateUpdated() { }
        public NftStateUpdated(MetaNft nft)
        {
            Nft = nft;
        }
    }

    [MetaMessage(MessageCodesCore.RefreshOwnedNftsRequest, MessageDirection.ServerInternal)]
    public class RefreshOwnedNftsRequest : MetaMessage
    {
    }
    [MetaMessage(MessageCodesCore.RefreshOwnedNftsResponse, MessageDirection.ServerInternal)]
    public class RefreshOwnedNftsResponse : MetaMessage
    {
    }

    [MetaMessage(MessageCodesCore.GetNftCollectionInfoRequest, MessageDirection.ServerInternal)]
    public class GetNftCollectionInfoRequest : MetaMessage
    {
        public NftCollectionId CollectionId;

        GetNftCollectionInfoRequest() { }
        public GetNftCollectionInfoRequest(NftCollectionId collectionId)
        {
            CollectionId = collectionId;
        }
    }
    [MetaMessage(MessageCodesCore.GetNftCollectionInfoResponse, MessageDirection.ServerInternal)]
    public class GetNftCollectionInfoResponse : MetaMessage
    {
        public NftManagerState.Collection Collection;
        public HashSet<NftId> PendingMetadataWrites;

        GetNftCollectionInfoResponse() { }
        public GetNftCollectionInfoResponse(NftManagerState.Collection collection, HashSet<NftId> pendingMetadataWrites)
        {
            Collection = collection;
            PendingMetadataWrites = pendingMetadataWrites;
        }
    }

    [MetaMessage(MessageCodesCore.RefreshCollectionLedgerInfoRequest, MessageDirection.ServerInternal)]
    public class RefreshCollectionLedgerInfoRequest : MetaMessage
    {
        public NftCollectionId CollectionId;

        RefreshCollectionLedgerInfoRequest() { }
        public RefreshCollectionLedgerInfoRequest(NftCollectionId collectionId)
        {
            CollectionId = collectionId;
        }
    }
    [MetaMessage(MessageCodesCore.RefreshCollectionLedgerInfoResponse, MessageDirection.ServerInternal)]
    public class RefreshCollectionLedgerInfoResponse : MetaMessage
    {
    }
}
