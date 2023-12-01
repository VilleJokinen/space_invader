// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Analytics
{
    [RuntimeOptions("AnalyticsSinkBigQuery", isStatic: true, "Configuration options for the BigQuery analytics sink.")]
    public class AnalyticsSinkBigQueryOptions : RuntimeOptionsBase
    {
        [MetaDescription("Enables the BigQuery analytics sink.")]
        public bool                     Enabled                         { get; private set; }
        [MetaDescription("The maximum duration that an event is held before flushing it out.")]
        public TimeSpan                 MaxPendingDuration              { get; private set; } = TimeSpan.FromMinutes(5);
        [MetaDescription("The maximum number of events per chunk.")]
        public int                      EventsPerChunk                  { get; private set; } = 100;
        [MetaDescription("The number of chunk buffers to use for uploading. If there are no empty buffers due to uploads failing or falling behind, further events are dropped.")]
        public int                      NumChunkBuffers                 { get; private set; } = 10;

        [MetaDescription("Enables the native BigQuery row de-duplication based on `insertId`. When disabled, the data processor should de-duplicate events based on the `event_id` column.")]
        public bool                     BigQueryEnableRowDeduplication  { get; private set; } = true;
        [MetaDescription("The project ID of the destination BigQuery table.")]
        public string                   BigQueryProjectId               { get; private set; }
        [MetaDescription("The dataset ID of the destination BigQuery table.")]
        public string                   BigQueryDatasetId               { get; private set; }
        [MetaDescription("The ID of the destination BigQuery table.")]
        public string                   BigQueryTableId                 { get; private set; }

        [Sensitive]
        [MetaDescription("The AWS secrets manager path or a file path to the credentials for accessing the BigQuery table.")]
        public string                   BigQueryCredentialsJsonPath     { get; private set; }

        [Sensitive, IgnoreDataMember]
        [MetaDescription("The access credentials to the BigQuery table.")]
        public string                   BigQueryCredentialsJson         { get; private set; }

        public override async Task OnLoadedAsync()
        {
            if (Enabled)
            {
                if (string.IsNullOrEmpty(BigQueryProjectId))
                    throw new InvalidOperationException("BigQueryProjectId must be set");
                if (string.IsNullOrEmpty(BigQueryDatasetId))
                    throw new InvalidOperationException("BigQueryDatasetId must be set");
                if (string.IsNullOrEmpty(BigQueryTableId))
                    throw new InvalidOperationException("BigQueryTableId must be set");
                if (string.IsNullOrEmpty(BigQueryCredentialsJsonPath))
                    throw new InvalidOperationException("BigQueryCredentialsJsonPath must be set");
                if (NumChunkBuffers <= 0)
                    throw new InvalidOperationException("NumChunkBuffers must be greater than 0");
                if (EventsPerChunk <= 0)
                    throw new InvalidOperationException("EventsPerChunk must be greater than 0");

                BigQueryCredentialsJson = await SecretUtil.ResolveSecretAsync(Log, BigQueryCredentialsJsonPath).ConfigureAwait(false);

                if (string.IsNullOrEmpty(BigQueryCredentialsJson))
                    throw new InvalidOperationException("Could not resolve credentials");
            }
        }
    }
}
