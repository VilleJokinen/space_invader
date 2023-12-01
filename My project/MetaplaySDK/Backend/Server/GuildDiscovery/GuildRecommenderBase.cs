// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.GuildDiscovery;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;

namespace Metaplay.Server.GuildDiscovery
{
    public abstract class GuildRecommenderConfigBase : EphemeralEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.GuildRecommender;
        public override IShardingStrategy   ShardingStrategy        => new ServiceShardingStrategy(numServiceShards: 1);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(5);
    }

    public abstract class GuildRecommenderActorBase : EphemeralEntityActor
    {
        class Tick { public static readonly Tick Instance = new Tick(); }

        static readonly Prometheus.Counter      c_requests          = Prometheus.Metrics.CreateCounter("game_guildrecommender_requests_total", "Number of guild recommendation requests");
        static readonly Prometheus.Counter      c_updates           = Prometheus.Metrics.CreateCounter("game_guildrecommender_updates_total", "Number of guild recommendation info updates");
        static readonly Prometheus.Gauge        c_poolSize          = Prometheus.Metrics.CreateGauge("game_guildrecommender_pool_size", "Number of guild infos in a recommender pool", "pool");

        protected override sealed AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        public static readonly EntityId EntityId = EntityId.Create(EntityKindCloudCore.GuildRecommender, 0);

        bool _actorStarted;
        List<PersistedGuildDiscoveryPool> _allPools;
        int _persistPoolNdx;

        public GuildRecommenderActorBase(EntityId entityId) : base(entityId)
        {
            _allPools = new List<PersistedGuildDiscoveryPool>();
        }

        #region Lifecycle and Persistence

        protected sealed override async Task Initialize()
        {
            _actorStarted = true;

            int logicVersion = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;

            foreach (PersistedGuildDiscoveryPool pool in _allPools)
                await pool.Initialize(logicVersion, _log);

            StartRandomizedPeriodicTimer(TimeSpan.FromSeconds(10), Tick.Instance);

            // Explicitly publish the metrics. These are can often 0-value which are suppressed by default.
            foreach (PersistedGuildDiscoveryPool pool in _allPools)
            {
                Prometheus.Gauge.Child gauge = c_poolSize.WithLabels(pool.PoolId);
                gauge.Set(pool.Count);
                gauge.Publish();
            }
            c_requests.Publish();
            c_updates.Publish();
        }

        protected sealed override async Task OnShutdown()
        {
            foreach (var pool in _allPools)
                await pool.Shutdown();
        }

        protected override void RegisterHandlers()
        {
            ReceiveAsync<Tick>(ReceiveTick);
            base.RegisterHandlers();
        }

        async Task ReceiveTick(Tick tick)
        {
            if (_allPools.Count == 0)
                return;

            if (await _allPools[_persistPoolNdx].PersistStep())
                _persistPoolNdx = (_persistPoolNdx + 1) % _allPools.Count;

            foreach (var pool in _allPools)
                c_poolSize.WithLabels(pool.PoolId).Set(pool.Count);
        }

        #endregion

        #region Messages

        [EntityAskHandler]
        InternalGuildRecommendationResponse HandleInternalGuildRecommendationRequest(InternalGuildRecommendationRequest request)
        {
            c_requests.Inc();

            List<GuildDiscoveryInfoBase> recommendations = CreateRecommendations(request.Context);
            return new InternalGuildRecommendationResponse(recommendations);
        }

        [MessageHandler]
        void HandleInternalGuildRecommenderGuildUpdate(InternalGuildRecommenderGuildUpdate updateMsg)
        {
            c_updates.Inc();

            foreach (var pool in _allPools)
                pool.OnGuildUpdate(new IGuildDiscoveryPool.GuildInfo(updateMsg.PublicDiscoveryInfo, updateMsg.ServerOnlyDiscoveryInfo));
        }

        [MessageHandler]
        void HandleInternalGuildRecommenderGuildRemove(InternalGuildRecommenderGuildRemove removeMsg)
        {
            c_updates.Inc();

            foreach (var pool in _allPools)
                pool.OnGuildRemove(removeMsg.GuildId);
        }

        [EntityAskHandler]
        InternalGuildRecommenderInspectPoolsResponse HandleInternalGuildRecommenderInspectPoolsRequest(InternalGuildRecommenderInspectPoolsRequest request)
        {
            List<InternalGuildRecommenderInspectPoolsResponse.PoolInfo> poolInfos = new List<InternalGuildRecommenderInspectPoolsResponse.PoolInfo>();
            foreach (PersistedGuildDiscoveryPool pool in _allPools)
                poolInfos.Add(new InternalGuildRecommenderInspectPoolsResponse.PoolInfo(pool.PoolId, pool.Count));
            return new InternalGuildRecommenderInspectPoolsResponse(poolInfos);
        }

        [EntityAskHandler]
        InternalGuildRecommenderInspectPoolResponse HandleInternalGuildRecommenderInspectPoolsRequest(InternalGuildRecommenderInspectPoolRequest request)
        {
            foreach (PersistedGuildDiscoveryPool pool in _allPools)
            {
                if (pool.PoolId != request.Poold)
                    continue;

                List<IGuildDiscoveryPool.InspectionInfo> inputEntries = pool.Inspect(request.MaxCount);
                List<InternalGuildRecommenderInspectPoolResponse.PoolEntry> outputEntries = new List<InternalGuildRecommenderInspectPoolResponse.PoolEntry>();
                foreach (IGuildDiscoveryPool.InspectionInfo inputEntry in inputEntries)
                {
                    outputEntries.Add(new InternalGuildRecommenderInspectPoolResponse.PoolEntry(
                        inputEntry.PassesFilter,
                        inputEntry.GuildInfo.PublicDiscoveryInfo,
                        inputEntry.GuildInfo.ServerOnlyDiscoveryInfo,
                        inputEntry.LastRefreshedAt));
                }
                return new InternalGuildRecommenderInspectPoolResponse(outputEntries);
            }
            throw new InvalidEntityAsk("unknown pool");
        }

        [EntityAskHandler]
        InternalGuildRecommenderTestGuildResponse HandleInternalGuildRecommenderTestGuildRequest(InternalGuildRecommenderTestGuildRequest request)
        {
            OrderedDictionary<string, InternalGuildRecommenderTestGuildResponse.PoolStatus> pools = new OrderedDictionary<string, InternalGuildRecommenderTestGuildResponse.PoolStatus>(capacity: _allPools.Count);
            foreach (PersistedGuildDiscoveryPool pool in _allPools)
            {
                pool.TestGuild(request.PublicDiscoveryInfo, request.ServerOnlyDiscoveryInfo, out bool includedInPool, out bool poolDataPassesFilter, out bool freshDataPassesFilter);
                pools[pool.PoolId] = new InternalGuildRecommenderTestGuildResponse.PoolStatus(includedInPool, poolDataPassesFilter, freshDataPassesFilter);
            }
            return new InternalGuildRecommenderTestGuildResponse(pools);
        }

        #endregion

        protected IGuildDiscoveryPool RegisterPool(PersistedGuildDiscoveryPool pool)
        {
            if (_actorStarted)
                throw new InvalidOperationException("Cannot register new pools at runtime. Register all pools in ctor.");
            _allPools.Add(pool);
            return pool;
        }

        protected abstract List<GuildDiscoveryInfoBase> CreateRecommendations(GuildDiscoveryPlayerContextBase playerContext);
    }
}

#endif
