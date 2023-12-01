// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Cloud.Web3;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using Metaplay.Core.Web3;
using Metaplay.Server.Authentication;
using Metaplay.Server.Authentication.Authenticators;
using Metaplay.Server.Database;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;

namespace Metaplay.Server.Web3
{
    [Web3EnabledCondition]
    [Table("NftManagers")]
    public class PersistedNftManager : IPersistedEntity
    {
        [Key]
        [PartitionKey]
        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string   EntityId        { get; set; }

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime PersistedAt     { get; set; }

        [Required]
        public byte[]   Payload         { get; set; }

        [Required]
        public int      SchemaVersion   { get; set; }

        [Required]
        public bool     IsFinal         { get; set; }
    }

    [Web3EnabledCondition]
    [Table("Nfts")]
    [Index(nameof(CollectionId))]
    [Index(nameof(StringComparableTokenIdEquivalent))]
    [Index(nameof(OwnerEntityId))]
    [Index(nameof(OwnerAddress))]
    public class PersistedNft : IPersistedItem
    {
        /// <summary>
        /// Combination of collection id and the NFT's collection-local id.
        /// </summary>
        [Key]
        [PartitionKey]
        [Required]
        [MaxLength(256)]
        [Column(TypeName = "varchar(256)")]
        public string   GlobalId        { get; set; }

        [Required]
        [MaxLength(128)]
        [Column(TypeName = "varchar(128)")]
        public string   CollectionId    { get; set; }

        [Required]
        [MaxLength(128)]
        [Column(TypeName = "varchar(128)")]
        public string   TokenId         { get; set; }

        /// <summary>
        /// A string which, when ordinally string-compared among NFTs, yields
        /// equivalent ordering as if TokenId was compared numerically.
        /// You can use this in SQL to order by increasing/decreasing TokenId.
        /// </summary>
        /// <remarks>
        /// This is produced by <see cref="NftManager.GetLengthPrefixedTokenIdString(NftId)"/>.
        /// </remarks>
        [Required]
        [MaxLength(128)]
        [Column(TypeName = "varchar(128)")]
        public string   StringComparableTokenIdEquivalent { get; set; }

        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string   OwnerEntityId   { get; set; }

        [MaxLength(128)]
        [Column(TypeName = "varchar(128)")]
        public string   OwnerAddress    { get; set; }

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime PersistedAt     { get; set; }

        [Required]
        public byte[]   Payload         { get; set; }

        [Required]
        public ulong    UpdateCounter   { get; set; }

        /// <summary>
        /// Schema version used for user-defined <see cref="ISchemaMigratable"/> schema migrations
        /// of the <see cref="MetaNft"/> serialized in <see cref="Payload"/>.
        /// Note the difference from <see cref="SchemaVersion"/>!
        /// </summary>
        [Required]
        public int      MetaNftUserSchemaVersion { get; set; }

        /// <summary>
        /// SDK-level schema version for the <see cref="PersistedNft"/> itself.
        /// Note the difference from <see cref="MetaNftUserSchemaVersion"/>!
        /// </summary>
        [Required]
        public int      SchemaVersion   { get; set; }

        public NftKey NftKey => new NftKey(
            NftCollectionId.FromString(CollectionId),
            NftId.ParseFromString(TokenId));
    }

    [Web3EnabledCondition]
    [EntityConfig]
    internal sealed class NftManagerConfig : PersistedEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.NftManager;
        public override Type                EntityActorType         => typeof(NftManager);
        public override IShardingStrategy   ShardingStrategy        => new ServiceShardingStrategy(numServiceShards: 1);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(10);
    }

    [MetaSerializable]
    [SupportedSchemaVersions(1, 1)]
    public class NftManagerState : ISchemaMigratable
    {
        [MetaMember(1)] public OrderedDictionary<NftCollectionId, Collection> Collections = new();

        /// <summary>
        /// Bookkeeping for a one-time migration for populating <see cref="PersistedNft.StringComparableTokenIdEquivalent"/>
        /// in all existing persisted NFTs.
        /// </summary>
        [MetaMember(2)] public bool HasPopulatedPersistedNftStringComparableTokenIds = false;

        [MetaSerializable]
        public class Collection
        {
            [MetaMember(1)] public MetaTime?                                        ObservedToTime      = null;
            [MetaMember(2)] public NftCollectionLedgerInfo                          LedgerInfoMaybe     = null;
            [MetaMember(3)] public OrderedDictionary<NftId, UninitializedNftInfo>   RecentUninitializedNfts = new();
            public const int MaxRememberedUninitializedNfts = 100;

            /// <summary>
            /// Record of metadata writes that need to be completed.
            /// Metadata writes happen in the background so as not to block other
            /// operations of NftManager, in case a big batch of metadatas need
            /// to be written.
            ///
            /// When new NFTs are initialized (or edited), entries are added here
            /// before the NFTs are inserted (or updated) in the database.
            /// This is therefore conservative: an entry may exist here without
            /// the corresponding NFT existing in database, in case the actor
            /// crashes after writing this.
            ///
            /// Entries are removed from here after the background write operation
            /// is complete.
            /// </summary>
            /// <remarks>
            /// Related transient-state bookkeeping exists in <see cref="NftManager.TransientState.PendingMetadataWrites"/>
            /// and <see cref="NftManager.TransientState.OngoingMetadataWrite"/>.
            /// </remarks>
            [MetaMember(4)] public OrderedSet<NftId> PendingMetadataWrites = new();
        }

        [MetaSerializable]
        public struct UninitializedNftInfo
        {
            [MetaMember(1)] public EntityId OwnerEntityId;
            [MetaMember(2)] public NftOwnerAddress OwnerAddress;

            public UninitializedNftInfo(EntityId ownerEntityId, NftOwnerAddress ownerAddress)
            {
                OwnerEntityId = ownerEntityId;
                OwnerAddress = ownerAddress;
            }
        }

        public void RememberUninitializedNft(NftKey key, UninitializedNftInfo info)
        {
            Collection collection = Collections[key.CollectionId];

            collection.RecentUninitializedNfts.Remove(key.TokenId); // \note First, remove the item's old occurrence (if any), so the new one gets added to the end.
            collection.RecentUninitializedNfts.Add(key.TokenId, info);
            while (collection.RecentUninitializedNfts.Count > Collection.MaxRememberedUninitializedNfts)
                collection.RecentUninitializedNfts.Remove(collection.RecentUninitializedNfts.First().Key);
        }

        public void ForgetUninitializedNft(NftKey key)
        {
            Collection collection = Collections[key.CollectionId];
            collection.RecentUninitializedNfts.Remove(key.TokenId);
        }
    }

    public class NftManager : PersistedEntityActor<PersistedNftManager, NftManagerState>
    {
        protected override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();
        protected override TimeSpan SnapshotInterval => TimeSpan.FromMinutes(3);

        public static readonly EntityId EntityId = EntityId.Create(EntityKindCloudCore.NftManager, 0);

        NftManagerState _state;
        TransientState  _transientState;

        NftLedgerDataFetcher _ledgerDataFetcher;

        class TransientState
        {
            public OrderedDictionary<NftCollectionId, Collection> Collections = new();

            /// <summary>
            /// NFTs for which a metadata write is pending but hasn't yet been started.
            /// Just before an NFT is written to database, an entry is added here
            /// (and in the persistent <see cref="NftManagerState.Collection.PendingMetadataWrites"/>).
            /// When a metadata write background operation starts, all the NFTs included
            /// in that operation are removed from this set and put into
            /// <see cref="OngoingMetadataWriteState.RemainingNfts"/>.
            /// However, an NFT isn't removed from the persistent
            /// <see cref="NftManagerState.Collection.PendingMetadataWrites"/>
            /// until the metadata has actually been written.
            ///
            /// This is also populated based on the persistent
            /// <see cref="NftManagerState.Collection.PendingMetadataWrites"/>
            /// at actor start, in case previous pending metadata writes
            /// didn't complete.
            /// </summary>
            public OrderedSet<NftKey> PendingMetadataWrites = new();
            /// <summary>
            /// State for a background metadata write operation.
            /// </summary>
            public OngoingMetadataWriteState OngoingMetadataWrite = null;

            public class Collection
            {
                public NftId? MaxPersistedNftId;
                public ImmutableXNftObserver ImmutableXNftObserver;
                public IBlobStorage MetadataStorage;
            }

            public class OngoingMetadataWriteState
            {
                /// <summary>
                /// Lock for updating this <see cref="OngoingMetadataWriteState"/>.
                /// </summary>
                public object Lock = new object();
                /// <summary>
                /// The NFTs still waiting to have their metadata written.
                /// This is for reporting progress to the dashboard.
                /// </summary>
                public OrderedSet<NftKey> RemainingNfts;

                public OngoingMetadataWriteState(OrderedSet<NftKey> remainingNfts)
                {
                    RemainingNfts = remainingNfts;
                }
            }
        }

        class PollObservers { public static readonly PollObservers Instance = new PollObservers(); }
        class FlushPendingMetadataWrites { public static readonly FlushPendingMetadataWrites Instance = new FlushPendingMetadataWrites(); }
        class RefreshCollectionInfoFromLedger { public static readonly RefreshCollectionInfoFromLedger Instance = new RefreshCollectionInfoFromLedger(); }

        public NftManager(EntityId entityId) : base(entityId)
        {
            _ledgerDataFetcher = new NftLedgerDataFetcher(_log);
        }

        protected override async Task Initialize()
        {
            PersistedNftManager persisted = await MetaDatabase.Get().TryGetAsync<PersistedNftManager>(_entityId.ToString());
            await InitializePersisted(persisted);

            StartPeriodicTimer(TimeSpan.FromSeconds(5), PollObservers.Instance);
            StartPeriodicTimer(TimeSpan.FromSeconds(1), FlushPendingMetadataWrites.Instance);
            StartPeriodicTimer(TimeSpan.FromSeconds(60), RefreshCollectionInfoFromLedger.Instance);
        }

        protected override Task<NftManagerState> InitializeNew()
        {
            return Task.FromResult(new NftManagerState());
        }

        protected override async Task PersistStateImpl(bool isInitial, bool isFinal)
        {
            _log.Debug("Persisting state (isInitial={IsInitial}, isFinal={IsFinal}, schemaVersion={SchemaVersion})", isInitial, isFinal, _entityConfig.CurrentSchemaVersion);

            // Serialize and compress the state
            byte[] persistedPayload = SerializeToPersistedPayload(_state, resolver: null, logicVersion: null);

            PersistedNftManager persisted = new PersistedNftManager
            {
                EntityId        = _entityId.ToString(),
                PersistedAt     = DateTime.UtcNow,
                Payload         = persistedPayload,
                SchemaVersion   = _entityConfig.CurrentSchemaVersion,
                IsFinal         = isFinal,
            };

            if (isInitial)
                await MetaDatabase.Get().InsertAsync(persisted).ConfigureAwait(false);
            else
                await MetaDatabase.Get().UpdateAsync(persisted).ConfigureAwait(false);
        }

        protected override Task<NftManagerState> RestoreFromPersisted(PersistedNftManager persisted)
        {
            NftManagerState state = DeserializePersistedPayload<NftManagerState>(persisted.Payload, resolver: null, logicVersion: null);
            return Task.FromResult(state);
        }

        protected override async Task PostLoad(NftManagerState payload, DateTime persistedAt, TimeSpan elapsedTime)
        {
            _state = payload;

            // One-time migration for populating PersistedNft.StringComparableTokenIdEquivalent.
            // Note that in rare cases this might actually get run more than once (which is OK),
            // because the HasPopulatedPersistedNftStringComparableTokenIds bookkeeping is
            // conservative, because the NFTs and NftManager itself get persisted separately.
            if (!_state.HasPopulatedPersistedNftStringComparableTokenIds)
            {
                await StoragePopulateNftStringIdComparableTokenIdsAsync(_log);
                _state.HasPopulatedPersistedNftStringComparableTokenIds = true;
            }

            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();
            DateTime currentTime = DateTime.UtcNow;

            _transientState = new TransientState();

            // Initialize collection states (both persistent and runtime).

            foreach (Web3Options.NftCollectionSpec collection in web3Options.NftCollections.Values)
            {
                NftCollectionId collectionId = collection.CollectionId;

                // Ensure _state.Collections contains an entry for the collection.
                if (!_state.Collections.ContainsKey(collectionId))
                    _state.Collections.Add(collectionId, new NftManagerState.Collection());

                _transientState.Collections.Add(collectionId, new TransientState.Collection());

                NftManagerState.Collection collectionState = _state.Collections[collectionId];
                TransientState.Collection collectionTransientState = _transientState.Collections[collectionId];

                collectionTransientState.MaxPersistedNftId = await StorageTryGetMaxNftIdInCollectionAsync(collectionId);

                if (collection.MetadataManagementMode == Web3Options.NftMetadataManagementMode.Authoritative)
                    collectionTransientState.MetadataStorage = web3Options.CreateNftCollectionMetadataStorage(collection);

                // Ledger-dependent initialization.
                if (collection.Ledger == Web3Options.NftCollectionLedger.LocalTesting)
                {
                    // Local testing mode - nothing to set up
                }
                else if (collection.Ledger == Web3Options.NftCollectionLedger.ImmutableX)
                {
                    // Initialize NFT ownership change observer.
                    EthereumAddress contractAddress = collection.ContractAddress;

                    DateTime startTime;
                    if (collectionState.ObservedToTime.HasValue)
                    {
                        // Prefer to continue from where left off, but don't go arbitrarily far into past,
                        // because it could take a long time to catch up with the present.
                        startTime = Util.Max(collectionState.ObservedToTime.Value.ToDateTime(), currentTime - TimeSpan.FromMinutes(20));
                    }
                    else
                        startTime = currentTime - TimeSpan.FromMinutes(10);

                    collectionTransientState.ImmutableXNftObserver = new ImmutableXNftObserver(contractAddress, startTime);
                }
                else
                {
                    _log.Error($"Unhandled {nameof(collection.Ledger)}={{Ledger}} in collection {{CollectionId}}, ignoring", collection.Ledger, collectionId);
                }

                // Start querying collection info from ledger
                StartRefreshCollectionLedgerInfo(collectionId);

                // If pending metadata writes were left unfinished from the previous actor incarnation,
                // continue them. FlushPendingMetadataWrites polling reads _transientState.PendingMetadataWrites
                // and starts the background write operation.
                foreach (NftId nftId in collectionState.PendingMetadataWrites)
                {
                    if (collection.MetadataManagementMode == Web3Options.NftMetadataManagementMode.Authoritative)
                        _transientState.PendingMetadataWrites.Add(new NftKey(collectionId, nftId));
                }
            }
        }

        protected override void PostStop()
        {
            if (_transientState != null)
            {
                foreach (TransientState.Collection collection in _transientState.Collections.Values)
                {
                    if (collection.ImmutableXNftObserver != null)
                    {
                        collection.ImmutableXNftObserver.Dispose();
                        collection.ImmutableXNftObserver = null;
                    }
                }
            }

            base.PostStop();
        }

        [EntityAskHandler]
        void HandleRefreshCollectionLedgerInfoRequest(EntityAsk ask, RefreshCollectionLedgerInfoRequest request)
        {
            StartRefreshCollectionLedgerInfo(
                request.CollectionId,
                onCompleted: response => ReplyToAsk(ask, response),
                onError: message => RefuseAsk(ask, new InvalidEntityAsk(message)));
        }

        void StartRefreshCollectionLedgerInfo(NftCollectionId collectionId, Action<RefreshCollectionLedgerInfoResponse> onCompleted = null, Action<string> onError = null)
        {
            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();

            ContinueTaskOnActorContext(
                _ledgerDataFetcher.TryGetCollectionLedgerInfoAsync(web3Options, collectionId),
                handleSuccess: collectionInfoMaybe =>
                {
                    if (collectionInfoMaybe != null)
                    {
                        _state.Collections[collectionId].LedgerInfoMaybe = collectionInfoMaybe;
                        onCompleted?.Invoke(new RefreshCollectionLedgerInfoResponse());
                    }
                    else
                        onError?.Invoke("Collection not found in ledger");
                },
                handleFailure: exception =>
                {
                    _log.Warning("Query of collection info from ledger failed, for collection {CollectionId}: {Exception}", collectionId, exception);
                    onError?.Invoke(exception.ToString());
                });
        }

        [CommandHandler]
        async Task HandlePollObserversCommandAsync(PollObservers _)
        {
            FullGameConfig gameConfig = GetActiveBaselineGameConfig();

            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();

            foreach ((NftCollectionId collectionId, TransientState.Collection collectionTransientState) in _transientState.Collections)
            {
                ImmutableXNftObserver observer = collectionTransientState.ImmutableXNftObserver;
                if (observer == null)
                    continue;

                ImmutableXNftObserver.PollResult pollResult = observer.PollChanges();

                _state.Collections[collectionId].ObservedToTime = MetaTime.FromDateTime(pollResult.ObservedUpTo);

                foreach (ImmutableXNftObserver.NftChange nftChange in pollResult.Changes)
                {
                    NftOwnerAddress nftOwnerAddress = NftOwnerAddress.CreateEthereum(nftChange.Owner);
                    NftKey nftKey = new NftKey(collectionId, NftId.ParseFromString(nftChange.TokenId.GetTokenIdString()));

                    try
                    {
                        await UpdateNftOwnerAddressAsync(gameConfig, web3Options, nftKey, nftOwnerAddress);
                    }
                    catch (NftRestorationException ex)
                    {
                        _log.Error("Failed to restore persisted {NftKey} for ownership update from observer: {Exception}", nftKey, ex);
                    }
                }
            }
        }

        [CommandHandler]
        async Task HandleFlushPendingMetadataWritesAsync(FlushPendingMetadataWrites _)
        {
            FullGameConfig gameConfig = GetActiveBaselineGameConfig();

            // Nothing new to write -> do nothing.
            if (_transientState.PendingMetadataWrites.Count == 0)
                return;

            // Don't allow multiple concurrent background metadata writes.
            // This keeps state management simpler.
            if (_transientState.OngoingMetadataWrite != null)
                return;

            // Get the current batch of pending writes, and clear _transientState.PendingMetadataWrites.
            // By cleaing _transientState.PendingMetadataWrites, we allow further modifications to
            // the NFTs while the current background operation is still in progress, such that those
            // modifications will get their own metadata writes after this background operation.
            List<NftKey> nftKeysToWrite = new List<NftKey>(_transientState.PendingMetadataWrites);
            _transientState.PendingMetadataWrites.Clear();

            // Assign state for this operation.
            TransientState.OngoingMetadataWriteState metadataWriteState = new TransientState.OngoingMetadataWriteState(
                remainingNfts: new OrderedSet<NftKey>(nftKeysToWrite));
            _transientState.OngoingMetadataWrite = metadataWriteState;

            _log.Info("Starting write of {NumNfts} NFT metadatas", nftKeysToWrite.Count);

            // Fetch the NFTs from database as they currently are, and generate their metadatas.
            OrderedDictionary<NftKey, byte[]> nftMetadatasToWrite = new();
            foreach (NftKey nftKey in nftKeysToWrite)
            {
                MetaNft nft;
                try
                {
                    nft = await StorageTryGetNftAsync(nftKey, gameConfig);
                }
                catch (NftRestorationException ex)
                {
                    _log.Error("Failed to restore persisted {NftKey} for metadata write; not writing this metadata: {Exception}", nftKey, ex);
                    continue;
                }

                if (nft == null)
                {
                    // \todo Need additional persistent bookkeeping to avoid this situation.
                    _log.Warning("NFT {NftKey} had a pending metadata write but the NFT does not exist in the database. This is likely caused by a crash in a previous NFT initialization.", nftKey);
                    continue;
                }

                if (!HasMetadataWriteAccess(nftKey.CollectionId))
                {
                    _log.Error("NFT {NftKey} had a pending metadata write but there is no metadata write access for the collection; ignoring.", nftKey);
                    continue;
                }

                byte[] metadata;
                try
                {
                    metadata = CreateNftMetadataBytes(nft, gameConfig);
                }
                catch (Exception ex)
                {
                    _log.Error("Got an exception while trying to generate metadata for NFT {NftKey}; not writing this metadata: {Exception}", nftKey, ex);
                    continue;
                }

                nftMetadatasToWrite.Add(nftKey, metadata);
            }

            Func<Task<OrderedSet<NftKey>>> writeMetadataAsync = async () =>
            {
                OrderedSet<NftKey> nftsToRetry = new();

                foreach ((NftKey nftKey, byte[] metadata) in nftMetadatasToWrite)
                {
                    IBlobStorage metadataStorage = GetMetadataStorage(nftKey.CollectionId);

                    bool didWrite;
                    try
                    {
                        await WriteMetadataAsync(metadataStorage, nftKey.TokenId, metadata);
                        didWrite = true;
                    }
                    catch (Exception ex)
                    {
                        _log.Warning("Failed to write metadata for NFT {NftKey}: {Exception}", nftKey, ex);
                        nftsToRetry.Add(nftKey);
                        didWrite = false;
                    }

                    if (didWrite)
                    {
                        lock (metadataWriteState.Lock)
                            metadataWriteState.RemainingNfts.Remove(nftKey);
                    }
                }

                return nftsToRetry;
            };

            // Start metadata write operation in background.
            ContinueTaskOnActorContext(
                writeMetadataAsync(),
                handleSuccessAsync: async (OrderedSet<NftKey> nftsToRetry) =>
                {
                    _log.Info("Metadata write done, {NumNftsToRetry} NFTs will be retried later", nftsToRetry.Count);

                    OnNftMetadataWritesDone(nftKeysToWrite.Except(nftsToRetry));
                    await PersistStateIntermediate();

                    foreach (NftKey nftKey in nftsToRetry)
                        _transientState.PendingMetadataWrites.Add(nftKey);

                    _transientState.OngoingMetadataWrite = null;
                },
                handleFailure: exception =>
                {
                    _log.Error("Metadata write failed: {Exception}", exception);

                    OnNftMetadataWritesDone(nftKeysToWrite);

                    _transientState.OngoingMetadataWrite = null;
                });
        }

        void OnNftMetadataWritesDone(IEnumerable<NftKey> nftKeys)
        {
            // After NFT metadata has been written, remove the NFT from the persistent collectionState.PendingMetadataWrites,
            // unless further modifications to the NFT have happened since the metadata write operation was
            // started (indicated by the NFT's presence in _transientState.PendingMetadataWrites).

            foreach (NftKey nftKey in nftKeys)
            {
                if (!_transientState.PendingMetadataWrites.Contains(nftKey))
                {
                    NftManagerState.Collection collectionState = _state.Collections[nftKey.CollectionId];
                    collectionState.PendingMetadataWrites.Remove(nftKey.TokenId);
                }
            }
        }

        [CommandHandler]
        void HandleRefreshCollectionInfoFromLedger(RefreshCollectionInfoFromLedger _)
        {
            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();

            foreach (Web3Options.NftCollectionSpec collection in web3Options.NftCollections.Values)
                StartRefreshCollectionLedgerInfo(collection.CollectionId);
        }

        [EntityAskHandler]
        async Task<BatchInitializeNftsResponse> HandleBatchInitializeNftsRequestAsync(BatchInitializeNftsRequest request)
        {
            FullGameConfig gameConfig = GetActiveBaselineGameConfig();

            NftCollectionId collectionId = request.CollectionId;
            TransientState.Collection collectionTransientState = _transientState.Collections[collectionId];

            bool allowOverwrite = request.AllowOverwrite;
            bool validateOnly = request.ValidateOnly;

            List<BatchInitializeNftsRequest.NftInitSpec> nftInitSpecs = request.NftInitSpecs;

            foreach ((BatchInitializeNftsRequest.NftInitSpec spec, int specIndex) in nftInitSpecs.ZipWithIndex())
            {
                if (spec.Nft == null)
                    throw new BatchInitializeNftsRefusal("Null NFT state.", $"Null {nameof(spec.Nft)} in {nameof(BatchInitializeNftsRequest)}.{nameof(BatchInitializeNftsRequest.NftInitSpecs)} at index {specIndex}.");
            }

            if (!validateOnly)
                _log.Info("Initializing {NumTokens} tokens in collection {CollectionId}, allowOverwrite={AllowOverwrite}...", nftInitSpecs.Count, collectionId, allowOverwrite);

            // Resolve whether explicit or implicit (auto-allocated) token ids are being used for this batch.
            bool hasExplicitTokenIds;
            if (nftInitSpecs.Any(spec => spec.TokenId.HasValue))
            {
                if (!nftInitSpecs.All(spec => spec.TokenId.HasValue))
                {
                    BatchInitializeNftsRequest.NftInitSpec specWithExplicitId = nftInitSpecs.First(spec => spec.TokenId.HasValue);
                    BatchInitializeNftsRequest.NftInitSpec specWithImplicitId = nftInitSpecs.First(spec => !spec.TokenId.HasValue);

                    throw new BatchInitializeNftsRefusal(
                        "Token id specification error.",
                        details:
                            $"NFT at {specWithExplicitId.SourceInfo} has an explicit token id ({specWithExplicitId.TokenId.Value}) but NFT at {specWithImplicitId.SourceInfo} has implicit (omitted) token id. "
                            + "Either all must have an explicit token id, or all must have implicit token id.");
                }

                hasExplicitTokenIds = true;
            }
            else
                hasExplicitTokenIds = false;

            // Resolve the token id values.

            IEnumerable<NftId> resolvedNftIds;

            if (hasExplicitTokenIds)
                resolvedNftIds = nftInitSpecs.Select(spec => spec.TokenId.Value);
            else
            {
                NftId runningTokenId = collectionTransientState.MaxPersistedNftId?.Plus1() ?? NftId.Zero;

                List<NftId> ids = new List<NftId>();
                for (int i = 0; i < nftInitSpecs.Count; i++)
                {
                    ids.Add(runningTokenId);
                    runningTokenId = runningTokenId.Plus1();
                }

                resolvedNftIds = ids;
            }

            // Check no duplicates inside this batch itself.

            HashSet<NftId> resolvedNftIdsSet = new HashSet<NftId>();
            foreach (NftId nftId in resolvedNftIds)
            {
                bool ok = resolvedNftIdsSet.Add(nftId);
                if (!ok)
                    throw new BatchInitializeNftsRefusal("NFT id conflict.", $"Duplicate NFT id {nftId} within batch initialization.");
            }

            // Check overwrites of existing NFTs.
            // If overwrites are not allowed, error out right away.

            // \note overwrittenNfts and overwrittenNftStates may differ in which NFTs they
            //       contain in case StorageTryGetNftAsync throws (due to e.g. deserialization error):
            //       overwrittenNfts will contain even such broken NFTs but overwrittenNftStates
            //       won't. The resulting behavior is that intact NFTs will copy their base info
            //       (such as ownership) into the new NFTs, but broken NFTs won't.
            HashSet<NftId> overwrittenNfts = new();
            Dictionary<NftId, MetaNft> overwrittenNftStates = new();
            foreach (NftId nftId in resolvedNftIds)
            {
                MetaNft existingNftState;
                bool nftExists;
                try
                {
                    existingNftState = await StorageTryGetNftAsync(new NftKey(collectionId, nftId), gameConfig);
                    nftExists = existingNftState != null;
                }
                catch (NftRestorationException)
                {
                    existingNftState = null;
                    nftExists = true;
                }

                if (nftExists)
                {
                    if (allowOverwrite)
                    {
                        overwrittenNfts.Add(nftId);
                        if (existingNftState != null)
                            overwrittenNftStates.Add(nftId, existingNftState);
                    }
                    else
                        throw new BatchInitializeNftsRefusal("NFT id conflict.", $"NFT with id {nftId} already exists in collection {collectionId}.");
                }
            }

            // Finalize NFT states and do some validation.

            List<MetaNft> nfts = new List<MetaNft>();
            foreach ((NftId nftId, MetaNft nftIter) in resolvedNftIds.Zip(nftInitSpecs.Select(spec => spec.Nft)))
            {
                MetaNft nft = nftIter; // \note Need to copy iteration variable because we're using `ref` on it below.
                nft.SetBaseProperties(
                    nftId,
                    ownerEntity: EntityId.None,
                    ownerAddress: NftOwnerAddress.None,
                    isMinted: false,
                    updateCounter: 0);

                // If overwrites are enabled, some state (such as ownership) can be
                // copied from the overwritten state.
                if (overwrittenNftStates.TryGetValue(nftId, out MetaNft overwrittenNft))
                {
                    nft.CopyBaseProperties(overwrittenNft);
                    nft.UpdateCounter++;
                }

                // Resolve references just to validate them, in order to error early here
                // to avoid persisting broken NFTs.
                try
                {
                    IGameConfigDataResolver resolver = gameConfig.SharedConfig;
                    MetaSerialization.ResolveMetaRefs(ref nft, resolver);
                }
                catch (Exception ex)
                {
                    throw new BatchInitializeNftsRefusal("Cannot resolve config references.", $"Failed to resolve config references in NFT (id {nftId}): {ex}");
                }

                // Check request-given collection id vs. actual NFT type collection id.
                NftCollectionId newNftCollectionId = NftTypeRegistry.Instance.GetCollectionId(nft);
                if (newNftCollectionId != collectionId)
                    throw new BatchInitializeNftsRefusal("Mismatching collection.", $"Attempted to initialize a new NFT (id {nftId}) in collection {collectionId}, but the NFT is of type {nft.GetType().Name} which belongs to collection {newNftCollectionId}.");

                if (request.ShouldWriteMetadata)
                {
                    // Check that metadata can be generated. This can fail e.g. because of user-defined properties throwing.
                    // This is a sanity check and does not guarantee that further updates to the MetaNft state do not break metadata generation.
                    try
                    {
                        JObject metadata = NftTypeRegistry.Instance.GenerateMetadata(nft);
                        string metadataString = metadata.ToString();
                        _ = metadataString;
                    }
                    catch (Exception ex)
                    {
                        throw new BatchInitializeNftsRefusal("Cannot generate NFT metadata.", $"Got an exception while trying to generate metadata for NFT (id {nftId}): {ex}");
                    }
                }

                nfts.Add(nft);
            }

            // Initialize NFTs: create in database, and record pending metadata.
            // Or, if only validating, just produce corresponding return values.

            if (validateOnly)
            {
                List<BatchInitializeNftsResponse.NftResponse> nftResponses = new();
                foreach (MetaNft nft in nfts)
                {
                    MetaNft overwrittenNftMaybe = overwrittenNftStates.GetValueOrDefault(nft.TokenId);

                    nftResponses.Add(new BatchInitializeNftsResponse.NftResponse(
                        nft: nft,
                        overwrittenNftMaybe: overwrittenNftMaybe));
                }

                return new BatchInitializeNftsResponse(nftResponses);
            }
            else
            {
                // \todo #nft This does not achieve atomicity when there are multiple NFTs being initialized.
                //       Need write-ahead log or similar.

                if (request.ShouldWriteMetadata)
                    await AddPendingMetadataWritesAsync(nfts.Select(GetNftKey));

                // Store to database.
                List<BatchInitializeNftsResponse.NftResponse> nftResponses = new();
                foreach (MetaNft nft in nfts)
                {
                    _state.ForgetUninitializedNft(GetNftKey(nft));
                    if (overwrittenNfts.Contains(nft.TokenId))
                        await StorageUpdateNftAsync(nft);
                    else
                        await StorageInsertNftAsync(nft);
                    // Update MaxPersistedNftId tracking.
                    collectionTransientState.MaxPersistedNftId = Util.Max(collectionTransientState.MaxPersistedNftId ?? NftId.Zero, nft.TokenId);

                    MetaNft overwrittenNftMaybe = overwrittenNftStates.GetValueOrDefault(nft.TokenId);

                    nftResponses.Add(new BatchInitializeNftsResponse.NftResponse(
                        nft: nft,
                        overwrittenNftMaybe: overwrittenNftMaybe));

                    if (overwrittenNftMaybe == null)
                    {
                        // Start querying the NFT's owner from the ledger, if requested.
                        // A reason to *not* request querying the owner is to avoid sending
                        // lots of requests to the ledger at a high rate, in case a big
                        // batch init was made. The owner will get resolved eventually if
                        // the owner logs in as a player and binds the wallet.
                        if (request.ShouldQueryOwnersFromLedger)
                            StartRefreshNftOwner(gameConfig, GetNftKey(nft));
                    }
                }

                // If overwrote existing NFTs, inform owners about the state updates.
                foreach (MetaNft nft in nfts)
                {
                    if (overwrittenNfts.Contains(nft.TokenId)
                     && nft.OwnerEntity != EntityId.None)
                    {
                        // \todo #nft This is silly in case the owner entity isn't already active, because
                        //       the owner entity will query owned NFTs at session start anyway.
                        //       #nft-ownership-message-wakeup
                        CastMessage(nft.OwnerEntity, new NftStateUpdated(nft));
                    }
                }

                _log.Info("Initialized {NumTokens} tokens in collection {CollectionId}, overwrote {NumOverwritten} existing NFTs", nftInitSpecs.Count, collectionId, nftResponses.Count(r => r.OverwrittenNftMaybe != null));

                return new BatchInitializeNftsResponse(nftResponses);
            }
        }

        [EntityAskHandler]
        async Task<TryGetExistingNftIdInRangeResponse> HandleTryGetExistingNftIdInRangeRequestAsync(TryGetExistingNftIdInRangeRequest request)
        {
            NftCollectionId collectionId = request.CollectionId;
            IEnumerable<NftId> nftIds = NftUtil.GetNftIdRange(request.FirstTokenId, request.NumTokens);

            foreach (NftId nftId in nftIds)
            {
                if (await StorageNftExistsAsync(new NftKey(collectionId, nftId)))
                    return new TryGetExistingNftIdInRangeResponse(nftId);
            }

            return new TryGetExistingNftIdInRangeResponse(null);
        }

        [EntityAskHandler]
        void HandleRefreshNftRequest(EntityAsk ask, RefreshNftRequest request)
        {
            FullGameConfig gameConfig = GetActiveBaselineGameConfig();

            StartRefreshNftOwner(
                gameConfig,
                request.NftKey,
                onCompleted: response => ReplyToAsk(ask, response),
                onError: message => RefuseAsk(ask, new InvalidEntityAsk(message)));
        }

        void StartRefreshNftOwner(FullGameConfig gameConfig, NftKey nftKey, Action<RefreshNftResponse> onCompleted = null, Action<string> onError = null)
        {
            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();

            ContinueTaskOnActorContext(
                _ledgerDataFetcher.TryGetNftOwnerAddressAsync(web3Options, nftKey),
                handleSuccessAsync: async (NftOwnerAddress ownerMaybe) =>
                {
                    if (ownerMaybe != NftOwnerAddress.None)
                    {
                        try
                        {
                            await UpdateNftOwnerAddressAsync(gameConfig, web3Options, nftKey, ownerMaybe);
                            onCompleted?.Invoke(new RefreshNftResponse());
                        }
                        catch (NftRestorationException ex)
                        {
                            _log.Error("Failed to restore persisted {NftKey} for ownership update: {Exception}", nftKey, ex);
                            onError?.Invoke(ex.ToString());
                        }
                    }
                    else
                        onCompleted?.Invoke(new RefreshNftResponse());
                },
                handleFailure: exception =>
                {
                    _log.Warning("NFT owner query failed, for token {TokenId}: {Exception}", nftKey.TokenId, exception);
                    onError?.Invoke(exception.ToString());
                });
        }

        async Task UpdateNftOwnerAddressAsync(FullGameConfig gameConfig, Web3Options web3Options, NftKey nftKey, NftOwnerAddress nftOwnerAddress)
        {
            // \todo #nft This block of code does unnecessarily many database accesses to the NFTs.

            EntityId ownerEntityId = await ResolveNftOwnerEntityIdFromOwnerAddressAsync(web3Options, nftOwnerAddress);

            if (!await StorageNftExistsAsync(nftKey))
                _state.RememberUninitializedNft(nftKey, new NftManagerState.UninitializedNftInfo(ownerEntityId, nftOwnerAddress));
            else
            {
                // Update ownership, and mark as minted (now that we know the NFT exists in the ledger).
                await SetNftOwnershipAsync(gameConfig, nftKey, ownerEntityId, nftOwnerAddress);
                await SetNftMintedAsync(gameConfig, nftKey);
            }
        }

        async Task<EntityId> ResolveNftOwnerEntityIdFromOwnerAddressAsync(Web3Options web3Options, NftOwnerAddress nftOwnerAddress)
        {
            switch (nftOwnerAddress.Type)
            {
                case NftOwnerAddress.AddressType.None:
                    return EntityId.None;

                case NftOwnerAddress.AddressType.Ethereum:
                {
                    // Resolve ownership based on Ethereum auth entry.
                    // Note that the auth entry might not exist, in which case there's no owner entity (EntityId.None).
                    EthereumAddress ethereumAddress = EthereumAddress.FromString(nftOwnerAddress.AddressString);
                    AuthenticationKey authKey = new AuthenticationKey(AuthenticationPlatform.Ethereum, ImmutableXAuthenticator.CreateEthereumAuthenticationUserId(web3Options, ethereumAddress));
                    PersistedAuthenticationEntry persistedAuthEntry = await MetaDatabase.Get().TryGetAsync<PersistedAuthenticationEntry>(authKey.ToString());
                    EntityId ownerEntityId = persistedAuthEntry?.PlayerEntityId ?? EntityId.None;
                    return ownerEntityId;
                }

                default:
                    _log.Error($"Unhandled {nameof(NftOwnerAddress)}.{nameof(NftOwnerAddress.AddressType)}: {nftOwnerAddress.Type}");
                    return EntityId.None;
            }
        }

        [EntityAskHandler]
        async Task<RepublishNftMetadataResponse> HandleRepublishNftMetadataRequestAsync(RepublishNftMetadataRequest request)
        {
            FullGameConfig gameConfig = GetActiveBaselineGameConfig();

            // \todo #nft Tolerate exceptions - don't crash manager

            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();
            NftKey nftKey = request.NftKey;
            NftCollectionId collectionId = nftKey.CollectionId;
            Web3Options.NftCollectionSpec collection = web3Options.GetNftCollection(collectionId);

            if (collection.MetadataManagementMode != Web3Options.NftMetadataManagementMode.Authoritative)
                throw new InvalidEntityAsk($"Cannot publish metadata for this NFT because collection {collectionId} has been configured with {nameof(collection.MetadataManagementMode)}={collection.MetadataManagementMode}, expected {Web3Options.NftMetadataManagementMode.Authoritative}");

            // Write metadata.
            MetaNft nft = await StorageGetNftAsync(nftKey, gameConfig);
            IBlobStorage metadataStorage = GetMetadataStorage(collectionId);
            byte[] metadata = CreateNftMetadataBytes(nft, gameConfig);
            await WriteMetadataAsync(metadataStorage, nft.TokenId, metadata);

            // In case there's a metadata write pending for this NFT, remove it.
            // This is not super important, because an extra metadata write is not harmful.
            // However, this affects how the dashboard reports pending metadata writes.
            _transientState.PendingMetadataWrites.Remove(nftKey);
            if (_transientState.OngoingMetadataWrite != null)
            {
                lock (_transientState.OngoingMetadataWrite.Lock)
                    _transientState.OngoingMetadataWrite.RemainingNfts.Remove(nftKey);
            }

            return new RepublishNftMetadataResponse();
        }

        [EntityAskHandler]
        async Task<SetNftOwnershipDebugResponse> HandleSetNftOwnershipDebugRequestAsync(SetNftOwnershipDebugRequest request)
        {
            // \todo #nft Tolerate exceptions - don't crash manager
            FullGameConfig gameConfig = GetActiveBaselineGameConfig();
            await SetNftOwnershipAsync(gameConfig, request.NftKey, request.NewOwner, request.NewOwnerAddress);
            return new SetNftOwnershipDebugResponse();
        }

        async Task SetNftOwnershipAsync(FullGameConfig gameConfig, NftKey nftKey, EntityId newOwnerEntity, NftOwnerAddress newOwnerAddress)
        {
            MetaNft nft = await StorageGetNftAsync(nftKey, gameConfig);
            EntityId oldOwnerEntity = nft.OwnerEntity;
            NftOwnerAddress oldOwnerAddress = nft.OwnerAddress;
            if (newOwnerEntity != oldOwnerEntity
             || newOwnerAddress != oldOwnerAddress)
            {
                nft.OwnerEntity = newOwnerEntity;
                nft.OwnerAddress = newOwnerAddress;
                await StorageUpdateNftAsync(nft);

                if (newOwnerEntity != oldOwnerEntity)
                {
                    // \todo #nft-ownership-message-wakeup
                    if (oldOwnerEntity != EntityId.None)
                        CastMessage(oldOwnerEntity, new NftOwnershipRemoved(nftKey));

                    // \todo #nft-ownership-message-wakeup
                    if (newOwnerEntity != EntityId.None)
                        CastMessage(newOwnerEntity, new NftOwnershipGained(nft));

                    _log.Info("Changed ownership of {NftKey} from entity {OldOwnerEntity} (address {OldOwnerAddress}) to {NewOwnerEntity} (address {NewOwnerAddress})", nftKey, oldOwnerEntity, oldOwnerAddress, newOwnerEntity, newOwnerAddress);
                }
                else if (newOwnerAddress != oldOwnerAddress)
                {
                    // \todo #nft-ownership-message-wakeup
                    if (newOwnerEntity != EntityId.None)
                        CastMessage(newOwnerEntity, new NftStateUpdated(nft));
                    _log.Info("Changed ownership of {NftKey} within entity {OwnerEntity} from address {OldOwnerAddress} to {NewOwnerAddress}", nftKey, newOwnerEntity, oldOwnerAddress, newOwnerAddress);
                }
            }
            else
                _log.Debug("No-op ownership assignment of {NftKey} with owner entity {OwnerEntity}, address {OwnerAddress}", nftKey, oldOwnerEntity, oldOwnerAddress);
        }

        async Task SetNftMintedAsync(FullGameConfig gameConfig, NftKey nftKey)
        {
            MetaNft nft = await StorageGetNftAsync(nftKey, gameConfig);
            if (!nft.IsMinted)
            {
                nft.IsMinted = true;
                nft.UpdateCounter++;
                await StorageUpdateNftAsync(nft);

                // \todo #nft-ownership-message-wakeup
                if (nft.OwnerEntity != EntityId.None)
                    CastMessage(nft.OwnerEntity, new NftStateUpdated(nft));

                _log.Info("Marked {NftKey} as minted (current owner: entity {OwnerEntity}, address {OwnerAddress})", nftKey, nft.OwnerEntity, nft.OwnerAddress);
            }
            else
                _log.Debug("No-op mintedness assignment of {NftKey} (current owner: {OwnerEntity}, address {OwnerAddress})", nftKey, nft.OwnerEntity, nft.OwnerAddress);
        }

        [EntityAskHandler]
        async Task<OwnerUpdateNftStatesResponse> HandleOwnerUpdateNftStatesRequestAsync(OwnerUpdateNftStatesRequest request)
        {
            FullGameConfig gameConfig = GetActiveBaselineGameConfig();

            List<MetaNft> nfts = request.Nfts;

            // \todo #nft This is a poor implementation that does not achieve atomicity when there are multiple NFTs being updated.
            //       Need write-ahead log or similar.

            // Validate legitimacy of updates
            foreach (MetaNft nft in nfts)
            {
                NftKey nftKey = GetNftKey(nft);

                MetaNft nftOldState;
                try
                {
                    nftOldState = await StorageTryGetNftAsync(nftKey, gameConfig);
                }
                catch (NftRestorationException ex)
                {
                    _log.Error("Failed to restore persisted {NftKey} for update by owner: {Exception}", nftKey, ex);
                    throw new InvalidEntityAsk($"Failed to restore persisted {nftKey}: {ex}");
                }

                if (nftOldState == null)
                    throw new InvalidEntityAsk($"No such NFT: {nftKey}");

                if (nftOldState.OwnerEntity != request.AssertedOwner)
                    throw new InvalidEntityAsk($"NFT state update asserts owner {request.AssertedOwner.Value}, but actual owner of {GetNftKey(nft)} is {nftOldState.OwnerEntity}.");

                if (nft.OwnerEntity != nftOldState.OwnerEntity)
                    throw new InvalidEntityAsk($"NFT state updates are not permitted to change the NFT's owner entity. Attempted to change owner entity of {GetNftKey(nft)} from {nftOldState.OwnerEntity} to {nft.OwnerEntity}.");
                if (nft.OwnerAddress != nftOldState.OwnerAddress)
                    throw new InvalidEntityAsk($"NFT state updates are not permitted to change the NFT's owner address. Attempted to change owner address of {GetNftKey(nft)} from {nftOldState.OwnerAddress} to {nft.OwnerAddress}.");
                if (nft.IsMinted != nftOldState.IsMinted)
                    throw new InvalidEntityAsk($"NFT state updates are not permitted to change the NFT's mintedness status. Attempted to change mintedness status of {GetNftKey(nft)} from {nftOldState.IsMinted} to {nft.IsMinted}.");
                if (nft.UpdateCounter != nftOldState.UpdateCounter + 1)
                    throw new InvalidEntityAsk($"NFT state update counter mismatch. NFT {GetNftKey(nft)}: old counter {nftOldState.UpdateCounter}, new attempted counter {nft.UpdateCounter} (expected old counter incremented by 1)");
            }

            // Persist updated NFTs and metadatas

            await AddPendingMetadataWritesAsync(nfts.Select(GetNftKey));

            foreach (MetaNft nft in nfts)
                await StorageUpdateNftAsync(nft);

            _log.Debug("Updated NFTs {NftKeys}", string.Join(", ", nfts.Select(GetNftKey)));

            return new OwnerUpdateNftStatesResponse();
        }

        [EntityAskHandler]
        async Task<GetNftResponse> HandleGetNftRequestAsync(GetNftRequest request)
        {
            // \todo #nft Tolerate exceptions - don't crash manager

            FullGameConfig gameConfig = GetActiveBaselineGameConfig();

            NftKey nftKey = request.NftKey;
            NftQueryItem queryItem;
            try
            {
                queryItem = new NftQueryItem(
                    nftKey,
                    isSuccess: true,
                    await StorageGetNftAsync(nftKey, gameConfig),
                    error: null);
            }
            catch (NftRestorationException ex)
            {
                queryItem = new NftQueryItem(
                    nftKey,
                    isSuccess: false,
                    nft: null,
                    error: ex.ToString());
            }

            bool hasPendingMetadataWrite;
            {
                if (_transientState.PendingMetadataWrites.Contains(nftKey))
                    hasPendingMetadataWrite = true;
                else if (_transientState.OngoingMetadataWrite != null)
                {
                    lock (_transientState.OngoingMetadataWrite.Lock)
                        hasPendingMetadataWrite = _transientState.OngoingMetadataWrite.RemainingNfts.Contains(nftKey);
                }
                else
                    hasPendingMetadataWrite = false;
            }

            return new GetNftResponse(queryItem, hasPendingMetadataWrite);
        }

        [EntityAskHandler]
        async Task<QueryNftsResponse> HandleQueryNftsRequestAsync(QueryNftsRequest request)
        {
            FullGameConfig gameConfig = GetActiveBaselineGameConfig();

            List<NftQueryResultItem> resultItems = await StorageQueryNftsAsync(request.Collection, request.Owner, gameConfig);

            return new QueryNftsResponse(resultItems.Select(item =>
                new NftQueryItem(
                    nftKey: item.NftKey,
                    isSuccess: item.Nft != null,
                    nft: item.Nft,
                    error: item.Exception?.ToString()))
                .ToList());
        }

        [EntityAskHandler]
        void HandleRefreshNftsOwnedByEntityRequest(EntityAsk ask, RefreshNftsOwnedByEntityRequest request)
        {
            FullGameConfig gameConfig = GetActiveBaselineGameConfig();

            EntityId ownerId = request.Owner;
            List<NftOwnerAddress> ownedAddresses = request.OwnedAddresses;

            Web3Options web3Options = RuntimeOptionsRegistry.Instance.GetCurrent<Web3Options>();

            ContinueTaskOnActorContext(
                _ledgerDataFetcher.GetNftsOwnedByAddressesAsync(web3Options, ownedAddresses),
                handleSuccessAsync: async (OrderedDictionary<NftKey, NftOwnerAddress> ownedNfts) =>
                {
                    try
                    {
                        await UpdateOwnedTokensAsync(gameConfig, ownerId, ownedNfts);
                        ReplyToAsk(ask, new RefreshNftsOwnedByEntityResponse());
                    }
                    catch (NftRestorationException ex)
                    {
                        _log.Error("Failed to restore persisted {NftKey} for owned-NFTs refresh: {Exception}", ex.NftKey, ex);
                        RefuseAsk(ask, new InvalidEntityAsk($"Failed to restore persisted {ex.NftKey}: {ex}"));
                    }
                },
                handleFailure: exception =>
                {
                    _log.Warning("NFT wallet content query failed, for owner addresses [ {Addresses} ]: {Exception}", string.Join(", ", ownedAddresses), exception);
                    RefuseAsk(ask, new InvalidEntityAsk(exception.ToString()));
                });
        }

        /// <summary>
        /// Called with the results of NFT ownership queries from ledgers.
        /// According to the ledgers, <paramref name="ownerId"/> owns <paramref name="newlyKnownOwnedNfts"/>.
        /// This method updates the NFTManager's ownership information accordingly.
        /// </summary>
        async Task UpdateOwnedTokensAsync(FullGameConfig gameConfig, EntityId ownerId, OrderedDictionary<NftKey, NftOwnerAddress> newlyKnownOwnedNfts)
        {
            // Get NFTs that the server thinks (before applying the new info) the owner owns.
            OrderedDictionary<NftKey, NftQueryResultItem> lastKnownOwnedNfts = (await StorageQueryNftsAsync(collection: null, owner: ownerId, gameConfig))
                                                                               .ToOrderedDictionary(queryItem => queryItem.NftKey);

            // If an NFT wasn't known to be owned by ownerId, but is now, then transfer its ownership
            // to ownerId.
            // Furthermore, mark the known NFTs as minted (if they're not already), because we now
            // know the NFTs exist in a ledger.
            foreach ((NftKey nftKey, NftOwnerAddress nftOwnerAddress) in newlyKnownOwnedNfts)
            {
                // \todo #nft This block of code does unnecessarily many database accesses to the NFTs.

                if (!await StorageNftExistsAsync(nftKey))
                {
                    _state.RememberUninitializedNft(nftKey, new NftManagerState.UninitializedNftInfo(ownerId, nftOwnerAddress));
                    continue;
                }

                if (!lastKnownOwnedNfts.ContainsKey(nftKey)
                 || (lastKnownOwnedNfts[nftKey].Nft != null
                     && lastKnownOwnedNfts[nftKey].Nft.OwnerAddress != nftOwnerAddress))
                {
                    await SetNftOwnershipAsync(gameConfig, nftKey, ownerId, nftOwnerAddress);
                }

                await SetNftMintedAsync(gameConfig, nftKey);
            }

            // If an NFT was known to be owned by ownerId, but isn't anymore, then take away
            // its ownership from ownerId. We don't currently know who the new owner is,
            // so we set the owner to EntityId.None. The owner will be eventually discovered
            // either as a result of the new owner entering the game, or via the ImmutableXNftObservers.
            // (Alternatively, we could now query the ownership of each token from Immutable X,
            // but that could require a bunch of api calls.)
            //
            // Notable exception: this ownership revocation is not done for NFTs that aren't known to
            // be minted in a ledger. The purpose is to allow a player to hold a not-yet-minted
            // NFT in-game.
            foreach ((NftKey nftKey, NftQueryResultItem queryItem) in lastKnownOwnedNfts)
            {
                if (queryItem.Nft == null)
                    continue;

                if (!queryItem.Nft.IsMinted)
                    continue;

                if (!newlyKnownOwnedNfts.ContainsKey(nftKey))
                    await SetNftOwnershipAsync(gameConfig, nftKey, EntityId.None, newOwnerAddress: NftOwnerAddress.None);
            }
        }

        [EntityAskHandler]
        GetNftCollectionInfoResponse HandleGetNftCollectionInfoRequest(GetNftCollectionInfoRequest request)
        {
            // \todo #nft Tolerate exceptions - don't crash manager

            NftCollectionId             collectionId    = request.CollectionId;
            NftManagerState.Collection  collectionState = _state.Collections[collectionId];

            HashSet<NftId> pendingMetadataWrites = new HashSet<NftId>();
            {
                foreach (NftKey nftKey in _transientState.PendingMetadataWrites)
                {
                    if (nftKey.CollectionId == collectionId)
                        pendingMetadataWrites.Add(nftKey.TokenId);
                }

                if (_transientState.OngoingMetadataWrite != null)
                {
                    lock (_transientState.OngoingMetadataWrite.Lock)
                    {
                        foreach (NftKey nftKey in _transientState.OngoingMetadataWrite.RemainingNfts)
                        {
                            if (nftKey.CollectionId == collectionId)
                                pendingMetadataWrites.Add(nftKey.TokenId);
                        }
                    }
                }
            }

            return new GetNftCollectionInfoResponse(collectionState, pendingMetadataWrites);
        }

        #region Database access helpers

        static async Task StorageInsertNftAsync(MetaNft nft)
        {
            PersistedNft persisted = ConvertToPersistedNft(nft);
            await MetaDatabase.Get().InsertAsync(persisted).ConfigureAwait(false);
        }

        static async Task StorageUpdateNftAsync(MetaNft nft)
        {
            PersistedNft persisted = ConvertToPersistedNft(nft);
            await MetaDatabase.Get().UpdateAsync(persisted).ConfigureAwait(false);
        }

        static async Task<bool> StorageNftExistsAsync(NftKey nftKey)
        {
            string nftGlobalId = GetPersistedNftGlobalId(nftKey);
            return await MetaDatabase.Get().TestExistsAsync<PersistedNft>(nftGlobalId);
        }

        static async Task<MetaNft> StorageTryGetNftAsync(NftKey nftKey, FullGameConfig gameConfig)
        {
            string nftGlobalId = GetPersistedNftGlobalId(nftKey);
            PersistedNft persisted = await MetaDatabase.Get().TryGetAsync<PersistedNft>(nftGlobalId);
            if (persisted == null)
                return null;
            return CreateNftFromPersisted(persisted, gameConfig);
        }

        static async Task<MetaNft> StorageGetNftAsync(NftKey nftKey, FullGameConfig gameConfig)
        {
            return await StorageTryGetNftAsync(nftKey, gameConfig)
                   ?? throw new InvalidOperationException($"No such NFT found: {nftKey}");
        }

        static async Task<NftId?> StorageTryGetMaxNftIdInCollectionAsync(NftCollectionId collectionId)
        {
            return await MetaDatabase.Get().TryGetMaxNftIdInCollectionAsync(collectionId);
        }

        struct NftQueryResultItem
        {
            public NftKey NftKey;
            public MetaNft Nft;
            public NftRestorationException Exception;

            public NftQueryResultItem(NftKey nftKey, MetaNft nft, NftRestorationException exception)
            {
                NftKey = nftKey;
                Nft = nft;
                Exception = exception;
            }
        }

        static async Task<List<NftQueryResultItem>> StorageQueryNftsAsync(NftCollectionId collection, EntityId? owner, FullGameConfig gameConfig)
        {
            List<PersistedNft> persisteds = await MetaDatabase.Get().QueryNftsAsync(collection, owner);

            List<NftQueryResultItem> resultItems = new List<NftQueryResultItem>();
            foreach (PersistedNft persisted in persisteds)
            {
                NftQueryResultItem item;
                try
                {
                    item = new NftQueryResultItem(
                        nftKey: persisted.NftKey,
                        nft: CreateNftFromPersisted(persisted, gameConfig),
                        exception: null);
                }
                catch (NftRestorationException ex)
                {
                    item = new NftQueryResultItem(
                        nftKey: persisted.NftKey,
                        nft: null,
                        exception: ex);
                }

                resultItems.Add(item);
            }

            return resultItems;
        }

        /// <summary>
        /// Thrown when a persisted NFT fails to deserialize or schema-migrate.
        /// This is *not* the error that gets thrown when the database access itself fails.
        /// </summary>
        class NftRestorationException : Exception
        {
            public NftKey NftKey;

            public NftRestorationException(NftKey nftKey, string message)
                : base(message)
            {
                NftKey = nftKey;
            }

            public NftRestorationException(NftKey nftKey, string message, Exception innerException)
                : base(message, innerException)
            {
                NftKey = nftKey;
            }
        }

        static MetaNft CreateNftFromPersisted(PersistedNft persisted, FullGameConfig gameConfig)
        {
            // Deserialize MetaNft
            ISharedGameConfig sharedGameConfig = gameConfig.SharedConfig;
            MetaNft nft;
            try
            {
                nft = MetaSerialization.DeserializeTagged<MetaNft>(persisted.Payload, MetaSerializationFlags.Persisted, resolver: sharedGameConfig, logicVersion: null);
            }
            catch (Exception ex)
            {
                throw new NftRestorationException(persisted.NftKey, "Failed to deserialize NFT.", ex);
            }

            // Run user-defined schema migrations on the MetaNft
            RunMetaNftUserSchemaMigrations(nft, persisted, new NftSchemaMigrationContext(sharedGameConfig));

            return nft;
        }

        static void RunMetaNftUserSchemaMigrations(MetaNft nft, PersistedNft persisted, NftSchemaMigrationContext migrationContext)
        {
            SchemaMigrator migrator = SchemaMigrationRegistry.Instance.GetSchemaMigrator(nft.GetType());

            int nftUserSchemaVersion;
            if (persisted.MetaNftUserSchemaVersion == 0)
            {
                // Nft was persisted before MetaNftUserSchemaVersion was introduced - assume oldest supported version.
                nftUserSchemaVersion = migrator.OldestSupportedSchemaVersion;
            }
            else
                nftUserSchemaVersion = persisted.MetaNftUserSchemaVersion;

            if (nftUserSchemaVersion < migrator.OldestSupportedSchemaVersion)
                throw new InvalidOperationException($"NFT {nft.TokenId} of type {nft.GetType()} was persisted with schema version {nftUserSchemaVersion} which is lower than the oldest supported version {migrator.OldestSupportedSchemaVersion}.");
            else
            {
                nft.SchemaMigrationContext = migrationContext;
                try
                {
                    migrator.RunMigrations(nft, nftUserSchemaVersion);
                }
                catch (Exception ex)
                {
                    throw new NftRestorationException(persisted.NftKey, $"Failed to schema-migrate NFT from version {nftUserSchemaVersion}.", ex);
                }
                finally
                {
                    nft.SchemaMigrationContext = null;
                }
            }
        }

        static PersistedNft ConvertToPersistedNft(MetaNft nft)
        {
            NftKey nftKey = GetNftKey(nft);

            SchemaMigrator metaNftUserSchemaMigrator = SchemaMigrationRegistry.Instance.GetSchemaMigrator(nft.GetType());

            return new PersistedNft
            {
                GlobalId = GetPersistedNftGlobalId(nftKey),
                CollectionId = nftKey.CollectionId.ToString(),
                TokenId = nftKey.TokenId.ToString(),
                StringComparableTokenIdEquivalent = GetLengthPrefixedTokenIdString(nftKey.TokenId),
                OwnerEntityId = nft.OwnerEntity.ToString(),
                OwnerAddress = nft.OwnerAddress.ToString(),
                PersistedAt = DateTime.UtcNow,
                Payload = MetaSerialization.SerializeTagged(nft, MetaSerializationFlags.Persisted, logicVersion: null),
                UpdateCounter = nft.UpdateCounter,
                MetaNftUserSchemaVersion = metaNftUserSchemaMigrator.CurrentSchemaVersion,
                SchemaVersion = 1, // \note Schema version not used yet, this is here for futureproofness
            };
        }

        static string GetPersistedNftGlobalId(NftKey nftKey)
        {
            return $"{nftKey.CollectionId}/{nftKey.TokenId}";
        }

        static async Task StoragePopulateNftStringIdComparableTokenIdsAsync(IMetaLogger log)
        {
            log.Info($"Populating {nameof(PersistedNft.StringComparableTokenIdEquivalent)} for existing persisted NFTs...");

            MetaDatabase db = MetaDatabase.Get();

            List<PersistedNft> persistedNfts = await db.QueryNftsAsync(collection: null, owner: null);
            log.Info("... {NumNfts} existing NFTs queried, migrating...", persistedNfts.Count);

            foreach (PersistedNft persistedNft in persistedNfts)
            {
                persistedNft.StringComparableTokenIdEquivalent = GetLengthPrefixedTokenIdString(NftId.ParseFromString(persistedNft.TokenId));
                await db.UpdateAsync(persistedNft);
            }

            log.Info("... existing NFTs migrated.", persistedNfts);
        }

        static string GetLengthPrefixedTokenIdString(NftId nftId)
        {
            // Note: in the below comments, we're talking specifically
            // about base-10 number representations.

            // Return the token id string prefixed with its length,
            // with the length string zero-padded to the biggest possible
            // length of the length string (among all valid token ids).
            //
            // These kinds of strings, when compared lexicographically to each
            // other, yield equivalent results to a numeric comparison.
            // In some contexts (e.g. SQL) it is easier to compare strings
            // rather than convert to potentially large integers.
            //
            // For human-readability, we put a semicolon after the length prefix.
            //
            // For example:
            //  tokenId 1          results in 01;1
            //  tokenId 100        results in 03;100
            //  tokenId 1234567890 results in 10;1234567890

            string tokenIdString = nftId.ToString();

            // The length string is padded to be of length 2, which is the biggest
            // possible length of the length string:
            //   MaxTokenId.ToString().Length.ToString().Length == 2,
            // where MaxTokenId is pow(2, 256)-1.
            string paddedLengthString = tokenIdString.Length.ToString("D2", CultureInfo.InvariantCulture);

            return paddedLengthString + ";" + tokenIdString;
        }

        #endregion

        Task AddPendingMetadataWriteAsync(NftKey nftKey)
        {
            return AddPendingMetadataWritesAsync(new NftKey[]{ nftKey });
        }

        async Task AddPendingMetadataWritesAsync(IEnumerable<NftKey> nftKeys)
        {
            bool changedPersistentState = false;

            foreach (NftKey nftKey in nftKeys)
            {
                NftCollectionId collectionId = nftKey.CollectionId;
                NftId nftId = nftKey.TokenId;

                if (HasMetadataWriteAccess(collectionId))
                {
                    if (!_transientState.PendingMetadataWrites.Contains(nftKey))
                    {
                        NftManagerState.Collection collectionState = _state.Collections[collectionId];

                        collectionState.PendingMetadataWrites.Add(nftId);
                        changedPersistentState = true;

                        _transientState.PendingMetadataWrites.Add(nftKey);
                    }
                }
            }

            if (changedPersistentState)
                await PersistStateIntermediate();
        }

        bool HasMetadataWriteAccess(NftCollectionId collectionId)
        {
            TransientState.Collection collectionTransientState = _transientState.Collections[collectionId];
            return collectionTransientState.MetadataStorage != null;
        }

        IBlobStorage GetMetadataStorage(NftCollectionId collectionId)
        {
            TransientState.Collection collectionTransientState = _transientState.Collections[collectionId];

            return collectionTransientState.MetadataStorage
                   ?? throw new InvalidOperationException($"Collection {collectionId} has no metadata storage because it is not using {nameof(Web3Options.NftMetadataManagementMode)}.{Web3Options.NftMetadataManagementMode.Authoritative}.");
        }

        async static Task WriteMetadataAsync(IBlobStorage storage, NftId nftId, byte[] metadata)
        {
            try
            {
                await storage.PutAsync(nftId.ToString(), metadata, new BlobStoragePutHints { ContentType = "application/json" });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to write NFT metadata of {nftId} to blob storage", ex);
            }
        }

        static byte[] CreateNftMetadataBytes(MetaNft nftParam, FullGameConfig gameConfig)
        {
            IGameConfigDataResolver resolver        = gameConfig.SharedConfig;
            MetaNft                 nft             = MetaSerialization.CloneTagged(nftParam, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver);
            NftMetadataSpec         nftMetadataSpec = NftTypeRegistry.Instance.GetSpec(nft.GetType()).MetadataSpec;
            JObject                 metadata        = nftMetadataSpec.GenerateMetadata(nft);
            string                  metadataString  = metadata.ToString();
            byte[]                  metadataBytes   = Encoding.UTF8.GetBytes(metadataString);

            return metadataBytes;
        }

        static NftKey GetNftKey(MetaNft nft) => NftTypeRegistry.Instance.GetNftKey(nft);

        static FullGameConfig GetActiveBaselineGameConfig() => GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig;
    }
}
