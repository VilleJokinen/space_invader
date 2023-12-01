// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Cloud.Web3;
using Metaplay.Core;
using Metaplay.Core.Web3;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Metaplay.Server.Web3
{
    [RuntimeOptions("Web3", isStatic: true, "Configuration options for web3 features.")]
    public class Web3Options : RuntimeOptionsBase
    {
        #region NFT metadata/media storage configuration

        public bool UsePseudoBucketInPublicBlobStorage { get; private set; } = IsLocalEnvironment;

        [Sensitive]
        [MetaDescription("Deprecated. ~~Explicit AWS/S3 access keys. Leave undefined to use system-level settings, eg, IRSA.~~")]
        public string AwsAccessKey  { get; private set; }
        [Sensitive]
        [MetaDescription("Deprecated. ~~Explicit AWS secret key.~~")]
        public string AwsSecretKey  { get; private set; }
        [MetaDescription($"Default S3 region to use for storing NFT metadata. Can be overridden per NFT collection in `{nameof(NftCollections)}[].{nameof(NftCollectionSpec.MetadataS3Region)}`.")]
        public string S3Region      { get; private set; }
        [MetaDescription($"Default S3 bucket name for storing NFT metadata (e.g. metaplay-nft). Can be overridden per NFT collection in `{nameof(NftCollections)}[].{nameof(NftCollectionSpec.MetadataBucketName)}`.")]
        public string NftBucketName { get; private set; }
        [MetaDescription($"Default public URL to the NFT metadata bucket (e.g. https://meta-dev.metaplay.dev/nft). Can be overridden per NFT collection in `{nameof(NftCollections)}[].{nameof(NftCollectionSpec.MetadataBucketUrl)}`.")]
        public string NftBucketUrl  { get; private set; }
        [MetaDescription($"Default root path within the NFT metadata bucket (e.g. hello-nft). Can be overridden per NFT collection in `{nameof(NftCollections)}[].{nameof(NftCollectionSpec.MetadataBasePath)}`.")]
        public string NftBasePath   { get; private set; }

        #endregion

        #region Immutable X configuration

        [MetaDescription("Immutable X network to connect to. \"None\" means disabled.")]
        public EthereumNetwork ImmutableXNetwork { get; private set; } = EthereumNetwork.None;

        /// <summary>
        /// The API URL of the currently active ImmutableX environment. <c>null</c> if Immutable X is not enabled.
        /// </summary>
        [ComputedValue]
        [MetaDescription("Immutable X API Base Url.")]
        public string ImmutableXApiUrl { get; private set; } = null;

        [MetaDescription("Enable social authentication with ImmutableX accounts.")]
        public bool EnableImmutableXPlayerAuthentication { get; private set; } = false;

        [MetaDescription("Message template to be signed in wallet software for player to bind their Immutable X wallet.")]
        public string ImmutableXPlayerAuthenticationMessageTemplate { get; private set; } =
@"Use your crypto wallet in {ProductName}

Signing this message binds the wallet into the game. This does not cost you any gas.

# Receipt
player_id: {PlayerId}
eth_wallet: {EthAccount}
immutablex_wallet: {ImxAccount}
timestamp: {Timestamp}
signature: {Signature}
";

        [MetaDescription("Message template shown in Immutable X when player is binding their wallet.")]
        public string ImmutableXPlayerAuthenticationDescriptionTemplate { get; private set; } = "Sign in into {ProductName}.";

        [MetaDescription("Product name shown in Immutable X sign in request.")]
        public string ImmutableXPlayerAuthenticationProductName { get; private set; } = null;

        [MetaDescription("Time limit within which the client must sign the Immutable X authentication challenge.")]
        public TimeSpan ImmutableXPlayerAuthenticationTimeLimit { get; private set; } = TimeSpan.FromMinutes(5);

        [Sensitive]
        [MetaDescription("HMAC secret used in Immutable X player authentication challenge. Should be unguessable string. The current environment type (local/staging/prod) is automatically appended to the secret preventing cross-env MAC reuse.")]
        public string ImmutableXPlayerAuthenticationChallengeHmacSecret { get; private set; } = null;

        [Sensitive]
        [MetaDescription("Optional Immutable X API Key. See [Immutable X API Documentation](https://docs.x.immutable.com/docs/api-rate-limiting) for details.")]
        public string ImmutableXApiKey { get; private set; } = null;

        #endregion

        #region NFT collections

        public class NftCollectionSpec
        {
            /// <summary>
            /// Here just for code convenience, not set in options file.
            /// Redundant, because it's available as the key in <see cref="NftCollections"/>.
            /// </summary>
            [IgnoreDataMember]
            public NftCollectionId CollectionId { get; set; }

            public EthereumAddress ContractAddress { get; private set; }
            public NftCollectionLedger Ledger { get; private set; }
            public string MetadataFolder { get; private set; }

            /// <summary>
            /// Specifies how the NFT metadata is managed for this collection.
            /// Affects whether this game server can write or only read NFT metadata.
            /// </summary>
            public NftMetadataManagementMode MetadataManagementMode { get; private set; } = NftMetadataManagementMode.Authoritative;

            /// <summary>
            /// If set (non-null, non-empty), overrides <see cref="Web3Options.S3Region"/> for this collection.
            /// </summary>
            public string MetadataS3Region { get; private set; }
            /// <summary>
            /// If set (non-null, non-empty), overrides <see cref="Web3Options.NftBucketName"/> for this collection.
            /// </summary>
            public string MetadataBucketName { get; private set; }
            /// <summary>
            /// If set (non-null, non-empty), overrides <see cref="Web3Options.NftBucketUrl"/> for this collection.
            /// </summary>
            public string MetadataBucketUrl { get; private set; }
            /// <summary>
            /// If set (non-null, non-empty), overrides <see cref="Web3Options.NftBasePath"/> for this collection.
            /// </summary>
            public string MetadataBasePath { get; private set; }
        }

        public enum NftCollectionLedger
        {
            /// <summary>
            /// Not set. Illegal
            /// </summary>
            Unset = 0,

            /// <summary>
            /// NFT is in local testing mode and no external blockchain or ledger is being used.
            /// </summary>
            LocalTesting,

            /// <summary>
            /// NFT is published and the system relies on ImmutableX to manage ownerhip.
            /// </summary>
            ImmutableX,
        }

        public enum NftMetadataManagementMode
        {
            /// <summary>
            /// This game backend has authority over the NFT metadata. The NFTs are first
            /// initialized in this game and then their metadata is automatically written.
            /// </summary>
            Authoritative,
            /// <summary>
            /// This game backend only has read access to the NFT metadata.
            /// The metadata is initialized externally and the game can only read the
            /// metadata (via the public metadata URL) and present the in-game NFT states
            /// based on the metadata.
            /// When the game encounters an NFT it hasn't seen before, it attempts to
            /// initialize its game-side state by automatically querying the metadata.
            /// </summary>
            Foreign,
        }

        [MetaDescription("Parameters for the NFT collections in this game.")]
        public OrderedDictionary<string, NftCollectionSpec> NftCollections { get; private set; } = new();

        #endregion

        public override Task OnLoadedAsync()
        {
            if (!new Web3EnabledCondition().IsEnabled)
            {
                // Do nothing if Web3 features are disabled.
                // \todo #nft Ideally we'd check internal consistency of the config, anyway.
                return Task.CompletedTask;
            }

            #region Default NFT metadata/media storage configuration

            if (UsePseudoBucketInPublicBlobStorage)
            {
                if (string.IsNullOrEmpty(NftBasePath))
                    NftBasePath = "nft";
            }

            #endregion

            #region Immutable X configuration

            if (ImmutableXNetwork == EthereumNetwork.None)
                ImmutableXApiUrl = null;
            else
                ImmutableXApiUrl = EthereumNetworkProperties.GetPropertiesForNetwork(ImmutableXNetwork).ImmutableXApiUrl;

            if (EnableImmutableXPlayerAuthentication)
            {
                ImmutableXLoginChallenge.ValidateMessageTemplate(ImmutableXPlayerAuthenticationMessageTemplate);
                ImmutableXLoginChallenge.ValidateDescriptionTemplate(ImmutableXPlayerAuthenticationDescriptionTemplate);

                if (ImmutableXNetwork == EthereumNetwork.None)
                    throw new InvalidOperationException($"Web3:{nameof(ImmutableXNetwork)} must be set when Web3:{nameof(EnableImmutableXPlayerAuthentication)} is enabled.");

                if (string.IsNullOrEmpty(ImmutableXPlayerAuthenticationProductName))
                    throw new InvalidOperationException($"Web3:{nameof(ImmutableXPlayerAuthenticationProductName)} must be set when Web3:{nameof(EnableImmutableXPlayerAuthentication)} is enabled.");

                if (string.IsNullOrEmpty(ImmutableXPlayerAuthenticationChallengeHmacSecret))
                    throw new InvalidOperationException($"Web3:{nameof(ImmutableXPlayerAuthenticationChallengeHmacSecret)} must be set when Web3:{nameof(EnableImmutableXPlayerAuthentication)} is enabled.");
            }

            #endregion

            #region NFT collections

            {
                foreach ((string collectionId, NftCollectionSpec collection) in NftCollections)
                    collection.CollectionId = NftCollectionId.FromString(collectionId);

                foreach (NftCollectionSpec collection in NftCollections.Values)
                {
                    if (collection.Ledger == NftCollectionLedger.Unset)
                    {
                        throw new InvalidOperationException(
                            $"Option Web3:{nameof(NftCollections)}: collection {collection.CollectionId} does not specify a {nameof(collection.Ledger)}"
                            + $", which is required for all collections. You can use {nameof(NftCollectionLedger.LocalTesting)}"
                            + " when you haven't yet registered the collection in any service (e.g. Immutable X).");
                    }

                    if (collection.ContractAddress == default)
                    {
                        if (collection.Ledger == NftCollectionLedger.ImmutableX)
                        {
                            throw new InvalidOperationException(
                                $"Option Web3:{nameof(NftCollections)}: collection {collection.CollectionId} has zero address at {nameof(collection.ContractAddress)}."
                                + $" Must provide a valid address when {nameof(collection.Ledger)} is {nameof(NftCollectionLedger.ImmutableX)}");
                        }
                    }

                    if (collection.Ledger == NftCollectionLedger.ImmutableX && ImmutableXNetwork == EthereumNetwork.None)
                        throw new InvalidOperationException($"Option Web3:{nameof(NftCollections)}: collection {collection.CollectionId} uses {NftCollectionLedger.ImmutableX}, but Web3:{nameof(ImmutableXNetwork)} is {EthereumNetwork.None}");

                    if (string.IsNullOrEmpty(collection.MetadataFolder))
                        throw new InvalidOperationException($"Option Web3:{nameof(NftCollections)}: collection {collection.CollectionId} does not specify {nameof(collection.MetadataFolder)}");

                    if (!UsePseudoBucketInPublicBlobStorage)
                    {
                        if (collection.MetadataManagementMode == NftMetadataManagementMode.Authoritative)
                        {
                            if (string.IsNullOrEmpty(collection.MetadataS3Region) && string.IsNullOrEmpty(S3Region))
                                throw new InvalidOperationException($"Option Web3:{nameof(NftCollections)}, collection {collection.CollectionId}: Metadata storage S3 region is not configured. Configure either the default Web3:{nameof(S3Region)}, or {nameof(collection.MetadataS3Region)} specifically for this collection.");
                            if (string.IsNullOrEmpty(collection.MetadataBucketName) && string.IsNullOrEmpty(NftBucketName))
                                throw new InvalidOperationException($"Option Web3:{nameof(NftCollections)}, collection {collection.CollectionId}: Metadata storage bucket name is not configured. Configure either the default Web3:{nameof(NftBucketName)}, or {nameof(collection.MetadataBucketName)} specifically for this collection.");
                        }

                        if (string.IsNullOrEmpty(collection.MetadataBucketUrl) && string.IsNullOrEmpty(NftBucketUrl))
                            throw new InvalidOperationException($"Option Web3:{nameof(NftCollections)}, collection {collection.CollectionId}: Metadata public URL is not configured. Configure either the default Web3:{nameof(NftBucketUrl)}, or {nameof(collection.MetadataBucketUrl)} specifically for this collection.");
                        if (string.IsNullOrEmpty(collection.MetadataBasePath) && string.IsNullOrEmpty(NftBasePath))
                            throw new InvalidOperationException($"Option Web3:{nameof(NftCollections)}, collection {collection.CollectionId}: Metadata base path is not configured. Configure either the default Web3:{nameof(NftBasePath)}, or {nameof(collection.MetadataBasePath)} specifically for this collection.");
                    }
                }

                Util.CheckPropertyDuplicates(
                    NftCollections.Values.Where(c => c.ContractAddress != default),
                    c => c.ContractAddress,
                    (collectionA, collectionB, contractAddress) =>
                        throw new InvalidOperationException($"Option Web3:{nameof(NftCollections)} has collections {collectionA.CollectionId} and {collectionB.CollectionId} with the same contract {contractAddress}. Collections must have unique contracts."));

                Util.CheckPropertyDuplicates(
                    NftCollections.Values,
                    c => GetNftCollectionMetadataApiUrl(c),
                    (collectionA, collectionB, metadataApiUrl) =>
                        throw new InvalidOperationException($"Option Web3:{nameof(NftCollections)} has collections {collectionA.CollectionId} and {collectionB.CollectionId} with the same metadata API URL \"{metadataApiUrl}\". Collections should have distinct metadata API URLs."));

                Util.CheckPropertyDuplicates(
                    NftCollections.Values,
                    c => (GetNftMetadataBucketName(c), GetNftCollectionMetadataPath(c)),
                    (collectionA, collectionB, bucketTuple) =>
                        throw new InvalidOperationException($"Option Web3:{nameof(NftCollections)} has collections {collectionA.CollectionId} and {collectionB.CollectionId} with the same metadata (bucketName, basePath/folder) tuple {bucketTuple}. Collections should have distinct metadata storage locations."));

                HashSet<NftCollectionId> collectionsSet = NftCollections.Values.Select(c => c.CollectionId).ToHashSet();
                foreach (NftTypeSpec nftTypeSpec in NftTypeRegistry.Instance.GetAllSpecs())
                {
                    if (!collectionsSet.Contains(nftTypeSpec.CollectionId))
                        throw new InvalidOperationException($"Option Web3:{nameof(NftCollections)} does not contain collection {nftTypeSpec.CollectionId} which is used by NFT type {nftTypeSpec.NftType.ToNamespaceQualifiedTypeString()}");
                }
            }

            #endregion

            return Task.CompletedTask;
        }

        public IBlobStorage CreateNftCollectionMetadataStorage(NftCollectionSpec collection)
        {
            if (collection.MetadataManagementMode != NftMetadataManagementMode.Authoritative)
                throw new InvalidOperationException($"Cannot set up NFT metadata storage access for collection {collection.CollectionId} because the collection has {nameof(collection.MetadataManagementMode)}={collection.MetadataManagementMode}, expected {NftMetadataManagementMode.Authoritative}");

            if (!UsePseudoBucketInPublicBlobStorage)
            {
                return new S3BlobStorage(
                    accessKey: AwsAccessKey,
                    secretKey: AwsSecretKey,
                    regionName: GetNftMetadataS3Region(collection),
                    bucketName: GetNftMetadataBucketName(collection),
                    basePath: GetNftCollectionMetadataPath(collection));
            }
            else
            {
                BlobStorageOptions blobStorageOptions = RuntimeOptionsRegistry.Instance.GetCurrent<BlobStorageOptions>();
                return blobStorageOptions.CreatePublicBlobStorage(Path.Join("Web3PseudoBucket", GetNftCollectionMetadataPath(collection)));
            }
        }

        public string GetNftMetadataS3Region(NftCollectionSpec collection)
        {
            string collectionOverride = collection.MetadataS3Region;
            return !string.IsNullOrEmpty(collectionOverride) ? collectionOverride : S3Region;
        }

        public string GetNftMetadataBucketName(NftCollectionSpec collection)
        {
            string collectionOverride = collection.MetadataBucketName;
            return !string.IsNullOrEmpty(collectionOverride) ? collectionOverride : NftBucketName;
        }

        public string GetNftMetadataBucketUrl(NftCollectionSpec collection)
        {
            string collectionOverride = collection.MetadataBucketUrl;
            return !string.IsNullOrEmpty(collectionOverride) ? collectionOverride : NftBucketUrl;
        }

        public string GetNftMetadataBasePath(NftCollectionSpec collection)
        {
            string collectionOverride = collection.MetadataBasePath;
            return !string.IsNullOrEmpty(collectionOverride) ? collectionOverride : NftBasePath;
        }

        public string GetNftCollectionMetadataPath(NftCollectionSpec collection)
        {
            string basePath = GetNftMetadataBasePath(collection);
            string folder = collection.MetadataFolder;

            return $"{basePath}/{folder}";
        }

        public string GetNftCollectionMetadataApiUrl(NftCollectionSpec collection)
        {
            string bucketUrl = GetNftMetadataBucketUrl(collection);
            string collectionPath = GetNftCollectionMetadataPath(collection);

            return $"{bucketUrl}/{collectionPath}";
        }

        public string GetNftMetadataUrl(NftCollectionSpec collection, NftId tokenId)
        {
            string collectionMetadataApiUrl = GetNftCollectionMetadataApiUrl(collection);
            return $"{collectionMetadataApiUrl}/{tokenId}";
        }

        public NftCollectionSpec GetNftCollection(NftCollectionId collectionId)
        {
            return TryGetNftCollection(collectionId)
                   ?? throw new ArgumentException($"No NFT collection {collectionId} is configured.", nameof(collectionId));
        }

        public NftCollectionSpec TryGetNftCollection(NftCollectionId id)
        {
            if (NftCollections.TryGetValue(id?.Value, out NftCollectionSpec collection))
                return collection;
            else
                return null;
        }
    }

    public static class NftCollectionLedgerExtensions
    {
        public static bool IsRealLedger(this Web3Options.NftCollectionLedger ledger)
            => ledger != Web3Options.NftCollectionLedger.LocalTesting;

        public static string GetName(this Web3Options.NftCollectionLedger ledger)
        {
            switch (ledger)
            {
                case Web3Options.NftCollectionLedger.LocalTesting: return "Local Testing";
                case Web3Options.NftCollectionLedger.ImmutableX: return "Immutable X";
                default: return "<unknown>";
            }
        }
    }
}
