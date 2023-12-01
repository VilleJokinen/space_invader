// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    [EntityConfig]
    internal sealed class StatsCollectorProxyConfig : EphemeralEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.StatsCollectorProxy;
        public override Type                EntityActorType         => typeof(StatsCollectorProxy);
        public override IShardingStrategy   ShardingStrategy        => new ServiceShardingStrategy(EntityKindCloudCore.StatsCollectorProxy);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Actor for proxying the collection (for <see cref="StatsCollectorManager"/>) on all shards in the server cluster.
    /// </summary>
    public class StatsCollectorProxy : EphemeralEntityActor
    {
        protected override sealed AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        class StatisticsTick { public static readonly StatisticsTick Instance = new StatisticsTick(); }

        private static ConcurrentDictionary<MetaActivableKey, MetaActivableStatistics>              ActivableStatistics     = new ConcurrentDictionary<MetaActivableKey, MetaActivableStatistics>();
        // \todo [nuutti] For MetaOffers, we'd like ActivableStatistics, OfferStatistics, and OfferPerGroupStatistics to stay in sync.
        //                Currently this is not guaranteed, as they're updated separately. Significant divergence is not likely though.
        //                #meta-offers
        private static ConcurrentDictionary<MetaOfferId, MetaOfferStatistics>                       OfferStatistics         = new ConcurrentDictionary<MetaOfferId, MetaOfferStatistics>();
        private static ConcurrentDictionary<(MetaOfferGroupId, MetaOfferId), MetaOfferStatistics>   OfferPerGroupStatistics = new ConcurrentDictionary<(MetaOfferGroupId, MetaOfferId), MetaOfferStatistics>();

        public static Dictionary<EntityKind, List<IActiveEntityInfo>>                               ActiveEntities          = new Dictionary<EntityKind, List<IActiveEntityInfo>>(); // Time ordered list of recently active entities

        public static RefreshKeyCounter<EntityId>                                                   NumConcurrentsCounter   = new RefreshKeyCounter<EntityId>(refreshInterval: MetaDuration.FromMinutes(1));

        List<PlayerExperimentAssignmentChange.Sample>                                               _emptyExperimentAssignmentChangeBuffer  = new List<PlayerExperimentAssignmentChange.Sample>();
        private static ConcurrentDictionary<PlayerExperimentId, PlayerExperimentAssignmentChange>   PlayerExperimentAssignmentChanges       = new ConcurrentDictionary<PlayerExperimentId, PlayerExperimentAssignmentChange>();
        private static DateTime                                                                     NextPlayerExperimentBufferFullWarningAt = DateTime.MinValue;

        string _selfShardName;

        public StatsCollectorProxy(EntityId entityId) : base(entityId)
        {
            ClusteringOptions clusterOpts = RuntimeOptionsRegistry.Instance.GetCurrent<ClusteringOptions>();
            _selfShardName = clusterOpts.SelfAddress.ToString();
        }

        protected override void PreStart()
        {
            base.PreStart();

            // Subscribe to local update events
            Context.System.EventStream.Subscribe(_self, typeof(UpdateShardLiveEntityCount));
            Context.System.EventStream.Subscribe(_self, typeof(ActiveEntityInfoEnvelope));
        }

        protected override void PostStop()
        {
            // Unubscribe
            Context.System.EventStream.Unsubscribe(_self, typeof(UpdateShardLiveEntityCount));
            Context.System.EventStream.Unsubscribe(_self, typeof(ActiveEntityInfoEnvelope));

            base.PostStop();
        }

        protected override Task Initialize()
        {
            StartPeriodicTimer(TimeSpan.FromSeconds(10), StatisticsTick.Instance);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Periodically send collected statistics to <see cref="StatsCollectorManager"/>.
        /// </summary>
        [CommandHandler]
        void HandleStatisticsTick(StatisticsTick _)
        {
            SendActivableStatisticsInfo();
            SendActiveEntitiesInfo();
            SendConcurrentsUpdate();
            SendPlayerExperimentAssignmentSamples();
        }

        void SendActivableStatisticsInfo()
        {
            FullMetaActivableStatistics statisticsToSend = new FullMetaActivableStatistics();
            MetaOffersStatistics offersStatistics = new MetaOffersStatistics();

            // Collect the pending stats from ActivableStatistics into a FullMetaActivableStatistics,
            // removing from ActivableStatistics as we go.
            foreach ((MetaActivableKey activableKey, MetaActivableStatistics _) in ActivableStatistics)
            {
                if (ActivableStatistics.TryRemove(activableKey, out MetaActivableStatistics activableStatistics))
                {
                    MetaActivableKindStatistics kindStatistics      = StatsUtil.GetOrAddDefaultConstructed(statisticsToSend.KindStatistics, activableKey.KindId);
                    string                      activableIdString   = ((IStringId)activableKey.ActivableId).Value; // \note #activable-id-type
                    StatsUtil.AccumulateValue(kindStatistics.ActivableStatistics, activableIdString, activableStatistics, MetaActivableStatistics.Sum);
                }
            }

            // Same for offers
            foreach ((MetaOfferId offerId, MetaOfferStatistics _) in OfferStatistics)
            {
                if (OfferStatistics.TryRemove(offerId, out MetaOfferStatistics offerStatistics))
                    StatsUtil.AccumulateValue(offersStatistics.Offers, offerId, offerStatistics, MetaOfferStatistics.Sum);
            }

            // Same for offers' per-group stats
            foreach (((MetaOfferGroupId groupId, MetaOfferId offerId), MetaOfferStatistics _) in OfferPerGroupStatistics)
            {
                if (OfferPerGroupStatistics.TryRemove((groupId, offerId), out MetaOfferStatistics offerStatistics))
                {
                    MetaOfferGroupStatistics offerGroupStatistics = StatsUtil.GetOrAddDefaultConstructed(offersStatistics.OfferGroups, groupId);
                    StatsUtil.AccumulateValue(offerGroupStatistics.OfferPerGroupStatistics, offerId, offerStatistics, MetaOfferStatistics.Sum);
                }
            }

            // Send to StatsCollectorManager.
            // Only send if there have been any stats since the last tick.
            if (statisticsToSend.KindStatistics.Count > 0
             || offersStatistics.HasAny)
            {
                CastMessage(StatsCollectorManager.EntityId, new MetaActivableStatisticsInfo(statisticsToSend, offersStatistics));
            }
        }

        public static void IncreaseActivableStatistics(MetaActivableKey activableKey, MetaActivableStatistics statisticsDelta)
        {
            ActivableStatistics.AddOrUpdate(activableKey,
                addValue: statisticsDelta,
                updateValueFactory: (_, existingStatistics) => MetaActivableStatistics.Sum(existingStatistics, statisticsDelta));
        }

        public static void IncreaseMetaOffersStatistics(MetaOfferGroupId groupId, MetaOfferId offerId, MetaOfferStatistics offerStatisticsDelta, MetaOfferStatistics offerPerGroupStatisticsDelta)
        {
            OfferStatistics.AddOrUpdate(offerId,
                addValue: offerStatisticsDelta,
                updateValueFactory: (_, existingStatistics) => MetaOfferStatistics.Sum(existingStatistics, offerStatisticsDelta));

            OfferPerGroupStatistics.AddOrUpdate((groupId, offerId),
                addValue: offerPerGroupStatisticsDelta,
                updateValueFactory: (_, existingStatistics) => MetaOfferStatistics.Sum(existingStatistics, offerPerGroupStatisticsDelta));
        }

        [CommandHandler]
        public void HandleUpdateShardEntityCount(UpdateShardLiveEntityCount updateStats)
        {
            // Forward stats updates to StatsCollectorManager
            CastMessage(StatsCollectorManager.EntityId, updateStats);
        }

        /// <summary>
        /// When an entity does some activity, add them to a time-ordered list of
        /// the most recently active entities
        /// </summary>
        /// <param name="activeEntityInfo"></param>
        [CommandHandler]
        public void HandleActiveEntityInfoEnvelope(ActiveEntityInfoEnvelope activeEntityInfoEnvelope)
        {
            // Remove info from the envlope
            IActiveEntityInfo activeEntityInfo = activeEntityInfoEnvelope.Value;

            // There will be one list for each entity kind
            EntityKind entityKind = activeEntityInfo.EntityId.Kind;
            if (!ActiveEntities.ContainsKey(entityKind))
                ActiveEntities.Add(entityKind, new List<IActiveEntityInfo>());
            List<IActiveEntityInfo> activeEntitiesTyped = ActiveEntities[entityKind];

            // [todo] paul - This (especially the remove) probably doesn't scale
            // very well to large numbers of entities.

            // Remove entry for this player if it's already in the list
            activeEntitiesTyped.RemoveAll(item => item.EntityId == activeEntityInfo.EntityId);

            // Add player to top of list as the most recent active player
            activeEntitiesTyped.Insert(0, activeEntityInfo);

            // Trim list to size
            if (activeEntitiesTyped.Count > StatsCollectorManager.MaxActiveEntities)
                activeEntitiesTyped.RemoveRange(StatsCollectorManager.MaxActiveEntities, activeEntitiesTyped.Count - StatsCollectorManager.MaxActiveEntities);
        }

        /// <summary>
        /// Send the current ActiveEntities lists to the StatsCollectorManager.
        /// </summary>
        void SendActiveEntitiesInfo()
        {
            CastMessage(StatsCollectorManager.EntityId, new UpdateShardActiveEntityList(_selfShardName, ActiveEntities));
        }

        void SendConcurrentsUpdate()
        {
            MetaTime sampledAt = MetaTime.Now;
            int numConcurrents = NumConcurrentsCounter.GetNumAlive();
            CastMessage(StatsCollectorManager.EntityId, new StatsCollectorNumConcurrentsUpdate(_selfShardName, numConcurrents, sampledAt));
        }

        #region Player Experiment Samples

        /// <summary>
        /// Tracks samples of a variant assignment in a single experiment.
        /// </summary>
        class PlayerExperimentAssignmentChange
        {
            public readonly struct Sample
            {
                public readonly EntityId            PlayerId;
                public readonly ExperimentVariantId AddedIntoGroupId;
                public readonly bool                WasRemovedFromGroup;
                public readonly ExperimentVariantId RemovedFromGroupId;

                public Sample(EntityId playerId, ExperimentVariantId addedIntoGroupId, bool wasRemovedFromGroup, ExperimentVariantId removedFromGroupId)
                {
                    PlayerId = playerId;
                    AddedIntoGroupId = addedIntoGroupId;
                    WasRemovedFromGroup = wasRemovedFromGroup;
                    RemovedFromGroupId = removedFromGroupId;
                }
            }

            public object                                       Lock            = new object();
            public List<Sample>                                 Samples         = new List<Sample>();
            public const int                                    MaxNumSamples   = 100;
        }

        void SendPlayerExperimentAssignmentSamples()
        {
            // Consume samples from buffer
            OrderedDictionary<PlayerExperimentId, OrderedDictionary<EntityId, StatsCollectorPlayerExperimentAssignmentSampleUpdate.Sample>> resultSamples = null;
            foreach ((PlayerExperimentId experimentId, PlayerExperimentAssignmentChange value) in PlayerExperimentAssignmentChanges)
            {
                // Keep locked area short by swapping the sample list as a whole. The processing logic
                // is pretty simple, but let's keep it out of the critical section anyway.
                List<PlayerExperimentAssignmentChange.Sample> samples;
                lock(value.Lock)
                {
                    if (value.Samples.Count == 0)
                        continue;

                    samples = value.Samples;
                    value.Samples = _emptyExperimentAssignmentChangeBuffer;
                }

                resultSamples ??= new ();
                if (!resultSamples.ContainsKey(experimentId))
                    resultSamples.Add(experimentId, new OrderedDictionary<EntityId, StatsCollectorPlayerExperimentAssignmentSampleUpdate.Sample>());

                OrderedDictionary<EntityId, StatsCollectorPlayerExperimentAssignmentSampleUpdate.Sample> playerGroupChanges = resultSamples[experimentId];
                foreach (PlayerExperimentAssignmentChange.Sample changeSample in samples)
                {
                    // If we have multiple samples of the same player for the same experiment, use the earliest for the "from" data
                    bool hasFromVariant;
                    ExperimentVariantId fromVariantId;
                    if (playerGroupChanges.TryGetValue(changeSample.PlayerId, out StatsCollectorPlayerExperimentAssignmentSampleUpdate.Sample updateSample))
                    {
                        hasFromVariant = updateSample.HasFromVariant;
                        fromVariantId = updateSample.FromVariant;
                    }
                    else
                    {
                        hasFromVariant = changeSample.WasRemovedFromGroup;
                        fromVariantId = changeSample.RemovedFromGroupId;
                    }

                    playerGroupChanges[changeSample.PlayerId] = new StatsCollectorPlayerExperimentAssignmentSampleUpdate.Sample(
                        intoVariant: changeSample.AddedIntoGroupId,
                        fromVariant: fromVariantId,
                        hasFromVariant: hasFromVariant);
                }

                samples.Clear();
                _emptyExperimentAssignmentChangeBuffer = samples;
            }

            NextPlayerExperimentBufferFullWarningAt = DateTime.MinValue;

            if (resultSamples != null)
                CastMessage(StatsCollectorManager.EntityId, new StatsCollectorPlayerExperimentAssignmentSampleUpdate(resultSamples));
        }

        /// <summary>
        /// Adds assignment into sample assignment set.
        /// </summary>
        public static void PlayerAssignmentIntoExperimentChanged(EntityId playerId, PlayerExperimentId experimentId, ExperimentVariantId addedIntoGroupId, bool wasRemovedFromGroup, ExperimentVariantId removedFromGroupId)
        {
            // Track change sample. This may lose some data.
            // \todo: better sample logic. This just keeps N latest changes. Could keep N per Experiment&Variant, and make N smaller #abtesting

            PlayerExperimentAssignmentChange assignmentChange;
            if (!PlayerExperimentAssignmentChanges.TryGetValue(experimentId, out assignmentChange))
            {
                _ = PlayerExperimentAssignmentChanges.TryAdd(experimentId, new PlayerExperimentAssignmentChange());
                assignmentChange = PlayerExperimentAssignmentChanges[experimentId];
            }

            PlayerExperimentAssignmentChange.Sample sample = new PlayerExperimentAssignmentChange.Sample(
                playerId:               playerId,
                addedIntoGroupId:       addedIntoGroupId,
                wasRemovedFromGroup:    wasRemovedFromGroup,
                removedFromGroupId:     removedFromGroupId);

            bool sampleWasAdded;
            lock(assignmentChange.Lock)
            {
                if (assignmentChange.Samples.Count < PlayerExperimentAssignmentChange.MaxNumSamples)
                {
                    assignmentChange.Samples.Add(sample);
                    sampleWasAdded = true;
                }
                else
                {
                    sampleWasAdded = false;
                }
            }

            if (!sampleWasAdded && DateTime.UtcNow >= NextPlayerExperimentBufferFullWarningAt)
            {
                // Rate limiting to avoid getting spammy.
                // \note: Checking is still racy so we might get multiple log lines.
                NextPlayerExperimentBufferFullWarningAt = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                Serilog.Log.Warning("Too many pending Experiment change notifications pending. Dropping. Experiment visualisations in Dashboard may get out-of-date.");
            }
        }

        #endregion
    }
}
