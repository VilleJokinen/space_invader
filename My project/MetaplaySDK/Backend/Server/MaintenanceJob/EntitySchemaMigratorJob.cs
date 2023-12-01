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
using static System.FormattableString;

namespace Metaplay.Server.MaintenanceJob.EntitySchemaMigrator
{
    [MetaSerializableDerived(200)]
    public class EntitySchemaMigratorJobSpec : MaintenanceJob.MaintenanceJobSpec
    {
        [MetaMember(1)] EntityKind _entityKind;

        public override string       JobTitle    => $"Schema Migrator for {_entityKind}s";
        public override string       JobDescription => $"Scans through all existing {_entityKind} entities in the database, and migrates them to the current schema version.";
        public override string       MetricsTag  => $"SchemaMigratorFor{_entityKind}";
        public override int          Priority    => DatabaseScanJobPriorities.EntitySchemaMigrator;
        public override EntityKind   EntityKind  => _entityKind;

        public override object JobKindDiscriminator => EntityKind;

        EntitySchemaMigratorJobSpec(){ }
        public EntitySchemaMigratorJobSpec(EntityKind entityKind)
        {
            if (!EntityConfigRegistry.Instance.TryGetPersistedConfig(entityKind, out PersistedEntityConfig _))
                throw new ArgumentException($"Invalid EntityKind {entityKind} for EntitySchemaMigratorJobSpec, no such PersistedEntityActor registered");

            _entityKind = entityKind;
        }

        public override DatabaseScanProcessor CreateProcessor(DatabaseScanProcessingStatistics initialStatisticsMaybe)
        {
            return new EntitySchemaMigratorProcessor(_entityKind, (EntitySchemaMigratorProcessingStatistics)initialStatisticsMaybe);
        }

        public override DatabaseScanProcessingStatistics ComputeAggregateStatistics(IEnumerable<DatabaseScanProcessingStatistics> parts)
        {
            return EntitySchemaMigratorProcessingStatistics.ComputeAggregate(parts.Cast<EntitySchemaMigratorProcessingStatistics>());
        }

        public override OrderedDictionary<string, object> CreateSummary(DatabaseScanProcessingStatistics statsParam)
        {
            EntitySchemaMigratorProcessingStatistics stats = (EntitySchemaMigratorProcessingStatistics)statsParam;

            OrderedDictionary<string, object> summary = new OrderedDictionary<string, object>();

            summary.Add("Null-payload items", stats.NumNullPayloads);
            summary.Add("Unfinished migrations", stats.NumMigrationsStarted - stats.NumMigrationsFinished);

            foreach ((int version, int beforeCount) in stats.SchemaVersionCountsBefore.OrderBy(kv => kv.Key))
                summary.Add(Invariant($"Version {version} count before"), beforeCount);

            foreach ((int version, int afterCount) in stats.SchemaVersionCountsAfter.OrderBy(kv => kv.Key))
                summary.Add(Invariant($"Version {version} count after"), afterCount);

            foreach ((EntitySchemaMigrationKey migrationKey, int successfulCount) in stats.SuccessfulMigrationCounts.OrderBy(kv => kv.Key))
                summary.Add(Invariant($"Successful migrations from version {migrationKey.From} to {migrationKey.To}"), successfulCount);

            IEnumerable<EntitySchemaMigrationKey> failedMigrationKeys = stats.LegacyFailedMigrations.Keys
                                                                        .Union(stats.FailedMigrations.Keys)
                                                                        .OrderBy(migrationKey => migrationKey);

            foreach (EntitySchemaMigrationKey migrationKey in failedMigrationKeys)
            {
                int failureCount = (stats.LegacyFailedMigrations.GetValueOrDefault(migrationKey, null)?.Count ?? 0)
                                   + (stats.FailedMigrations.GetValueOrDefault(migrationKey, null)?.Count ?? 0);
                summary.Add(Invariant($"Failed migrations from version {migrationKey.From} to {migrationKey.To}"), failureCount);
            }

            foreach ((EntitySchemaMigrationKey migrationKey, ListWithBoundedRecall<DatabaseScanEntityError> failed) in stats.FailedMigrations.OrderBy(kv => kv.Key))
            {
                foreach (DatabaseScanEntityError failure in failed.Recent)
                    summary[Invariant($"Failed {failure.EntityId} (version {migrationKey.From} to {migrationKey.To})")] = Invariant($"At {failure.Timestamp}: {failure.Description}");
            }

            return summary;
        }
    }

    [MetaSerializable]
    public struct EntitySchemaMigrationKey : IEquatable<EntitySchemaMigrationKey>, IComparable<EntitySchemaMigrationKey>
    {
        [MetaMember(1)] public int From;
        [MetaMember(2)] public int To;

        public EntitySchemaMigrationKey(int from, int to)
        {
            From = from;
            To = to;
        }

        public bool             Equals(EntitySchemaMigrationKey other)      => From == other.From && To == other.To;
        public override bool    Equals(object obj)                          => obj is EntitySchemaMigrationKey other && Equals(other);
        public override int     GetHashCode()                               => Util.CombineHashCode(From.GetHashCode(), To.GetHashCode());

        public int CompareTo(EntitySchemaMigrationKey other) => GetComparisonKey().CompareTo(other.GetComparisonKey());
        (int, int) GetComparisonKey() => (From, To);
    }

    [MetaSerializableDerived(200)]
    public class EntitySchemaMigratorProcessingStatistics : DatabaseScanProcessingStatistics
    {
        [MetaMember(1)] public int                                                                      NumNullPayloads             = 0;
        [MetaMember(6)] public int                                                                      NumMigrationsStarted        = 0;
        [MetaMember(7)] public int                                                                      NumMigrationsFinished       = 0;
        [MetaMember(2)] public Dictionary<int, int>                                                     SchemaVersionCountsBefore   = new Dictionary<int, int>();
        [MetaMember(3)] public Dictionary<EntitySchemaMigrationKey, int>                                SuccessfulMigrationCounts   = new Dictionary<EntitySchemaMigrationKey, int>();
        [MetaMember(4)] public Dictionary<EntitySchemaMigrationKey, ListWithBoundedRecall<EntityId>>    LegacyFailedMigrations      = new Dictionary<EntitySchemaMigrationKey, ListWithBoundedRecall<EntityId>>();
        [MetaMember(8)] public Dictionary<EntitySchemaMigrationKey, ListWithBoundedRecall<DatabaseScanEntityError>> FailedMigrations = new Dictionary<EntitySchemaMigrationKey, ListWithBoundedRecall<DatabaseScanEntityError>>();
        [MetaMember(5)] public Dictionary<int, int>                                                     SchemaVersionCountsAfter    = new Dictionary<int, int>();

        public static EntitySchemaMigratorProcessingStatistics ComputeAggregate(IEnumerable<EntitySchemaMigratorProcessingStatistics> parts)
        {
            EntitySchemaMigratorProcessingStatistics aggregate = new EntitySchemaMigratorProcessingStatistics();

            foreach (EntitySchemaMigratorProcessingStatistics part in parts)
            {
                aggregate.NumNullPayloads += part.NumNullPayloads;
                aggregate.NumMigrationsStarted += part.NumMigrationsStarted;
                aggregate.NumMigrationsFinished += part.NumMigrationsFinished;
                StatsUtil.AggregateCounters(aggregate.SchemaVersionCountsBefore, part.SchemaVersionCountsBefore);
                StatsUtil.AggregateCounters(aggregate.SuccessfulMigrationCounts, part.SuccessfulMigrationCounts);
                StatsUtil.AggregateLists(aggregate.LegacyFailedMigrations, part.LegacyFailedMigrations);
                StatsUtil.AggregateLists(aggregate.FailedMigrations, part.FailedMigrations);
                StatsUtil.AggregateCounters(aggregate.SchemaVersionCountsAfter, part.SchemaVersionCountsAfter);
            }

            return aggregate;
        }
    }

    [RuntimeOptions("EntitySchemaMigrator", isStatic: true, "Configuration options for the \"Entity Schema Migrator\" maintenance scan jobs.")]
    public class EntitySchemaMigratorOptions : RuntimeOptionsBase
    {
        // \todo [nuutti] These parameters are an overly fussy way to control the speed of a job:
        //       adjusting the speed will likely involve scaling all of MigrationBufferSize,
        //       ScanBatchSize, and MaxSimultaneousMigrations.
        //       Ideally there would be just one easy-to-use primary throughput parameter.
        //
        //       Furthermore, these are misleading because these are *per scan worker* -
        //       so for example the real "max simultaneous migrations" will be this
        //       MaxSimultaneousMigrations multiplied by the number of workers.
        //
        //       Same applies to EntityRefresherOptions.
        //       #maintenance-job-param-convenience

        [MetaDescription("The number of scanned entity IDs to keep buffered while waiting for in-flight refresh requests to complete.")]
        public int      MigrationBufferSize         { get; private set; } = 80;
        [MetaDescription("The number of entities to fetch from the database in one query.")]
        public int      ScanBatchSize               { get; private set; } = 40;
        [MetaDescription("When scanning for entities, the time delay between consecutive database queries.")]
        public TimeSpan ScanInterval                { get; private set; } = TimeSpan.FromSeconds(1);
        [MetaDescription("The maximum number of parallel refresh requests to entities.")]
        public int      MaxSimultaneousMigrations   { get; private set; } = 20;
        [MetaDescription("The maximum number of retries before marking this entity migration as failed.")]
        public int      MaxAttemptsPerMigration     { get; private set; } = 2;
    }

    [MetaSerializableDerived(200)]
    public class EntitySchemaMigratorProcessor : DatabaseScanProcessor
    {
        static EntitySchemaMigratorOptions GetOptions() => RuntimeOptionsRegistry.Instance.GetCurrent<EntitySchemaMigratorOptions>();

        [MetaMember(1)] EntityKind                                  _entityKind;
        [MetaMember(2)] EntitySchemaMigratorProcessingStatistics    _statistics;
        [MetaMember(3)] MigrationRequestTool                        _migrations = new MigrationRequestTool();

        [MetaSerializable]
        class MigrationRequestTool : SimultaneousRequestTool<MigrationSpec, EntityEnsureOnLatestSchemaVersionResponse>
        {
            public override int MaxSimultaneousRequests => GetOptions().MaxSimultaneousMigrations;
            public override int MaxAttemptsPerRequest   => GetOptions().MaxAttemptsPerMigration;
        }

        public override int         DesiredScanBatchSize        => GetOptions().ScanBatchSize;
        public override TimeSpan    ScanInterval                => GetOptions().ScanInterval;
        public override TimeSpan    TickInterval                => 0.5 * GetOptions().ScanInterval;
        public override TimeSpan    PersistInterval             => TimeSpan.FromSeconds(5);

        public override bool                                CanCurrentlyProcessMoreItems    => _migrations.NumRequestsInBuffer < GetOptions().MigrationBufferSize;
        public override bool                                HasCompletedAllWorkSoFar        => _migrations.HasCompletedAllRequestsSoFar;
        public override DatabaseScanProcessingStatistics    Stats                           => _statistics;

        [MetaSerializable]
        struct MigrationSpec
        {
            [MetaMember(1)] public EntityId EntityId            { get; private set; }
            [MetaMember(2)] public int      FromSchemaVersion   { get; private set; }

            public MigrationSpec(EntityId entityId, int fromSchemaVersion)
            {
                EntityId = entityId;
                FromSchemaVersion = fromSchemaVersion;
            }
        }

        EntitySchemaMigratorProcessor(){ }
        public EntitySchemaMigratorProcessor(EntityKind entityKind, EntitySchemaMigratorProcessingStatistics initialStatisticsMaybe)
        {
            _entityKind = entityKind;
            _statistics = initialStatisticsMaybe ?? new EntitySchemaMigratorProcessingStatistics();
        }

        public override Task StartProcessItemBatchAsync(IContext context, IEnumerable<IPersistedEntity> items)
        {
            int targetSchemaVersion = GetTargetSchemaVersion();

            foreach (IPersistedEntity item in items)
            {
                if (item.Payload == null)
                {
                    _statistics.NumNullPayloads++;
                    continue;
                }

                StatsUtil.IncrementCounter(_statistics.SchemaVersionCountsBefore, item.SchemaVersion);

                if (item.SchemaVersion >= targetSchemaVersion)
                {
                    StatsUtil.IncrementCounter(_statistics.SchemaVersionCountsAfter, item.SchemaVersion);
                    continue;
                }

                _statistics.NumMigrationsStarted++;
                _migrations.AddRequest(new MigrationSpec(
                    EntityId.ParseFromString(item.EntityId),
                    fromSchemaVersion: item.SchemaVersion));
            }

            return Task.CompletedTask;
        }

        public override Task TickAsync(IContext context)
        {
            int targetSchemaVersion = GetTargetSchemaVersion();

            _migrations.Update(
                attemptRequestAsync: (MigrationSpec migration) =>
                    context.ActorEntityAskAsync<EntityEnsureOnLatestSchemaVersionResponse>(
                        migration.EntityId,
                        new EntityEnsureOnLatestSchemaVersionRequest()),

                onRequestSuccess: (MigrationSpec migration, EntityEnsureOnLatestSchemaVersionResponse response) =>
                    RecordFinishedMigrationStatistics(
                        isSuccess:      true,
                        entityId:       migration.EntityId,
                        fromVersion:    migration.FromSchemaVersion,
                        toVersion:      response.CurrentSchemaVersion,
                        error:          default),

                onRequestFailure: (MigrationSpec migration, RequestErrorInfo error) =>
                    RecordFinishedMigrationStatistics(
                        isSuccess:      false,
                        entityId:       migration.EntityId,
                        fromVersion:    migration.FromSchemaVersion,
                        toVersion:      targetSchemaVersion,
                        error:          error)
            );

            return Task.CompletedTask;
        }

        public override void Cancel(IContext context)
        {
        }

        void RecordFinishedMigrationStatistics(bool isSuccess, EntityId entityId, int fromVersion, int toVersion, RequestErrorInfo error)
        {
            _statistics.NumMigrationsFinished++;

            EntitySchemaMigrationKey migrationKey = new EntitySchemaMigrationKey(fromVersion, toVersion);

            if (isSuccess)
                StatsUtil.IncrementCounter(_statistics.SuccessfulMigrationCounts, migrationKey);
            else
                StatsUtil.AddToList(_statistics.FailedMigrations, migrationKey, new DatabaseScanEntityError(entityId, error.Timestamp, error.Description));

            int afterSchemaVersion = isSuccess ? toVersion : fromVersion;
            StatsUtil.IncrementCounter(_statistics.SchemaVersionCountsAfter, afterSchemaVersion);
        }

        int GetTargetSchemaVersion()
        {
            return EntityConfigRegistry.Instance.GetPersistedConfig(_entityKind).CurrentSchemaVersion;
        }
    }
}
