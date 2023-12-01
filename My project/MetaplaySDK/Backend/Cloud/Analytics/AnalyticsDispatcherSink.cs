// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Analytics
{
    /// <summary>
    /// Base class for an analytics event sink. This class can be extended to implement sinks for writing analytics
    /// events into custom targets (eg, BigQuery, S3, Kinesis, etc.) using custom formats (JSON, Parquet, etc.)
    /// </summary>
    public abstract class AnalyticsDispatcherSinkBase : IAsyncDisposable
    {
        protected readonly IMetaLogger _log;

        protected AnalyticsDispatcherSinkBase()
        {
            _log = MetaLogger.ForContext(GetType());
        }

        protected AnalyticsDispatcherSinkBase(IMetaLogger log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public abstract void EnqueueBatches(List<AnalyticsEventBatch> batches);
    }

    public class AnalyticsDispatcherSinkFactory : IMetaIntegrationSingleton<AnalyticsDispatcherSinkFactory>
    {
        public virtual async Task<IEnumerable<AnalyticsDispatcherSinkBase>> CreateSinksAsync()
        {
            List<AnalyticsDispatcherSinkBase> activeSinks = new List<AnalyticsDispatcherSinkBase>();
            if (AnalyticsDispatcherSinkJsonBlobStorage.TryCreate() is { } jsonBlobSink)
                activeSinks.Add(jsonBlobSink);
            if (await AnalyticsDispatcherSinkBigQuery.TryCreateAsync() is { } bigQuerySink)
                activeSinks.Add(bigQuerySink);
            if (AnalyticsDispatcherSinkSerilog.TryCreate() is { } serilogSink)
                activeSinks.Add(serilogSink);
            return activeSinks;
        }
    }
}
