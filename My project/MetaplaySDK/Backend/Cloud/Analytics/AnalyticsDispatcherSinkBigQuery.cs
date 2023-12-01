// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Analytics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Analytics
{
    /// <summary>
    /// Writes events into a BigQuery table in a Firebase-like format.
    /// </summary>
    public class AnalyticsDispatcherSinkBigQuery : AnalyticsDispatcherSinkBase
    {
        AnalyticsSinkBigQueryOptions    _opts;
        BigQueryBatchWriter             _writer;
        AnalyticsSinkMetrics            _metrics;
        BigQueryBatchWriter.BatchBuffer _currentBatchBuffer;
        MetaTime?                       _autoflushAt;

        AnalyticsDispatcherSinkBigQuery(IMetaLogger log, AnalyticsSinkBigQueryOptions opts, BigQueryBatchWriter writer, AnalyticsSinkMetrics metrics) : base(log)
        {
            _opts = opts;
            _writer = writer;
            _metrics = metrics;
            _currentBatchBuffer = null;
            _autoflushAt = null;

            if (!BigQueryFormatter.IsInitialized)
                throw new InvalidOperationException($"BigQueryFormatter must be initialized for AnalyticsDispatcherSinkBigQuery to work");
        }

        /// <summary>
        /// Create instance of the sink, if it is enabled in the options. Otherwise, return null.
        /// </summary>
        public static async Task<AnalyticsDispatcherSinkBigQuery> TryCreateAsync()
        {
            AnalyticsSinkBigQueryOptions opts = RuntimeOptionsRegistry.Instance.GetCurrent<AnalyticsSinkBigQueryOptions>();
            if (!opts.Enabled)
                return null;

            IMetaLogger             log     = MetaLogger.ForContext<AnalyticsDispatcherSinkBigQuery>();
            AnalyticsSinkMetrics    metrics = AnalyticsSinkMetrics.ForSink("bigquery");
            BigQueryBatchWriter     writer  = await BigQueryBatchWriter.CreateAsync(log, opts.BigQueryProjectId, opts.BigQueryCredentialsJson, opts.BigQueryDatasetId, opts.BigQueryTableId, opts.NumChunkBuffers, metrics);
            return new AnalyticsDispatcherSinkBigQuery(log, opts, writer, metrics);
        }

        public override async ValueTask DisposeAsync()
        {
            // Flush final buffer
            if (_currentBatchBuffer != null)
            {
                _log.Information("Final flush: {NumFinalEvents} events", _currentBatchBuffer.NumRows);
                FlushBatchBuffer();
            }

            // Cleanup writer
            await _writer.DisposeAsync();

            await base.DisposeAsync();
        }

        public override void EnqueueBatches(List<AnalyticsEventBatch> batches)
        {
            AnalyticsEventBatchHelper.EventEnumerator enumerator = AnalyticsEventBatchHelper.EnumerateBatches(batches).GetEnumerator();
            while (enumerator.MoveNext())
            {
                // We have something to write. Ensure we have a write buffer

                if (_currentBatchBuffer == null)
                {
                    _currentBatchBuffer = _writer.TryAllocateBatchBuffer();
                    if (_currentBatchBuffer != null)
                    {
                        // Successfully created new write buffer.
                        _autoflushAt = MetaTime.Now + MetaDuration.FromTimeSpan(_opts.MaxPendingDuration);
                    }
                    else
                    {
                        int numRemainingEvents = enumerator.NumRemainingEvents + 1; // +1 because this event was dropped too
                        int numRemainingBatches = enumerator.NumRemainingBatches + 1; // +1 because this batch was dropped too

                        _log.Warning("Unable to allocate write buffer, dropping {NumEvents} events in {NumBatches} batches", numRemainingEvents, numRemainingBatches);

                        _metrics.BatchesDropped.Inc(numRemainingBatches);
                        _metrics.EventsDropped.Inc(numRemainingEvents);
                        break;
                    }
                }

                // Write the events the buffer until the buffer becomes full or we run out of events.
                // \note: the inner do-while is redundant but is there to communicate the expected code flow.

                do
                {
                    BigQueryFormatter.Instance.WriteEvent(_currentBatchBuffer, enumerator.Current, _opts.BigQueryEnableRowDeduplication);
                    if (_currentBatchBuffer.NumRows >= _opts.EventsPerChunk)
                    {
                        FlushBatchBuffer();
                        break;
                    }
                } while (enumerator.MoveNext());
            }

            if (_autoflushAt.HasValue && MetaTime.Now > _autoflushAt)
                FlushBatchBuffer();
        }

        void FlushBatchBuffer()
        {
            if (_currentBatchBuffer == null)
                throw new InvalidOperationException("no buffer to flush");

            _writer.SubmitBufferForWriting(_currentBatchBuffer);
            _currentBatchBuffer = null;
            _autoflushAt = null;
        }
    }
}
