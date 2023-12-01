// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Web3;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Forms;
using Metaplay.Core.Json;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.Web3;
using Metaplay.Server.AdminApi.AuditLog;
using Metaplay.Server.Web3;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;
using static System.FormattableString;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller for endpoints for managing NFTs
    /// </summary>
    [Web3EnabledCondition]
    public class NftController : GameAdminApiController
    {
        public NftController(ILogger<NftController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerOwnedNftsRefreshed)]
        public class PlayerEventOwnedNftsRefreshed : PlayerEventPayloadBase
        {
            [MetaMember(1)] public bool IsSuccess;
            [MetaMember(2)] public string Error;

            PlayerEventOwnedNftsRefreshed() { }
            public PlayerEventOwnedNftsRefreshed(bool isSuccess, string error)
            {
                IsSuccess = isSuccess;
                Error = error;
            }

            public override string EventTitle => "NFTs refreshed";
            public override string EventDescription
            {
                get
                {
                    if (IsSuccess)
                        return "Refreshed the player's owned NFTs.";
                    else
                        return "Tried to refresh the player's owned NFTs, but the refresh failed.";
                }
            }
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.PlayerDebugAssignedNftOwnership)]
        public class PlayerEventDebugAssignedNftOwnership : PlayerEventPayloadBase
        {
            [MetaMember(1)] public NftKey NftKey;

            PlayerEventDebugAssignedNftOwnership() { }
            public PlayerEventDebugAssignedNftOwnership(NftKey nftKey) { NftKey = nftKey; }

            public override string EventTitle => "Debug-assigned NFT ownership";
            public override string EventDescription => $"This player was debug-assigned ownership of NFT {NftKey}.";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.NftCollectionRefreshed)]
        public class NftCollectionRefreshed : NftCollectionEventPayloadBase
        {
            [MetaMember(1)] public bool IsSuccess;
            [MetaMember(2)] public string Error;

            NftCollectionRefreshed() { }
            public NftCollectionRefreshed(bool isSuccess, string error)
            {
                IsSuccess = isSuccess;
                Error = error;
            }

            public override string EventTitle => "NFT collection refreshed";
            public override string EventDescription
            {
                get
                {
                    if (IsSuccess)
                        return "Refreshed the collection's info from its ledger (e.g. Immutable X).";
                    else
                        return "Tried to refreshed the collection's info from its ledger (e.g. Immutable X), but the refresh failed.";
                }
            }
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.NftInitialized)]
        public class NftEventInitialized : NftEventPayloadBase
        {
            [MetaSerializable]
            public enum NftInitFlavor
            {
                Single = 0,
                Batch = 1,
                BatchFromMetadata = 2,
            }

            // \note JsonIgnored for now to avoid audit log query breakage if an exception is thrown from a property of a MetaNft-derived class.
            [MetaMember(1), JsonIgnore] public MetaNft InitialState;
            [MetaMember(3), JsonIgnore] public MetaNft OverwrittenNftMaybe;
            [MetaMember(2)] public NftInitFlavor InitFlavor;

            NftEventInitialized() { }
            public NftEventInitialized(MetaNft initialState, MetaNft overwrittenNftMaybe, NftInitFlavor initFlavor)
            {
                InitialState = initialState;
                OverwrittenNftMaybe = overwrittenNftMaybe;
                InitFlavor = initFlavor;
            }

            public override string EventTitle => "NFT initialized";
            public override string EventDescription
            {
                get
                {
                    string initFlavorStr;
                    switch (InitFlavor)
                    {
                        case NftInitFlavor.Single:              initFlavorStr = ""; break;
                        case NftInitFlavor.Batch:               initFlavorStr = ", as part of batch initialization"; break;
                        case NftInitFlavor.BatchFromMetadata:   initFlavorStr = ", by fetching existing metadata"; break;
                        default:
                            initFlavorStr = "";
                            break;
                    }

                    string overwroteStr;
                    if (OverwrittenNftMaybe != null)
                        overwroteStr = " Overwrote an existing NFT state.";
                    else
                        overwroteStr = "";

                    return $"Initialized the NFT's game-side state{initFlavorStr}.{overwroteStr}";
                }
            }
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.NftStateEdited)]
        public class NftEventStateEdited : NftEventPayloadBase
        {
            // \note JsonIgnored for now to avoid audit log query breakage if an exception is thrown from a property of a MetaNft-derived class.
            [MetaMember(1), JsonIgnore] public MetaNft OldState;
            [MetaMember(2), JsonIgnore] public MetaNft NewState;

            NftEventStateEdited() { }
            public NftEventStateEdited(MetaNft oldState, MetaNft newState)
            {
                OldState = oldState;
                NewState = newState;
            }

            public override string EventTitle => "NFT state edited";
            public override string EventDescription => "Edited the existing NFT's game-side state.";
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.NftOwnershipDebugAssigned)]
        public class NftEventOwnershipDebugAssigned : NftEventPayloadBase
        {
            [MetaMember(1)] public EntityId NewOwnerEntityId;

            NftEventOwnershipDebugAssigned() { }
            public NftEventOwnershipDebugAssigned(EntityId newOwnerEntityId) { NewOwnerEntityId = newOwnerEntityId; }

            public override string EventTitle => "NFT ownership debug-assigned";
            public override string EventDescription => $"Debug-assigned ownership of the NFT to {NewOwnerEntityId}";
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.NftRefreshed)]
        public class NftEventRefreshed : NftEventPayloadBase
        {
            [MetaMember(1)] public bool IsSuccess;
            [MetaMember(2)] public string Error;

            NftEventRefreshed() { }
            public NftEventRefreshed(bool isSuccess, string error)
            {
                IsSuccess = isSuccess;
                Error = error;
            }

            public override string EventTitle => "NFT refreshed";
            public override string EventDescription
            {
                get
                {
                    if (IsSuccess)
                        return "Refreshed the NFT's ownership.";
                    else
                        return "Tried to refreshed the NFT's ownership, but the refresh failed.";
                }
            }
        }
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.NftMetadataRepublished)]
        public class NftEventMetadataRepublished : NftEventPayloadBase
        {
            public override string EventTitle => "NFT metadata republished";
            public override string EventDescription => "Republished the NFT's metadata to the metadata bucket.";
        }

        [HttpPost("nft/setOwnershipDebug")]
        [RequirePermission(MetaplayPermissions.ApiNftSetOwnershipDebug)]
        public async Task<ActionResult> SetNftOwnershipDebug([FromQuery] string collection, [FromQuery] string token, [FromQuery] string newOwnerEntity)
        {
            NftCollectionId collectionId        = NftCollectionId.FromString(collection);
            NftId           tokenId             = NftId.ParseFromString(token);
            NftKey          nftKey              = new NftKey(collectionId, tokenId);
            EntityId        newOwnerEntityId    = EntityId.ParseFromString(newOwnerEntity);

            _ = await AskEntityAsync<SetNftOwnershipDebugResponse>(NftManager.EntityId, new SetNftOwnershipDebugRequest(nftKey, newOwnerEntityId, newOwnerAddress: NftOwnerAddress.None));

            List<EventBuilder> auditLogEvents = new List<EventBuilder>();
            if (newOwnerEntityId != EntityId.None)
                auditLogEvents.Add(new PlayerEventBuilder(newOwnerEntityId, new PlayerEventDebugAssignedNftOwnership(nftKey)));
            auditLogEvents.Add(new NftEventBuilder(nftKey, new NftEventOwnershipDebugAssigned(newOwnerEntityId)));
            await WriteRelatedAuditLogEventsAsync(auditLogEvents);

            return Ok();
        }

        public struct LedgerConfiguration
        {
            public string DisplayName;
            public string NetworkName;

            public LedgerConfiguration(string displayName, string networkName)
            {
                DisplayName = displayName;
                NetworkName = networkName;
            }
        }

        public struct NftCollectionBriefInfo
        {
            public NftCollectionId CollectionId;
            public Web3Options.NftCollectionLedger Ledger;
            public bool HasLedger;
            public string LedgerName;
            public EthereumAddress ContractAddress;
            public Web3Options.NftMetadataManagementMode MetadataManagementMode;

            public NftCollectionLedgerInfo LedgerInfo;
        }

        public struct GeneralNftInfo
        {
            public List<LedgerConfiguration> Ledgers;
            public List<NftCollectionBriefInfo> Collections;
        }

        /// <summary>
        /// API endpoint to return general information about NFT configuration in the system
        /// Usage:  GET /api/nft
        /// Test:   curl http://localhost:5550/api/nft
        /// </summary>
        [HttpGet("nft")]
        [RequirePermission(MetaplayPermissions.ApiNftView)]
        public async Task<ActionResult<GeneralNftInfo>> GetGeneralNftInfoAsync()
        {
            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();

            List<LedgerConfiguration> ledgers = new List<LedgerConfiguration>();
            List<NftCollectionBriefInfo> collections = new List<NftCollectionBriefInfo>();

            // \todo #nft Generalize this when there are more supported ledgers.
            if (web3Options.ImmutableXNetwork != EthereumNetwork.None)
                ledgers.Add(new LedgerConfiguration(displayName: "Immutable X", networkName: web3Options.ImmutableXNetwork.ToString()));

            foreach (Web3Options.NftCollectionSpec collection in web3Options.NftCollections.Values)
            {
                // \todo #nft Do just one query to NftManager to get all collections.
                NftManagerState.Collection collectionInfo = (await AskEntityAsync<GetNftCollectionInfoResponse>(NftManager.EntityId, new GetNftCollectionInfoRequest(collection.CollectionId))).Collection;

                collections.Add(new NftCollectionBriefInfo()
                {
                    CollectionId = collection.CollectionId,
                    Ledger = collection.Ledger,
                    HasLedger = collection.Ledger.IsRealLedger(),
                    LedgerName = collection.Ledger.GetName(),
                    ContractAddress = collection.ContractAddress,
                    MetadataManagementMode = collection.MetadataManagementMode,

                    LedgerInfo = collectionInfo.LedgerInfoMaybe,
                });
            }

            return Ok(new GeneralNftInfo
            {
                Ledgers = ledgers,
                Collections = collections,
            });
        }

        public struct NftCollectionInfo
        {
            public NftCollectionId CollectionId;
            public Web3Options.NftCollectionLedger Ledger;
            public bool HasLedger;
            public string LedgerName;
            public EthereumAddress? ContractAddress;
            public string MetadataApiUrl;
            public Web3Options.NftMetadataManagementMode MetadataManagementMode;

            public string BatchInitPlaceholderText;

            public NftCollectionLedgerInfo LedgerInfo;

            public List<NftBriefInfo> Nfts;

            public List<OngoingMetadataDownloadInfo> OngoingMetadataDownloads;

            public List<UninitializedNftBriefInfo> UninitializedNfts;

            public NftConfigWarning? ConfigWarning;
        }

        public struct OngoingMetadataDownloadInfo
        {
            public MetaGuid TaskId;
            public NftId FirstTokenId;
            public NftId LastTokenId;
            public int NumDownloaded;
            public int NumTotal;

            public OngoingMetadataDownloadInfo(MetaGuid taskId, NftId firstTokenId, NftId lastTokenId, int numDownloaded, int numTotal)
            {
                TaskId = taskId;
                FirstTokenId = firstTokenId;
                LastTokenId = lastTokenId;
                NumDownloaded = numDownloaded;
                NumTotal = numTotal;
            }
        }

        public struct NftBriefInfo
        {
            public string QueryError;

            public NftId TokenId;
            public EntityId Owner;
            public string OwnerAddress;
            /// <inheritdoc cref="MetaNft.IsMinted"/>
            public bool IsMinted;
            public string TypeName;
            public string Name;
            public string Description;
            public string ImageUrl;
            public bool HasPendingMetadataWrite;

            public NftBriefInfo(string queryError, NftId tokenId, EntityId owner, string ownerAddress, bool isMinted, string typeName, string name, string description, string imageUrl, bool hasPendingMetadataWrite)
            {
                QueryError = queryError;
                TokenId = tokenId;
                Owner = owner;
                OwnerAddress = ownerAddress;
                IsMinted = isMinted;
                TypeName = typeName;
                Name = name;
                Description = description;
                ImageUrl = imageUrl;
                HasPendingMetadataWrite = hasPendingMetadataWrite;
            }

            public static NftBriefInfo Create(MetaNft nft, HashSet<NftId> pendingMetadataWrites)
            {
                NftMetadataSpec metadataSpec = NftTypeRegistry.Instance.GetSpec(nft.GetType()).MetadataSpec;
                return new NftBriefInfo(
                    queryError: null,
                    tokenId: nft.TokenId,
                    owner: nft.OwnerEntity,
                    ownerAddress: GetOwnerAddressStringForDashboard(nft.OwnerAddress),
                    isMinted: nft.IsMinted,
                    typeName: nft.GetType().Name,
                    name: TryOrDefault(() => metadataSpec.TryGetName(nft), "<Name property threw an exception>"),
                    description: TryOrDefault(() => metadataSpec.TryGetDescription(nft), "<Description property threw an exception>"),
                    imageUrl: TryOrDefault(() => metadataSpec.TryGetImageUrl(nft), "<ImageUrl property threw an exception>"),
                    hasPendingMetadataWrite: pendingMetadataWrites.Contains(nft.TokenId));
            }

            public static NftBriefInfo Create(NftQueryItem item, HashSet<NftId> pendingMetadataWrites)
            {
                if (item.IsSuccess)
                    return Create(item.Nft, pendingMetadataWrites);
                else
                {
                    return new NftBriefInfo(
                        queryError: item.Error,
                        tokenId: item.NftKey.TokenId,
                        owner: EntityId.None,
                        ownerAddress: "<error>",
                        isMinted: false,
                        typeName: "<error>",
                        name: "<error>",
                        description: "<error>",
                        imageUrl: "<error>",
                        hasPendingMetadataWrite: pendingMetadataWrites.Contains(item.NftKey.TokenId));
                }
            }
        }

        public struct UninitializedNftBriefInfo
        {
            public NftId TokenId;
            public EntityId Owner;
            public string OwnerAddress;

            public UninitializedNftBriefInfo(NftId tokenId, NftManagerState.UninitializedNftInfo info)
            {
                TokenId = tokenId;
                Owner = info.OwnerEntityId;
                OwnerAddress = GetOwnerAddressStringForDashboard(info.OwnerAddress);
            }
        }

        public struct NftConfigWarning
        {
            public string Title;
            public string Message;

            public NftConfigWarning(string title, string message)
            {
                Title = title;
                Message = message;
            }
        }

        /// <summary>
        /// API endpoint to return information about an NFT collection
        /// Usage:  GET /api/nft/{COLLECTIONID}
        /// Test:   curl http://localhost:5550/api/nft/FooBar
        /// </summary>
        [HttpGet("nft/{collectionIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiNftView)]
        public async Task<ActionResult<NftCollectionInfo>> GetNftCollectionAsync(string collectionIdStr)
        {
            NftCollectionId collectionId = NftCollectionId.FromString(collectionIdStr);
            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();

            Web3Options.NftCollectionSpec collection = GetNftCollectionSpec(web3Options, collectionId);

            GetNftCollectionInfoResponse collectionInfoResponse = await AskEntityAsync<GetNftCollectionInfoResponse>(NftManager.EntityId, new GetNftCollectionInfoRequest(collectionId));
            NftManagerState.Collection collectionInfo = collectionInfoResponse.Collection;

            // Query the NFTs in this collection.

            List<NftQueryItem> queryItems = (await AskEntityAsync<QueryNftsResponse>(NftManager.EntityId, new QueryNftsRequest(collection: collectionId))).Items;
            IGameConfigDataResolver resolver = GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig.SharedConfig;

            foreach (NftQueryItem item in queryItems.Where(it => it.IsSuccess))
            {
                MetaNft nft = item.Nft;
                MetaSerialization.ResolveMetaRefs(ref nft, resolver);
            }

            // Query ongoing NFT metadata downloads (for init-NFTs-from-metadata operation)
            // concerning this collection.

            List<OngoingMetadataDownloadInfo> ongoingMetadataDownloads;
            {
                BackgroundTaskStatusResponse response = await AskEntityAsync<BackgroundTaskStatusResponse>(BackgroundTaskActor.EntityId, new BackgroundTaskStatusRequest(nameof(DownloadNftMetadatasTask)));

                IEnumerable<BackgroundTaskStatus> taskStatuses =
                    response.Tasks
                    .Where(task => ((DownloadNftMetadatasTask)task.Task).CollectionId == collectionId
                                   && !task.Completed);

                ongoingMetadataDownloads =
                    taskStatuses.Select(taskStatus =>
                    {
                        DownloadNftMetadatasTask task = (DownloadNftMetadatasTask)taskStatus.Task;
                        DownloadNftMetadatasTask.Output output = (DownloadNftMetadatasTask.Output)taskStatus.Output;

                        NftId firstTokenId = task.FirstTokenId;
                        int numTokens = task.NumTokens;

                        // \note NftId doesn't define general addition operator, so compute lastTokenId = firstTokenId + numTokens - 1
                        //       with a O(numTokens) loop... Inefficient, but we're dealing with a metadata download
                        //       task, so numTokens can't be massively large anyway.
                        NftId lastTokenId = firstTokenId;
                        for (int i = 0; i < numTokens-1; i++)
                            lastTokenId = lastTokenId.Plus1();

                        return new OngoingMetadataDownloadInfo(
                            taskId: taskStatus.Id,
                            firstTokenId: firstTokenId,
                            lastTokenId: lastTokenId,
                            numDownloaded: output.NumDownloaded,
                            numTotal: numTokens);
                    }).ToList();
            }

            // Resolve config warnings for dashboard.
            // Namely, mismatch between metadata api url in server options vs. ledger configuration.

            string metadataApiUrl = web3Options.GetNftCollectionMetadataApiUrl(collection);

            NftConfigWarning? configWarning;
            if (collectionInfo.LedgerInfoMaybe != null
             && collectionInfo.LedgerInfoMaybe.MetadataApiUrl != metadataApiUrl)
            {
                configWarning = new NftConfigWarning(
                    title: "Mismatching metadata API URL configuration",
                    message: $"Metadata API URL is configured as {metadataApiUrl} in the server options, but as {collectionInfo.LedgerInfoMaybe.MetadataApiUrl} in {collection.Ledger.GetName()}. Please verify your configurations.");
            }
            else
                configWarning = null;

            return Ok(new NftCollectionInfo()
            {
                CollectionId = collectionId,
                Ledger = collection.Ledger,
                HasLedger = collection.Ledger.IsRealLedger(),
                LedgerName = collection.Ledger.GetName(),
                ContractAddress = collection.ContractAddress,
                MetadataApiUrl = web3Options.GetNftCollectionMetadataApiUrl(collection),
                MetadataManagementMode = collection.MetadataManagementMode,

                BatchInitPlaceholderText = GenerateNftCollectionBatchInitPlaceholderText(collectionId),

                LedgerInfo = collectionInfo.LedgerInfoMaybe,

                Nfts = queryItems.Select(item => NftBriefInfo.Create(item, collectionInfoResponse.PendingMetadataWrites)).ToList(),

                OngoingMetadataDownloads = ongoingMetadataDownloads,

                UninitializedNfts = collectionInfo.RecentUninitializedNfts
                                    .Select(kv => new UninitializedNftBriefInfo(tokenId: kv.Key, info: kv.Value))
                                    .ToList(),

                ConfigWarning = configWarning,
            });
        }

        public struct NftInfo
        {
            public string QueryError;

            public NftId TokenId;
            public EntityId Owner;
            public string OwnerAddress;
            /// <inheritdoc cref="MetaNft.IsMinted"/>
            public bool IsMinted;
            public string TypeName;
            public string Name;
            public string Description;
            public string ImageUrl;
            public string Metadata;
            public string MetadataUrl;
            public MetaNft Model;
            public string ModelSerializationError;
            public bool HasPendingMetadataWrite;

            public Web3Options.NftCollectionLedger Ledger;
            public bool HasLedger;
            public string LedgerName;
            public Web3Options.NftMetadataManagementMode MetadataManagementMode;
        }

        /// <summary>
        /// API endpoint to return information about an NFT
        /// Usage:  GET /api/nft/{COLLECTIONID}/{TOKENID}
        /// Test:   curl http://localhost:5550/api/nft/FooBar/12345
        /// </summary>
        [HttpGet("nft/{collectionIdStr}/{tokenIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiNftView)]
        public async Task<ActionResult<NftCollectionInfo>> GetNftAsync(string collectionIdStr, string tokenIdStr)
        {
            NftCollectionId collectionId = NftCollectionId.FromString(collectionIdStr);
            NftId tokenId = NftId.ParseFromString(tokenIdStr);
            NftKey nftKey = new NftKey(collectionId, tokenId);

            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();
            Web3Options.NftCollectionSpec collection = GetNftCollectionSpec(web3Options, collectionId);

            GetNftResponse nftResponse = await AskEntityAsync<GetNftResponse>(NftManager.EntityId, new GetNftRequest(nftKey));
            NftQueryItem queryItem = nftResponse.Item;
            IGameConfigDataResolver resolver = GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig.SharedConfig;

            if (queryItem.IsSuccess)
            {
                MetaNft nft = queryItem.Nft;
                MetaSerialization.ResolveMetaRefs(ref nft, resolver);

                NftMetadataSpec metadataSpec = NftTypeRegistry.Instance.GetSpec(nft.GetType()).MetadataSpec;
                string metadataString;
                try
                {
                    JObject metadata = metadataSpec.GenerateMetadata(nft);
                    metadataString = metadata.ToString();
                }
                catch (Exception ex)
                {
                    metadataString = $"<Exception while generating metadata: {ex}>";
                }

                string modelSerializationError;
                try
                {
                    _ = JsonSerialization.SerializeToString(nft, AdminApiJsonSerialization.Serializer);
                    modelSerializationError = null;
                }
                catch (Exception ex)
                {
                    modelSerializationError = $"Failed to serialize NFT state: {ex}";
                }

                return Ok(new NftInfo()
                {
                    TokenId = nft.TokenId,
                    Owner = nft.OwnerEntity,
                    OwnerAddress = GetOwnerAddressStringForDashboard(nft.OwnerAddress),
                    IsMinted = nft.IsMinted,
                    TypeName = nft.GetType().Name,
                    Name = TryOrDefault(() => metadataSpec.TryGetName(nft), "<Name property threw an exception>"),
                    Description = TryOrDefault(() => metadataSpec.TryGetDescription(nft), "<Description property threw an exception>"),
                    ImageUrl = TryOrDefault(() => metadataSpec.TryGetImageUrl(nft), "<ImageUrl property threw an exception>"),
                    Metadata = metadataString,
                    MetadataUrl = web3Options.GetNftMetadataUrl(collection, nft.TokenId),
                    Model = modelSerializationError == null ? nft : null,
                    ModelSerializationError = modelSerializationError,
                    HasPendingMetadataWrite = nftResponse.HasPendingMetadataWrite,

                    Ledger = collection.Ledger,
                    HasLedger = collection.Ledger.IsRealLedger(),
                    LedgerName = collection.Ledger.GetName(),
                    MetadataManagementMode = collection.MetadataManagementMode,
                });
            }
            else
            {
                return Ok(new NftInfo()
                {
                    QueryError = queryItem.Error,

                    TokenId = queryItem.NftKey.TokenId,
                    Owner = EntityId.None,
                    OwnerAddress = "<error>",
                    IsMinted = false,
                    TypeName = "<error>",
                    Name = "<error>",
                    Description = "<error>",
                    ImageUrl = "<error>",
                    Metadata = "<error>",
                    MetadataUrl = "<error>",
                    Model = null,
                    ModelSerializationError = "<error>",
                    HasPendingMetadataWrite = nftResponse.HasPendingMetadataWrite,

                    MetadataManagementMode = collection.MetadataManagementMode,
                });
            }
        }

        static string GetOwnerAddressStringForDashboard(NftOwnerAddress ownerAddress)
        {
            if (ownerAddress == NftOwnerAddress.None)
                return "None";
            else
                return ownerAddress.AddressString;
        }

        static T TryOrDefault<T>(Func<T> func, T defaultValue = default)
        {
            try
            {
                return func();
            }
            catch
            {
                return defaultValue;
            }
        }

        [MetaSerializable]
        public class NftInitializationParams
        {
            [MetaFormDisplayProps(
                displayName: "Token Id",
                DisplayHint = "If left empty, the backend will automatically select the next id after the largest existing id in this collection (or 0 if no tokens exist yet).",
                DisplayPlaceholder = "Enter token id or leave empty to set automatically")]
            [MetaFormFieldCustomValidator(typeof(NftIdStringOrOmittedFormValidator))]
            [MetaMember(1)] public string TokenId;
            [MetaMember(2)] public MetaNft Nft;
            [MetaFormDisplayProps(
                displayName: "Allow Overwrite",
                DisplayHint = "If overwriting is allowed and an NFT already exists with the same id, its state will be overwritten. Use with caution!")]
            [MetaMember(3)] public bool AllowOverwrite = false;
        }

        [HttpPost("nft/{collectionIdStr}/{tokenIdStr}/initialize")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiNftInitialize)]
        public async Task<ActionResult> InitializeNftAsync(string collectionIdStr, string tokenIdStr)
        {
            NftInitializationParams initParams = await ParseBodyAsync<NftInitializationParams>();

            // \todo #nft #nft-init-token-id-kludge Grep from dashboard for comment.
            if (tokenIdStr == "automaticTokenId")
                tokenIdStr = null;
            if (initParams.TokenId == "")
                initParams.TokenId = null;
            if (initParams.TokenId != tokenIdStr)
                throw new MetaplayHttpException(400, "Token id mismatch.", $"Token id passed in body ({initParams.TokenId}) doesn't match that passed in URL ({tokenIdStr}).");

            if (initParams.Nft == null)
                throw new MetaplayHttpException(400, "Null NFT state.", $"Initialization request contains null NFT state.");

            NftCollectionId collectionId = NftCollectionId.FromString(collectionIdStr);
            NftId? tokenId = tokenIdStr != null ? NftId.ParseFromString(tokenIdStr) : null;

            BatchInitializeNftsResponse response;
            try
            {
                response = await AskEntityAsync<BatchInitializeNftsResponse>(NftManager.EntityId, new BatchInitializeNftsRequest(
                    new List<BatchInitializeNftsRequest.NftInitSpec>
                    {
                        new BatchInitializeNftsRequest.NftInitSpec(tokenId, initParams.Nft, sourceInfo: "n/a")
                    },
                    collectionId,
                    shouldWriteMetadata: true,
                    shouldQueryOwnersFromLedger: true,
                    allowOverwrite: initParams.AllowOverwrite,
                    validateOnly: false));
            }
            catch (BatchInitializeNftsRefusal refusal)
            {
                throw new MetaplayHttpException(400, refusal.Message, refusal.Details);
            }
            catch (EntityAskRefusal refusal)
            {
                throw new MetaplayHttpException(400, "Failed to initialize NFT.", refusal.Message);
            }

            BatchInitializeNftsResponse.NftResponse nftResponse = response.Nfts.Single();
            MetaNft nft = nftResponse.Nft;
            await WriteAuditLogEventAsync(
                new NftEventBuilder(NftTypeRegistry.Instance.GetNftKey(nft),
                new NftEventInitialized(
                    initialState: nft,
                    overwrittenNftMaybe: nftResponse.OverwrittenNftMaybe,
                    NftEventInitialized.NftInitFlavor.Single)));

            return Ok();
        }

        /// <summary>
        /// Somewhat arbitrary safety limit for batch initialization,
        /// to avoid very long-running operations.
        /// </summary>
        public const int    BatchInitializationMaxNumTokens       =  1000;
        // \note Needs to be const to be used in attributes - so can't do BatchInitializationMaxNumTokens.ToString().
        //       Keep these in sync!
        public const string BatchInitializationMaxNumTokensString = "1000";

        [MetaSerializable]
        public class NftBatchInitializationParams
        {
            [MetaMember(1)] public string Csv;
            [MetaMember(2)] public bool AllowOverwrite = false;
            [MetaMember(3)] public bool ValidateOnly;
        }

        public class BatchInitNftMetadata
        {
            // Parsed from csv.
            public NftId? Id;
            public string NftClass;

            // Not parsed from sheet, but resolved based on NftClass.
            public Type ResolvedNftType;
        }

        public class NftBatchInitializationResponse
        {
            public bool IsSuccess;
            public List<NftBriefInfo> Nfts;
            public int NumNftsOverwritten;
            public ErrorInfo Error;

            public class ErrorInfo
            {
                public string Message;
                public string Details;

                public ErrorInfo(string message, string details)
                {
                    Message = message;
                    Details = details;
                }
            }

            NftBatchInitializationResponse(bool isSuccess, List<NftBriefInfo> nfts, int numNftsOverwritten, ErrorInfo error)
            {
                IsSuccess = isSuccess;
                Nfts = nfts;
                NumNftsOverwritten = numNftsOverwritten;
                Error = error;
            }

            public static NftBatchInitializationResponse Success(List<NftBriefInfo> nfts, int numNftsOverwritten)
                => new NftBatchInitializationResponse(isSuccess: true, nfts, numNftsOverwritten, error: null);

            public static NftBatchInitializationResponse Failure(string message, string details)
                => new NftBatchInitializationResponse(isSuccess: false, nfts: null, numNftsOverwritten: 0, new ErrorInfo(message, details));

            public static NftBatchInitializationResponse Failure(string message, Exception exception)
                => new NftBatchInitializationResponse(isSuccess: false, nfts: null, numNftsOverwritten: 0, new ErrorInfo(message, exception.ToString()));
        }

        [HttpPost("nft/{collectionIdStr}/batchInitialize")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiNftInitialize)]
        public async Task<ActionResult<NftBatchInitializationResponse>> BatchInitializeNftsAsync(string collectionIdStr)
        {
            NftBatchInitializationParams initParams = await ParseBodyAsync<NftBatchInitializationParams>();
            bool validateOnly = initParams.ValidateOnly;

            NftCollectionId collectionId = NftCollectionId.FromString(collectionIdStr);

            IEnumerable<Type> nftTypes = NftTypeRegistry.Instance.GetAllSpecs().Select(spec => spec.NftType);

            if (!nftTypes.Any())
            {
                return NftBatchInitializationResponse.Failure(
                    $"No {nameof(MetaNft)} subclasses defined.",
                    $"To initialize NFTs, there must exist at least 1 concrete subclass of {nameof(MetaNft)}, but there exist none.");
            }

            if (initParams.Csv == null)
                return NftBatchInitializationResponse.Failure("Null CSV.", "");
            if (initParams.Csv.Trim() == "")
                return NftBatchInitializationResponse.Failure("Empty CSV.", "");

            SpreadsheetContent sheet;
            try
            {
                sheet = GameConfigHelper.ParseCsvToSpreadsheet("NftBatchInit", Encoding.UTF8.GetBytes(initParams.Csv));
            }
            catch (Exception ex)
            {
                return NftBatchInitializationResponse.Failure("Failed to parse CSV.", ex);
            }

            // Add a synthetic id column and a value for it on each row.
            // It's only added to satisfy GameConfigSpreadsheetReader. We want each row to be its own item.
            // In particular, we cannot use the Id column (corresponding to BatchInitNftMetadata.Id) as the id,
            // because it is optional.
            // \todo A bit dirty to mutate sheet. Could construct new SpreadsheetContent instead.
            const string RowIdMemberName = "_RowId";
            {
                // Pad all rows with empty cells to equal length.
                // This ensures the appended row id cells will line up correctly.
                int maxColumns = sheet.Cells.Max(row => row.Count);
                foreach (List<SpreadsheetCell> row in sheet.Cells)
                {
                    while (row.Count < maxColumns)
                        row.Add(new SpreadsheetCell("", row: row[0].Row, column: row.Count));
                }

                // Add header cell
                {
                    List<SpreadsheetCell> header = sheet.Cells[0];
                    header.Add(new SpreadsheetCell($"{RowIdMemberName} #key", row: 0, column: header.Count));
                }

                // Add id cell on each row
                for (int rowNdx = 1; rowNdx < sheet.Cells.Count; rowNdx++)
                {
                    List<SpreadsheetCell> row = sheet.Cells[rowNdx];
                    string value = rowNdx.ToString(CultureInfo.InvariantCulture);
                    row.Add(new SpreadsheetCell(value, row: rowNdx, column: row.Count));
                }
            }

            // Parse sheet into GameConfigSyntaxTree.

            GameConfigSourceInfo sourceInfo = new SpreadsheetFileSourceInfo(sheet.Name);
            GameConfigBuildLog buildLog = new GameConfigBuildLog().WithSource(sourceInfo);
            List<GameConfigSyntaxTree.RootObject> readObjects;

            try
            {
                GameConfigParsePipeline.PreprocessSpreadsheet(buildLog, sheet, isLibrary: true);
                readObjects = GameConfigSpreadsheetReader.TransformLibrarySpreadsheet(buildLog, sheet);

                if (buildLog.HasErrors())
                    return NftBatchInitializationResponse.Failure("Failed to parse sheet.", string.Join("\n", buildLog.Messages));
            }
            catch (Exception ex)
            {
                return NftBatchInitializationResponse.Failure("Failed to parse sheet.", ex);
            }

            // Parse NFTs from the GameConfigSyntaxTree objects.
            //
            // In the following loop, we'll parse data from each syntax object as two different types:
            // BatchInitNftMetadata, and the NFT class type (which can depend dynamically on the metadata).
            // So we'll invoke GameConfigOutputItemParser twice for each syntax object, with different target types.
            // This differs from the typical game config item parsing, where each syntax object is parsed as just one type.

            // Different "unknown member" handling for metadata and actual NFT payloads.
            // Any members not belonging to metadata are assumed to belong to the NFT payload.
            GameConfigParseLibraryPipelineConfig metadataPipelineConfig = new GameConfigParseLibraryPipelineConfig(unknownMemberHandling: UnknownConfigMemberHandling.Ignore);
            GameConfigParseLibraryPipelineConfig nftPipelineConfig      = new GameConfigParseLibraryPipelineConfig(unknownMemberHandling: UnknownConfigMemberHandling.Error);

            List<BatchInitializeNftsRequest.NftInitSpec> batchInitNftSpecs = new List<BatchInitializeNftsRequest.NftInitSpec>();

            foreach (GameConfigSyntaxTree.RootObject obj in readObjects)
            {
                // Try to resolve the row number of the object
                string rowNumber = (obj.Location is GameConfigSpreadsheetLocation location)
                    ? (location.Rows.Start + 1).ToString(CultureInfo.InvariantCulture)
                    : "<unknown>";

                // Parse BatchInitNftMetadata which gets us the Id as well as the (optional) NFT class name.

                BatchInitNftMetadata metadata;
                try
                {
                    metadata = (BatchInitNftMetadata)GameConfigOutputItemParser.ParseObject(metadataPipelineConfig, buildLog, typeof(BatchInitNftMetadata), obj.Node);
                }
                catch (Exception ex)
                {
                    return NftBatchInitializationResponse.Failure(Invariant($"Failed to parse data at row number {rowNumber}."), ex);
                }

                if (buildLog.HasErrors())
                    return NftBatchInitializationResponse.Failure(Invariant($"Failed to parse data at row number {rowNumber}."), string.Join("\n", buildLog.Messages));

                // Resolve NFT type.

                try
                {
                    NftTypeSpec nftTypeSpec = NftUtil.ResolveNftTypeInCollection(collectionId, metadata.NftClass);
                    metadata.ResolvedNftType = nftTypeSpec.NftType;
                }
                catch (Exception ex)
                {
                    return NftBatchInitializationResponse.Failure(Invariant($"Failed to resolve NFT class type at row number {rowNumber}."), ex);
                }

                // Parse the NFT contents using the resolved NFT type.

                // First, create a copy of obj.Node, with all members removed that are expected to not
                // be members of the NFT class: the synthetic row if member, and members of BatchInitNftMetadata.
                // The remaining members are expected to correspond to members in the actual NFT class.
                // We do this to avoid false "unknown member" errors from ParseObject.
                // \todo A cleaner way would be nice. But we're already kind of abusing the config parser here.
                GameConfigSyntaxTree.ObjectNode nftNode = (GameConfigSyntaxTree.ObjectNode)obj.Node.Clone();
                nftNode.Members.Remove(new GameConfigSyntaxTree.NodeMemberId(name: RowIdMemberName, variantId: null));
                nftNode.Members.RemoveWhere(kv => typeof(BatchInitNftMetadata).GetMember(kv.Key.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any());

                MetaNft nft;
                try
                {
                    nft = (MetaNft)GameConfigOutputItemParser.ParseObject(nftPipelineConfig, buildLog, metadata.ResolvedNftType, nftNode);
                }
                catch (Exception ex)
                {
                    return NftBatchInitializationResponse.Failure(Invariant($"Failed to parse {metadata.ResolvedNftType.Name} from row number {rowNumber}."), ex);
                }

                if (buildLog.HasErrors())
                    return NftBatchInitializationResponse.Failure(Invariant($"Failed to parse data at row number {rowNumber}."), string.Join("\n", buildLog.Messages));

                batchInitNftSpecs.Add(new BatchInitializeNftsRequest.NftInitSpec(
                    metadata.Id,
                    nft,
                    sourceInfo: Invariant($"row number {rowNumber}")));
            }

            if (batchInitNftSpecs.Count < 1)
                return NftBatchInitializationResponse.Failure("Number of tokens is less than 1.", Invariant($"Must initialize at least 1 token; got {batchInitNftSpecs.Count}."));
            if (batchInitNftSpecs.Count > BatchInitializationMaxNumTokens)
                return NftBatchInitializationResponse.Failure("Number of tokens exceeds safety limit.", Invariant($"For safety, the number of tokens initialized in one batch must be at most {BatchInitializationMaxNumTokens}; got {batchInitNftSpecs.Count}."));

            BatchInitializeNftsResponse response;
            try
            {
                response = await AskEntityAsync<BatchInitializeNftsResponse>(NftManager.EntityId,
                    new BatchInitializeNftsRequest(
                        batchInitNftSpecs,
                        collectionId,
                        shouldWriteMetadata: true,
                        shouldQueryOwnersFromLedger: false, // Don't query from ledger (like we do in single-init case) - could get too spammy in big batch inits.
                        allowOverwrite: initParams.AllowOverwrite,
                        validateOnly: validateOnly));
            }
            catch (BatchInitializeNftsRefusal refusal)
            {
                return NftBatchInitializationResponse.Failure(refusal.Message, refusal.Details);
            }
            catch (EntityAskRefusal refusal)
            {
                return NftBatchInitializationResponse.Failure("Validation failure.", refusal.Message);
            }

            IGameConfigDataResolver resolver = GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig.SharedConfig;
            foreach (BatchInitializeNftsResponse.NftResponse nftResponse in response.Nfts)
            {
                MetaNft nft = nftResponse.Nft;
                MetaSerialization.ResolveMetaRefs(ref nft, resolver);
            }

            if (!validateOnly)
            {
                foreach (BatchInitializeNftsResponse.NftResponse nftResponse in response.Nfts)
                {
                    await WriteAuditLogEventAsync(
                        new NftEventBuilder(NftTypeRegistry.Instance.GetNftKey(nftResponse.Nft),
                        new NftEventInitialized(
                            initialState: nftResponse.Nft,
                            overwrittenNftMaybe: nftResponse.OverwrittenNftMaybe,
                            NftEventInitialized.NftInitFlavor.Batch)));
                }
            }

            return NftBatchInitializationResponse.Success(
                nfts: response.Nfts.Select(r => NftBriefInfo.Create(r.Nft, pendingMetadataWrites: new HashSet<NftId>())).ToList(),
                numNftsOverwritten: response.Nfts.Count(r => r.OverwrittenNftMaybe != null));
        }

        string GenerateNftCollectionBatchInitPlaceholderText(NftCollectionId collectionId)
        {
            List<Type> nftTypes = NftTypeRegistry.Instance.GetAllSpecs()
                                  .Where(spec => spec.CollectionId == collectionId)
                                  .Select(spec => spec.NftType)
                                  .ToList();

            if (nftTypes.Count == 0)
                return "";

            Type nftType = nftTypes.First(); // If there's multiple types, use the first one as an example. \todo Could be more comprehensive.

            IEnumerable<MemberInfo> nftTypeUserFields = MetaSerializerTypeRegistry.GetTypeSpec(nftType).Members
                                                        .Select(m => m.MemberInfo)
                                                        .Where(m => m.DeclaringType != typeof(MetaNft));

            List<string> header = new List<string>();
            header.Add(nameof(BatchInitNftMetadata.Id));
            header.Add(nameof(BatchInitNftMetadata.NftClass));
            foreach (MemberInfo member in nftTypeUserFields)
                header.Add(member.Name);

            // \todo This only produces example input for the Id and NftClass fields,
            //       as the user fields can be whatever and it's not easy to produce
            //       sensible example values for those. But could still produce example
            //       values for some common types.
            List<string> partialExampleRow = new List<string>();
            partialExampleRow.Add("123");
            partialExampleRow.Add(nftType.Name);

            return string.Join(",", header) + "\n"
                   + string.Join(",", partialExampleRow) + ",...game-specific fields here...\n"
                   + "...";
        }

        [MetaSerializable]
        public class NftBatchInitializationFromMetadataParams
        {
            [MetaFormDisplayProps("First Token Id")]
            [MetaFormFieldCustomValidator(typeof(NftIdStringFormValidator))]
            [MetaMember(1)] public string FirstTokenId;

            [MetaFormDisplayProps(
                displayName: "Number of Tokens",
                DisplayHint = $"Currently a safety limit of {BatchInitializationMaxNumTokensString} is imposed to avoid very long-running operations. If you need to initialize more NFTs, please do it using multiple batch initialization operations.")]
            [MetaValidateInRange(
                min: 1,
                max: BatchInitializationMaxNumTokens)]
            [MetaMember(2)] public int NumTokens = 1;

            [MetaFormDisplayProps(
                displayName: "MetaNft C# class name",
                DisplayHint = "Name of the C# MetaNft subclass to use. If there is only one class belonging to this collection, this can be left empty.")]
            [MetaFormFieldCustomValidator(typeof(NftClassNameFormValidator))]
            [MetaMember(3)] public string NftClass;

            [MetaFormDisplayProps(
                displayName: "Allow Overwrite",
                DisplayHint = "If overwriting is allowed, NFTs from this batch will overwrite the state of any existing NFTs with the same ids. Use with caution!")]
            [MetaMember(4)] public bool AllowOverwrite = false;
        }

        public class NftBatchInitializationFromMetadataResponse
        {
            public List<NftBriefInfo> Nfts;
            public int NumNftsOverwritten;

            public NftBatchInitializationFromMetadataResponse(List<NftBriefInfo> nfts, int numNftsOverwritten)
            {
                Nfts = nfts;
                NumNftsOverwritten = numNftsOverwritten;
            }
        }

        [HttpPost("nft/{collectionIdStr}/batchInitializeFromMetadata")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiNftInitialize)]
        public async Task<ActionResult<NftBatchInitializationFromMetadataResponse>> BatchInitializeNftsFromMetadataAsync(string collectionIdStr)
        {
            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();

            NftBatchInitializationFromMetadataParams initParams = await ParseBodyAsync<NftBatchInitializationFromMetadataParams>();

            NftCollectionId collectionId = NftCollectionId.FromString(collectionIdStr);

            if (web3Options.TryGetNftCollection(collectionId) == null)
                throw new InvalidEntityAsk($"No collection {collectionId} has been configured");

            NftId firstTokenId = NftId.ParseFromString(initParams.FirstTokenId);
            int numTokens = initParams.NumTokens;

            if (numTokens < 1)
                throw new MetaplayHttpException(400, "Number of tokens is less than 1.", $"Must initialize at least 1 token; got {numTokens}.");
            if (numTokens > BatchInitializationMaxNumTokens)
                throw new MetaplayHttpException(400, "Number of tokens exceeds safety limit.", $"For safety, the number of tokens initialized in one batch must be at most {BatchInitializationMaxNumTokens}; got {numTokens}.");

            NftTypeSpec nftTypeSpec;
            try
            {
                nftTypeSpec = NftUtil.ResolveNftTypeInCollection(collectionId, initParams.NftClass);
            }
            catch (Exception ex)
            {
                throw new MetaplayHttpException(400, "Failed to resolve NFT class.", ex.Message);
            }

            // Unless overwrite is allowed, check the specified token id range is unpopulated.
            // \note The situation can still change between this check and the actual initialization of the NFTs.
            //       NftManager does its own check before fulfilling BatchInitializeNftsFromMetadataRequest.
            //       This check here is just an early check to catch mistakes before performing the more time-consuming
            //       parts of this admin api request, in particular the downloading of the metadatas.
            if (!initParams.AllowOverwrite)
            {
                TryGetExistingNftIdInRangeResponse response = await AskEntityAsync<TryGetExistingNftIdInRangeResponse>(NftManager.EntityId, new TryGetExistingNftIdInRangeRequest(collectionId, firstTokenId, numTokens));
                if (response.Id.HasValue)
                    throw new MetaplayHttpException(400, "Tokens already exist in the given id range.", $"NFT with id {response.Id.Value} already exists in collection {collectionId}.");
            }

            // Start background task for downloading the metadatas.
            // The ongoing task gets shown on the dashboard's NFT collection page,
            // but here in this controller we also wait for the task to complete,
            // and then proceed with the NFT initialization.
            // \todo Is there a relevant situation where this controller might abort
            //       before the background task is complete, leaving the completed
            //       task dangling?

            MetaGuid downloadTaskId = MetaGuid.New();
            _ = await AskEntityAsync<StartBackgroundTaskResponse>(BackgroundTaskActor.EntityId, new StartBackgroundTaskRequest(downloadTaskId, new DownloadNftMetadatasTask(collectionId, firstTokenId, numTokens)));

            BackgroundTaskStatus downloadTaskStatus;
            while (true)
            {
                BackgroundTaskStatusResponse response = await AskEntityAsync<BackgroundTaskStatusResponse>(BackgroundTaskActor.EntityId, new BackgroundTaskStatusRequest(nameof(DownloadNftMetadatasTask)));
                downloadTaskStatus = response.Tasks.Single(task => task.Id == downloadTaskId);
                if (downloadTaskStatus.Completed)
                {
                    _ = await AskEntityAsync<StartBackgroundTaskResponse>(BackgroundTaskActor.EntityId, new ForgetBackgroundTaskRequest(downloadTaskId));
                    break;
                }
                await Task.Delay(2000);
            }

            if (downloadTaskStatus.Failure != null)
                throw new MetaplayHttpException(400, "Failed to download NFT metadata.", downloadTaskStatus.Failure);

            // Instantiate NFTs from the metadatas.

            DownloadNftMetadatasTask.Output taskOutput = (DownloadNftMetadatasTask.Output)downloadTaskStatus.Output;
            OrderedDictionary<NftId, byte[]> metadatas = taskOutput.Metadatas;

            ISharedGameConfig sharedGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig.SharedConfig;
            NftMetadataImportContext metadataImportContext = new NftMetadataImportContext(sharedGameConfig);

            List<MetaNft> nfts = new List<MetaNft>();
            foreach ((NftId nftId, byte[] metadataBytes) in metadatas)
            {
                JObject metadata;
                try
                {
                    metadata = JsonSerialization.Deserialize<JObject>(metadataBytes);
                }
                catch (Exception ex)
                {
                    throw new MetaplayHttpException(400, "Failed to parse metadata JSON.", $"Failed to parse JSON for token {nftId}: {ex}");
                }

                MetaNft nft;
                try
                {
                    nft = NftUtil.CreateNftFromMetadata(
                        nftTypeSpec.NftType,
                        nftTypeSpec.MetadataSpec,
                        metadata,
                        metadataImportContext,
                        // Initial NFT base properties.
                        tokenId: nftId,
                        ownerEntity: EntityId.None,
                        ownerAddress: NftOwnerAddress.None,
                        isMinted: false,
                        updateCounter: 0);
                }
                catch (Exception ex)
                {
                    throw new MetaplayHttpException(400, "Failed to map metadata to C# NFT.", $"Failed to create token {nftId} from its metadata: {ex}");
                }

                nfts.Add(nft);
            }

            // Tell NftManager to initialize the NFTs.


            BatchInitializeNftsResponse batchInitResponse;
            try
            {
                IEnumerable<BatchInitializeNftsRequest.NftInitSpec> nftInitSpecs = nfts.Select(nft => new BatchInitializeNftsRequest.NftInitSpec(nft.TokenId, nft, sourceInfo: $"id={nft.TokenId}"));
                batchInitResponse = await AskEntityAsync<BatchInitializeNftsResponse>(NftManager.EntityId,
                    new BatchInitializeNftsRequest(
                        nftInitSpecs.ToList(),
                        collectionId,
                        shouldWriteMetadata: false, // Don't write metadata, because these NFTs were just initialized from metadata.
                        shouldQueryOwnersFromLedger: false, // Don't query from ledger (like we do in single-init case) - could get too spammy in big batch inits.
                        allowOverwrite: initParams.AllowOverwrite,
                        validateOnly: false));
            }
            catch (BatchInitializeNftsRefusal refusal)
            {
                throw new MetaplayHttpException(400, refusal.Message, refusal.Details);
            }
            catch (EntityAskRefusal refusal)
            {
                throw new MetaplayHttpException(400, "Failed to initialize NFTs from metadata.", refusal.Message);
            }

            IGameConfigDataResolver resolver = GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig.SharedConfig;
            foreach (BatchInitializeNftsResponse.NftResponse nftResponse in batchInitResponse.Nfts)
            {
                MetaNft nft = nftResponse.Nft;
                MetaSerialization.ResolveMetaRefs(ref nft, resolver);
            }

            foreach (BatchInitializeNftsResponse.NftResponse nftResponse in batchInitResponse.Nfts)
            {
                await WriteAuditLogEventAsync(
                    new NftEventBuilder(NftTypeRegistry.Instance.GetNftKey(nftResponse.Nft),
                    new NftEventInitialized(
                        initialState: nftResponse.Nft,
                        overwrittenNftMaybe: nftResponse.OverwrittenNftMaybe,
                        NftEventInitialized.NftInitFlavor.BatchFromMetadata)));
            }

            return new NftBatchInitializationFromMetadataResponse(
                nfts: batchInitResponse.Nfts.Select(r => NftBriefInfo.Create(r.Nft, pendingMetadataWrites: new HashSet<NftId>())).ToList(),
                numNftsOverwritten: batchInitResponse.Nfts.Count(r => r.OverwrittenNftMaybe != null));
        }

        [HttpPost("players/{ownerIdStr}/refreshOwnedNfts")]
        [RequirePermission(MetaplayPermissions.ApiNftRefreshFromLedger)]
        public async Task<ActionResult> RefreshOwnedNfts(string ownerIdStr)
        {
            EntityId ownerId = ParsePlayerIdStr(ownerIdStr);

            try
            {
                _ = await AskEntityAsync<RefreshOwnedNftsResponse>(ownerId, new RefreshOwnedNftsRequest());
            }
            catch (EntityAskRefusal refusal)
            {
                await WriteAuditLogEventAsync(new PlayerEventBuilder(ownerId, new PlayerEventOwnedNftsRefreshed(isSuccess: false, refusal.Message)));
                throw new MetaplayHttpException((int)HttpStatusCode.InternalServerError, "Refresh failed.", refusal.Message);
            }

            await WriteAuditLogEventAsync(new PlayerEventBuilder(ownerId, new PlayerEventOwnedNftsRefreshed(isSuccess: true, error: null)));

            return Ok();
        }

        [HttpPost("nft/{collectionIdStr}/{tokenIdStr}/refresh")]
        [RequirePermission(MetaplayPermissions.ApiNftRefreshFromLedger)]
        public async Task<ActionResult> RefreshSingleNft(string collectionIdStr, string tokenIdStr)
        {
            NftCollectionId collectionId = NftCollectionId.FromString(collectionIdStr);
            NftId tokenId = NftId.ParseFromString(tokenIdStr);
            NftKey nftKey = new NftKey(collectionId, tokenId);

            try
            {
                _ = await AskEntityAsync<RefreshNftResponse>(NftManager.EntityId, new RefreshNftRequest(nftKey));
            }
            catch (EntityAskRefusal refusal)
            {
                await WriteAuditLogEventAsync(new NftEventBuilder(nftKey, new NftEventRefreshed(isSuccess: false, refusal.Message)));
                throw new MetaplayHttpException((int)HttpStatusCode.InternalServerError, "Failed to refresh NFT from ledger.", refusal.Message);
            }

            await WriteAuditLogEventAsync(new NftEventBuilder(nftKey, new NftEventRefreshed(isSuccess: true, error: null)));

            return Ok();
        }

        [HttpPost("nft/{collectionIdStr}/refresh")]
        [RequirePermission(MetaplayPermissions.ApiNftRefreshFromLedger)]
        public async Task<ActionResult> RefreshCollection(string collectionIdStr)
        {
            NftCollectionId collectionId = NftCollectionId.FromString(collectionIdStr);

            try
            {
                _ = await AskEntityAsync<RefreshCollectionLedgerInfoResponse>(NftManager.EntityId, new RefreshCollectionLedgerInfoRequest(collectionId));
            }
            catch (EntityAskRefusal refusal)
            {
                await WriteAuditLogEventAsync(new NftCollectionEventBuilder(collectionId, new NftCollectionRefreshed(isSuccess: false, refusal.Message)));
                throw new MetaplayHttpException((int)HttpStatusCode.InternalServerError, "Refresh failed.", refusal.Message);
            }

            await WriteAuditLogEventAsync(new NftCollectionEventBuilder(collectionId, new NftCollectionRefreshed(isSuccess: true, error: null)));

            return Ok();
        }

        [HttpPost("nft/{collectionIdStr}/{tokenIdStr}/republishMetadata")]
        [RequirePermission(MetaplayPermissions.ApiNftRepublishMetadata)]
        public async Task<ActionResult> RepublishNftMetadata(string collectionIdStr, string tokenIdStr)
        {
            NftCollectionId collectionId = NftCollectionId.FromString(collectionIdStr);
            NftId tokenId = NftId.ParseFromString(tokenIdStr);
            NftKey nftKey = new NftKey(collectionId, tokenId);

            _ = await AskEntityAsync<RepublishNftMetadataResponse>(NftManager.EntityId, new RepublishNftMetadataRequest(nftKey));
            await WriteAuditLogEventAsync(new NftEventBuilder(nftKey, new NftEventMetadataRepublished()));

            return Ok();
        }

        [MetaSerializable]
        public class NftEditParams
        {
            [MetaMember(1)] public MetaNft Nft;
        }

        [HttpPost("nft/{collectionIdStr}/{tokenIdStr}/edit")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiNftEdit)]
        public async Task<ActionResult> EditNftAsync(string collectionIdStr, string tokenIdStr)
        {
            NftCollectionId collectionId = NftCollectionId.FromString(collectionIdStr);
            NftId tokenId = NftId.ParseFromString(tokenIdStr);
            NftKey nftKey = new NftKey(collectionId, tokenId);
            NftEditParams editParams = await ParseBodyAsync<NftEditParams>();

            MetaNft nft = editParams.Nft;

            NftCollectionId newCollectionId = NftTypeRegistry.Instance.GetCollectionId(nft);
            if (NftTypeRegistry.Instance.GetCollectionId(nft) != collectionId)
                throw new MetaplayHttpException(400, "Collection id mismatch.", $"Attempted to edit NFT {tokenId} in collection {collectionId}, but the new NFT state is of type {nft.GetType().Name} which belongs to collection {newCollectionId}.");

            {
                TryGetExistingNftIdInRangeResponse response = await AskEntityAsync<TryGetExistingNftIdInRangeResponse>(NftManager.EntityId, new TryGetExistingNftIdInRangeRequest(collectionId, tokenId, 1));
                if (!response.Id.HasValue)
                    throw new MetaplayHttpException(400, "No such token.", $"Attempted to edit NFT {nftKey}, but no such NFT exists in the game yet.");
            }

            {
                BatchInitializeNftsResponse response;
                try
                {
                    response = await AskEntityAsync<BatchInitializeNftsResponse>(NftManager.EntityId, new BatchInitializeNftsRequest(
                        new List<BatchInitializeNftsRequest.NftInitSpec>
                        {
                            new BatchInitializeNftsRequest.NftInitSpec(tokenId, nft, sourceInfo: "n/a")
                        },
                        collectionId: collectionId,
                        shouldWriteMetadata: true,
                        shouldQueryOwnersFromLedger: false,
                        allowOverwrite: true,
                        validateOnly: false));
                }
                catch (BatchInitializeNftsRefusal refusal)
                {
                    throw new MetaplayHttpException(400, refusal.Message, refusal.Details);
                }
                catch (EntityAskRefusal refusal)
                {
                    throw new MetaplayHttpException(400, "Failed to edit NFT.", refusal.Message);
                }

                BatchInitializeNftsResponse.NftResponse nftResponse = response.Nfts.Single();
                await WriteAuditLogEventAsync(new NftEventBuilder(nftKey, new NftEventStateEdited(oldState: nftResponse.OverwrittenNftMaybe, newState: nftResponse.Nft)));
            }

            return Ok();
        }

        static Web3Options.NftCollectionSpec GetNftCollectionSpec(Web3Options web3Options, NftCollectionId collectionId)
        {
            return web3Options.TryGetNftCollection(collectionId)
                   ?? throw new MetaplayHttpException(404, "No such NFT collection", $"Cannot find collection {collectionId}");
        }

        public abstract class NftIdStringFormValidatorBase : IMetaFormValidator
        {
            bool _allowNullOrEmpty;

            public NftIdStringFormValidatorBase(bool allowNullOrEmpty)
            {
                _allowNullOrEmpty = allowNullOrEmpty;
            }

            public void Validate(object field, FormValidationContext ctx)
            {
                if (field == null)
                {
                    if (!_allowNullOrEmpty)
                        ctx.Fail("Id must be provided");
                    return;
                }

                if (!(field is string))
                {
                    ctx.Fail($"string-typed field expected, got type {field?.GetType().ToGenericTypeString()} instead");
                    return;
                }

                string idString = (string)field;

                if (idString == "")
                {
                    if (!_allowNullOrEmpty)
                        ctx.Fail("Id must be provided");
                    return;
                }

                try
                {
                    NftId.ParseFromString(idString);
                }
                catch (Exception ex)
                {
                    ctx.Fail(ex.Message);
                }
            }
        }

        public class NftIdStringOrOmittedFormValidator : NftIdStringFormValidatorBase
        {
            public NftIdStringOrOmittedFormValidator()
                : base(allowNullOrEmpty: true)
            {
            }
        }

        public class NftIdStringFormValidator : NftIdStringFormValidatorBase
        {
            public NftIdStringFormValidator()
                : base(allowNullOrEmpty: false)
            {
            }
        }

        public class NftClassNameFormValidator : IMetaFormValidator
        {
            public void Validate(object field, FormValidationContext ctx)
            {
                if (field == null)
                    return;

                if (!(field is string))
                {
                    ctx.Fail($"string-typed field expected, got type {field?.GetType().ToGenericTypeString()} instead");
                    return;
                }

                string className = (string)field;

                if (className == "")
                    return;

                IEnumerable<string> nftTypeNames = NftTypeRegistry.Instance.GetAllSpecs().Select(spec => spec.NftType.Name);

                if (!nftTypeNames.Contains(className))
                {
                    ctx.Fail(
                        $"'{className}' is not a known concrete subclass of {nameof(MetaNft)}. " +
                        $"Known classes: {string.Join(", ", nftTypeNames)}.");
                    return;
                }
            }
        }
    }
}
