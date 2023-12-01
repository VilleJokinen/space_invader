// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Serilog.Core;
using Serilog.Events;

namespace Metaplay.Cloud.Application
{
    /// <summary>
    /// Sink which when enabled terminates the application on error. The sink should be chained after real sinks.
    /// </summary>
    class MetaplayOnErrorTerminateSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            // Check if should terminate process on error
            if (logEvent.Level >= LogEventLevel.Error)
            {
                EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance?.GetCurrent<EnvironmentOptions>();
                if (envOpts?.ExitOnLogError ?? false)
                    Application.ForceTerminate(51, "Log error occurred, exiting..");
            }
        }
    }
}
