// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Serialization;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Web3
{
    public static class NftUtil
    {
        public static MetaNft CreateNftFromMetadata(
            Type nftType,
            NftMetadataSpec metadataSpec,
            JObject metadata,
            NftMetadataImportContext importContext,
            // Initial base properties for the NFT.
            NftId tokenId,
            EntityId ownerEntity,
            NftOwnerAddress ownerAddress,
            bool isMinted,
            ulong updateCounter)
        {
            MetaNft nft = (MetaNft)Activator.CreateInstance(nftType);
            nft.SetBaseProperties(
                tokenId,
                ownerEntity,
                ownerAddress,
                isMinted,
                updateCounter);

            NftMetadataImportContext oldImportContext = nft.MetadataImportContext; // \note Likely null, but be safe.
            nft.MetadataImportContext = importContext;
            try
            {
                metadataSpec.ApplyMetadataToNft(nft, metadata);
                nft.OnMetadataImported(importContext);
            }
            finally
            {
                nft.MetadataImportContext = oldImportContext;
            }

            MetaSerialization.ResolveMetaRefs(ref nft, importContext.SharedGameConfig);

            return nft;
        }

        /// <summary>
        /// Resolve a NFT type in the given collection, according to these rules:
        /// - If <paramref name="explicitNftClassNameMaybe"/> is null or empty, then
        ///   there must exist exactly 1 NFT type in the collection, and that type is returned.
        ///   If 0 or more than 1 types exist in the collection, an exception is thrown.
        /// - Otherwise, <paramref name="explicitNftClassNameMaybe"/> must be the name
        ///   of one of the types in the collection, and that type is returned.
        ///   If no such type exists in the collection, an exception is thrown.
        /// </summary>
        public static NftTypeSpec ResolveNftTypeInCollection(NftCollectionId collectionId, string explicitNftClassNameMaybe)
        {
            IEnumerable<NftTypeSpec> allNftTypeSpecs = NftTypeRegistry.Instance.GetAllSpecs();

            if (string.IsNullOrEmpty(explicitNftClassNameMaybe))
            {
                IEnumerable<NftTypeSpec> specsInCollection = allNftTypeSpecs.Where(spec => spec.CollectionId == collectionId);
                if (specsInCollection.Count() == 0)
                    throw new InvalidOperationException($"There are no {nameof(MetaNft)} classes belonging to collection {collectionId}.");
                if (specsInCollection.Count() > 1)
                {
                    throw new InvalidOperationException(
                        $"{nameof(MetaNft)} class name must be specified, because there are multiple classes " +
                        $"belonging to collection {collectionId}: {string.Join(", ", specsInCollection.Select(spec => spec.NftType.Name))}.");
                }

                return specsInCollection.Single();
            }
            else
            {
                NftTypeSpec nftTypeSpec = allNftTypeSpecs.SingleOrDefault(spec => spec.NftType.Name == explicitNftClassNameMaybe);
                if (nftTypeSpec == null)
                {
                    IEnumerable<NftTypeSpec> specsInCollection = allNftTypeSpecs.Where(spec => spec.CollectionId == collectionId);

                    throw new InvalidOperationException(
                        $"'{explicitNftClassNameMaybe}' is not a known concrete subclass of {nameof(MetaNft)}. " +
                        $"Known classes in collection {collectionId}: {string.Join(", ", specsInCollection.Select(spec => spec.NftType.Name))}.");
                }

                if (nftTypeSpec.CollectionId != collectionId)
                {
                    throw new InvalidOperationException(
                        $"Trying to use NFT class {nftTypeSpec.NftType.Name} for collection {collectionId}, " +
                        $"but that class belongs to collection {nftTypeSpec.CollectionId}.");
                }

                return nftTypeSpec;
            }
        }

        public static IEnumerable<NftId> GetNftIdRange(NftId firstId, int numIds)
        {
            NftId nftIdIter = firstId;
            for (int i = 0; i < numIds; i++)
            {
                yield return nftIdIter;
                nftIdIter = nftIdIter.Plus1();
            }
        }
    }
}
