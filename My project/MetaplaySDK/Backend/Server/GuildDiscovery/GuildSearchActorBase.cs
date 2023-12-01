// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Guild;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Server.Database;
using Metaplay.Server.Guild.InternalMessages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;

namespace Metaplay.Server.GuildDiscovery
{
    public abstract class GuildSearchConfigBase : EphemeralEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.GuildSearch;
        public override IShardingStrategy   ShardingStrategy        => new ServiceShardingStrategy(EntityKindCloudCore.GuildSearch);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// The component responsible for the guild search. There is a single GuildSearch on each server node, and
    /// that GuildSearch is responsible for executing the search queries on that particular node.
    /// </summary>
    // \todo: make a static class. Needs "AnonShard"/"AdHocShard" EntityShard for making the EntityAsks (or metadata queries)
    //        to the coarsely matched guilds.
    public abstract class GuildSearchActorBase : EphemeralEntityActor
    {
        class ExecuteNextSearch { static public readonly ExecuteNextSearch Instance = new ExecuteNextSearch(); }

        static readonly Prometheus.HistogramConfiguration c_searchDurationConfig = new Prometheus.HistogramConfiguration
        {
            Buckets     = new double[] { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0, 5.0, 10.0 },
        };

        static readonly Prometheus.Counter      c_searchRequests            = Prometheus.Metrics.CreateCounter("game_guildsearch_requests_total", "Number of guild search requests");
        static readonly Prometheus.Counter      c_searchErrors              = Prometheus.Metrics.CreateCounter("game_guildsearch_errors_total", "Number of guild searches that ended with error");
        static readonly Prometheus.Histogram    c_searchDuration            = Prometheus.Metrics.CreateHistogram("game_guildsearch_search_duration", "Duration of successful guild searches, including queue time", c_searchDurationConfig);

        protected override sealed AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        static readonly int NumSearchResults = 20;
        static readonly int MaxGuildSearchQueueLength = 10;
        static readonly int MaxConcurrentGuildSearches = 2;

        public static EntityId EntityIdOnCurrentNode = EntityId.None;

        class WorkRequest
        {
            public GuildSearchParamsBase            SearchParams;
            public GuildDiscoveryPlayerContextBase  SearchContext;
            public EntityAsk                        Ask;
            public Stopwatch                        Stopwatch;

            public WorkRequest(GuildSearchParamsBase searchParams, GuildDiscoveryPlayerContextBase searchContext, EntityAsk ask, Stopwatch stopwatch)
            {
                SearchParams = searchParams;
                SearchContext = searchContext;
                Ask = ask;
                Stopwatch = stopwatch;
            }
        }
        Queue<WorkRequest> _guildSearchQueue = new Queue<WorkRequest>();
        int _numConcurrentSearches = 0;

        public GuildSearchActorBase(EntityId entityId) : base(entityId)
        {
        }

        protected override Task Initialize()
        {
            EntityIdOnCurrentNode = _entityId;
            return Task.CompletedTask;
        }

        protected override Task OnShutdown()
        {
            EntityIdOnCurrentNode = EntityId.None;
            return Task.CompletedTask;
        }

        [EntityAskHandler]
        void HandleInternalGuildSearchRequest(EntityAsk ask, InternalGuildSearchRequest request)
        {
            c_searchRequests.Inc();

            if (_guildSearchQueue.Count > MaxGuildSearchQueueLength)
            {
                // queue is too long already, reject immediately
                _log.Warning("Guild search rejected, work queue is too long.");
                ReplyToAsk(ask, new InternalGuildSearchResponse(isError: true, guildInfos: null));
                c_searchErrors.Inc();
                return;
            }

            // Add to the queue
            var workRequest = new WorkRequest(
                searchParams:   request.SearchParams,
                searchContext:  request.SearchContext,
                ask:            ask,
                stopwatch:      Stopwatch.StartNew());
            _guildSearchQueue.Enqueue(workRequest);

            // Wake up executor when needed
            TryEnqueueNextSearch();
        }

        void TryEnqueueNextSearch()
        {
            // nothing to do?
            if (_guildSearchQueue.Count == 0)
                return;

            // already at max capacity?
            if (_numConcurrentSearches >= MaxConcurrentGuildSearches)
                return;

            ++_numConcurrentSearches;
            _self.Tell(ExecuteNextSearch.Instance, sender: null);
        }

        [CommandHandler]
        void HandleExecuteNextSearch(ExecuteNextSearch executeNextSearch)
        {
            if (!_guildSearchQueue.TryDequeue(out var workItem))
                return;

            _ = Task.Run(async () =>
            {
                List<GuildDiscoveryInfoBase> guildInfos;
                try
                {
                    guildInfos = await TrySearchAsync(workItem);
                }
                catch (Exception ex)
                {
                    _log.Warning("Guild search failed with error: {Cause}", ex);
                    c_searchErrors.Inc();

                    // \note: ReplyToAsk is not thread-safe. Execute on actor thread.
                    await ExecuteOnActorContextAsync(() =>
                    {
                        ReplyToAsk(workItem.Ask, new InternalGuildSearchResponse(isError: true, guildInfos: null));
                    });
                    return;
                }

                // \note: ReplyToAsk is not thread-safe. Execute on actor thread.
                await ExecuteOnActorContextAsync(() =>
                {
                    ReplyToAsk(workItem.Ask, new InternalGuildSearchResponse(isError: false, guildInfos));
                });
                c_searchDuration.Observe(workItem.Stopwatch.Elapsed.TotalSeconds);
            })
            .ContinueWith(_ =>
            {
                // search completed, enqueue next
                --_numConcurrentSearches;
                TryEnqueueNextSearch();
            }, TaskScheduler.Current);
        }

        async Task<List<GuildDiscoveryInfoBase>> TrySearchAsync(WorkRequest workItem)
        {
            MetaDatabase db = MetaDatabase.Get(QueryPriority.Low);
            List<EntityId> guildIds = await db.SearchGuildIdsByNameAsync(NumSearchResults, workItem.SearchParams.SearchString);

            // query Discovery infos from guilds
            // \todo: Keep discovery data in metadata registry and AskMany from there?

            List<(EntityId, Task<InternalGuildDiscoveryGuildDataResponse>)> discoveryResponses = new List<(EntityId, Task<InternalGuildDiscoveryGuildDataResponse>)>();
            foreach (EntityId guildId in guildIds)
            {
                // \note: EntityAskAsync is not thread-safe. Execute on actor thread.
                Task<InternalGuildDiscoveryGuildDataResponse> asyncResponse = ExecuteOnActorContextAsync(() => EntityAskAsync<InternalGuildDiscoveryGuildDataResponse>(guildId, InternalGuildDiscoveryGuildDataRequest.Instance));
                discoveryResponses.Add((guildId, asyncResponse));
            }

            // filter the guilds and map to DiscoveryInfos

            List<GuildDiscoveryInfoBase> guildInfos = new List<GuildDiscoveryInfoBase>();
            foreach ((var guildId, var discoveryResponse) in discoveryResponses)
            {
                try
                {
                    InternalGuildDiscoveryGuildDataResponse discoveryResult = await discoveryResponse;
                    if (discoveryResult.IsSuccess())
                    {
                        // Filter with game-specific fields
                        if (FilterSearchResult(discoveryResult.PublicDiscoveryInfo, discoveryResult.ServerOnlyDiscoveryInfo, workItem.SearchParams, workItem.SearchContext))
                            guildInfos.Add(discoveryResult.PublicDiscoveryInfo);
                    }
                    else
                    {
                        _log.Warning("Got refusal while inspecting {GuildId} for search.", guildId);

                        // \todo: If guild is permanently closed, we could keep a negative-cache to avoid quering it next time.
                        //        This should be pretty rare, assuming guild names (and search lookups) get robustly cleaned up,
                        //        but even a simple in-memory negative set should limit the potential sillyness here.
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning("Got error while inspecting {GuildId} for search: {Cause}", guildId, ex);
                }
            }

            return guildInfos;
        }

        /// <summary>
        /// Provides the final filtering for guild search. This function is fed with coarsely filtered guilds, and returns
        /// true if the guild descibed with <paramref name="publicDiscoveryInfo"/> and <paramref name="serverOnlyDiscoveryInfo"/>
        /// matches the <paramref name="searchParams"/>.
        /// </summary>
        protected abstract bool FilterSearchResult(GuildDiscoveryInfoBase publicDiscoveryInfo, GuildDiscoveryServerOnlyInfoBase serverOnlyDiscoveryInfo, GuildSearchParamsBase searchParams, GuildDiscoveryPlayerContextBase searchContext);
    }
}

#endif
