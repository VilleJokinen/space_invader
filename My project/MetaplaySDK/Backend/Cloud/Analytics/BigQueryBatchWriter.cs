// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Google;
using Google.Apis.Bigquery.v2.Data;
using Google.Apis.Requests;
using Google.Cloud.BigQuery.V2;
using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Analytics
{
    /// <summary>
    /// Helper to write rows into BigQuery table as batches in the background. BigQueryBatchWriter handles
    /// normal error conditions and retries as necessary.
    /// BigQueryBatchWriter does not buffer data boundlessy, instead BigQueryBatchWriter manages a limited
    /// set of <see cref="BigQueryBatchWriter.BatchBuffer"/>s. Caller should allocate a batch buffer from
    /// the writer, write rows to be added there and then submit the buffer back to the writer. If no free
    /// batch buffers are available, the allocation fails and caller may not proceed. Written batch buffers
    /// are available for allocation again after the write into BigQuery table has completed in the background.
    /// </summary>
    public class BigQueryBatchWriter : IAsyncDisposable
    {
        public class BatchBuffer
        {
            public List<BigQueryInsertRow> Rows = new List<BigQueryInsertRow>();
            public int NumRows => Rows.Count;
            public void Add(BigQueryInsertRow row)
            {
                Rows.Add(row);
            }
        }

        readonly IMetaLogger            _log;
        readonly BigQueryClient         _client;
        readonly TableReference         _tableReference;
        readonly AnalyticsSinkMetrics   _metrics;
        readonly int                    _maxNumOpenBatches;
        readonly object                 _lock;
        readonly List<Task>             _flushTasks;
        int                             _numOpenBatches;

        public BigQueryBatchWriter(IMetaLogger log, BigQueryClient client, TableReference tableReference, AnalyticsSinkMetrics metrics, int maxNumOpenBatches)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _tableReference = tableReference ?? throw new ArgumentNullException(nameof(tableReference));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _maxNumOpenBatches = maxNumOpenBatches;
            _lock = new object();
            _flushTasks = new List<Task>();
            _numOpenBatches = 0;
        }

        public static async Task<BigQueryBatchWriter> CreateAsync(IMetaLogger log, string projectId, string credentialsJson, string datasetId, string tableId, int maxNumOpenBatches, AnalyticsSinkMetrics metrics)
        {
            BigQueryClientBuilder builder = new BigQueryClientBuilder();
            builder.ProjectId = projectId;
            builder.JsonCredentials = credentialsJson;

            BigQueryClient client = await builder.BuildAsync();
            TableReference tableReference = client.GetTableReference(projectId, datasetId, tableId);

            // Test the table exists
            _ = await client.GetTableAsync(tableReference);

            return new BigQueryBatchWriter(log, client, tableReference, metrics, maxNumOpenBatches);
        }

        public async ValueTask DisposeAsync()
        {
            Task[] ongoingFlushes;
            lock (_lock)
            {
                ongoingFlushes = _flushTasks.ToArray();
            }
            await Task.WhenAll(ongoingFlushes).ConfigureAwait(false);

            // BigQueryClient does not need to be disposed if it is long-running. But needs to
            // be disposed if created often. We cannot know which case it is here, so Dispose()
            _client.Dispose();
        }

        /// <summary>
        /// Allocates new batch buffer. If the number of open (unsubmitted or submit has not yet finished) buffers
        /// exceeds the limit, returns null.
        /// </summary>
        public BatchBuffer TryAllocateBatchBuffer()
        {
            lock (_lock)
            {
                if (_numOpenBatches >= _maxNumOpenBatches)
                    return null;
                _numOpenBatches++;
            }
            return new BatchBuffer();
        }

        /// <summary>
        /// Submits a batch for writing in background. Caller must not modify the buffer after this call.
        /// </summary>
        public void SubmitBufferForWriting(BatchBuffer buffer)
        {
            Task flushTask = Task.Run(async () => await ExecuteFlushAsync(buffer));
            lock (_lock)
            {
                _flushTasks.Add(flushTask);
                _ = flushTask.ContinueWith(task =>
                {
                    lock (_lock)
                    {
                        _flushTasks.Remove(flushTask);
                        _numOpenBatches--;
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        async Task ExecuteFlushAsync(BatchBuffer buffer)
        {
            int numRetries = 0;

            if (buffer.NumRows == 0)
                return;

            for (;;)
            {
                int errorHttpStatusCode;
                bool isRateLimitExceededError;

                try
                {
                    InsertOptions options = new InsertOptions();
                    options.SkipInvalidRows = true;
                    options.SuppressInsertErrors = true;

                    Stopwatch sw = Stopwatch.StartNew();
                    BigQueryInsertResults results = await _client.InsertRowsAsync(_tableReference, buffer.Rows, options);
                    if (results.Status == BigQueryInsertStatus.AllRowsInserted)
                    {
                        // Success patch
                        _metrics.EventsFlushed.Inc(buffer.NumRows);
                        _metrics.ChunksFlushed.Inc();
                        _metrics.BatchWriteDuration.Observe(sw.Elapsed.TotalSeconds);
                        return;
                    }

                    // Only happens if input is fully or partially invalid. Log this and exit. No point in retrying with broken data.

                    BigQueryInsertRowErrors sampleResult    = results.Errors.FirstOrDefault();
                    SingleError             sampleError     = sampleResult?.FirstOrDefault();
                    BigQueryInsertRow       sampleRow       = sampleResult?.OriginalRow;

                    _log.Error(
                        "Attempted to write invalid data into into BigQuery table ({DatasetId}, {TableId}). {NumFailures} rows failed, for errors such as {SampleError} for {SampleRow}.",
                        _tableReference.DatasetId,
                        _tableReference.TableId,
                        results.OriginalRowsWithErrors,
                        PrettyPrint.Compact(sampleError),
                        PrettyPrint.Compact(sampleRow));

                    // Metrics. Report the whole batch as failed, but report events as precisely as possible.

                    int numErrorRows = results.Errors.Count();
                    _metrics.EventsFlushed.Inc(buffer.NumRows - numErrorRows);
                    _metrics.EventsDropped.Inc(numErrorRows);
                    _metrics.ChunksDropped.Inc();
                    return;
                }
                catch (GoogleApiException apiEx)
                {
                    errorHttpStatusCode = (int)apiEx.HttpStatusCode;
                    isRateLimitExceededError = apiEx.Error?.Errors?.Any(error => string.Equals("rateLimitExceeded", error.Reason, StringComparison.Ordinal)) ?? false;
                    _log.Warning("Writing into BigQuery table ({DatasetId}, {TableId}) failed with api error: {Error}", _tableReference.DatasetId, _tableReference.TableId, apiEx);
                }
                catch (HttpRequestException httpEx)
                {
                    errorHttpStatusCode = (int)(httpEx.StatusCode ?? default);
                    isRateLimitExceededError = false;
                    _log.Warning("Writing into BigQuery table ({DatasetId}, {TableId}) failed with http error: {Error}", _tableReference.DatasetId, _tableReference.TableId, httpEx);
                }
                catch(Exception ex)
                {
                    errorHttpStatusCode = 0;
                    isRateLimitExceededError = false;
                    _log.Warning("Writing into BigQuery table ({DatasetId}, {TableId}) failed with unknown error: {Error}", _tableReference.DatasetId, _tableReference.TableId, ex);
                }

                bool allowRetry;
                switch (errorHttpStatusCode)
                {
                    case 400: // Invalid
                    {
                        allowRetry = false;
                        break;
                    }

                    case 403:
                    {
                        // 403 Not allowed is emitted for various non-retryable errors and
                        // for rate limiting. Retry only if the error was for rate limiting.
                        if (isRateLimitExceededError)
                            allowRetry = true;
                        else
                            allowRetry = false;
                        break;
                    }

                    case 500:
                    case 503:
                    case 0:     // Untyped exception. Cannot really do anything, so let's just retry.
                    default:    // Unknown exception.
                    {
                        allowRetry = true;
                        break;
                    }
                }

                // This is a retryable error. Retry with back-off.
                // BigQuery SLA defines the following back-off strategy: Wait for 1 second, then retry. If retry fails, double the wait
                // and repeat up to 32 seconds.
                numRetries++;
                if (allowRetry && numRetries >= 7)
                {
                    // Retry failure
                    _log.Error("Writing into BigQuery table ({DatasetId}, {TableId}) failed. Failed to write {NumRows} rows.", _tableReference.DatasetId, _tableReference.TableId);
                    _metrics.EventsDropped.Inc(buffer.NumRows);
                    _metrics.ChunksDropped.Inc();
                    return;
                }

                int retryCooldownSeconds = (1 << (numRetries - 1)); // following sequence: 1, 2, 4, 8, 16, 32
                _log.Information("Will retry write into BigQuery table ({DatasetId}, {TableId}) after {NumSeconds} num seconds. Retry attempt {RetryNumber}.", _tableReference.DatasetId, _tableReference.TableId, retryCooldownSeconds, numRetries);
                await Task.Delay(TimeSpan.FromSeconds(retryCooldownSeconds));
            }
        }
    }
}
