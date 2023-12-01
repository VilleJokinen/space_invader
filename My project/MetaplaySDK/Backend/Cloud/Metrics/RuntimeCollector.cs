// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Threading;

namespace Metaplay.Cloud.Metrics
{
    public class RuntimeCollector : EventListener, IDisposable
    {
        // see: https://stebet.net/monitoring-gc-and-memory-allocations-with-net-core-2-2-and-application-insights/

        // from https://docs.microsoft.com/en-us/dotnet/framework/performance/garbage-collection-etw-events
        public enum Keyword : long
        {
            Gc                 = 0x0000001,
            Type               = 0x0080000,
            GcHeapAndTypeNames = 0x1000000,
            Threading          = 0x0010000,
        }

        private readonly Process            _process;
        private readonly SocketCollector    _sockets;
        private readonly Timer              _frequentTimer;
        private readonly Timer              _timer;
        private readonly Stopwatch          _stopwatch;

        internal class CollectInfo
        {
            public readonly uint    Id;
            public readonly uint    Generation;
            public readonly uint    Reason;
            public readonly uint    Type;
            public readonly long    Timestamp;

            public CollectInfo(uint id, uint generation, uint reason, uint type, long timestamp)
            {
                Id = id;
                Generation = generation;
                Reason = reason;
                Type = type;
                Timestamp = timestamp;
            }
        }

        private long                _suspendBeginAt = 0;
        private List<CollectInfo>   _collections    = new List<CollectInfo>();

        static readonly Prometheus.HistogramConfiguration s_collectTimeElapsedConfig = new Prometheus.HistogramConfiguration
        {
            Buckets = Defaults.LatencyDurationBuckets,
            LabelNames = new string[] { "gen", "blocking" }
        };

        static readonly Prometheus.HistogramConfiguration s_suspendTimeConfig = new Prometheus.HistogramConfiguration
        {
            Buckets = Defaults.LatencyDurationBuckets
        };

        // Meta stats
        readonly Prometheus.Counter     c_collectTimeDelay  = Prometheus.Metrics.CreateCounter  ("dotnet_collect_time_delay_seconds_total", "Duration in seconds how much the metrics collecting was delayed from expected");

        // Garbage collector metrics
        readonly Prometheus.Gauge       c_heapSize          = Prometheus.Metrics.CreateGauge    ("dotnet_gc_heap_size", ".NET Garbage Collection current heap size");
        readonly Prometheus.Counter     c_allocatedMemory   = Prometheus.Metrics.CreateCounter  ("dotnet_gc_memory_allocated_total", ".NET Garbage Collection total allocated allocated");
        readonly Prometheus.Counter     c_numCollections    = Prometheus.Metrics.CreateCounter  ("dotnet_gc_collections_total", ".NET Garbage Collection number of collections", new Prometheus.CounterConfiguration { LabelNames = new string[] { "gen" } });
        readonly Prometheus.Gauge       c_gen0Size          = Prometheus.Metrics.CreateGauge    ("dotnet_gc_gen0_size", ".NET Garbage Collection generation 0 size in bytes");
        readonly Prometheus.Counter     c_gen0Promoted      = Prometheus.Metrics.CreateCounter  ("dotnet_gc_gen0_promoted_total", ".NET Garbage Collection generation 0 bytes promoted to generation 1");
        readonly Prometheus.Gauge       c_gen1Size          = Prometheus.Metrics.CreateGauge    ("dotnet_gc_gen1_size", ".NET Garbage Collection generation 1 size in bytes");
        readonly Prometheus.Counter     c_gen1Promoted      = Prometheus.Metrics.CreateCounter  ("dotnet_gc_gen1_promoted_total", ".NET Garbage Collection generation 1 bytes promoted to generation 2");
        readonly Prometheus.Gauge       c_gen2Size          = Prometheus.Metrics.CreateGauge    ("dotnet_gc_gen2_size", ".NET Garbage Collection generation 2 size in bytes");
        readonly Prometheus.Counter     c_gen2Survived      = Prometheus.Metrics.CreateCounter  ("dotnet_gc_gen2_survived_total", ".NET Garbage Collection generation 2 bytes survived");
        readonly Prometheus.Gauge       c_lohSize           = Prometheus.Metrics.CreateGauge    ("dotnet_gc_loh_size", ".NET Garbage Collection large-object-heap size in bytes");
        readonly Prometheus.Counter     c_lohSurvived       = Prometheus.Metrics.CreateCounter  ("dotnet_gc_loh_survived_total", ".NET Garbage Collection generation large-object-heap bytes survived");
        readonly Prometheus.Gauge       c_pohSize           = Prometheus.Metrics.CreateGauge    ("dotnet_gc_poh_size", ".NET Garbage Collection pinned-object-heap size in bytes");
        readonly Prometheus.Counter     c_pohSurvived       = Prometheus.Metrics.CreateCounter  ("dotnet_gc_poh_survived_total", ".NET Garbage Collection generation pinned-object-heap bytes survived");
        readonly Prometheus.Histogram   c_gcTimeElapsed     = Prometheus.Metrics.CreateHistogram("dotnet_gc_time_elapsed", "Time spent in garbage collection", s_collectTimeElapsedConfig);
        readonly Prometheus.Histogram   c_gcSuspendTime     = Prometheus.Metrics.CreateHistogram("dotnet_gc_suspend_elapsed", "Total time spent with execution engine (partially) suspended", s_suspendTimeConfig);

        // CPU metrics
        readonly Prometheus.Gauge       c_processorCount    = Prometheus.Metrics.CreateGauge    ("dotnet_cpu_processor_count", ".NET number of logical processors");
        readonly Prometheus.Counter     c_totalCPUTime      = Prometheus.Metrics.CreateCounter  ("dotnet_cpu_time_total", ".NET cumulative process CPU usage time (in seconds)");
        readonly Prometheus.Counter     c_privilegedCPUTime = Prometheus.Metrics.CreateCounter  ("dotnet_cpu_privileged_time_total", ".NET cumulative process CPU usage time (in seconds)");
        readonly Prometheus.Counter     c_userCPUTime       = Prometheus.Metrics.CreateCounter  ("dotnet_cpu_user_time_total", ".NET cumulative process CPU usage time (in seconds)");

        // Memory metrics
        readonly Prometheus.Gauge       c_processMemoryAllocated    = Prometheus.Metrics.CreateGauge("dotnet_memory_allocated", "Total memory (in bytes) allocated to process");
        readonly Prometheus.Gauge       c_nonPagedSystemMemory      = Prometheus.Metrics.CreateGauge("dotnet_memory_non_paged_system", "Amount of non-paged system memory (in bytes) allocated to process");
        readonly Prometheus.Gauge       c_pagedMemory               = Prometheus.Metrics.CreateGauge("dotnet_memory_paged", "Amount of paged memory (in bytes) allocated to process");
        readonly Prometheus.Gauge       c_pagedSystemMemory         = Prometheus.Metrics.CreateGauge("dotnet_memory_paged_system", "Amount of pageable system memory (in bytes) allocated to process");
        readonly Prometheus.Gauge       c_privateMemory             = Prometheus.Metrics.CreateGauge("dotnet_memory_private", "Amount of private memory (in bytes) allocated to process");
        readonly Prometheus.Gauge       c_virtualMemory             = Prometheus.Metrics.CreateGauge("dotnet_memory_virtual", "Amount of virtual memory (in bytes) allocated to process");

        // ThreadPool metrics
        // See: https://docs.microsoft.com/en-us/dotnet/framework/performance/thread-pool-etw-events
        readonly Prometheus.Gauge       c_threadPoolThreads             = Prometheus.Metrics.CreateGauge("dotnet_threadpool_threads", ".NET ThreadPool number of active threads");
        readonly Prometheus.Counter     c_threadPoolPendingWorkItems    = Prometheus.Metrics.CreateCounter("dotnet_threadpool_pending_work_items_total", ".NET ThreadPool cumulative number of pending work items");
        readonly Prometheus.Gauge       c_threadPoolPendingWorkItemsOld = Prometheus.Metrics.CreateGauge("dotnet_threadpool_pending_work_items", ".NET ThreadPool current number of pending work items");
        readonly Prometheus.Counter     c_threadPoolCompletedWorkItems  = Prometheus.Metrics.CreateCounter("dotnet_threadpool_completed_work_items_total", ".NET ThreadPool cumulative number of completed work items");
        readonly Prometheus.Gauge       c_threadPoolWorkerThreads       = Prometheus.Metrics.CreateGauge("dotnet_threadpool_worker_threads", ".NET ThreadPool number of active worker threads");
        readonly Prometheus.Gauge       c_threadPoolRetiredThreads      = Prometheus.Metrics.CreateGauge("dotnet_threadpool_retired_threads", ".NET ThreadPool number of retired worker threads");
        readonly Prometheus.Gauge       c_threadPoolWorkerThroughput    = Prometheus.Metrics.CreateGauge("dotnet_threadpool_worker_throughput", ".NET ThreadPool number of completions per unit of time");
        readonly Prometheus.Counter     c_threadPoolWorkerThreadStart   = Prometheus.Metrics.CreateCounter("dotnet_threadpool_thread_start_total", ".NET ThreadPool cumulative number of threads started");
        readonly Prometheus.Counter     c_threadPoolWorkerThreadStop    = Prometheus.Metrics.CreateCounter("dotnet_threadpool_thread_stop_total", ".NET ThreadPool cumulative number of threads stopped");
        readonly Prometheus.Counter     c_threadPoolWorkerRetireStart   = Prometheus.Metrics.CreateCounter("dotnet_threadpool_thread_retirement_start_total", ".NET ThreadPool cumulative number of thread retirements started");
        readonly Prometheus.Counter     c_threadPoolWorkerRetireStop    = Prometheus.Metrics.CreateCounter("dotnet_threadpool_thread_retirement_stop_total", ".NET ThreadPool cumulative number of thread retirements stopped");

        const int FrequentTickIntervalSeconds = 1;
        const int TickIntervalSeconds = 5;
        long _prevTickTimestamp = Stopwatch.GetTimestamp();

        public RuntimeCollector()
        {
            _process = Process.GetCurrentProcess();
            _sockets = SocketCollector.CreateForCurrentOS();

            _frequentTimer = new Timer(CollectFrequentMetrics, null, 0, FrequentTickIntervalSeconds * 1000);
            _timer = new Timer(CollectMetrics, null, 0, TickIntervalSeconds * 1000);
            _stopwatch = Stopwatch.StartNew();
        }

        public override void Dispose()
        {
            _process?.Dispose();
            _sockets?.Dispose();
            _frequentTimer?.Dispose();
            _timer?.Dispose();

            base.Dispose();
        }

        void CollectFrequentMetrics(object state)
        {
            // Resolve how much time passed since previous
            long curTimestamp = Stopwatch.GetTimestamp();
            long durationSincePrevTick = unchecked(curTimestamp - _prevTickTimestamp);
            _prevTickTimestamp = curTimestamp;
            double duration = durationSincePrevTick / (double)Stopwatch.Frequency;
            c_collectTimeDelay.Inc(Math.Max(0.0, duration - FrequentTickIntervalSeconds));

            // ThreadPool
            c_threadPoolThreads.Set(ThreadPool.ThreadCount);
            c_threadPoolPendingWorkItems.Inc(ThreadPool.PendingWorkItemCount);
            c_threadPoolPendingWorkItemsOld.Set(ThreadPool.PendingWorkItemCount);
            c_threadPoolCompletedWorkItems.IncTo(ThreadPool.CompletedWorkItemCount);
        }

        void CollectMetrics(object state)
        {
            //Console.WriteLine("  [ETW] CollectMetrics()");
            CollectGarbageCollectorMetrics();
            CollectProcessMetrics();
            _sockets.Collect();
        }

        void CollectGarbageCollectorMetrics()
        {
            c_heapSize.Set(GC.GetTotalMemory(forceFullCollection: false));
        }

        void CollectProcessMetrics()
        {
            // Force refresh of stats (clears cached values)
            _process.Refresh();

            // CPU usage
            // \note sometimes causes a decrease without the clamp
            c_processorCount.Set(Environment.ProcessorCount);
            c_totalCPUTime.IncTo(_process.TotalProcessorTime.TotalSeconds);
            c_privilegedCPUTime.IncTo(_process.PrivilegedProcessorTime.TotalSeconds);
            c_userCPUTime.IncTo(_process.UserProcessorTime.TotalSeconds);

            // Memory stats
            c_processMemoryAllocated.Set(_process.WorkingSet64);
            c_nonPagedSystemMemory.Set(_process.NonpagedSystemMemorySize64);
            c_pagedMemory.Set(_process.PagedMemorySize64);
            c_pagedSystemMemory.Set(_process.PagedSystemMemorySize64);
            c_privateMemory.Set(_process.PrivateMemorySize64);
            c_virtualMemory.Set(_process.VirtualMemorySize64);
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            //Console.WriteLine("  [ETW] OnEventSourceCreated(): {0}", eventSource.Name);

            // .NET Garbage Collection events
            // \todo [petri] this doesn't seem to happen ever (at least when running from VS)
            if (eventSource.Name.Equals("Microsoft-Windows-DotNETRuntime"))
            {
                //Console.WriteLine("  !! [ETW] Start listening to events from {0}", eventSource.Name);
                // \note Change this to EventLevel.Verbose to receive events about memory allocations (one event every 100kB of allocations)
                EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)(Keyword.Gc | Keyword.GcHeapAndTypeNames | Keyword.Type /*| Keyword.Threading*/));
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs args)
        {
            //Console.WriteLine("    OnEventWritten(): #{0} {1}", args.EventId, args.EventName);
            switch (args.EventName)
            {
                case "GCHeapStats_V1":
                    ProcessGCHeapStatsV1(args);
                    break;

                case "GCHeapStats_V2":
                    ProcessGCHeapStatsV2(args);
                    break;

                case "GCAllocationTick_V3":
                    // \note only happens on Verbose level
                    ProcessAllocationTick(args);
                    break;

                case "GCStart_V2":
                    // \note this only measures the time in actual GC, not the thread suspension etc.
                    ProcessGCStart(args);
                    break;

                case "GCEnd_V1":
                    ProcessGCEnd(args);
                    break;

                case "GCSuspendEEBegin_V1":
                    ProcessSuspendEEBegin(args);
                    break;

                case "GCRestartEEEnd_V1":
                    ProcessRestartEEEnd(args);
                    break;

                case "GCGlobalHeapHistory_V2":
                case "GCPerHeapHistory_V3":
                case "GCTriggered":
                case "GCFinalizersEnd_V1":
                case "GCFinalizersBegin_V1":
                case "GCSuspendEEEnd_V1":
                case "GCRestartEEBegin_V1":
                case "GCCreateSegment_V1":
                case "GCFreeSegment_V1":
                case "GCCreateConcurrentThread_V1":
                case "GCTerminateConcurrentThread_V1":
                    //Console.WriteLine("[ETW] @{0}ms {1}: {2}", _stopwatch.ElapsedTicks * 1000.0f / (float)Stopwatch.Frequency, args.EventName, string.Join(" ", args.Payload.Select(arg => arg.ToString())));
                    break;

                case "GCGlobalHeapHistory_V3": // #205: GCGlobalHeapHistory_V3 15928640 8 0 0 0 30 8 1 87 0 0
                    // \todo [petri] new in .NET 5, handle these? see https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/ClrEtwAll.man
                    break;

                case "GCGlobalHeapHistory_V4": // #205: GCGlobalHeapHistory_V4 21134208 8 2 2 1 30 8 1 86 0 0 8
                    // \todo [petri] new in .NET 6, handle it? see https://raw.githubusercontent.com/dotnet/runtime/main/src/coreclr/gc/gc.cpp
                    break;

                case "BulkType":
                case "GCMarkWithType":
                case "RuntimeInformationStart":
                case "GCJoin_V2":
                case "PinObjectAtGCTime":
                case "IncreaseMemoryPressure":
                    // ignore
                    break;

                case "EventCounters":
                    break;

                // Threading
                case "ThreadPoolWorkerThreadStart":
                    c_threadPoolWorkerThreads.Set((int)(uint)args.Payload[0]);
                    c_threadPoolRetiredThreads.Set((int)(uint)args.Payload[1]);
                    c_threadPoolWorkerThreadStart.Inc();
                    break;

                case "ThreadPoolWorkerThreadStop":
                    c_threadPoolWorkerThreads.Set((int)(uint)args.Payload[0]);
                    c_threadPoolRetiredThreads.Set((int)(uint)args.Payload[1]);
                    c_threadPoolWorkerThreadStop.Inc();
                    break;

                case "ThreadPoolWorkerThreadRetirementStart":
                    c_threadPoolWorkerThreads.Set((int)(uint)args.Payload[0]);
                    c_threadPoolRetiredThreads.Set((int)(uint)args.Payload[1]);
                    c_threadPoolWorkerRetireStart.Inc();
                    break;

                case "ThreadPoolWorkerThreadRetirementStop":
                    c_threadPoolWorkerThreads.Set((int)(uint)args.Payload[0]);
                    c_threadPoolRetiredThreads.Set((int)(uint)args.Payload[1]);
                    c_threadPoolWorkerRetireStop.Inc();
                    break;

                case "ThreadPoolWorkerThreadAdjustmentSample":
                    c_threadPoolWorkerThroughput.Set((double)args.Payload[0]);
                    break;

                case "ThreadPoolWorkerThreadAdjustmentAdjustment":
                    //double avgThroughput = (double)args.Payload[0];
                    //int newWorkerThreadCount = (int)(uint)args.Payload[1];
                    //int reason = (int)(uint)args.Payload[2];
                    //Console.WriteLine("***** ThreadPool adjustment: throughput={0}, newWorkerCount={1}, reason={2}", avgThroughput, newWorkerThreadCount, reason);
                    break;

                case "ThreadRunning":
                case "ThreadCreating":
                case "ThreadCreated":
                case "IOThreadCreate_V1":
                case "IOThreadTerminate_V1":
                case "IOThreadRetire_V1":
                case "IOThreadUnretire_V1":
                case "ThreadPoolWorkerThreadWait":
                    // collect?
                    break;

                default:
                    Console.WriteLine("[ETW] Unknown event #{0}: {1} {2}", args.EventId, args.EventName, string.Join(" ", args.Payload.Select(arg => arg.ToString())));
                    break;
            }
        }

        void ProcessGCHeapStatsV1(EventWrittenEventArgs ev)
        {
            //Console.WriteLine("  [ETW] Gen0: size={0}, promoted={1}", (ulong)ev.Payload[0], (ulong)ev.Payload[1]);
            //Console.WriteLine("  [ETW] Gen1: size={0}, promoted={1}", (ulong)ev.Payload[2], (ulong)ev.Payload[3]);
            //Console.WriteLine("  [ETW] Gen2: size={0}, survived={1}", (ulong)ev.Payload[4], (ulong)ev.Payload[5]);
            //Console.WriteLine("  [ETW] LOH: size={0}, survived={1}", (ulong)ev.Payload[6], (ulong)ev.Payload[7]);

            c_gen0Size.Set((ulong)ev.Payload[0]);
            c_gen0Promoted.Inc((ulong)ev.Payload[1]);
            c_gen1Size.Set((ulong)ev.Payload[2]);
            c_gen1Promoted.Inc((ulong)ev.Payload[3]);
            c_gen2Size.Set((ulong)ev.Payload[4]);
            c_gen2Survived.Inc((ulong)ev.Payload[5]);
            c_lohSize.Set((ulong)ev.Payload[6]);
            c_lohSurvived.Inc((ulong)ev.Payload[7]);
        }

        void ProcessGCHeapStatsV2(EventWrittenEventArgs ev)
        {
            c_gen0Size.Set((ulong)ev.Payload[0]);
            c_gen0Promoted.Inc((ulong)ev.Payload[1]);
            c_gen1Size.Set((ulong)ev.Payload[2]);
            c_gen1Promoted.Inc((ulong)ev.Payload[3]);
            c_gen2Size.Set((ulong)ev.Payload[4]);
            c_gen2Survived.Inc((ulong)ev.Payload[5]);
            c_lohSize.Set((ulong)ev.Payload[6]);
            c_lohSurvived.Inc((ulong)ev.Payload[7]);
            c_pohSize.Set((ulong)ev.Payload[14]);
            c_pohSurvived.Inc((ulong)ev.Payload[15]);
        }

        void ProcessAllocationTick(EventWrittenEventArgs ev)
        {
            //Console.WriteLine("  [ETW] ProcessAllocationEvent(): {0} / {1}", (ulong)ev.Payload[3], ev.Payload[5]);
            // \todo [petri] keep track of type (Payload[5]) as rough estimate how much each type allocates memory
            c_allocatedMemory.Inc((ulong)ev.Payload[3]);
            //Console.WriteLine("[ETW] Memory allocation tick: {0} ({1} bytes)", ev.Payload[5], (ulong)ev.Payload[3]);
        }

        void ProcessGCStart(EventWrittenEventArgs args)
        {
            CollectInfo info = new CollectInfo((uint)args.Payload[0], (uint)args.Payload[1], (uint)args.Payload[2], (uint)args.Payload[3], _stopwatch.ElapsedTicks);
            _collections.Add(info);
        }

        void ProcessGCEnd(EventWrittenEventArgs args)
        {
            CollectInfo current = (_collections.Count > 0) ? _collections[_collections.Count - 1] : null;
            if (current?.Id == (uint)args.Payload[0])
            {
                long elapsedTicks = _stopwatch.ElapsedTicks - current.Timestamp;
                double elapsedSeconds = elapsedTicks / (double)Stopwatch.Frequency;

                string genId = $"gen{current.Generation}";
                c_numCollections.WithLabels(genId).Inc(1);

                bool isBlocking = current.Type != 1;
                c_gcTimeElapsed.WithLabels(genId, isBlocking ? "yes" : "no").Observe(elapsedSeconds);

                // Print pauses longer than 10ms
                //if (isBlocking && elapsedSeconds >= 0.01)
                //    Console.WriteLine("GC end: id={0}, gen={1}, reason={2}, type={3}, elapsed={4}ms", current.Id, current.Generation, current.Reason, current.Type, elapsedSeconds * 1000.0);

                // Pop current collection
                _collections.RemoveAt(_collections.Count - 1);
            }
            else
                Console.WriteLine("[ETW] GC end mismatch !");
        }

        void ProcessSuspendEEBegin(EventWrittenEventArgs args)
        {
            _suspendBeginAt = _stopwatch.ElapsedTicks;
        }

        void ProcessRestartEEEnd(EventWrittenEventArgs args)
        {
            long elapsedTicks = _stopwatch.ElapsedTicks - _suspendBeginAt;
            double elapsedSeconds = elapsedTicks / (double)Stopwatch.Frequency;
            c_gcSuspendTime.Observe(elapsedSeconds);
            //if (elapsedSeconds > 0.001)
            //    Console.WriteLine("Suspend total: {0:0.0}ms", elapsedSeconds * 1000.0);
            _suspendBeginAt = 0;
        }
    }
}
