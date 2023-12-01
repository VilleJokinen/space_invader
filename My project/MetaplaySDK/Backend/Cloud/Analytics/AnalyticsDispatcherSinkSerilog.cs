// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Json;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Text;
using static System.FormattableString;

namespace Metaplay.Cloud.Analytics
{
    /// <summary>
    /// Log all events with Serilog. Only intended for debugging purposes, never enable in production!
    /// </summary>
    public class AnalyticsDispatcherSinkSerilog : AnalyticsDispatcherSinkBase
    {
        bool _jsonMode;

        public AnalyticsDispatcherSinkSerilog(bool jsonMode) : base()
        {
            _jsonMode = jsonMode;
        }

        public static AnalyticsDispatcherSinkSerilog TryCreate()
        {
            // Not enabled. To enable, comment out the next line. Note that this is intentionally
            // made inconvenient to discourage the use. This should not be used ever except in local
            // debugging.

            //return new AnalyticsDispatcherSinkSerilog(jsonMode: true);
            return null;
        }

        public override void EnqueueBatches(List<AnalyticsEventBatch> batches)
        {
            foreach (AnalyticsEventBatch batch in batches)
            {
                StringBuilder sb = new StringBuilder();
                for (int ndx = 0; ndx < batch.Count; ndx++)
                {
                    AnalyticsEventEnvelope ev = batch.Events[ndx];

                    if (!_jsonMode)
                    {
                        sb.AppendLine(Invariant($"  {ev.UniqueId} @{ev.ModelTime} v{ev.SchemaVersion}: {PrettyPrint.Compact(ev.Payload)}"));
                    }
                    else
                    {
                        using (StringWriter writer = new StringWriter(sb))
                        using (JsonTextWriter jsonTextWriter = new JsonTextWriter(writer))
                            JsonSerialization.Serialize(ev, JsonSerialization.AnalyticsEventSerializer, jsonTextWriter);
                        sb.AppendLine();
                    }
                }

                _log.Information("Batch of {NumEvents} analytics events from {SourceId}:\n{Payload}", batch.Count, batch.SourceId, sb.ToString());
            }
        }
    }
}
