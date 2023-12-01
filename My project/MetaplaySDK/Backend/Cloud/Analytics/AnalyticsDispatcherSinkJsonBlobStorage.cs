// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Metaplay.Core;
using Metaplay.Core.Json;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Cloud.Analytics
{
    [RuntimeOptions("AnalyticsSinkJsonBlobStorage", isStatic: true, "Configuration options for the JSON analytics sink.")]
    public class AnalyticsSinkJsonBlobStorageOptions : RuntimeOptionsBase
    {
        [MetaDescription("Enables the JSON analytics sink.")]
        public bool                     Enabled             { get; set; }
        [MetaDescription("Template for the file paths to use when writing batches.")]
        public string                   FilePath            { get; set; } = "ServerAnalytics/{Year}/{Month}/{Day}/{HostName}-{Hour}{Minute}{Second}-{UniqueId}.json{CompressSuffix}";
        [MetaDescription("Example of a file path with the template parameters filled.")]
        public string                   ExampleFilePath     => _exampleFilePath;
        [MetaDescription("Compression mode for the stored JSON files (`None` or `Gzip`).")]
        public AnalyticsCompressionMode CompressMode        { get; set; } = AnalyticsCompressionMode.Gzip;
        [MetaDescription("Suffix for the filename based on the `CompressMode` used.")]
        public string                   CompressSuffix      => _compressSuffix;
        [MetaDescription("The target number of events per chunk.")]
        public int                      EventsPerChunk      { get; set; } = 100_000;
        [MetaDescription("The number of chunk buffers to use for uploading. If there are no empty buffers due to uploads failing or falling behind, further events are dropped.")]
        public int                      NumChunkBuffers     { get; set; } = 10;
        [MetaDescription("The maximum duration that an event is held before flushing it out.")]
        public TimeSpan                 MaxPendingDuration  { get; set; } = TimeSpan.FromMinutes(5);

        [MetaDescription("Optional S3 bucket name where the generated files are stored. By default, the server's private S3 bucket is used.")]
        public string                   S3BucketName        { get; set; }
        [MetaDescription("If `S3BucketName` is defined: The region where the custom S3 bucket resides.")]
        public string                   S3Region            { get; set; }
        [MetaDescription("If `S3BucketName` is defined: The canned ACL to use. Currently only accepts `BucketOwnerFullControl` or null.")]
        public string                   S3CannedACL         { get; set; }

        [IgnoreDataMember]
        string                          _exampleFilePath;

        [IgnoreDataMember]
        string                          _compressSuffix;

        public override Task OnLoadedAsync()
        {
            // If disabled, no need to check validity
            if (!Enabled)
                return Task.CompletedTask;

            // Validate config
            if (string.IsNullOrEmpty(FilePath))
                throw new InvalidOperationException("FilePath must be defined");

            _exampleFilePath = RenderFilePath("example.host", new DateTime(2021, 8, 1, 23, 5, 0, DateTimeKind.Utc), uniqueId: 0xDADACAFE);
            if (_exampleFilePath.Contains("{") || _exampleFilePath.Contains("}"))
                throw new InvalidOperationException($"Invalid FilePath template, braces remain after resolving variables: '{_exampleFilePath}'");

            _compressSuffix = CompressMode switch
            {
                AnalyticsCompressionMode.None => "",
                AnalyticsCompressionMode.Gzip => ".gz",
                _ => throw new InvalidOperationException($"Invalid AnalyticsCompressionMode {CompressMode}")
            };

            if (EventsPerChunk < 1)
                throw new InvalidOperationException("EventsPerChunk must be at least 1");

            if (MaxPendingDuration < TimeSpan.FromSeconds(1))
                throw new InvalidOperationException("MaxPendingDuration must be at least 1 second");

            if (NumChunkBuffers < 2)
                throw new InvalidOperationException("NumChunkBuffers must be at least 2");

            if (!string.IsNullOrEmpty(S3BucketName) && string.IsNullOrEmpty(S3Region))
                throw new InvalidOperationException("S3Region must also be specified when S3BucketName is specified");

            return Task.CompletedTask;
        }

        public string RenderFilePath(string hostName, DateTime now, uint uniqueId)
        {
            string str = FilePath;

            // General parameters
            str = str.Replace("{HostName}", hostName);
            str = str.Replace("{CompressSuffix}", _compressSuffix);
            str = str.Replace("{UniqueId}", $"{uniqueId:X8}");

            // Batch completion timestamp
            // \todo [petri] prefix strings with BatchYear or something?
            str = str.Replace("{Year}", Invariant($"{now.Year:00}"));
            str = str.Replace("{Month}", Invariant($"{now.Month:00}"));
            str = str.Replace("{Day}", Invariant($"{now.Day:00}"));
            str = str.Replace("{Hour}", Invariant($"{now.Hour:00}"));
            str = str.Replace("{Minute}", Invariant($"{now.Minute:00}"));
            str = str.Replace("{Second}", Invariant($"{now.Second:00}"));

            return str;
        }
    }

    /// <summary>
    /// Compression mode for storing analytics events in.
    /// </summary>
    public enum AnalyticsCompressionMode
    {
        None,
        Gzip,
    }

    /// <summary>
    /// Log all events as JSON into a <see cref="IBlobStorage"/> (S3 in cloud, or disk when running locally).
    /// </summary>
    public class AnalyticsDispatcherSinkJsonBlobStorage : AnalyticsDispatcherSinkBase, IMetaIntegrationConstructible<AnalyticsDispatcherSinkJsonBlobStorage>
    {
        // Configuration
        AnalyticsSinkJsonBlobStorageOptions _opts = RuntimeOptionsRegistry.Instance.GetCurrent<AnalyticsSinkJsonBlobStorageOptions>();
        IBlobStorage                        _blobStorage;
        string                              _hostName;
        AnalyticsSinkMetrics                _metrics;

        // Writing
        ChunkBufferManager _chunkBufferManager;
        RandomPCG          _rng = RandomPCG.CreateNew();
        MemoryStream       _activeMemoryStream;
        GZipStream         _gzipStream;
        TextWriter         _textWriter;
        JsonTextWriter     _jsonWriter;

        // Stats
        MetaTime                _oldestEventAt      = MetaTime.Now;
        int                     _numBufferedEvents  = 0;
        int                     _numBufferedBatches = 0;

        /// <summary>
        /// Create instance of the sink, if it is enabled in the options. Otherwise, return null.
        /// </summary>
        /// <returns></returns>
        public static AnalyticsDispatcherSinkJsonBlobStorage TryCreate()
        {
            AnalyticsSinkJsonBlobStorageOptions opts = RuntimeOptionsRegistry.Instance.GetCurrent<AnalyticsSinkJsonBlobStorageOptions>();
            if (opts.Enabled)
                return IntegrationRegistry.Create<AnalyticsDispatcherSinkJsonBlobStorage>();
            else
                return null;
        }

        protected AnalyticsDispatcherSinkJsonBlobStorage() : base()
        {
            ClusteringOptions clusterOpts = RuntimeOptionsRegistry.Instance.GetCurrent<ClusteringOptions>();

            if (string.IsNullOrEmpty(_opts.S3BucketName))
            {
                // No custom S3 bucket defined, use default BlobStorage
                BlobStorageOptions blobOpts = RuntimeOptionsRegistry.Instance.GetCurrent<BlobStorageOptions>();
                _blobStorage = blobOpts.CreatePrivateBlobStorage(path: "");
            }
            else
            {
                // \note only support IRSA-based access control, for safety
                _blobStorage = new S3BlobStorage(accessKey: null, secretKey: null, regionName: _opts.S3Region, _opts.S3BucketName, basePath: "", cannedACL: _opts.S3CannedACL);
            }

            _hostName = clusterOpts.RemotingHost;
            _metrics = AnalyticsSinkMetrics.ForSink("jsonblob");

            // Initialize chunk flushing & chunk writer
            _chunkBufferManager = new ChunkBufferManager(numChunkBuffers: _opts.NumChunkBuffers);
        }

        bool TryInitializeWriter()
        {
            if (_activeMemoryStream != null)
                throw new InvalidOperationException($"Writer already initialized!");

            // Try to allocate chunk to write to, or bail out if allocation fails
            if (!_chunkBufferManager.TryAllocate(out _activeMemoryStream))
                return false;

            // Create compress stream
            Stream compressedStream;
            if (_opts.CompressMode == AnalyticsCompressionMode.Gzip)
            {
                _gzipStream = new GZipStream(_activeMemoryStream, CompressionLevel.Fastest, leaveOpen: true);
                compressedStream = _gzipStream;
            }
            else
                compressedStream = _activeMemoryStream;

            // Create TextWriter (force Unix newline)
            // Note that the underlying stream will be left open as it is manually disposed.
            _textWriter = new StreamWriter(compressedStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
            _textWriter.NewLine = "\n";

            // Create JSON writer (set CloseOutput for clarity)
            _jsonWriter = new JsonTextWriter(_textWriter);
            _jsonWriter.CloseOutput = true;

            return true;
        }

        void FlushWriter()
        {
            // Flush & close writers
            ((IDisposable)_jsonWriter)?.Dispose();
            _gzipStream?.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            _log.Information("Final flush: {NumFinalEvents} events", _numBufferedEvents);

            // Flush final chunk
            FlushChunk();

            // Cleanup chunk buffers
            await _chunkBufferManager.DisposeAsync();

            await base.DisposeAsync();
        }

        protected virtual bool SerializeEvent(AnalyticsEventEnvelope ev, JsonWriter writer)
        {
            JsonSerialization.AnalyticsEventSerializer.Serialize(writer, ev);
            return true;
        }

        public override void EnqueueBatches(List<AnalyticsEventBatch> batches)
        {
            // If no currently buffered events, advance _oldestEventAt
            if (_numBufferedEvents == 0)
                _oldestEventAt = MetaTime.Now;

            // Serialize all events into the buffer as JSON
            for (int batchNdx = 0; batchNdx < batches.Count; batchNdx++)
            {
                AnalyticsEventBatch batch = batches[batchNdx];

                // If no active memory stream, allocate one & initialize writer
                if (_activeMemoryStream == null)
                {
                    // Try to initialize writer (or if unable to, drop the remaining batches)
                    if (TryInitializeWriter())
                        _log.Information("Allocated new chunk buffer for writing");
                    else
                    {
                        int numRemainingBatches = batches.Count - batchNdx;
                        int numRemainingEvents = batches.Skip(batchNdx).Sum(batch => batch.Count);

                        _log.Warning("Unable to allocate chunk buffer, dropping {NumEvents} events in {NumBatches} batches", numRemainingEvents, numRemainingBatches);
                        _metrics.BatchesDropped.Inc(numRemainingBatches);
                        _metrics.EventsDropped.Inc(numRemainingEvents);

                        // Bail out, because no place to write to!
                        return;
                    }
                }

                // Serialize all events in the batch as JSON
                int numEventsSerialized = 0;
                for (int ndx = 0; ndx < batch.Count; ndx++)
                {
                    if (SerializeEvent(batch.Events[ndx], _jsonWriter))
                    {
                        _textWriter.Write("\n");
                        numEventsSerialized++;
                    }
                }

                // Statistics
                _numBufferedEvents += numEventsSerialized;
                _numBufferedBatches += 1;

                // Check if should flush
                if (_numBufferedEvents >= _opts.EventsPerChunk)
                    FlushChunk();
            }

            // Check if should flush based on max pending time
            if (MetaTime.Now - _oldestEventAt >= MetaDuration.FromTimeSpan(_opts.MaxPendingDuration))
            {
                _log.Information("Time-based flush of {NumEvents} events", _numBufferedEvents);
                FlushChunk();
            }
        }

        public void FlushChunk()
        {
            // Nothing to flush, keep going
            if (_numBufferedEvents == 0 || _activeMemoryStream == null)
                return;

            // Ensure all bytes have been written
            FlushWriter();

            // Resolve file name from template
            string filePath = _opts.RenderFilePath(_hostName, DateTime.UtcNow, _rng.NextUInt());
            int numEventsInBuffer = _numBufferedEvents;
            _log.Information("Flushing chunk of {NumEvents} events ({TotalBytes} bytes, in {NumBatches} batches) as {FilePath}", _numBufferedEvents, _activeMemoryStream.Position, _numBufferedBatches, filePath);

            // Flush the final chunk
            _chunkBufferManager.ReleaseAndFlush(_activeMemoryStream, async (byte[] bytes) =>
            {
                // Retry a few times in case of failure
                const int NumRetries = 10;
                for (int retryNdx = 0; retryNdx < NumRetries; retryNdx++)
                {
                    try
                    {
                        Stopwatch sw = Stopwatch.StartNew();

                        // Write to blob storage
                        await _blobStorage.PutAsync(filePath, bytes, hintsMaybe: null).ConfigureAwait(false);

                        // Success, we're done
                        _metrics.ChunksFlushed.Inc();
                        _metrics.EventsFlushed.Inc(numEventsInBuffer);
                        _metrics.BatchWriteDuration.Observe(sw.Elapsed.TotalSeconds);
                        _metrics.NumBytesWritten.Inc(bytes.Length);
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (retryNdx == NumRetries - 1)
                        {
                            _log.Error("Failed to flush chunk, giving up: {Exception}", ex);
                            // \todo [petri] write to disk or some other backup location?

                            _metrics.ChunksDropped.Inc();
                            _metrics.EventsDropped.Inc(numEventsInBuffer);
                        }
                        else
                        {
                            _log.Warning("Failed to flush chunk, retrying in a bit: {Exception}", ex);
                            await Task.Delay(1_000);
                        }
                    }
                }

                // unreachable
            });

            // Forget chunk buffer (allocate new one later)
            _activeMemoryStream = null;

            // Reset stats
            _oldestEventAt = MetaTime.Now;
            _numBufferedEvents = 0;
            _numBufferedBatches = 0;
        }
    }
}
