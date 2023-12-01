// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.Authentication;
using Metaplay.Server.Database;
using Metaplay.Server.GameConfig;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using Prometheus;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Server.Matchmaking
{
    public class AsyncMatchmakingEnabledCondition : MetaplayFeatureEnabledConditionAttribute
    {
        public override bool IsEnabled => IntegrationRegistry.IntegrationClasses<IAsyncMatchmaker>().Any();
    }

    public abstract class AsyncMatchmakerOptionsBase : RuntimeOptionsBase
    {
        [MetaDescription("How many players to store in the matchmaking cache.")]
        public int      PlayerCacheSize             { get; protected set; } = 100_000;
        [MetaDescription("How many players to persist into the database. (Max 16k)")]
        public int      DatabaseCacheSize           { get; protected set; } = 16_000;
        [MetaDescription("How many MMR buckets to divide the playerbase to.")]
        public int      MmrBucketCount              { get; protected set; } = 100;
        [MetaDescription("The minimum size for a bucket.")]
        public int      MmrBucketMinSize            { get; protected set; } = 20;
        [MetaDescription("The initial size for a bucket.")]
        public int      BucketInitialSize           { get; protected set; } = 200;
        [MetaDescription("How often to rebalance the matchmaking buckets.")]
        public TimeSpan BucketsRebalanceInterval    { get; protected set; } = TimeSpan.FromHours(4);
        [MetaDescription("How many samples to keep for bucket size estimation.")]
        public int      BucketsRebalanceSampleCount { get; protected set; } = 1_000_000;
        [MetaDescription("How often to scan the database for new players.")]
        public TimeSpan DatabaseScanTick            { get; protected set; } = TimeSpan.FromSeconds(30);
        [MetaDescription("How many players to fetch with a single database call.")]
        public int      DatabaseScanBatchSize       { get; protected set; } = 64;
        [MetaDescription("How many players are fetched if all buckets are well populated.")]
        public int      DatabaseScanMinimumFetch    { get; protected set; } = 64;
        [MetaDescription("The maximum number of players fetched in a single database scan tick.")]
        public int      DatabaseScanMaximumFetch    { get; protected set; } = 1024;
        [MetaDescription("The number of eligible candidates to find when matching a query.")]
        public int      MatchingCandidateAmount     { get; protected set; } = 10;
        [MetaDescription("The maximum number of candidates to check when matching a query.")]
        public int      MatchingMaxSearchNum        { get; protected set; } = 500;
        [MetaDescription("The initial minimum mmr for the buckets.")]
        public int      InitialMinMmr               { get; protected set; } = 0;
        [MetaDescription("The initial maximum mmr for the buckets.")]
        public int      InitialMaxMmr               { get; protected set; } = 1000;
        [MetaDescription("The mmr step size to use when collecting analytics about buckets.")]
        public int      AnalyticsMmrStep            { get; protected set; } = 50;
    }

    /// <summary>
    /// Represents the database-persisted portion <see cref="AsyncMatchmakerActorBase{TPersistedPlayer,TPlayerModel,TMmPlayerModel,TMmQuery, TMatchmakerState}"/>.
    /// </summary>
    [AsyncMatchmakingEnabledCondition]
    [Table("Matchmakers")]
    public class PersistedMatchmakerState : IPersistedEntity
    {
        [Key]
        [PartitionKey]
        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string EntityId { get; set; }

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime PersistedAt { get; set; }

        [Required] public byte[] Payload { get; set; } // TaggedSerialized<MatchmakerState>

        [Required] public int SchemaVersion { get; set; } // Schema version for object

        [Required] public bool IsFinal { get; set; }
    }

    [MetaSerializable]
    public enum MatchmakingResponseType
    {
        Success,
        NoneFound
    }

    [MetaMessage(MessageCodesCore.AsyncMatchmakingRequest, MessageDirection.ServerInternal)]
    public class AsyncMatchmakingRequest : MetaMessage
    {
        public int                  NumRetries;
        public AsyncMatchmakerQueryBase  Query;
        public OrderedSet<EntityId> ExcludedPlayerIds;

        public AsyncMatchmakingRequest() { }

        public AsyncMatchmakingRequest(int numRetries, AsyncMatchmakerQueryBase query, OrderedSet<EntityId> excludedPlayerIds)
        {
            NumRetries        = numRetries;
            Query             = query;
            ExcludedPlayerIds = excludedPlayerIds;
        }
    }

    [MetaMessage(MessageCodesCore.AsyncMatchmakingResponse, MessageDirection.ServerInternal)]
    public class AsyncMatchmakingResponse : MetaMessage
    {
        public MatchmakingResponseType ResponseType;
        public EntityId                BestCandidate;

        public AsyncMatchmakingResponse() { }

        public AsyncMatchmakingResponse(MatchmakingResponseType responseType, EntityId bestCandidate)
        {
            ResponseType  = responseType;
            BestCandidate = bestCandidate;
        }
    }

    [MetaMessage(MessageCodesCore.AsyncMatchmakingPlayerStateUpdate, MessageDirection.ServerInternal)]
    public class AsyncMatchmakingPlayerStateUpdate : MetaMessage
    {
        public EntityId PlayerId;                // Id of the player that should be updated
        public byte[]   SerializedMmPlayerModel; // Null if state no longer valid (Not a valid target)

        public AsyncMatchmakingPlayerStateUpdate() { }

        public static AsyncMatchmakingPlayerStateUpdate Create<TMmPlayerModel>(EntityId playerId, TMmPlayerModel? mmModel)
            where TMmPlayerModel : struct, IAsyncMatchmakerPlayerModel
        {
            return new AsyncMatchmakingPlayerStateUpdate()
            {
                PlayerId = playerId,
                // \todo [nomi] struct cannot be MetaSerializableDerived, so have to double serialize here :/
                //          Figure out a better way
                SerializedMmPlayerModel = mmModel.HasValue ? MetaSerialization.SerializeTagged(mmModel.Value, MetaSerializationFlags.IncludeAll, null) : null
            };
        }
    }

    // This is different from MatchmakingPlayerStateUpdate because SDK matchmaker controller
    // has no easy way to construct a MatchmakerPlayerModel.
    [MetaMessage(MessageCodesCore.AsyncMatchmakerPlayerEnrollRequest, MessageDirection.ServerInternal)]
    public class AsyncMatchmakerPlayerEnrollRequest : MetaMessage
    {
        public EntityId PlayerId  { get; private set; } // Id of the player that should be updated
        public bool     IsRemoval { get; private set; }

        public AsyncMatchmakerPlayerEnrollRequest() { }

        public AsyncMatchmakerPlayerEnrollRequest(EntityId playerId, bool isRemoval)
        {
            PlayerId  = playerId;
            IsRemoval = isRemoval;
        }
    }

    [MetaMessage(MessageCodesCore.AsyncMatchmakerPlayerEnrollResponse, MessageDirection.ServerInternal)]
    public class AsyncMatchmakerPlayerEnrollResponse : MetaMessage
    {
        public static readonly AsyncMatchmakerPlayerEnrollResponse Success = new(true);
        public static readonly AsyncMatchmakerPlayerEnrollResponse Failure = new(false);

        public bool IsSuccess { get; private set; }

        AsyncMatchmakerPlayerEnrollResponse() { }

        AsyncMatchmakerPlayerEnrollResponse(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }
    }

    [MetaMessage(MessageCodesCore.AsyncMatchmakerInfoRequest, MessageDirection.ServerInternal)]
    public class AsyncMatchmakerInfoRequest : MetaMessage
    {
        public static readonly AsyncMatchmakerInfoRequest Instance = new();

        AsyncMatchmakerInfoRequest() { }
    }

    [MetaMessage(MessageCodesCore.AsyncMatchmakerInfoResponse, MessageDirection.ServerInternal)]
    public class AsyncMatchmakerInfoResponse : MetaMessage
    {
        [MetaSerializable]
        public sealed class BucketInfo
        {
            [MetaMember(1)] public int            MmrLow;
            [MetaMember(2)] public int            MmrHigh;
            [MetaMember(3)] public int            NumPlayers;
            [MetaMember(4)] public int            Capacity;
            [MetaMember(5)] public float          FillPercentage;
            [MetaMember(6)] public IBucketLabel[] Labels;
            [MetaMember(7)] public int            LabelHash;

            BucketInfo() { }

            public BucketInfo(int mmrLow, int mmrHigh, int numPlayers, int capacity,
                float fillPercentage, IBucketLabel[] labels, int labelHash)
            {
                MmrLow         = mmrLow;
                MmrHigh        = mmrHigh;
                NumPlayers     = numPlayers;
                Capacity       = capacity;
                FillPercentage = fillPercentage;
                Labels         = labels;
                LabelHash      = labelHash;
            }
        }

        public string       Name;
        public string       Description;
        public int          PlayersInBuckets;
        public float        BucketsOverallFillPercentage;
        public BucketInfo[] BucketInfos;
        public int          StateSizeInBytes;
        public bool         HasEnoughDataForBucketRebalance;
        public MetaTime     LastRebalanceOperationTime;
        public bool         HasFinishedBucketUpdate;

        public AsyncMatchmakerInfoResponse() { }

        public AsyncMatchmakerInfoResponse(string name, string description, int playersInBuckets, float bucketsOverallFillPercentage, BucketInfo[] bucketInfos, int stateSizeInBytes,
            bool hasEnoughDataForBucketRebalance, MetaTime lastRebalanceOperationTime,
            bool hasFinishedBucketUpdate)
        {
            Name                            = name;
            Description                     = description;
            PlayersInBuckets                = playersInBuckets;
            BucketsOverallFillPercentage    = bucketsOverallFillPercentage;
            BucketInfos                     = bucketInfos;
            StateSizeInBytes                = stateSizeInBytes;
            HasEnoughDataForBucketRebalance = hasEnoughDataForBucketRebalance;
            LastRebalanceOperationTime      = lastRebalanceOperationTime;
            HasFinishedBucketUpdate         = hasFinishedBucketUpdate;
        }
    }

    [MetaMessage(MessageCodesCore.AsyncMatchmakerPlayerInfoRequest, MessageDirection.ServerInternal)]
    public class AsyncMatchmakerPlayerInfoRequest : MetaMessage
    {
        public EntityId PlayerId { get; private set; }
        AsyncMatchmakerPlayerInfoRequest() { }

        public AsyncMatchmakerPlayerInfoRequest(EntityId playerId)
        {
            PlayerId = playerId;
        }
    }

    [MetaMessage(MessageCodesCore.AsyncMatchmakerPlayerInfoResponse, MessageDirection.ServerInternal)]
    public class AsyncMatchmakerPlayerInfoResponse : MetaMessage
    {
        public EntityId PlayerId      { get; private set; }
        public bool     IsParticipant { get; private set; }
        public int      DefenseMmr    { get; private set; }
        public float    Percentile    { get; private set; }

        public EntityId MatchmakerId          { get; private set; }
        public string   MatchmakerName        { get; private set; }
        public string   MatchmakerDescription { get; private set; }

        public AsyncMatchmakerInfoResponse.BucketInfo BucketInfo { get; private set; }

        public AsyncMatchmakerPlayerInfoResponse() { }

        public AsyncMatchmakerPlayerInfoResponse(EntityId playerId, bool isParticipant, int defenseMmr, float percentile,
            AsyncMatchmakerInfoResponse.BucketInfo bucketInfo,
            EntityId matchmakerId,
            string matchmakerName,
            string matchmakerDescription)
        {
            PlayerId                   = playerId;
            IsParticipant              = isParticipant;
            DefenseMmr                 = defenseMmr;
            Percentile                 = percentile;
            BucketInfo                 = bucketInfo;
            MatchmakerId               = matchmakerId;
            MatchmakerName             = matchmakerName;
            MatchmakerDescription      = matchmakerDescription;
        }

        public static AsyncMatchmakerPlayerInfoResponse ForNonParticipant(EntityId playerId,
            EntityId matchmakerId,
            string matchmakerName,
            string matchmakerDescription)
        {
            return new AsyncMatchmakerPlayerInfoResponse(
                playerId,
                false,
                default,
                default,
                default,
                matchmakerId,
                matchmakerName,
                matchmakerDescription);
        }

        public static AsyncMatchmakerPlayerInfoResponse ForParticipant(EntityId playerId, int defenseMmr, float percentile,
            AsyncMatchmakerInfoResponse.BucketInfo bucketInfo, EntityId matchmakerId,
            string matchmakerName,
            string matchmakerDescription)
        {
            return new AsyncMatchmakerPlayerInfoResponse(
                playerId,
                true,
                defenseMmr,
                percentile,
                bucketInfo,
                matchmakerId,
                matchmakerName,
                matchmakerDescription);
        }
    }

    /// <summary>
    /// Request the matchmaker to issue a rebalance operation.
    /// Will be responded with <see cref="EntityAskOk"/>
    /// </summary>
    [MetaMessage(MessageCodesCore.AsyncMatchmakerRebalanceBucketsRequest, MessageDirection.ServerInternal)]
    public class AsyncMatchmakerRebalanceBucketsRequest : MetaMessage
    {
        public static readonly AsyncMatchmakerRebalanceBucketsRequest Instance = new();

        AsyncMatchmakerRebalanceBucketsRequest() { }
    }

    /// <summary>
    /// Request the matchmaker to clear the matchmaker state and start a new database scan.
    /// Will be responded with <see cref="EntityAskOk"/>
    /// </summary>
    [MetaMessage(MessageCodesCore.AsyncMatchmakerClearStateRequest, MessageDirection.ServerInternal)]
    public class AsyncMatchmakerClearStateRequest : MetaMessage
    {
        public static readonly AsyncMatchmakerClearStateRequest Instance = new();

        AsyncMatchmakerClearStateRequest() { }
    }

    [MetaMessage(MessageCodesCore.AsyncMatchmakerInspectBucketRequest, MessageDirection.ServerInternal)]
    public class AsyncMatchmakerInspectBucketRequest : MetaMessage
    {
        public int BucketLabelHash  { get; private set; }
        public int PageSize         { get; private set; }
        public int PageIndex        { get; private set; }

        AsyncMatchmakerInspectBucketRequest() {}

        public AsyncMatchmakerInspectBucketRequest(int bucketLabelHash, int pageSize, int pageIndex)
        {
            BucketLabelHash = bucketLabelHash;
            PageSize        = pageSize;
            PageIndex       = pageIndex;
        }
    }

    [MetaMessage(MessageCodesCore.AsyncMatchmakerInspectBucketResponse, MessageDirection.ServerInternal)]
    public class AsyncMatchmakerInspectBucketResponse : MetaMessage
    {
        public int            BucketPlayerCount { get; private set; }
        public int            BucketMaxSize     { get; private set; }
        public int            PageSize          { get; private set; }
        public int            PageIndex         { get; private set; }
        public int            MmrLow            { get; private set; }
        public int            MmrHigh           { get; private set; }
        public byte[]         SerializedPlayers { get; private set; }
        public IBucketLabel[] Labels            { get; private set; }
        public int            LabelHash         { get; private set; }

        AsyncMatchmakerInspectBucketResponse() { }

        public static AsyncMatchmakerInspectBucketResponse CreateFor<TMmPlayerModel>(
            int bucketPlayerCount,
            int bucketMaxSize,
            int pageSize,
            int pageIndex,
            int mmrLow,
            int mmrHigh,
            IBucketLabel[] labels,
            int labelHash,
            IEnumerable<TMmPlayerModel> players)
            where TMmPlayerModel : struct, IAsyncMatchmakerPlayerModel
        {
            AsyncMatchmakerInspectBucketResponse response = new AsyncMatchmakerInspectBucketResponse();
            response.BucketPlayerCount = bucketPlayerCount;
            response.BucketMaxSize     = bucketMaxSize;
            response.PageSize          = pageSize;
            response.PageIndex         = pageIndex;
            response.MmrLow            = mmrLow;
            response.MmrHigh           = mmrHigh;
            response.Labels            = labels;
            response.LabelHash         = labelHash;

            using FlatIOBuffer ioB = new FlatIOBuffer();

            using (IOWriter ioW = new IOWriter(ioB))
            {
                foreach (TMmPlayerModel player in players.OrderByDescending(x => x.DefenseMmr).Skip(pageIndex * pageSize).Take(pageSize))
                    MetaSerialization.SerializeTagged(ioW, player, MetaSerializationFlags.SendOverNetwork, null);
            }

            response.SerializedPlayers = ioB.ToArray();

            return response;
        }

        public List<IAsyncMatchmakerPlayerModel> GetDeserializedPlayers(Type matchmakerModelType)
        {
            // \todo [nomi] When the serializer supports structs as MetaSerializableDerived, can yeet this.
            using IOReader               ioR     = new IOReader(SerializedPlayers);
            List<IAsyncMatchmakerPlayerModel> outList = new List<IAsyncMatchmakerPlayerModel>(BucketPlayerCount);

            while (ioR.Remaining > 0)
            {
                object model = MetaSerialization.DeserializeTagged(ioR, matchmakerModelType, MetaSerializationFlags.SendOverNetwork, null, null);
                outList.Add(model as IAsyncMatchmakerPlayerModel);
            }

            return outList;
        }
    }

    // MatchmakerEntityKindRegistry

    public static class AsyncMatchmakerEntityKindRegistry
    {
        static readonly Dictionary<EntityKind, AsyncMatchmakerEntityKind> _allMatchmakerEntityKinds = new Dictionary<EntityKind, AsyncMatchmakerEntityKind>();

        public static IEnumerable<AsyncMatchmakerEntityKind> AllMatchmakerKinds => _allMatchmakerEntityKinds.Values;

        public static void Register(AsyncMatchmakerEntityKind matchmakerKind)
        {
            _allMatchmakerEntityKinds[matchmakerKind.EntityKind] = matchmakerKind;
        }

        public static AsyncMatchmakerEntityKind ForEntityKind(EntityKind kind)
            => _allMatchmakerEntityKinds[kind];
    }

    // MatchmakerEntityKind

    public class AsyncMatchmakerEntityKind
    {
        readonly ThreadLocal<RandomPCG> _random = new ThreadLocal<RandomPCG>((() => RandomPCG.CreateNew()));
        public   int                    NumMatchmakers { get; private set; }
        public   Type                   QueryType      { get; }
        public   Type                   ModelType      { get; }
        public   EntityKind             EntityKind     { get; private set; }

        public AsyncMatchmakerEntityKind(EntityKind entityKind, Type queryType, Type modelType)
        {
            EntityKind  = entityKind;
            QueryType   = queryType;
            ModelType   = modelType;
            ClusteringOptions clusterOptions = RuntimeOptionsRegistry.Instance.GetCurrent<ClusteringOptions>();
            NumMatchmakers = clusterOptions.ClusterConfig.GetNodeCountForEntityKind(entityKind);
            AsyncMatchmakerEntityKindRegistry.Register(this);
        }

        /// <summary>
        /// Iterate over all matchmakers starting from a random index
        /// </summary>
        public IEnumerable<EntityId> GetQueryableMatchmakersRandom()
        {
            int startIndex = _random.Value.NextInt(NumMatchmakers);

            for (int i = 0; i < NumMatchmakers; i++)
            {
                yield return EntityId.Create(EntityKind, (ulong) ((startIndex + i) % NumMatchmakers));
            }
        }

        /// <summary>
        /// Iterate over all matchmakers in order of entityId
        /// </summary>
        public IEnumerable<EntityId> GetQueryableMatchmakersOrdered()
        {
            for (int i = 0; i < NumMatchmakers; i++)
            {
                yield return EntityId.Create(EntityKind, (ulong) i);
            }
        }

        /// <summary>
        /// Get the matchmaker (shard) responsible for storing the defending player's matchmaking data.
        /// <see cref="GetQueryableMatchmakersRandom"/> is used when querying.
        /// </summary>
        public EntityId GetMatchmakerForDefenderPlayer(EntityId playerId)
        {
            return EntityId.Create(EntityKind, (playerId.Value % (ulong) NumMatchmakers));
        }
    }

    // AsyncMatchmakerActorBase

    public abstract class AsyncMatchmakerConfigBase : PersistedEntityConfig
    {
        public override IShardingStrategy ShardingStrategy => new ServiceShardingStrategy(numServiceShards: 1);
        public override TimeSpan ShardShutdownTimeout => TimeSpan.FromSeconds(30);
    }

    public interface IAsyncMatchmaker : IMetaIntegration<IAsyncMatchmaker> { }

    /// <summary>
    /// The base Actor class for the asynchronous matchmaking service.
    /// The base class handles responding to EntityAsks, scanning the database, and keeping the mmr buckets in order.
    /// All that's left for the derived matchmaker is to figure out if a defender is a good match for the attacker.
    /// The mmr buckets are sized based on the mmr distribution of the playerbase, so that buckets that have fewer total players are also smaller.
    /// The total amount of stored players is limited, because most games cannot fit their entire playerbase into memory. Most of the parameters for
    /// the functioning of the matchmaker can be adjusted in <see cref="MatchmakerOptions"/>.
    /// </summary>
    /// <typeparam name="TPlayerModel">The game-specific playermodel</typeparam>
    /// <typeparam name="TMmPlayerModel">The matchmaker playermodel. This will be persisted to db and has to be tagged with <see cref="MetaSerializableAttribute"/></typeparam>
    /// <typeparam name="TMmQuery">The query used for matchmaking. This is passed to user-code as is.</typeparam>
    /// <typeparam name="TMatchmakerState">The persisted payload of the matchmaker that derives from <see cref="MatchmakerStateBase"/></typeparam>
    public abstract class AsyncMatchmakerActorBase<TPlayerModel, TMmPlayerModel, TMmQuery, TMatchmakerState, TMatchmakerOptions> :
        PersistedEntityActor<PersistedMatchmakerState, TMatchmakerState> , IAsyncMatchmaker
        where TPlayerModel : IPlayerModelBase
        where TMmPlayerModel : struct, IAsyncMatchmakerPlayerModel
        where TMmQuery : class, IAsyncMatchmakerQuery, new()
        where TMatchmakerState : AsyncMatchmakerActorBase<TPlayerModel, TMmPlayerModel, TMmQuery, TMatchmakerState, TMatchmakerOptions>.MatchmakerStateBase, new() // Serializer shenanigans
        where TMatchmakerOptions : AsyncMatchmakerOptionsBase
    {
        // \todo [nomi] Think about how to make persistence more granular (maybe not one single huge db operation)
        // \todo [nomi] Queue system if cannot find any opponents?
        // \todo [nomi] Can the matchmaker run into an infinite crash loop if a state is persisted into the db that causes it to crash?

        static readonly HistogramConfiguration c_matchmakerRequestMmrDifferenceConfig = new HistogramConfiguration()
        {
            Buckets = new double[]
            {
                0,
                1,
                2,
                5,
                10,
                20,
                50,
                100,
                200,
                500,
                1000,
                2000,
                5000,
                10_000,
            },
        };

        static readonly Counter   c_matchmakerRequests             = Prometheus.Metrics.CreateCounter("metaplay_matchmaker_requests_served", "Total number of requests served", "result");
        static readonly Histogram c_matchmakerRequestMmrDifference = Prometheus.Metrics.CreateHistogram("metaplay_matchmaker_requests_mmr_difference", "The mmr difference between the query and the returned candidate", c_matchmakerRequestMmrDifferenceConfig);
        static readonly Counter   c_matchmakerPlayerUpdates        = Prometheus.Metrics.CreateCounter("metaplay_matchmaker_playerupdates_received", "Total number of player updates received", "operation");
        static readonly Histogram c_databaseScanDurations          = Prometheus.Metrics.CreateHistogram("metaplay_matchmaker_dbscan_duration", "The duration of the whole database scan operation in seconds");
        static readonly Histogram c_databaseScanFoundRatio         = Prometheus.Metrics.CreateHistogram("metaplay_matchmaker_dbscan_found_ratio", "The ratio of desired players vs actual eligible players found");
        static readonly Gauge     c_bucketPopulations              = Prometheus.Metrics.CreateGauge("metaplay_matchmaker_bucket_populations", "Bucket populations", "mmr_range");
		static readonly Gauge     c_cacheCapacity                  = Prometheus.Metrics.CreateGauge("metaplay_matchmaker_cache_capacity", "The total capacity of the matchmaking cache.");
        static readonly Gauge     c_cachePopulation                = Prometheus.Metrics.CreateGauge("metaplay_matchmaker_cache_population", "The total population of the matchmaking cache.");
        static readonly Gauge     c_cacheBucketCount               = Prometheus.Metrics.CreateGauge("metaplay_matchmaker_cache_bucket_count", "The total number of buckets in the matchmaking cache.");

        /// <summary>
        /// A name for this matchmaker to show in the dashboard.
        /// </summary>
        protected abstract string MatchmakerName { get; }
        /// <summary>
        /// A description of this matchmaker to show in the dashboard.
        /// </summary>
        protected abstract string MatchmakerDescription { get; }


        /// <summary>
        /// Whether to enable the database scan feature at all.
        /// </summary>
        protected abstract bool EnableDatabaseScan { get; }

        /// <summary>
        /// At what bucket fill threshold do players NOT get added to the matchmaking buckets when receiving a <see cref="AsyncMatchmakingPlayerStateUpdate"/>
        /// <para>
        /// For example, <see cref="BucketFillLevelThreshold.Never"/> means player will ALWAYS get added to the buckets.
        /// </para>
        /// </summary>
        protected virtual BucketFillLevelThreshold PlayerIgnoreUpdateInsertThreshold => BucketFillLevelThreshold.LowPopulation;

        /// <summary>
        /// At what bucket fill threshold do players get removed from the matchmaking buckets when they are offered as a candidate
        /// </summary>
        protected virtual BucketFillLevelThreshold PlayerRemoveAfterCandidateOfferThreshold => BucketFillLevelThreshold.HighPopulation;

        protected IReadOnlyList<IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMmQuery>> BucketingStrategies { get; private set; }

        public static AsyncMatchmakerEntityKind Entities { get; private set; }

        [EntityActorRegisterCallback]
        static void InitForEntityKind(EntityConfigBase spec)
        {
            Entities = new AsyncMatchmakerEntityKind(spec.EntityKind, typeof(TMmQuery), typeof(TMmPlayerModel));
        }

        // DynamicBucketSizingData

        /// <summary>
        /// A container for sampling data for creating a <see cref="DynamicBucketSizingModel"/>.
        ///
        /// If not enough data has been collected, a default <see cref="DynamicBucketSizingModel"/> is created.
        /// </summary>
        public sealed class DynamicBucketSizingData
        {
            readonly Queue<int> _samples; // Contains Label hashes.

            public int MaxSize { get; private set; } // from MatchmakerOptions

            public bool HasEnoughData =>
                _samples.Count > Math.Min(MaxSize / 10, 1000);

            public DynamicBucketSizingData(int maxSamples)
            {
                MaxSize    = maxSamples;
                _samples = new Queue<int>(maxSamples);
            }

            /// <summary>
            /// Collect a sample of label hash and add it to <see cref="_samples"/>.
            /// If the sample queue is full, old samples are removed.
            /// </summary>
            public void AddSample(int sampleHash)
            {
                while (_samples.Count >= MaxSize)
                    _samples.Dequeue();
                _samples.Enqueue(sampleHash);
            }

            /// <summary>
            /// Build a new <see cref="DynamicBucketSizingModel"/> based on the collected samples.
            /// Bucket sizing is based on the proportion of the playerbase in that bucket.
            /// A default bucket size is defined in <see cref="MatchmakerOptionsBase"/> in case no samples hit a certain bucket.
            ///
            /// If not enough data has been collected, a default <see cref="DynamicBucketSizingModel"/> is returned using
            /// <see cref="DynamicBucketSizingModel.CreateDefault"/>.
            /// </summary>
            public DynamicBucketSizingModel BuildModel(AsyncMatchmakerBucketPool<TMmPlayerModel, TMmQuery> pool, TMatchmakerOptions options)
            {
                if (!HasEnoughData || MaxSize <= 0 || _samples.Count == 0)
                    return DynamicBucketSizingModel.CreateDefault(options);

                DynamicBucketSizingModel model = new();

                Dictionary<int, int> numSamplesByHash = new Dictionary<int, int>();
                foreach (int sampleHash in _samples)
                {
                    numSamplesByHash.TryGetValue(sampleHash, out int prevValue);
                    numSamplesByHash[sampleHash] = prevValue + 1;
                }

                // Hash collision in the labels might cause wrong bucket labels being fetched.
                // This results in two bucket's samples being added together.
                Dictionary<IBucketLabel[], float> bucketPopulationRatios = new Dictionary<IBucketLabel[], float>();
                foreach ((int hash, int numSamples) in numSamplesByHash)
                {
                    AsyncMatchmakerBucket<TMmPlayerModel> bucket = pool.TryGetBucketByHash(hash);
                    if (bucket != null)
                    {
                        float ratio = numSamples / (float)_samples.Count;
                        bucketPopulationRatios.Add(bucket.Labels, ratio);
                    }
                }

                model.BucketRatios = bucketPopulationRatios;

                return model;
            }
        }

        // DynamicBucketSizingModel

        /// <summary>
        /// <see cref="DynamicBucketSizingModel"/> is a model for bucket sizing based on data collected in <see cref="DynamicBucketSizingData"/>.
        ///
        /// During database scanning, each player's defenseMmr is added to the list of samples in <see cref="DynamicBucketSizingData"/>.
        /// When buckets are rebalanced, the distribution of the mmr in the playerbase is calculated from the collected samples and stored into
        /// <see cref="DynamicBucketSizingModel"/>. The bucket sizes are based on the ratio of players in each bucket, and adds up to a total
        /// of <see cref="MatchmakerOptions.PlayerCacheSize"/>.
        ///
        /// An additional minimum size for a bucket is defined in <see cref="MatchmakerOptions.MmrBucketMinSize"/>.
        /// If a player is encountered whose mmr is outside the range of previously allocated buckets, it is added to the bucket that most closely represents its mmr.
        /// </summary>
        [MetaSerializable]
        public sealed class DynamicBucketSizingModel
        {
            [MetaMember(1)] public Dictionary<IBucketLabel[], float> BucketRatios;

            Dictionary<int, float>  _hashedBucketRatios;
            public Dictionary<int, float> HashedBucketRatios
            {
                get
                {
                    if (_hashedBucketRatios == null)
                    {
                        _hashedBucketRatios = new Dictionary<int, float>();
                        foreach ((IBucketLabel[] key, float value) in BucketRatios)
                            _hashedBucketRatios[key.HashLabels()] = value;
                    }

                    return _hashedBucketRatios;
                }
            }
            public DynamicBucketSizingModel() { }

            public DynamicBucketSizingModel(Dictionary<IBucketLabel[], float> bucketRatios)
            {
                BucketRatios = bucketRatios;
            }

            /// <summary>
            /// Create a default <see cref="DynamicBucketSizingModel"/> that represents a uniform distribution of the mmr range between 0 and 10_000.
            /// </summary>
            /// <param name="numBuckets">The total number of buckets</param>
            public static DynamicBucketSizingModel CreateDefault(TMatchmakerOptions options)
            {
                return new DynamicBucketSizingModel(new Dictionary<IBucketLabel[], float>());
            }

            /// <summary>
            /// Create a new bucket based on this sizing model.
            /// </summary>
            public AsyncMatchmakerBucket<TMmPlayerModel> CreateBucket(IBucketLabel[] labels, AsyncMatchmakerOptionsBase options)
            {
                if (options.PlayerCacheSize <= 0)
                    throw new InvalidOperationException("PlayerCacheSize should be more than 0!");

                int bucketSize = options.BucketInitialSize;
                int labelsHash = labels.HashLabels();
                if (HashedBucketRatios.TryGetValue(labelsHash, out float ratioValue))
                    bucketSize = (int) (ratioValue * options.PlayerCacheSize);

                bucketSize = Math.Max(bucketSize, options.MmrBucketMinSize); // At least minimum size

                return new AsyncMatchmakerBucket<TMmPlayerModel>(bucketSize, labels);
            }

            /// <summary>
            /// Update an existing bucket with a new size and mmr-range based on a new model.
            /// The size is not changed if the total percentage change is less than <see cref="minSizeChange"/>.
            /// Returns true if size was changed.
            /// </summary>
            /// <param name="bucket">The bucket to update</param>
            /// <param name="bucketIndex">The index of the bucket</param>
            /// <param name="minSizeChange">The minimum percentage size change to care about</param>
            /// <param name="options">The runtime <see cref="MatchmakerOptions"/></param>
            /// <returns></returns>
            public bool UpdateBucket(AsyncMatchmakerBucket<TMmPlayerModel> bucket, float minSizeChange, AsyncMatchmakerOptionsBase options)
            {
                if (options.PlayerCacheSize <= 0)
                    throw new InvalidOperationException("PlayerCacheSize should be more than 0!");

                bool sizeChanged = true;

                int bucketSize = options.BucketInitialSize;

                if (HashedBucketRatios.TryGetValue(bucket.LabelsHash(), out float ratioValue))
                    bucketSize = (int)(ratioValue * options.PlayerCacheSize);

                bucketSize = Math.Max(bucketSize, options.MmrBucketMinSize); // At least minimum size

                if (Math.Abs(bucketSize - bucket.Capacity) < (bucket.Capacity * minSizeChange)) // Size didn't change enough to care about
                    sizeChanged = false;
                else
                    bucket.Resize(bucketSize);

                return sizeChanged;
            }
        }

        // \todo [nomi] user code can extend this, but migration hooks are in matchmaker base class for now.
        // MatchmakerStateBase
        [MetaSerializable]
        [MetaReservedMembers(1, 100)]
        public abstract class MatchmakerStateBase : ISchemaMigratable
        {
            [MetaMember(1)] public DynamicBucketSizingModel                SizingModel;
            [MetaMember(2)] public int                                     LastPlayerCacheTotalSize;
            [MetaMember(3)] public List<IAsyncMatchmakerBucketingStrategyState> BucketingStrategyStates;
            [MetaMember(4)] public MetaTime                                LastRebalanceOperationTime;
            [MetaMember(5)] public List<TMmPlayerModel>                    CachedPlayers;

            [IgnoreDataMember] public DynamicBucketSizingData                                     SizingData;
            [IgnoreDataMember] public AsyncMatchmakerBucketPool<TMmPlayerModel, TMmQuery>         BucketPool;
            [IgnoreDataMember] public Dictionary<EntityId, AsyncMatchmakerBucket<TMmPlayerModel>> PlayerToBucket;
        }

        class DbTickCommand
        {
            public static DbTickCommand Instance { get; } = new DbTickCommand();

            DbTickCommand() { }
        }

        class BucketsRebalanceCommand
        {
            public static BucketsRebalanceCommand Instance { get; } = new BucketsRebalanceCommand();

            BucketsRebalanceCommand() { }
        }

        /// <summary>
        /// Counts errors by type, and provides a string representation of the errors.
        /// </summary>
        class ErrorCounter
        {
            class ErrorInfo
            {
                public string Message;
                public int    Count;
            }

            public int                  Count          { get; private set; }
            Dictionary<Type, ErrorInfo> InternalErrors { get; set; }

            /// <summary>
            /// Record an error. If the error type is already recorded, increment the count.
            /// Due to some exceptions sharing common types, every unique error might not be recorded properly,
            /// but it should be good enough to alert the developer that something is wrong.
            /// </summary>
            public void Increment(Exception e)
            {
                Count++;
                InternalErrors ??= new Dictionary<Type, ErrorInfo>();

                if (InternalErrors.TryGetValue(e.GetType(), out ErrorInfo errorInfo))
                    errorInfo.Count++;
                else
                {
                    if (InternalErrors.Count >= 50) // Don't store more than 50 different error types.
                        return;

                    InternalErrors[e.GetType()] = new ErrorInfo {Message = e.Message, Count = 1};
                }
            }

            /// <summary>
            /// Return a string representation of the recorded errors.
            /// </summary>
            public string GetErrorString()
            {
                if (InternalErrors == null)
                    return string.Empty;

                StringBuilder sb = new StringBuilder();
                foreach ((Type type, ErrorInfo info) in InternalErrors)
                    sb.AppendLine(Invariant($"Type: {type} Count: {info.Count} Message: {info.Message}"));

                return sb.ToString();
            }
        }

        protected override sealed AutoShutdownPolicy ShutdownPolicy   => AutoShutdownPolicy.ShutdownNever();
        protected override        TimeSpan           SnapshotInterval => TimeSpan.FromHours(1); // Persist state every 1 hours

        protected TMatchmakerState State;

        readonly MetaDatabase _scanDb = MetaDatabase.Get(QueryPriority.Low);

        protected bool                  DbScanRunning { get; private set; }
        protected TMatchmakerOptions    MmOptions     { get; private set; }
        protected SystemOptions         SysOptions    { get; private set; }

        int  _stateSizeInBytes;
        bool _hasFinishedFirstBucketUpdate = false;

        List<EntityId> _dbScanPointers = new List<EntityId>();

        protected AsyncMatchmakerActorBase(EntityId entityId) : base(entityId) { }

        /// <summary>
        /// Can be overridden in derived class to add behavior to Initialize, but remember to call base.Initialize()
        /// </summary>
        protected override async Task Initialize()
        {
            MmOptions  = RuntimeOptionsRegistry.Instance.GetCurrent<TMatchmakerOptions>();
            SysOptions = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>();

            BucketingStrategies = InitializeAdditionalBucketingStrategies().Prepend(
                new MmrBucketingStrategy<TMmPlayerModel, TMmQuery>())
                .ToImmutableList();

            foreach (IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMmQuery> bucketingStrategy in BucketingStrategies)
            {
                if (bucketingStrategy.StrategyType == BucketingStrategyType.Unknown)
                    throw new InvalidOperationException($"BucketingStrategy label type needs to derive from either {typeof(IDistinctBucketLabel<>).Name} or {typeof(IRangedBucketLabel<>).Name}!");
            }

            // Try to fetch from database & restore from it (if exists)
            PersistedMatchmakerState persisted = await MetaDatabase.Get().TryGetAsync<PersistedMatchmakerState>(_entityId.ToString());

            if (persisted?.Payload != null)
                _stateSizeInBytes = persisted.Payload.Length;

            await InitializePersisted(persisted);

            if (EnableDatabaseScan)
                StartPeriodicTimer(TimeSpan.FromSeconds(10), MmOptions.DatabaseScanTick, DbTickCommand.Instance);

            StartPeriodicTimer(MmOptions.BucketsRebalanceInterval, BucketsRebalanceCommand.Instance);
        }

        protected override sealed Task<TMatchmakerState> InitializeNew()
        {
            // Create new state
            TMatchmakerState state = new();

            InitializeDefaultBuckets(state);
            InitializeNewDerivedState(state);

            state.BucketPool     = new AsyncMatchmakerBucketPool<TMmPlayerModel, TMmQuery>(BucketingStrategies, CreateNewBucket);
            state.SizingData     = new DynamicBucketSizingData(MmOptions.BucketsRebalanceSampleCount);
            state.PlayerToBucket = new Dictionary<EntityId, AsyncMatchmakerBucket<TMmPlayerModel>>();

            state.BucketingStrategyStates = new List<IAsyncMatchmakerBucketingStrategyState>();

            foreach (IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMmQuery> bucketingStrategy in BucketingStrategies)
            {
                if(bucketingStrategy.StateType == null)
                    continue;

                IAsyncMatchmakerBucketingStrategyState strategyState = bucketingStrategy.InitializeNew(MmOptions);
                bucketingStrategy.State = strategyState;

                if (strategyState != null)
                    state.BucketingStrategyStates.Add(strategyState);
            }

            return Task.FromResult(state);
        }

        static void InitializeDefaultBuckets(MatchmakerStateBase state)
        {
            TMatchmakerOptions mmOptions = RuntimeOptionsRegistry.Instance.GetCurrent<TMatchmakerOptions>();

            state.SizingData                 = new DynamicBucketSizingData(mmOptions.BucketsRebalanceSampleCount);
            state.SizingModel                = DynamicBucketSizingModel.CreateDefault(mmOptions);
            state.LastPlayerCacheTotalSize   = mmOptions.PlayerCacheSize;
            state.LastRebalanceOperationTime = MetaTime.Now;
        }

        protected override sealed Task<TMatchmakerState> RestoreFromPersisted(PersistedMatchmakerState persisted)
        {
            // Deserialize actual state
            TMatchmakerState state = DeserializePersistedPayload<TMatchmakerState>(persisted.Payload, resolver: null, logicVersion: null);

            TMatchmakerOptions matchmakerOpts = RuntimeOptionsRegistry.Instance.GetCurrent<TMatchmakerOptions>();

            if (state.LastPlayerCacheTotalSize != matchmakerOpts.PlayerCacheSize)
                _self.Tell(BucketsRebalanceCommand.Instance, _self);

            state.BucketingStrategyStates ??= new List<IAsyncMatchmakerBucketingStrategyState>();

            bool needsPruning = false;

            // Restore states for each strategy
            foreach (IAsyncMatchmakerBucketingStrategyState bucketingStrategyState in state.BucketingStrategyStates)
            {
                IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMmQuery> matchingStrategy =
                    BucketingStrategies.FirstOrDefault(x => x.StateType == bucketingStrategyState.GetType());

                if (matchingStrategy != null)
                    matchingStrategy.State = bucketingStrategyState;
                else
                {
                    needsPruning = true;
                    _log.Warning($"No matching bucketing provider found for state {bucketingStrategyState.GetType()}!");
                }
            }

            // Remove any extra states from the list
            if (needsPruning)
            {
                state.BucketingStrategyStates =
                    state.BucketingStrategyStates.Intersect(BucketingStrategies.Select(x => x.State).Where(x => x != null))
                        .ToList();
            }

            // Initialize any missing states for strategies that require one.
            foreach (IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMmQuery> strategy in BucketingStrategies)
            {
                if (strategy.State == null && strategy.StateType != null)
                {
                    IAsyncMatchmakerBucketingStrategyState newState = strategy.InitializeNew(MmOptions);
                    state.BucketingStrategyStates.Add(newState);
                    strategy.State = newState;
                }
            }

            return Task.FromResult(state);
        }

        protected override sealed Task PostLoad(TMatchmakerState payload, DateTime persistedAt, TimeSpan elapsedTime)
        {
            State = payload;

            State.BucketPool     ??= new AsyncMatchmakerBucketPool<TMmPlayerModel, TMmQuery>(BucketingStrategies, CreateNewBucket);
            State.SizingData     ??= new DynamicBucketSizingData(MmOptions.BucketsRebalanceSampleCount);
            State.PlayerToBucket ??= new Dictionary<EntityId, AsyncMatchmakerBucket<TMmPlayerModel>>();

            foreach (IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMmQuery> bucketingStrategy in BucketingStrategies)
                bucketingStrategy.PostLoad(MmOptions);

            // Put cached players into buckets.
            if (State.CachedPlayers != null)
                UpdateBuckets(State.CachedPlayers, new List<EntityId>());

            return Task.CompletedTask;
        }

        protected override sealed async Task PersistStateImpl(bool isInitial, bool isFinal)
        {
            Stopwatch watch = Stopwatch.StartNew();
            _log.Debug("Persisting state (isInitial={IsInitial}, isFinal={IsFinal}, schemaVersion={SchemaVersion})", isInitial, isFinal, _entityConfig.CurrentSchemaVersion);

            SetPlayersToDbCache();

            // Serialize and compress the state
            byte[] persistedPayload = SerializeToPersistedPayload(State, resolver: null, logicVersion: null);
            _stateSizeInBytes = persistedPayload.Length;

            // Persist in database
            PersistedMatchmakerState persisted = new PersistedMatchmakerState()
            {
                EntityId      = _entityId.ToString(),
                PersistedAt   = DateTime.UtcNow,
                Payload       = persistedPayload,
                SchemaVersion = _entityConfig.CurrentSchemaVersion,
                IsFinal       = isFinal,
            };

            if (_log.IsDebugEnabled)
            {
                // Logging the times here because matchmaker state can be huge, and might take a long time to persist.
                _log.Debug(Invariant($"Serialization of matchmaker state took {watch.ElapsedMilliseconds}ms. Persisted size is {_stateSizeInBytes / 1024} KB."));
                watch.Restart();
            }

            if (isInitial)
                await MetaDatabase.Get(QueryPriority.Normal).InsertAsync(persisted).ConfigureAwait(false);
            else
                await MetaDatabase.Get(QueryPriority.Normal).UpdateAsync(persisted).ConfigureAwait(false);

            if (_log.IsDebugEnabled)
                _log.Debug(Invariant($"Database update operation for matchmaker state took {watch.ElapsedMilliseconds}ms."));
        }

        void SetPlayersToDbCache()
        {
            State.CachedPlayers ??= new List<TMmPlayerModel>(MmOptions.DatabaseCacheSize);

            State.CachedPlayers.Clear();

            int   totalPlayers = State.BucketPool.AllBuckets.Sum(bucket => bucket.Count);
            float ratioToTake  = MmOptions.DatabaseCacheSize / (float)totalPlayers;

            // Take some players from each bucket to fill the database cache
            State.CachedPlayers.AddRange(
                State.BucketPool.AllBuckets.SelectMany(
                    bucket => bucket.Values.Take((int)MathF.Ceiling(bucket.Count * ratioToTake))));
        }

        #region MessageHandling

        /// <summary>
        /// <para>
        /// Matchmaking requests are handled as an EntityAsk.
        /// First, a <see cref="FindPlayerCandidatesForQuery"/> is called to get a list of candidates that could fulfill the query.
        /// Once a list of candidates has been selected, the best candidate of the list is sent in a <see cref="AsyncMatchmakingResponse"/>.
        /// </para>
        /// <para>
        /// If no candidates were found, an empty <see cref="EntityId"/> is returned with a <see cref="MatchmakingResponseType.NoneFound"/>.
        /// </para>
        /// </summary>
        [EntityAskHandler]
        public void HandleMatchmakingRequest(EntityShard.EntityAsk entityAsk, AsyncMatchmakingRequest request)
        {
            if (request.Query is not TMmQuery mmQuery)
                throw new InvalidEntityAsk($"Request contained an invalid IMatchmakingQuery {request}");

            if (_log.IsDebugEnabled)
                _log.Debug(Invariant($"Received a {nameof(AsyncMatchmakingRequest)} from playerId: {request.Query.AttackerId} with attackMmr: {request.Query.AttackMmr}"));

            using PooledList<(EntityId, float)> candidates    = FindPlayerCandidatesForQuery(mmQuery, request.NumRetries, request.ExcludedPlayerIds, out int candidatesChecked);
            EntityId                            bestCandidate = EntityId.None;

            if (candidates.Count > 0)
                bestCandidate = candidates.MaxBy(c => c.Item2).Item1;

            MatchmakingResponseType responseType = bestCandidate != EntityId.None
                ? MatchmakingResponseType.Success
                : MatchmakingResponseType.NoneFound;

            ReplyToAsk(entityAsk, new AsyncMatchmakingResponse(responseType, bestCandidate));

            if (bestCandidate != EntityId.None && State.PlayerToBucket.ContainsKey(bestCandidate))
            {
                AsyncMatchmakerBucket<TMmPlayerModel> bucket = State.PlayerToBucket[bestCandidate];

                TMmPlayerModel candidateModel = bucket[bestCandidate];
                c_matchmakerRequestMmrDifference.Observe(MathF.Abs(candidateModel.DefenseMmr - request.Query.AttackMmr));

                if (bucket.IsAboveThreshold(PlayerRemoveAfterCandidateOfferThreshold))
                {
                    bucket.RemoveById(bestCandidate);
                    State.PlayerToBucket.Remove(bestCandidate);
                }
            }

            // Update statistics
            c_matchmakerRequests.WithLabels(responseType.ToString()).Inc();

            if (_log.IsDebugEnabled)
            {
                _log.Debug(
                    Invariant($"MatchmakingRequest response {responseType} with {candidates.Count}/{candidatesChecked} candidates found/checked. ") +
                    $"Best candidate was {bestCandidate}.");
            }

            ValidateState(); // Debug only
        }

        /// <summary>
        /// <para>
        /// <see cref="AsyncMatchmakingPlayerStateUpdate"/>s are sent by the PlayerActor whenever it thinks the matchmaker
        /// should be aware of some change in the player state. The message should be sent to only one matchmaker to avoid
        /// generating too much traffic in the system. The matchmaker to send the update to can be selected with
        /// <see cref="AsyncMatchmakerEntityKind.GetMatchmakerForDefenderPlayer"/>.
        /// </para>
        /// <para>
        /// The matchmaker decides what to do with the message based on the contents and the current state of the matchmaking buckets.
        /// If the <see cref="AsyncMatchmakingPlayerStateUpdate"/> contains an empty state, it means that the player is no longer
        /// eligible for matchmaking and the matchmaker will remove it from the buckets. If the state is not empty, the matchmaker
        /// checks which bucket the player would be placed into, and if the player is already in some other bucket it will be moved to
        /// the new bucket. If the player wasn't found in any of the buckets, it is added only if the bucket is a low-population bucket.
        /// </para>
        /// <para>
        /// By adding players to buckets only if the bucket is not well populated, we can ease the burden on the database scanner if a bucket
        /// is hard to find players for, but can hopefully avoid situations where the same players are targeted over and over.
        /// </para>
        /// </summary>
        /// <param name="stateUpdate"></param>
        [MessageHandler]
        public void HandlePlayerUpdate(AsyncMatchmakingPlayerStateUpdate stateUpdate)
        {
            if (!stateUpdate.PlayerId.IsOfKind(EntityKindCore.Player))
                throw new InvalidOperationException($"PlayerUpdate contained an invalid EntityKind {stateUpdate}");

            if (_log.IsDebugEnabled)
                _log.Debug($"Received a {nameof(AsyncMatchmakingPlayerStateUpdate)} for playerId: {stateUpdate.PlayerId}");


            if (stateUpdate.SerializedMmPlayerModel == null) // Player is no longer a valid matchmaking target. Remove from buckets
            {
                if (State.PlayerToBucket.ContainsKey(stateUpdate.PlayerId))
                {
                    State.PlayerToBucket[stateUpdate.PlayerId].RemoveById(stateUpdate.PlayerId);
                    State.PlayerToBucket.Remove(stateUpdate.PlayerId);
                    c_matchmakerPlayerUpdates.WithLabels("Removed").Inc();
                }
                else
                {
                    c_matchmakerPlayerUpdates.WithLabels("NoOp").Inc();
                }
            }
            else
            {
                TMmPlayerModel mmPlayerModel =
                    MetaSerialization.DeserializeTagged<TMmPlayerModel>(stateUpdate.SerializedMmPlayerModel,
                        MetaSerializationFlags.IncludeAll, null, null);

                AsyncMatchmakerBucket<TMmPlayerModel> newBucket = State.BucketPool.GetBucketForPlayer(mmPlayerModel);

                if (State.PlayerToBucket.ContainsKey(mmPlayerModel.PlayerId))
                {
                    AsyncMatchmakerBucket<TMmPlayerModel> oldBucket = State.PlayerToBucket[mmPlayerModel.PlayerId];

                    if (newBucket != oldBucket)
                    {
                        oldBucket.RemoveById(mmPlayerModel.PlayerId);

                        EntityId? removed = newBucket.InsertOrReplace(mmPlayerModel);

                        State.PlayerToBucket[mmPlayerModel.PlayerId] = newBucket;

                        if (removed.HasValue)
                            State.PlayerToBucket.Remove(removed.Value);

                        c_matchmakerPlayerUpdates.WithLabels("BucketChanged").Inc();
                    }
                    else
                    {
                        newBucket.Update(mmPlayerModel);
                        c_matchmakerPlayerUpdates.WithLabels("Updated").Inc();
                    }
                }
                else if (!newBucket.IsAboveThreshold(PlayerIgnoreUpdateInsertThreshold))
                {
                    EntityId? removed = newBucket.InsertOrReplace(mmPlayerModel);
                    State.PlayerToBucket[mmPlayerModel.PlayerId] = newBucket;

                    if (removed.HasValue)
                        State.PlayerToBucket.Remove(removed.Value);

                    c_matchmakerPlayerUpdates.WithLabels("Inserted").Inc();
                    State.SizingData.AddSample(State.BucketPool.GetLabelHashForPlayer(mmPlayerModel));
                }
                else
                {
                    c_matchmakerPlayerUpdates.WithLabels("NoOp").Inc();
                }
            }

            ValidateState(); // Debug only
        }

        [EntityAskHandler]
        public AsyncMatchmakerInfoResponse HandleMatchmakerInfoRequest(AsyncMatchmakerInfoRequest request)
        {
            AsyncMatchmakerInfoResponse response = new
            (
                name: MatchmakerName,
                description: MatchmakerDescription,
                playersInBuckets: State.PlayerToBucket.Count,
                bucketsOverallFillPercentage: State.PlayerToBucket.Count / (float)State.BucketPool.AllBuckets.Sum(b => b.Capacity),
                bucketInfos: State.BucketPool.AllBuckets.Select(bucket =>
                    new AsyncMatchmakerInfoResponse.BucketInfo(
                        mmrLow: bucket.GetMmrLow(),
                        mmrHigh: bucket.GetMmrHigh(),
                        numPlayers: bucket.Count,
                        capacity: bucket.Capacity,
                        fillPercentage: bucket.Count / (float) bucket.Capacity,
                        labels: bucket.Labels,
                        labelHash: bucket.LabelsHash()))
                    .OrderBy(x => x.MmrLow).ThenBy(x => x.LabelHash).ToArray(),
                stateSizeInBytes: _stateSizeInBytes,
                hasEnoughDataForBucketRebalance: State.SizingData.HasEnoughData,
                lastRebalanceOperationTime: State.LastRebalanceOperationTime,
                hasFinishedBucketUpdate: _hasFinishedFirstBucketUpdate
            );

            return response;
        }

        [EntityAskHandler]
        public AsyncMatchmakerPlayerInfoResponse HandleMatchmakerPlayerInfoRequest(AsyncMatchmakerPlayerInfoRequest request)
        {
            EntityId playerId = request.PlayerId;
            if (!State.PlayerToBucket.TryGetValue(playerId, out AsyncMatchmakerBucket<TMmPlayerModel> bucket))
            {
                return AsyncMatchmakerPlayerInfoResponse.ForNonParticipant(
                    playerId,
                    _entityId,
                    MatchmakerName,
                    MatchmakerDescription);
            }

            TMmPlayerModel player = bucket[playerId];

            int totalPlayers = State.PlayerToBucket.Count;
            int playersBelow = State.BucketPool.AllBuckets.OrderBy(b =>
                b.GetMmrLow()).TakeWhile(b => b != bucket).Sum(x => x.Count);

            float percentile = playersBelow / (float)totalPlayers;

            return AsyncMatchmakerPlayerInfoResponse.ForParticipant(
                playerId: playerId,
                defenseMmr: player.DefenseMmr,
                percentile: percentile,
                bucketInfo: new AsyncMatchmakerInfoResponse.BucketInfo(
                    mmrLow: bucket.GetMmrLow(),
                    mmrHigh: bucket.GetMmrHigh(),
                    numPlayers: bucket.Count,
                    capacity: bucket.Capacity,
                    fillPercentage: bucket.Count / (float)bucket.Capacity,
                    labels: bucket.Labels,
                    labelHash: bucket.LabelsHash()),
                matchmakerId: _entityId,
                matchmakerName: MatchmakerName,
                matchmakerDescription: MatchmakerDescription);
        }

        [EntityAskHandler]
        public EntityAskOk HandleMatchmakerRebalanceRequest(AsyncMatchmakerRebalanceBucketsRequest _)
        {
            _self.Tell(BucketsRebalanceCommand.Instance, _self);
            return EntityAskOk.Instance;
        }

        [EntityAskHandler]
        public async Task<EntityAskOk> HandleMatchmakerClearStateRequest(AsyncMatchmakerClearStateRequest _)
        {
            State = await InitializeNew();

            foreach (IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMmQuery> strategy in BucketingStrategies)
                strategy.OnResetState(MmOptions);

            await PersistStateIntermediate();

            if(EnableDatabaseScan)
                _self.Tell(DbTickCommand.Instance, _self);

            _hasFinishedFirstBucketUpdate = false;

            return EntityAskOk.Instance;
        }

        [EntityAskHandler]
        public AsyncMatchmakerInspectBucketResponse HandleMatchmakerInspectBucketRequest(AsyncMatchmakerInspectBucketRequest request)
        {
            AsyncMatchmakerBucket<TMmPlayerModel> bucket = State.BucketPool.AllBuckets
                .FirstOrDefault(x => x.LabelsHash() == request.BucketLabelHash);

            if (bucket == null)
                throw new InvalidEntityAsk("Bucket with hash not found!");

            return AsyncMatchmakerInspectBucketResponse.CreateFor<TMmPlayerModel>(
                bucket.Count,
                bucket.Capacity,
                request.PageSize,
                request.PageIndex,
                bucket.GetMmrLow(),
                bucket.GetMmrHigh(),
                bucket.Labels,
                bucket.LabelsHash(),
                bucket);
        }

        [EntityAskHandler]
        public async Task<AsyncMatchmakerPlayerEnrollResponse> HandleMatchmakerPlayerEnrollRequest(AsyncMatchmakerPlayerEnrollRequest request)
        {
            if (!request.PlayerId.IsValid)
                throw new InvalidEntityAsk("Given playerId is not valid!");

            if (request.IsRemoval)
            {
                HandlePlayerUpdate(AsyncMatchmakingPlayerStateUpdate.Create<TMmPlayerModel>(request.PlayerId, null));
                return AsyncMatchmakerPlayerEnrollResponse.Success;
            }

            // Player already in matchmaker. Nothing to do.
            if (State.PlayerToBucket.ContainsKey(request.PlayerId))
                return AsyncMatchmakerPlayerEnrollResponse.Success;

            InternalEntityStateResponse response          = await EntityAskAsync<InternalEntityStateResponse>(request.PlayerId, InternalEntityStateRequest.Instance);
            FullGameConfig              specializedConfig = await ServerGameConfigProvider.Instance.GetSpecializedGameConfigAsync(response.StaticGameConfigId, response.DynamicGameConfigId, response.SpecializationKey);

            IPlayerModelBase model;
            try
            {
                model = (IPlayerModelBase)response.Model.Deserialize(resolver: specializedConfig.SharedConfig, response.LogicVersion);
                model.GameConfig   = specializedConfig.SharedConfig;
                model.LogicVersion = response.LogicVersion;
            }
            catch
            {
                throw new InvalidEntityAsk($"Cannot deserialize player with ID {request.PlayerId}.");
            }

            TMmPlayerModel? matchmakerModel = TryCreateModel((TPlayerModel)model);

            if (!matchmakerModel.HasValue)
                return AsyncMatchmakerPlayerEnrollResponse.Failure;

            AsyncMatchmakerBucket<TMmPlayerModel> bucket = State.BucketPool.GetBucketForPlayer(matchmakerModel.Value);

            EntityId? removed = bucket.InsertOrReplace(matchmakerModel.Value);
            State.PlayerToBucket[matchmakerModel.Value.PlayerId] = bucket;

            if (removed.HasValue)
                State.PlayerToBucket.Remove(removed.Value);

            return AsyncMatchmakerPlayerEnrollResponse.Success;
        }

        #endregion

        #region InternalCommands

        [CommandHandler]
        void HandleDbScanTick(DbTickCommand _)
        {
            if (!DbScanRunning && EnableDatabaseScan)
            {
                DbScanRunning = true;

                Stopwatch timer = Stopwatch.StartNew();

                _log.Debug("Starting database scan...");

                ContinueTaskOnActorContext(
                    Task.Run(ScanDbForPlayers),
                    players =>
                    {
                        DbScanRunning = false;

                        UpdateBuckets(players.Item1, players.Item2);

                        ValidateState(); // Debug only

                        timer.Stop();

                        c_databaseScanDurations.Observe(timer.Elapsed.TotalSeconds);

                        ObserveBuckets(); // Prometheus bucket count reporting

                        if (_log.IsDebugEnabled)
                            _log.Debug(Invariant($"Database scan finished in {timer.ElapsedMilliseconds}ms"));

                        _hasFinishedFirstBucketUpdate = true;
                    },
                    error =>
                    {
                        DbScanRunning = false;
                        _log.Error("Database scan failed: {ex}", error);
                    });
            }
        }

        /// <summary>
        /// <para>
        /// Rebalance the buckets based on current <see cref="DynamicBucketSizingData"/>.
        /// Buckets can be added or deleted if <see cref="MatchmakerOptions.MmrBucketCount"/> has changed.
        /// </para>
        /// <para>
        /// Some players might be lost from the buckets during a rebalancing operation so the <see cref="MatchmakerStateBase.PlayerToBucket"/> dictionary
        /// is also rebuilt. Players are moved between buckets if the bucket bounds get changed.
        /// </para>
        /// </summary>
        [CommandHandler]
        async Task HandleBucketRebalanceCommand(BucketsRebalanceCommand _)
        {
            _log.Debug("Starting mmr bucket rebalancing...");
            int oldNumBuckets = State.SizingModel.BucketRatios.Count;

            if (!State.SizingData.HasEnoughData)
            {
                if (State.LastPlayerCacheTotalSize != MmOptions.PlayerCacheSize)
                {
                    // Still want to rebalance buckets, but with default sizing model
                    State.SizingModel = DynamicBucketSizingModel.CreateDefault(MmOptions);
                }
                else
                {
                    _log.Debug("Rebalance operation canceled. Not enough data!");
                    return;
                }
            }
            else
            {
                State.SizingModel = State.SizingData.BuildModel(State.BucketPool, MmOptions);
            }

            bool needRebuild = oldNumBuckets != State.SizingModel.BucketRatios.Count;

            foreach (IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMmQuery> strategy in BucketingStrategies)
            {
                if (strategy is IAutoRebalancingBucketingStrategy<TMmPlayerModel> rebalancingStrategy)
                {
                    if (rebalancingStrategy.OnRebalance(MmOptions))
                        needRebuild = true;
                }
            }

            foreach (AsyncMatchmakerBucket<TMmPlayerModel> bucket in State.BucketPool.AllBuckets)
            {
                if (State.SizingModel.UpdateBucket(bucket, 0.1f, MmOptions))
                    needRebuild = true;
            }

            if (needRebuild) // Rebuild EntityId dictionary and move players around
            {
                State.PlayerToBucket.Clear();

                // Copy to not modify while iterating.
                AsyncMatchmakerBucket<TMmPlayerModel>[] oldBuckets = State.BucketPool.AllBuckets.ToArray();
                List<TMmPlayerModel> players = new List<TMmPlayerModel>();

                foreach (AsyncMatchmakerBucket<TMmPlayerModel> oldBucket in oldBuckets)
                {
                    // Copy to not modify collection while iterating.
                    players.Clear();
                    players.AddRange(oldBucket.Values);

                    foreach (TMmPlayerModel player in players)
                    {
                        AsyncMatchmakerBucket<TMmPlayerModel> newBucket = State.BucketPool.GetBucketForPlayer(player);

                        if (newBucket != oldBucket) // Bucket changed
                        {
                            oldBucket.RemoveById(player.PlayerId);
                            newBucket.InsertOrReplace(player);
                        }
                    }
                }

                State.BucketPool.PruneEmptyBuckets();

                // Update PlayerToBucket dictionary.
                foreach (AsyncMatchmakerBucket<TMmPlayerModel> bucket in State.BucketPool.AllBuckets)
                {
                    foreach (TMmPlayerModel player in bucket)
                        State.PlayerToBucket[player.PlayerId] = bucket;
                }
            }

            State.LastPlayerCacheTotalSize = MmOptions.PlayerCacheSize;
            State.LastRebalanceOperationTime = MetaTime.Now;

            _log.Debug("Bucket rebalancing finished!");

            ValidateState(); // Debug only

            await PersistStateIntermediate();
        }

#endregion

        /// <summary>
        /// <para>
        /// Find defender candidates for the given matchmaker query and return a list of candidates.
        /// This method can be overridden by the derived class to change the default behaviour.
        /// </para>
        /// <para>
        /// The default implementation will create a new <see cref="PooledList{TListItem}"/> that has a capacity of
        /// <see cref="AsyncMatchmakerOptionsBase.MatchingCandidateAmount"/>. A <see cref="AsyncMatchmakerBucketWalker{TMmPlayerModel,TMmQuery}"/> is used to scan through the mmr buckets
        /// for possible candidates. For each candidate found, <see cref="CheckPossibleMatchForQuery"/> is called to let the user-code
        /// decide if the match is possible and how good of a match it is. Search is concluded if the amount of found eligible candidates
        /// hits <see cref="AsyncMatchmakerOptionsBase.MatchingCandidateAmount"/> or the number of overall tested candidates hits
        /// <see cref="AsyncMatchmakerOptionsBase.MatchingMaxSearchNum"/>.
        /// </para>
        /// </summary>
        /// <param name="query">The matchmaker query</param>
        /// <param name="numRetries">The number of times this query has been retried</param>
        /// <param name="excludedPlayerIds">The list of excluded PlayerId's.</param>
        /// <param name="candidatesTested">The number of candidates tested while finding candidates. This should be updated by the function.</param>
        /// <returns>The list of found eligible candidates.</returns>
        protected virtual PooledList<(EntityId, float)> FindPlayerCandidatesForQuery(TMmQuery query, int numRetries, ICollection<EntityId> excludedPlayerIds, out int candidatesTested)
        {
            PooledList<(EntityId, float)> list = PooledList<(EntityId, float)>.Create(MmOptions.MatchingCandidateAmount);
            AsyncMatchmakerBucketWalker<TMmPlayerModel, TMmQuery> bucketWalker =
                new AsyncMatchmakerBucketWalker<TMmPlayerModel, TMmQuery>(State.BucketPool, query);

            candidatesTested = 0;
            foreach (TMmPlayerModel player in bucketWalker)
            {
                if (query.AttackerId == player.PlayerId)
                    continue;

                if (excludedPlayerIds != null && excludedPlayerIds.Contains(player.PlayerId))
                    continue;

                candidatesTested++;

                if (candidatesTested > MmOptions.MatchingMaxSearchNum)
                    break;

                bool isPossibleMatch = CheckPossibleMatchForQuery(query, player, numRetries, out float quality);

                if (!isPossibleMatch)
                    continue;

                list.Add((player.PlayerId, quality));

                if (list.Count >= MmOptions.MatchingCandidateAmount)
                    break;
            }

            return list;
        }

        /// <summary>
        /// Fetch a single batch of players from the database starting from a given PlayerId.
        /// </summary>
        async Task<IEnumerable<TPlayerModel>> FetchBatchFromDb(EntityId startId, int batchSize, ISharedGameConfig config, int activeLogicVersion, ErrorCounter errorCounter)
        {
            IGameConfigDataResolver resolver = config;
            List<PersistedPlayerBase> players = await _scanDb.QuerySinglePageRangeAsync<PersistedPlayerBase>(
                opName: "MatchmakerDbScan",
                startId.ToString(),
                batchSize,
                EntityId.Create(EntityKindCore.Player, SysOptions.AllowBotAccounts ? 0 : Authenticator.NumReservedBotIds).ToString(),
                EntityId.Create(EntityKindCore.Player, EntityId.ValueMask).ToString());

            PersistedEntityConfig entityConfig = EntityConfigRegistry.Instance.GetPersistedConfig(EntityKindCore.Player);

            return players.Where(player => player.Payload != null).Select(player =>
            {
                MetaDebug.Assert(
                    SysOptions.AllowBotAccounts || EntityId.ParseFromString(player.EntityId).Value >=
                    Authenticator.NumReservedBotIds, $"Got invalid player id {player.EntityId}");
                TPlayerModel model = default;
                // \todo [nomi] Matchmaker experiments?
                try
                {
                    model = entityConfig.DeserializeDatabasePayload<TPlayerModel>(player.Payload, resolver, activeLogicVersion);

                    model.GameConfig   = config;
                    model.LogicVersion = activeLogicVersion;

                    MetaTime     now         = MetaTime.Now;
                    MetaDuration elapsedTime = now - MetaTime.FromDateTime(player.PersistedAt);
                    model.OnRestoredFromPersistedState(now, elapsedTime);
                }
                catch (Exception e)
                {
                    errorCounter.Increment(e);
                }

                return model;
            }).Where(model => model != null);
        }

        /// <summary>
        /// <para>
        /// Scan the database for players in configurable batches. The amount of players to fetch is scaled based on the amount of players present in the buckets.
        /// The exact numbers for this behaviour can be adjusted from <see cref="MatchmakerOptions"/>.
        /// </para><para>
        /// When the average inverse bucket PopulationDensity is at 1 (every bucket has low population),
        /// the database scanner fetches <see cref="MatchmakerOptions.DatabaseScanMaximumFetch"/> amount of players.
        /// When all buckets have high population,
        /// the database scanner fetches <see cref="MatchmakerOptions.DatabaseScanMinimumFetch"/> amount of players.
        /// </para>
        /// </summary>
        /// <returns>A list of eligible players, and a list of ineligible players that can be removed</returns>
        async Task<(IList<TMmPlayerModel>, IList<EntityId>)> ScanDbForPlayers()
        {
            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();

            if (activeGameConfig?.BaselineGameConfig == null)
                return default;

            int batchSize = MmOptions.DatabaseScanBatchSize;

            // Calculate average of (1 - populationMetric)^2 from each bucket
            float avgInversePopulationDensitySqrd = State.BucketPool.IsEmpty ? 1:
                State.BucketPool.AllBuckets.Average(b => MathF.Pow(1 - b.PopulationDensity, 2));

            MetaDebug.Assert(avgInversePopulationDensitySqrd <= 1.0001, Invariant($"Matchmaker avgPopulationDensity is more than 1! {avgInversePopulationDensitySqrd}"));

            List<TMmPlayerModel> result = new();
            List<EntityId> ineligiblePlayers = new();
            HashSet<EntityId> uniquePlayers = new();
            RandomPCG random = RandomPCG.CreateNew();

            // Scale players to fetch based on average of populationMetric
            int playersToFetch = (int) (MmOptions.DatabaseScanMinimumFetch +
                (MmOptions.DatabaseScanMaximumFetch -
                    MmOptions.DatabaseScanMinimumFetch) * avgInversePopulationDensitySqrd);

            int batchesToFetch = (playersToFetch + batchSize - 1) / batchSize;

            if (_log.IsDebugEnabled)
                _log.Debug("Trying to find {Players} players in {Batches} batches...", playersToFetch, batchesToFetch);

            ISharedGameConfig sharedGameConfig = activeGameConfig.BaselineGameConfig.SharedConfig;
            int activeLogicVersion = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;

            ErrorCounter errorCounter = new ErrorCounter();

            for (int i = 0; i < batchesToFetch; i++)
            {
                if (_dbScanPointers.Count <= i)
                {
                    EntityId sampleId = GenerateRandomPlayerId(SysOptions.AllowBotAccounts);
                    _dbScanPointers.Add(sampleId);
                }

                // Reset pointer every now and then
                if (random.NextInt(100) < 1)
                    _dbScanPointers[i] = GenerateRandomPlayerId(SysOptions.AllowBotAccounts);

                IEnumerable<TPlayerModel> playerBatch = await FetchBatchFromDb(_dbScanPointers[i], batchSize, sharedGameConfig, activeLogicVersion, errorCounter);

                foreach (TPlayerModel playerModel in playerBatch)
                {
                    if (uniquePlayers.Contains(playerModel.PlayerId))
                        continue;

                    uniquePlayers.Add(playerModel.PlayerId);

                    TMmPlayerModel? mmModel = TryCreateModel(playerModel);

                    if (mmModel.HasValue)
                    {
                        result.Add(mmModel.Value);
                    }
                    else
                    {
                        ineligiblePlayers.Add(playerModel.PlayerId);
                    }

                    // Move scan pointer forward

                    const ulong maxEntityValue = EntityId.ValueMask;
                    if (playerModel.PlayerId.Value != maxEntityValue)
                        _dbScanPointers[i] = EntityId.Create(EntityKindCore.Player, playerModel.PlayerId.Value + 1);
                    else
                        _dbScanPointers[i] = SysOptions.AllowBotAccounts ?
                            EntityId.Create(EntityKindCore.Player, 0) :
                            EntityId.Create(EntityKindCore.Player, Authenticator.NumReservedBotIds);
                }
            }

            if (_log.IsDebugEnabled && errorCounter.Count == 0)
                _log.Debug("Found {Count} eligible players", result.Count);
            else if (errorCounter.Count > 0)
            {
                _log.Warning("Found {Count} eligible players, but {NumErrors} error(s) occurred while deserializing players from database\n{ErrorMessages}",
                    result.Count, errorCounter.Count, errorCounter.GetErrorString());
            }

            c_databaseScanFoundRatio.Observe(result.Count / (float)playersToFetch);

            return (result, ineligiblePlayers);
        }

        /// <summary>
        /// Update matchmaking buckets with the given players. Any ineligible players are removed from buckets.
        /// </summary>
        /// <param name="players">The players to be added / updated</param>
        /// <param name="ineligible">The players who are no longer eligible for matchmaking and should be removed</param>
        void UpdateBuckets(IList<TMmPlayerModel> players, IList<EntityId> ineligible)
        {
            void AddToBucket(in TMmPlayerModel player, AsyncMatchmakerBucket<TMmPlayerModel> bucket)
            {
                EntityId? removed = bucket.InsertOrReplace(player);

                if (removed.HasValue)
                    State.PlayerToBucket.Remove(removed.Value);

                State.PlayerToBucket[player.PlayerId] = bucket;
            }

            // Remove any ineligible players
            foreach (EntityId id in ineligible)
            {
                if (State.PlayerToBucket.TryGetValue(id, out AsyncMatchmakerBucket<TMmPlayerModel> bucket))
                {
                    bucket.RemoveById(id);
                    State.PlayerToBucket.Remove(id);
                }
            }

            foreach (TMmPlayerModel mmPlayer in players)
            {
                EntityId playerId = mmPlayer.PlayerId;

                State.SizingData.AddSample(State.BucketPool.GetLabelHashForPlayer(mmPlayer));
                AsyncMatchmakerBucket<TMmPlayerModel> desiredBucket = State.BucketPool.GetBucketForPlayer(mmPlayer);

                // Player already exists in a bucket
                if (State.PlayerToBucket.TryGetValue(playerId, out AsyncMatchmakerBucket<TMmPlayerModel> bucket))
                {
                    if (desiredBucket != bucket)
                    {
                        // Player mmr has changed and bucket needs to be changed
                        bucket.RemoveById(playerId);
                        AddToBucket(mmPlayer, desiredBucket);
                    }
                    else
                    {
                        bucket.Update(mmPlayer);
                    }
                }
                // Player does not exist in any buckets
                else
                {
                    AddToBucket(mmPlayer, desiredBucket);
                }
            }

            foreach (IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMmQuery> bucketingStrategy in BucketingStrategies)
            {
                if (bucketingStrategy is IAutoRebalancingBucketingStrategy<TMmPlayerModel> rebalancingBucketingStrategy)
                {
                    foreach (TMmPlayerModel mmPlayer in players)
                        rebalancingBucketingStrategy.CollectSample(mmPlayer);
                }
            }
        }

        /// <summary>
        /// Generate a random unique playerId. Can generate bot-reserved ids if <paramref name="botsOk"/> is true.
        /// </summary>
        static EntityId GenerateRandomPlayerId(bool botsOk)
        {
            for (int guard = 0; guard < 1000; guard++)
            {
                EntityId playerId = EntityId.CreateRandom(EntityKindCore.Player);
                if (botsOk || playerId.Value >= Authenticator.NumReservedBotIds)
                {
                    return playerId;
                }
            }

            return EntityId.CreateRandom(EntityKindCore.Player);
        }

        AsyncMatchmakerBucket<TMmPlayerModel> CreateNewBucket(IBucketLabel[] labels)
        {
            return State.SizingModel.CreateBucket(labels, MmOptions);
        }

        #region UserOverridden

        /// <summary>
        /// Initialize any members in the derived <typeparamref name="TMatchmakerState" /> when initializing a fresh state.
        /// </summary>
        /// <param name="state">The <typeparamref name="TMatchmakerState" /> to be initialized.</param>
        protected virtual void InitializeNewDerivedState(TMatchmakerState state) { }

        /// <summary>
        /// Try to create a matchmaker playermodel from a game-specific playermodel.
        /// Should return null if the player is not eligible for matchmaking.
        /// </summary>
        /// <param name="model">The game-specific playermodel</param>
        /// <returns>A MatchmakerPlayerModel</returns>
        protected abstract TMmPlayerModel? TryCreateModel(TPlayerModel model);

        /// <summary>
        /// Check if a defender is a possible match for a query, and how good of a match it is.
        /// This usually includes things like how close the attacker and defender MMR's are, and if the players
        /// belong to the same guild or not. The matchmaker by default will try to find candidates whose DefenseMmr is close to
        /// the queried AttackMmr before calling this function to do additional checks.
        /// </summary>
        /// <param name="query">The matchmaking query sent by the attacker</param>
        /// <param name="player">The matchmaker playermodel of the defender candidate</param>
        /// <param name="numRetries">How many retries has already been made for the same query. This can be used to slacken the requirements</param>
        /// <param name="quality">An out parameter indicating how good of a match it would be</param>
        /// <returns>A bool indicating whether a match is possible.</returns>
        protected abstract bool CheckPossibleMatchForQuery(TMmQuery query, in TMmPlayerModel player, int numRetries, out float quality);

        protected virtual IEnumerable<IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMmQuery>> InitializeAdditionalBucketingStrategies()
            => Array.Empty<IAsyncMatchmakerBucketingStrategy<TMmPlayerModel, TMmQuery>>();
#endregion

        /// <summary>
        /// Observe mmr buckets and send data to prometheus
        /// </summary>
        void ObserveBuckets()
        {
            static float OverlapPercentage(int min1, int min2, int max1, int max2)
            {
                float overlap = Math.Max(0, Math.Min(max1, max2) - Math.Max(min1, min2));
                float length = max1 - min1;
                return Math.Clamp(overlap / length, 0, 1);
            }

            if (State.BucketPool.IsEmpty)
                return;

            (int MinMmr, int MaxMmr, int Pop)[] mmrs = State.BucketPool.AllBuckets.Select(
                x =>
                    (MinMmr: x.GetMmrLow(), MaxMmr: x.GetMmrHigh(), Pop: x.Count))
                .ToArray();

            int totalMax = mmrs.Max(x => x.MaxMmr);

            for (int minMmr = 0; minMmr < totalMax; minMmr += MmOptions.AnalyticsMmrStep)
            {
                int maxMmr = minMmr + MmOptions.AnalyticsMmrStep;
                // Buckets are sized dynamically so there is no defined boundary. Using increments from MMOptions.
                int playersInBuckets = (int)mmrs.Select(bucket =>
                        (bucket.Pop, Percentage: OverlapPercentage(bucket.MinMmr, minMmr, bucket.MaxMmr, maxMmr)))
                    .Sum(bucket => bucket.Pop * bucket.Percentage);

                if (playersInBuckets > 0)
                {
                    c_bucketPopulations.WithLabels(Invariant($"{minMmr}-{maxMmr}")).Set(playersInBuckets);
                }
            }

            c_cacheCapacity.Set(State.BucketPool.AllBuckets.Sum(b => b.Capacity));
            c_cachePopulation.Set(State.BucketPool.AllBuckets.Sum(b => b.Count));
            c_cacheBucketCount.Set(State.BucketPool.NumBuckets);
        }

        /// <summary>
        /// Validate the state for the buckets and PlayerToBucket dictionary (debug mode only)
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        public void ValidateState()
        {
            foreach (AsyncMatchmakerBucket<TMmPlayerModel> bucket in State.BucketPool.AllBuckets)
                bucket.ValidateState(MmOptions);

            MetaDebug.Assert(State.PlayerToBucket.Count == State.BucketPool.AllBuckets.Sum(bucket => bucket.Count), "Matchmaker ValidateState failed: buckets and PlayerToBucket count mismatch!");

            foreach ((EntityId key, AsyncMatchmakerBucket<TMmPlayerModel> bucket) in State.PlayerToBucket)
                MetaDebug.Assert(bucket.ContainsKey(key), "Matchmaker ValidateState failed: PlayerToBucket mismatch!");
        }
    }
}
