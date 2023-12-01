// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.DatabaseScan.User;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Cloud.Sharding.EntityShard;
using static System.FormattableString;

namespace Metaplay.Server.DatabaseScan.TestJob
{
    // Here is defined a kind of database scan job for local testing/debugging purposes.
    // Set TestJobManager.IsEnabled temporarily to true to enable this. Don't commit it though!
    //
    // When enabled, the manager provides new test jobs randomly and frequently.
    // The jobs scan player ids with Value under 500, so you'll probably want to populate with bots.

    [MetaSerializable]
    public class TestJobManager : DatabaseScanJobManager
    {
        static readonly bool IsEnabled = false; // Don't commit as enabled!

        public override bool AllowMultipleSimultaneousJobs => true;

        [MetaMember(1)] int _runningId = 0;

        public override Task InitializeAsync(IContext context)
        {
            return Task.CompletedTask;
        }

        public override (DatabaseScanJobSpec jobSpec, bool canStart) TryGetNextDueJob(IContext context, MetaTime currentTime)
        {
            if (!IsEnabled)
                return (null, false);

            Random rnd = new Random();

            if (rnd.Next(2) == 0)
                return (null, false);

            return (new TestJobSpec(
                id:         _runningId++,
                priority:   rnd.Next(10),
                shouldFail: rnd.Next(10) == 0), true);
        }

        public override Task OnJobDidNotStartAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime)
        {
            return Task.CompletedTask;
        }

        public override IEnumerable<UpcomingDatabaseScanJob> GetUpcomingJobs(MetaTime currentTime)
        {
            // \todo Implement this (will need to tweak TryGetStartableJob for this to make sense)
            return Array.Empty<UpcomingDatabaseScanJob>();
        }

        public override Task OnJobStartedAsync(IContext context, DatabaseScanJobSpec jobSpec, DatabaseScanJobId jobdId, MetaTime currentTime)
        {
            return Task.CompletedTask;
        }

        public override Task OnJobCancellationBeganAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime)
        {
            return Task.CompletedTask;
        }

        public override Task OnJobStoppedAsync(IContext context, DatabaseScanJobSpec jobSpec, MetaTime currentTime, bool wasCancelled, IEnumerable<DatabaseScanWorkStatus> workerStatusMaybes)
        {
            return Task.CompletedTask;
        }

        public override bool TryGetEntityAskHandler(IContext context, EntityAsk ask, MetaMessage message, out Task handle)
        {
            handle = default;
            return false;
        }

        public override bool TryGetMessageHandler(IContext context, MetaMessage message, out Task handle)
        {
            handle = default;
            return false;
        }
    }

    [MetaSerializableDerived(100)]
    public class TestJobSpec : DatabaseScanJobSpec
    {
        [MetaMember(1)] int     _id;
        [MetaMember(2)] int     _priority;
        [MetaMember(3)] bool    _shouldFail;

        TestJobSpec(){ }
        public TestJobSpec(int id, int priority, bool shouldFail)
        {
            _id = id;
            _priority = priority;
            _shouldFail = shouldFail;
        }

        public override string       JobTitle                => Invariant($"Test job #{_id}, priority {Priority}");
        public override string       JobDescription          => $"Dummy job for development-time testing of the scan job system.";
        public override string       MetricsTag              => "TestJob";
        public override int          Priority                => _priority;
        public override EntityKind   EntityKind              => EntityKindCore.Player;
        public override ulong        EntityIdValueUpperBound => 500;

        public override DatabaseScanProcessor CreateProcessor(DatabaseScanProcessingStatistics initialStatisticsMaybe)
        {
            return new TestJobProcessor(
                id:         _id,
                shouldFail: _shouldFail);
        }

        public override DatabaseScanProcessingStatistics ComputeAggregateStatistics(IEnumerable<DatabaseScanProcessingStatistics> parts)
        {
            return null;
        }

        public override OrderedDictionary<string, object> CreateSummary(DatabaseScanProcessingStatistics stats)
        {
            return new OrderedDictionary<string, object>
            {
                { "Test job id", _id },
                { "Should fail?", _shouldFail },
            };
        }
    }

    [MetaSerializableDerived(100)]
    public class TestJobProcessor : DatabaseScanProcessor<PersistedPlayerBase>
    {
        [MetaMember(1)] int     _id;
        [MetaMember(2)] bool    _shouldFail;

        TestJobProcessor(){ }
        public TestJobProcessor(int id, bool shouldFail)
        {
            _id = id;
            _shouldFail = shouldFail;
        }

        public override int         DesiredScanBatchSize            => 2;
        public override TimeSpan    ScanInterval                    => TimeSpan.FromSeconds(0.5);
        public override TimeSpan    PersistInterval                 => TimeSpan.FromSeconds(5);
        public override TimeSpan    TickInterval                    => 0.5 * ScanInterval;
        public override bool        CanCurrentlyProcessMoreItems    => true;
        public override bool        HasCompletedAllWorkSoFar        => true;

        public override DatabaseScanProcessingStatistics Stats => null;

        public override void Cancel(IContext context)
        {
        }

        public override Task StartProcessItemBatchAsync(IContext context, IEnumerable<PersistedPlayerBase> items)
        {
            //context.Log.Debug("TestJob #{Id} scanned {Count} player(s)", _id, items.Count());

            if (_shouldFail && items.Any(player => EntityId.ParseFromString(player.EntityId).Value < 100))
                throw new InvalidOperationException($"TestJob #{_id} failure");

            return Task.CompletedTask;
        }

        public override Task TickAsync(IContext context)
        {
            return Task.CompletedTask;
        }
    }
}
