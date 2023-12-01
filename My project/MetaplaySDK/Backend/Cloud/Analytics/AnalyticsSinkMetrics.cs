// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Prometheus;
using System;

namespace Metaplay.Cloud.Analytics
{
    public class AnalyticsSinkMetrics
    {
        static readonly Prometheus.HistogramConfiguration c_writeDurationConfig = new Prometheus.HistogramConfiguration
        {
            Buckets     = Metaplay.Cloud.Metrics.Defaults.LatencyDurationBuckets,
            LabelNames  = new string[] { "sink" },
        };

        static readonly Prometheus.Counter          c_eventsFlushed         = Prometheus.Metrics.CreateCounter("metaplay_analytics_sink_events_flushed_total", "Number of events flushed into the sink", "sink");
        static readonly Prometheus.Counter          c_eventsDropped         = Prometheus.Metrics.CreateCounter("metaplay_analytics_sink_events_dropped_total", "Number of events dropped", "sink"); // \note includes events dropped due to insufficient memory buffers or writes to sink failures
        static readonly Prometheus.Counter          c_batchesDropped        = Prometheus.Metrics.CreateCounter("metaplay_analytics_sink_batches_dropped_total", "Number of batches dropped due to insufficient in-memory buffers", "sink");
        static readonly Prometheus.Counter          c_chunksFlushed         = Prometheus.Metrics.CreateCounter("metaplay_analytics_sink_chunks_flushed_total", "Number of chunks flushed into the sink", "sink");
        static readonly Prometheus.Counter          c_chunksDropped         = Prometheus.Metrics.CreateCounter("metaplay_analytics_sink_chunks_dropped_total", "Number of chunks dropped due to failing to write", "sink");
        static readonly Prometheus.Histogram        c_chunkWriteDuration    = Prometheus.Metrics.CreateHistogram("metaplay_analytics_sink_write_duration", "Duration of successful sink write operations", c_writeDurationConfig);
        static readonly Prometheus.Counter          c_numBytesWritten       = Prometheus.Metrics.CreateCounter("metaplay_analytics_sink_bytes_output_total", "Number of bytes successfully written into sink", "sink");

        public readonly Prometheus.Counter.Child    EventsFlushed;
        public readonly Prometheus.Counter.Child    EventsDropped;
        public readonly Prometheus.Counter.Child    BatchesDropped;
        public readonly Prometheus.Counter.Child    ChunksFlushed;
        public readonly Prometheus.Counter.Child    ChunksDropped;
        public readonly Prometheus.Histogram.Child  BatchWriteDuration;
        public readonly Prometheus.Counter.Child    NumBytesWritten;

        public AnalyticsSinkMetrics(Counter.Child eventsFlushed, Counter.Child eventsDropped, Counter.Child batchesDropped, Counter.Child chunksFlushed, Counter.Child chunksDropped, Histogram.Child batchWriteDuration, Prometheus.Counter.Child numBytesWritten)
        {
            EventsFlushed = eventsFlushed ?? throw new ArgumentNullException(nameof(eventsFlushed));
            EventsDropped = eventsDropped ?? throw new ArgumentNullException(nameof(eventsDropped));
            BatchesDropped = batchesDropped ?? throw new ArgumentNullException(nameof(batchesDropped));
            ChunksFlushed = chunksFlushed ?? throw new ArgumentNullException(nameof(chunksFlushed));
            ChunksDropped = chunksDropped ?? throw new ArgumentNullException(nameof(chunksDropped));
            BatchWriteDuration = batchWriteDuration ?? throw new ArgumentNullException(nameof(batchWriteDuration));
            NumBytesWritten = numBytesWritten ?? throw new ArgumentNullException(nameof(numBytesWritten));
        }

        public static AnalyticsSinkMetrics ForSink(string sinkName)
        {
            return new AnalyticsSinkMetrics(
                c_eventsFlushed.WithLabels(sinkName),
                c_eventsDropped.WithLabels(sinkName),
                c_batchesDropped.WithLabels(sinkName),
                c_chunksFlushed.WithLabels(sinkName),
                c_chunksDropped.WithLabels(sinkName),
                c_chunkWriteDuration.WithLabels(sinkName),
                c_numBytesWritten.WithLabels(sinkName));
        }
    }
}
