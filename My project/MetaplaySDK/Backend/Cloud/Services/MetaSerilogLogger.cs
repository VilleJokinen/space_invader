// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Application;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Prometheus;
using Serilog.Core;
using Serilog.Events;
using System;

namespace Metaplay.Cloud
{
    /// <summary>
    /// Methods for creating <see cref="IMetaLogger"/>s. Container for log-level switches that can be
    /// dynamically changed.
    /// </summary>
    public static class MetaLogger
    {
        static readonly Counter         c_logEvents         = Prometheus.Metrics.CreateCounter("game_log_events_total", "Total number of log events (per level)", "level");
        static readonly Counter.Child   c_logVerbose        = c_logEvents.WithLabels("verbose");
        static readonly Counter.Child   c_logDebug          = c_logEvents.WithLabels("debug");
        static readonly Counter.Child   c_logInformation    = c_logEvents.WithLabels("information");
        static readonly Counter.Child   c_logWarning        = c_logEvents.WithLabels("warning");
        static readonly Counter.Child   c_logError          = c_logEvents.WithLabels("error");

        public static LoggingLevelSwitch SerilogLogLevelSwitch  = new LoggingLevelSwitch(LogEventLevel.Debug);
        public static MetaLogLevelSwitch MetaLogLevelSwitch     = new MetaLogLevelSwitch(LogLevel.Debug);

        /// <summary>
        /// Change the global active minimum log level. This is generally respected throughout
        /// the server-side logging systems, both Serilog and the various <code>IMetaLogger</code>s.
        /// </summary>
        /// <param name="level">New minimum log level to use</param>
        public static void SetLoggingLevel(LogLevel level)
        {
            // Update both Serilog & Metaplay log level switches
            SerilogLogLevelSwitch.MinimumLevel = MetaplayLevelToSerilog(level);
            MetaLogLevelSwitch.MinimumLevel = level;
        }

        static LogEventLevel MetaplayLevelToSerilog(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Verbose:     return LogEventLevel.Verbose;
                case LogLevel.Debug:       return LogEventLevel.Debug;
                case LogLevel.Information: return LogEventLevel.Information;
                case LogLevel.Warning:     return LogEventLevel.Warning;
                case LogLevel.Error:       return LogEventLevel.Error;
                default:
                    throw new ArgumentException($"Invalid LogLevel {level}", nameof(level));
            }
        }

        static LogLevel SerilogLevelToMetaplay(LogEventLevel level)
        {
            switch (level)
            {
                case LogEventLevel.Verbose:     return LogLevel.Verbose;
                case LogEventLevel.Debug:       return LogLevel.Debug;
                case LogEventLevel.Information: return LogLevel.Information;
                case LogEventLevel.Warning:     return LogLevel.Warning;
                case LogEventLevel.Error:       return LogLevel.Error;
                default:
                    throw new ArgumentException($"Invalid LogEventLevel {level}", nameof(level));
            }
        }

        public static void IncLevelCounter(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Verbose:      c_logVerbose.Inc();     return;
                case LogLevel.Debug:        c_logDebug.Inc();       return;
                case LogLevel.Information:  c_logInformation.Inc(); return;
                case LogLevel.Warning:      c_logWarning.Inc();     return;
                case LogLevel.Error:        c_logError.Inc();       return;
                default:
                    throw new ArgumentException("Invalid LogLevel {level}", nameof(level));
            }
        }

        /// <summary>
        /// Create an <see cref="IMetaLogger"/> for a named log source. The source name
        /// will be shown in all logged events.
        /// </summary>
        /// <param name="logSource">Name of the source for the logs</param>
        /// <returns>An instance of IMetaLogger</returns>
        public static IMetaLogger ForContext(string logSource)
        {
            return new MetaSerilogLogger(logSource);
        }

        /// <summary>
        /// Create an <see cref="IMetaLogger"/> for a log source whose class name is used
        /// as the log source name. The source name will be shown in all logged events.
        /// </summary>
        /// <returns>An instance of IMetaLogger</returns>
        public static IMetaLogger ForContext(Type type)
        {
            return new MetaSerilogLogger(type.Name);
        }

        /// <summary>
        /// Create an <see cref="IMetaLogger"/> for a log source whose class name is used
        /// as the log source name. The source name will be shown in all logged events.
        /// </summary>
        /// <returns>An instance of IMetaLogger</returns>
        public static IMetaLogger ForContext<T>()
        {
            return new MetaSerilogLogger(typeof(T).Name);
        }
    }

    /// <summary>
    /// An implementation of <see cref="IMetaLogger"/> that forwards all logging to Serilog and collects metrics.
    /// </summary>
    public sealed class MetaSerilogLogger : MetaLoggerBase
    {
        readonly Serilog.ILogger _serilog;

        public MetaSerilogLogger(string logSource)
        {
            _serilog = Serilog.Log.ForContext(Constants.SourceContextPropertyName, logSource);
        }

        public static LogEventLevel LogLevelToSerilog(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Verbose:      return LogEventLevel.Verbose;
                case LogLevel.Debug:        return LogEventLevel.Debug;
                case LogLevel.Information:  return LogEventLevel.Information;
                case LogLevel.Warning:      return LogEventLevel.Warning;
                case LogLevel.Error:        return LogEventLevel.Error;
                default:
                    throw new ArgumentException($"Invalid LogLevel {level}", nameof(level));
            }
        }

        public override sealed bool IsLevelEnabled(LogLevel level) =>
            level >= MetaLogger.MetaLogLevelSwitch.MinimumLevel;

        public override sealed void LogEvent(LogLevel level, Exception ex, string format, params object[] args)
        {
            // Log using Serilog
            _serilog.Write(LogLevelToSerilog(level), ex, format, args);

            // Increment metrics
            MetaLogger.IncLevelCounter(level);
        }
    }
}
