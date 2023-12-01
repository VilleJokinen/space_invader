// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Analytics
{
    // AnalyticsDispatcherActor
    public class AnalyticsDispatcherActor : MetaReceiveActor
    {
        internal class TickFlush { public readonly static TickFlush Instance = new TickFlush(); }

        // Global counters
        Prometheus.Gauge            c_pendingBatches    = Prometheus.Metrics.CreateGauge("metaplay_analytics_pending_batches", "Number of current batches waiting to be flushed");
        Prometheus.Gauge            c_batchesDropped    = Prometheus.Metrics.CreateGauge("metaplay_analytics_batches_dropped_total", "Number of event batches dropped due to queue overflow");
        Prometheus.Gauge            c_eventsDropped     = Prometheus.Metrics.CreateGauge("metaplay_analytics_events_dropped_total", "Number of events dropped due to queue overflow");
        Prometheus.Counter          c_eventsReceived    = Prometheus.Metrics.CreateCounter("metaplay_analytics_events_received_total", "Number of analytics events received from game logic");

        public const int                MaxQueuedBatches    = 10_000;
        public static readonly TimeSpan TickInterval        = TimeSpan.FromMilliseconds(250);

        readonly AnalyticsDispatcherSinkBase[]  _sinks;

        // Batching
        ConcurrentQueue<SerializedAnalyticsEventBatch>  _pendingBatches = new ConcurrentQueue<SerializedAnalyticsEventBatch>();
        List<AnalyticsEventBatch>                       _tmpBatches     = new List<AnalyticsEventBatch>();

        public AnalyticsDispatcherActor(IEnumerable<AnalyticsDispatcherSinkBase> sinks)
        {
            // Store configure sinks
            _sinks = sinks.ToArray();

            // Hook handler for flushed batches
            // \note gets called on source thread
            EventBatchListener.HandleBatch = batchBuilder =>
            {
                // Only append to queue if not at capacity, to avoid OOMs
                if (_pendingBatches.Count >= MaxQueuedBatches)
                {
                    c_batchesDropped.Inc();
                    c_eventsDropped.Inc(batchBuilder.Count);
                }
                else
                {
                    // Steal contents so that the caller retains ownership of the original object but not contents.
                    // Objects are never shared across threads.
                    _pendingBatches.Enqueue(batchBuilder.StealToNewSerializedEventBatch());
                }
            };

            // Start flush timer
            Receive<TickFlush>(ReceiveTickFlush);
            Context.System.Scheduler.ScheduleTellRepeatedly(TickInterval, TickInterval, _self, TickFlush.Instance, ActorRefs.NoSender);

            // Register shutdown request listener
            ReceiveAsync<ShutdownSync>(ReceiveShutdownSync);
        }

        void ReceiveTickFlush(TickFlush _)
        {
            // Flush all pending batches to temp list first. The Batches are stored in
            // serialized format when issued and deserialized there into concrete event objects.
            while (_pendingBatches.TryDequeue(out SerializedAnalyticsEventBatch serializedBatch))
            {
                _tmpBatches.Add(serializedBatch.Deserialize(errorLog: _log));
                serializedBatch.Dispose();
            }

            // Collect statistics
            c_pendingBatches.Set(_tmpBatches.Count);
            int totalEvents = _tmpBatches.Sum(batch => batch.Count);
            c_eventsReceived.Inc(totalEvents);

            // Handle incoming batches (if any)
            if (_tmpBatches.Count > 0)
                _log.Debug("Received {BatchCount} batches with total of {EventCount} events", _tmpBatches.Count, totalEvents);

            // Send batches to all sinks
            // \note Enqueue even if there are no new events, so sink can do periodic flushing, too
            foreach (AnalyticsDispatcherSinkBase sink in _sinks)
                sink.EnqueueBatches(_tmpBatches);

            // Clear tmp events
            _tmpBatches.Clear();
        }

        async Task ReceiveShutdownSync(ShutdownSync shutdown)
        {
            // Dispose all sinks (and flush any pending events)
            _log.Info("Shutting down {NumSinks} analytics sinks..", _sinks.Length);
            await Task.WhenAll(_sinks.Select(async sink => await sink.DisposeAsync().ConfigureAwait(false)));

            // We're done
            _self.Tell(PoisonPill.Instance);
            Sender.Tell(ShutdownComplete.Instance);
        }
    }
}
