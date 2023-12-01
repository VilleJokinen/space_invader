// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.DatabaseScan.Priorities;
using Metaplay.Server.DatabaseScan.User;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server.MaintenanceJob.EntityRefresher
{
    [MetaSerializableDerived(201)]
    public class EntityRefresherJobSpec : MaintenanceJob.MaintenanceJobSpec
    {
        [MetaMember(1)] EntityKind _entityKind;

        public override string       JobTitle    => $"Refresher for {_entityKind}s";
        public override string       JobDescription => $"Scans through all existing {_entityKind} entities in the database, and refreshes them by waking them up as actors.";
        public override string       MetricsTag  => $"RefresherFor{_entityKind}";
        public override int          Priority    => DatabaseScanJobPriorities.EntityRefresher;
        public override EntityKind   EntityKind  => _entityKind;

        public override object JobKindDiscriminator => EntityKind;

        EntityRefresherJobSpec(){ }
        public EntityRefresherJobSpec(EntityKind entityKind)
        {
            if (!EntityConfigRegistry.Instance.TryGetPersistedConfig(entityKind, out PersistedEntityConfig _))
                throw new ArgumentException($"Invalid EntityKind {entityKind} for EntityRefresherJobSpec, no such PersistedEntityActor registered");

            _entityKind = entityKind;
        }

        public override DatabaseScanProcessor CreateProcessor(DatabaseScanProcessingStatistics initialStatisticsMaybe)
        {
            return new EntityRefresherProcessor(_entityKind, (EntityRefresherProcessingStatistics)initialStatisticsMaybe);
        }

        public override DatabaseScanProcessingStatistics ComputeAggregateStatistics(IEnumerable<DatabaseScanProcessingStatistics> parts)
        {
            return EntityRefresherProcessingStatistics.ComputeAggregate(parts.Cast<EntityRefresherProcessingStatistics>());
        }

        public override OrderedDictionary<string, object> CreateSummary(DatabaseScanProcessingStatistics statsParam)
        {
            EntityRefresherProcessingStatistics stats = (EntityRefresherProcessingStatistics)statsParam;

            OrderedDictionary<string, object> summary = new()
            {
                { "Null-payload items",     stats.NumNullPayloads },
                { "Unfinished refreshes",   stats.NumRefreshesStarted - stats.NumRefreshesFinished },
                { "Successful refreshes",   stats.NumSuccessfulRefreshes },
                { "Failed refreshes",       stats.LegacyFailedRefreshes.Count + stats.FailedRefreshes.Count },
            };

            foreach (DatabaseScanEntityError failure in stats.FailedRefreshes.Recent)
                summary[$"Failed {failure.EntityId}"] = $"At {failure.Timestamp}: {failure.Description}";

            return summary;
        }
    }

    [MetaSerializableDerived(201)]
    public class EntityRefresherProcessingStatistics : DatabaseScanProcessingStatistics
    {
        [MetaMember(1)] public int                                NumNullPayloads           = 0;
        [MetaMember(2)] public int                                NumRefreshesStarted       = 0;
        [MetaMember(3)] public int                                NumRefreshesFinished      = 0;
        [MetaMember(4)] public int                                NumSuccessfulRefreshes    = 0;
        [MetaMember(5)] public ListWithBoundedRecall<EntityId>    LegacyFailedRefreshes    = new ListWithBoundedRecall<EntityId>();
        [MetaMember(6)] public ListWithBoundedRecall<DatabaseScanEntityError> FailedRefreshes = new ListWithBoundedRecall<DatabaseScanEntityError>();

        public static EntityRefresherProcessingStatistics ComputeAggregate(IEnumerable<EntityRefresherProcessingStatistics> parts)
        {
            EntityRefresherProcessingStatistics aggregate = new EntityRefresherProcessingStatistics();

            foreach (EntityRefresherProcessingStatistics part in parts)
            {
                aggregate.NumNullPayloads += part.NumNullPayloads;
                aggregate.NumRefreshesStarted += part.NumRefreshesStarted;
                aggregate.NumRefreshesFinished += part.NumRefreshesFinished;
                aggregate.NumSuccessfulRefreshes += part.NumSuccessfulRefreshes;
                aggregate.LegacyFailedRefreshes.AddAllFrom(part.LegacyFailedRefreshes);
                aggregate.FailedRefreshes.AddAllFrom(part.FailedRefreshes);
            }

            return aggregate;
        }
    }

    [RuntimeOptions("EntityRefresher", isStatic: true, "Configuration options for the \"Entity Refresher\" maintenance scan jobs.")]
    public class EntityRefresherOptions : RuntimeOptionsBase
    {
        // \todo [nuutti] These parameters are an overly fussy way to control the speed of a job:
        //       adjusting the speed will likely involve scaling all of RefreshBufferSize,
        //       ScanBatchSize, and MaxSimultaneousRefreshes.
        //       Ideally there would be just one easy-to-use primary throughput parameter.
        //
        //       Furthermore, these are misleading because these are *per scan worker* -
        //       so for example the real "max simultaneous refreshes" will be this
        //       MaxSimultaneousRefreshes multiplied by the number of workers.
        //
        //       Same applies to EntitySchemaMigratorOptions.
        //       #maintenance-job-param-convenience

        [MetaDescription("The number of scanned entity IDs to keep buffered while waiting for in-flight refresh requests to complete.")]
        public int      RefreshBufferSize           { get; private set; } = 80;
        [MetaDescription("The number of entities to fetch from the database in one query.")]
        public int      ScanBatchSize               { get; private set; } = 40;
        [MetaDescription("When scanning for entities, the time delay between consecutive database queries.")]
        public TimeSpan ScanInterval                { get; private set; } = TimeSpan.FromSeconds(1);
        [MetaDescription("The maximum number of parallel refresh requests to entities.")]
        public int      MaxSimultaneousRefreshes    { get; private set; } = 20;
        [MetaDescription("The maximum number of retries before marking this entity refresh as failed.")]
        public int      MaxAttemptsPerRefresh       { get; private set; } = 2;
    }

    [MetaSerializableDerived(201)]
    public class EntityRefresherProcessor : DatabaseScanProcessor
    {
        static EntityRefresherOptions GetOptions() => RuntimeOptionsRegistry.Instance.GetCurrent<EntityRefresherOptions>();

        [MetaMember(1)] EntityKind                                  _entityKind;
        [MetaMember(2)] EntityRefresherProcessingStatistics         _statistics;
        [MetaMember(3)] RefreshRequestTool                          _refreshes = new RefreshRequestTool();

        [MetaSerializable]
        class RefreshRequestTool : SimultaneousRequestTool<RefreshSpec, EntityRefreshResponse>
        {
            public override int MaxSimultaneousRequests => GetOptions().MaxSimultaneousRefreshes;
            public override int MaxAttemptsPerRequest   => GetOptions().MaxAttemptsPerRefresh;
        }

        public override int         DesiredScanBatchSize        => GetOptions().ScanBatchSize;
        public override TimeSpan    ScanInterval                => GetOptions().ScanInterval;
        public override TimeSpan    TickInterval                => 0.5 * GetOptions().ScanInterval;
        public override TimeSpan    PersistInterval             => TimeSpan.FromSeconds(5);

        public override bool                                CanCurrentlyProcessMoreItems    => _refreshes.NumRequestsInBuffer < GetOptions().RefreshBufferSize;
        public override bool                                HasCompletedAllWorkSoFar        => _refreshes.HasCompletedAllRequestsSoFar;
        public override DatabaseScanProcessingStatistics    Stats                           => _statistics;

        [MetaSerializable]
        struct RefreshSpec
        {
            [MetaMember(1)] public EntityId EntityId { get; private set; }

            public RefreshSpec(EntityId entityId)
            {
                EntityId = entityId;
            }
        }

        EntityRefresherProcessor(){ }
        public EntityRefresherProcessor(EntityKind entityKind, EntityRefresherProcessingStatistics initialStatisticsMaybe)
        {
            _entityKind = entityKind;
            _statistics = initialStatisticsMaybe ?? new EntityRefresherProcessingStatistics();
        }

        public override Task StartProcessItemBatchAsync(IContext context, IEnumerable<IPersistedEntity> items)
        {
            foreach (IPersistedEntity item in items)
            {
                if (item.Payload == null)
                {
                    _statistics.NumNullPayloads++;
                    continue;
                }

                _statistics.NumRefreshesStarted++;
                _refreshes.AddRequest(new RefreshSpec(
                    EntityId.ParseFromString(item.EntityId)));
            }

            return Task.CompletedTask;
        }

        public override Task TickAsync(IContext context)
        {
            _refreshes.Update(
                attemptRequestAsync: (RefreshSpec refresh) =>
                    context.ActorEntityAskAsync<EntityRefreshResponse>(
                        refresh.EntityId,
                        new EntityRefreshRequest()),

                onRequestSuccess: (RefreshSpec refresh, EntityRefreshResponse _) =>
                    RecordFinishedRefreshStatistics(
                        isSuccess:      true,
                        entityId:       refresh.EntityId,
                        error:          default),

                onRequestFailure: (RefreshSpec refresh, RequestErrorInfo error) =>
                    RecordFinishedRefreshStatistics(
                        isSuccess:      false,
                        entityId:       refresh.EntityId,
                        error:          error)
            );

            return Task.CompletedTask;
        }

        public override void Cancel(IContext context)
        {
        }

        void RecordFinishedRefreshStatistics(bool isSuccess, EntityId entityId, RequestErrorInfo error)
        {
            _statistics.NumRefreshesFinished++;

            if (isSuccess)
                _statistics.NumSuccessfulRefreshes++;
            else
                _statistics.FailedRefreshes.Add(new DatabaseScanEntityError(entityId, error.Timestamp, error.Description));
        }
    }
}
