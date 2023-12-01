// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// Detect usage of deprecated defines.
// \todo [petri] can remove in R24 or later
#if COMPILE_LOG_LEVEL_VERBOSE
#   error COMPILE_LOG_LEVEL_VERBOSE is deprecated, use METAPLAY_LOG_LEVEL_VERBOSE instead!
#endif
#if COMPILE_LOG_LEVEL_DEBUG
#   error COMPILE_LOG_LEVEL_DEBUG is deprecated, use METAPLAY_LOG_LEVEL_DEBUG instead!
#endif
#if COMPILE_LOG_LEVEL_INFO
#   error COMPILE_LOG_LEVEL_INFO is deprecated, use METAPLAY_LOG_LEVEL_INFORMATION instead!
#endif
#if COMPILE_LOG_LEVEL_WARNING
#   error COMPILE_LOG_LEVEL_WARNING is deprecated, use METAPLAY_LOG_LEVEL_WARNING instead!
#endif
#if COMPILE_LOG_LEVEL_ERROR
#   error COMPILE_LOG_LEVEL_ERROR is deprecated, use METAPLAY_LOG_LEVEL_ERROR instead!
#endif

// Compile-time define for which log levels are included in the build.
// Defaults to Verbose in Unity Editor, and Debug otherwise (server & client builds).
#if !METAPLAY_LOG_LEVEL_VERBOSE && !METAPLAY_LOG_LEVEL_DEBUG && !METAPLAY_LOG_LEVEL_INFORMATION && !METAPLAY_LOG_LEVEL_WARNING && !METAPLAY_LOG_LEVEL_ERROR
#   if UNITY_EDITOR
#       define METAPLAY_LOG_LEVEL_VERBOSE
#   else
#       define METAPLAY_LOG_LEVEL_DEBUG
#   endif
#endif

// Make sure all the levels higher than the requested are also defined
// \note Cannot use in [Conditional()] attributes because these are file-local!
#if METAPLAY_LOG_LEVEL_VERBOSE
#   define METAPLAY_LOG_LEVEL_DEBUG
#endif
#if METAPLAY_LOG_LEVEL_DEBUG
#   define METAPLAY_LOG_LEVEL_INFORMATION
#endif
#if METAPLAY_LOG_LEVEL_INFORMATION
#   define METAPLAY_LOG_LEVEL_WARNING
#endif
#if METAPLAY_LOG_LEVEL_WARNING
#   define METAPLAY_LOG_LEVEL_ERROR
#endif

using Metaplay.Core;
using System;

namespace Metaplay.Core
{
    /// <summary>
    /// Level of a log message.
    /// </summary>
    public enum LogLevel
    {
        Invalid,
        Verbose,
        Debug,
        Information,
        Warning,
        Error
    }

    /// <summary>
    /// Base interface for system-agnostic logging in Metaplay. On the server, the logging is typically
    /// routed to Serilog. On the client, it is routed to Unity's console via <code>UnityLogger</code>.
    /// </summary>
    public interface IMetaLogger
    {
        bool IsVerboseEnabled       { get; }
        bool IsDebugEnabled         { get; }
        bool IsInformationEnabled   { get; }
        bool IsWarningEnabled       { get; }
        bool IsErrorEnabled         { get; }

        bool IsLevelEnabled (LogLevel level);
        void LogEvent       (LogLevel level, Exception ex, string format, params object[] args);
    }

    /// <summary>
    /// Abstract base class that has the default implementations for the <code>IsLevelEnabled</code>
    /// properties.
    /// </summary>
    public abstract class MetaLoggerBase : IMetaLogger
    {
#if METAPLAY_LOG_LEVEL_VERBOSE
        public bool IsVerboseEnabled => IsLevelEnabled(LogLevel.Verbose);
#else
        public bool IsVerboseEnabled => false;
#endif

#if METAPLAY_LOG_LEVEL_DEBUG
        public bool IsDebugEnabled => IsLevelEnabled(LogLevel.Debug);
#else
        public bool IsDebugEnabled => false;
#endif

#if METAPLAY_LOG_LEVEL_INFORMATION
        public bool IsInformationEnabled => IsLevelEnabled(LogLevel.Information);
#else
        public bool IsInformationEnabled => false;
#endif

#if METAPLAY_LOG_LEVEL_WARNING
        public bool IsWarningEnabled => IsLevelEnabled(LogLevel.Warning);
#else
        public bool IsWarningEnabled => false;
#endif

#if METAPLAY_LOG_LEVEL_ERROR
        public bool IsErrorEnabled => IsLevelEnabled(LogLevel.Error);
#else
        public bool IsErrorEnabled => false;
#endif

        public abstract bool IsLevelEnabled (LogLevel level);
        public abstract void LogEvent       (LogLevel level, Exception ex, string format, params object[] args);
    }

    public class MetaLogLevelSwitch
    {
        volatile LogLevel _minimumLevel;

        public LogLevel MinimumLevel
        {
            get => _minimumLevel;
            set => _minimumLevel = value;
        }

        public MetaLogLevelSwitch(LogLevel level) =>
            _minimumLevel = level;
    }

    /// <summary>
    /// A null implementation of <see cref="IMetaLogger"/> that discards all log events.
    /// </summary>
    public class MetaNullLogger : MetaLoggerBase
    {
        public static MetaNullLogger Instance = new MetaNullLogger();

        public override sealed bool IsLevelEnabled(LogLevel level) => false;
        public override sealed void LogEvent(LogLevel level, Exception ex, string format, params object[] args) { }
    }

    /// <summary>
    /// Named logging channel. Named channels are used to differentiate between messages coming from different sources.
    /// Each channel can have a different logging level, allowing for easier debugging of given sub-systems without
    /// spamming the log overall.
    ///
    /// Log levels can also be disabled at compile time, by defining the <code>METAPLAY_LOG_LEVEL_&lt;LEVEL&gt;</code>
    /// macro (eg, <code>METAPLAY_LOG_LEVEL_DEBUG</code>). Any debug logging below that levels is excluded from the
    /// builds, and thus avoids any runtime cost associated with it.
    /// </summary>
    public class LogChannel : MetaLoggerBase
    {
        public string               Name            { get; private set; }
        public IMetaLogger          Logger          { get; private set; }
        public MetaLogLevelSwitch   ChannelLevel    { get; private set; }

        public static LogChannel Empty => new LogChannel("null", logger: MetaNullLogger.Instance, new MetaLogLevelSwitch(LogLevel.Error));

        public LogChannel(string name, IMetaLogger logger, MetaLogLevelSwitch channelLevel)
        {
            if (name.Contains("{") || name.Contains("}"))
                throw new ArgumentException("Log channel names cannot contain braces!", nameof(name));

            Name = name ?? throw new ArgumentNullException(nameof(name));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            ChannelLevel = channelLevel ?? throw new ArgumentNullException(nameof(channelLevel));
        }

        public override sealed bool IsLevelEnabled(LogLevel level) =>
            level >= ChannelLevel.MinimumLevel && Logger.IsLevelEnabled(level);

        public override sealed void LogEvent(LogLevel level, Exception ex, string format, params object[] args)
        {
            LogLevel minLevel = ChannelLevel.MinimumLevel;
            bool isInitialized = minLevel != LogLevel.Invalid; // Invalid means log levels are not initialized
            if (level < minLevel)
                return;

#if UNITY_EDITOR
            // In Unity Editor, bold the channel name
            if (isInitialized)
                Logger.LogEvent(level, ex, $"<b>[{Name}]</b> {format}", args);
            else
                Logger.LogEvent(level, ex, $"<b>[{Name}/uninitialized]</b> {format}", args);
#else
            if (isInitialized)
                Logger.LogEvent(level, ex, $"[{Name}] {format}", args);
            else
                Logger.LogEvent(level, ex, $"[{Name}/uninitialized] {format}", args);
#endif
        }
    }
}

#pragma warning disable CA1050 // Declare types in namespaces
/// <summary>
/// Logging extension methods. In global namespace to avoid having to require <code>using Metaplay.Core</code> everywhere.
/// </summary>
public static class MetaLoggerExtensions
{
    // Verbose

#if !METAPLAY_LOG_LEVEL_VERBOSE
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Verbose(this IMetaLogger logger, string format)
    {
        if (logger.IsVerboseEnabled)
            logger.LogEvent(LogLevel.Verbose, ex: null, format, Array.Empty<object>());
    }

#if !METAPLAY_LOG_LEVEL_VERBOSE
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Verbose<T0>(this IMetaLogger logger, string format, T0 arg0)
    {
        if (logger.IsVerboseEnabled)
            logger.LogEvent(LogLevel.Verbose, ex: null, format, new object[] { arg0 });
    }

#if !METAPLAY_LOG_LEVEL_VERBOSE
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Verbose<T0, T1>(this IMetaLogger logger, string format, T0 arg0, T1 arg1)
    {
        if (logger.IsVerboseEnabled)
            logger.LogEvent(LogLevel.Verbose, ex: null, format, new object[] { arg0, arg1 });
    }

#if !METAPLAY_LOG_LEVEL_VERBOSE
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Verbose<T0, T1, T2>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2)
    {
        if (logger.IsVerboseEnabled)
            logger.LogEvent(LogLevel.Verbose, ex: null, format, new object[] { arg0, arg1, arg2 });
    }

#if !METAPLAY_LOG_LEVEL_VERBOSE
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Verbose<T0, T1, T2, T3>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        if (logger.IsVerboseEnabled)
            logger.LogEvent(LogLevel.Verbose, ex: null, format, new object[] { arg0, arg1, arg2, arg3 });
    }

#if !METAPLAY_LOG_LEVEL_VERBOSE
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Verbose<T0, T1, T2, T3, T4>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        if (logger.IsVerboseEnabled)
            logger.LogEvent(LogLevel.Verbose, ex: null, format, new object[] { arg0, arg1, arg2, arg3, arg4 });
    }

#if !METAPLAY_LOG_LEVEL_VERBOSE
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Verbose<T0, T1, T2, T3, T4, T5>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
    {
        if (logger.IsVerboseEnabled)
            logger.LogEvent(LogLevel.Verbose, ex: null, format, new object[] { arg0, arg1, arg2, arg3, arg4, arg5 });
    }

#if !METAPLAY_LOG_LEVEL_VERBOSE
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Verbose(this IMetaLogger logger, string format, params object[] args)
    {
        if (logger.IsVerboseEnabled)
            logger.LogEvent(LogLevel.Verbose, ex: null, format, args);
    }

    // Debug

#if !METAPLAY_LOG_LEVEL_DEBUG
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Debug(this IMetaLogger logger, string format)
    {
        if (logger.IsDebugEnabled)
            logger.LogEvent(LogLevel.Debug, ex: null, format, Array.Empty<object>());
    }

#if !METAPLAY_LOG_LEVEL_DEBUG
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Debug<T0>(this IMetaLogger logger, string format, T0 arg0)
    {
        if (logger.IsDebugEnabled)
            logger.LogEvent(LogLevel.Debug, ex: null, format, new object[] { arg0 });
    }

#if !METAPLAY_LOG_LEVEL_DEBUG
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Debug<T0, T1>(this IMetaLogger logger, string format, T0 arg0, T1 arg1)
    {
        if (logger.IsDebugEnabled)
            logger.LogEvent(LogLevel.Debug, ex: null, format, new object[] { arg0, arg1 });
    }

#if !METAPLAY_LOG_LEVEL_DEBUG
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Debug<T0, T1, T2>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2)
    {
        if (logger.IsDebugEnabled)
            logger.LogEvent(LogLevel.Debug, ex: null, format, new object[] { arg0, arg1, arg2 });
    }

#if !METAPLAY_LOG_LEVEL_DEBUG
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Debug<T0, T1, T2, T3>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        if (logger.IsDebugEnabled)
            logger.LogEvent(LogLevel.Debug, ex: null, format, new object[] { arg0, arg1, arg2, arg3 });
    }

#if !METAPLAY_LOG_LEVEL_DEBUG
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Debug<T0, T1, T2, T3, T4>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        if (logger.IsDebugEnabled)
            logger.LogEvent(LogLevel.Debug, ex: null, format, new object[] { arg0, arg1, arg2, arg3, arg4 });
    }

#if !METAPLAY_LOG_LEVEL_DEBUG
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Debug<T0, T1, T2, T3, T4, T5>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
    {
        if (logger.IsDebugEnabled)
            logger.LogEvent(LogLevel.Debug, ex: null, format, new object[] { arg0, arg1, arg2, arg3, arg4, arg5 });
    }

#if !METAPLAY_LOG_LEVEL_DEBUG
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Debug(this IMetaLogger logger, string format, params object[] args)
    {
        if (logger.IsDebugEnabled)
            logger.LogEvent(LogLevel.Debug, ex: null, format, args);
    }

    // Info (supported for backward-compatibility)

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Info(this IMetaLogger logger, string format)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, Array.Empty<object>());
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Info<T0>(this IMetaLogger logger, string format, T0 arg0)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Info<T0, T1>(this IMetaLogger logger, string format, T0 arg0, T1 arg1)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0, arg1 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Info<T0, T1, T2>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0, arg1, arg2 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Info<T0, T1, T2, T3>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0, arg1, arg2, arg3 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Info<T0, T1, T2, T3, T4>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0, arg1, arg2, arg3, arg4 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Info<T0, T1, T2, T3, T4, T5>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0, arg1, arg2, arg3, arg4, arg5 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Info(this IMetaLogger logger, string format, params object[] args)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, args);
    }

    // Information

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Information(this IMetaLogger logger, string format)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, Array.Empty<object>());
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Information<T0>(this IMetaLogger logger, string format, T0 arg0)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Information<T0, T1>(this IMetaLogger logger, string format, T0 arg0, T1 arg1)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0, arg1 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Information<T0, T1, T2>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0, arg1, arg2 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Information<T0, T1, T2, T3>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0, arg1, arg2, arg3 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Information<T0, T1, T2, T3, T4>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0, arg1, arg2, arg3, arg4 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Information<T0, T1, T2, T3, T4, T5>(this IMetaLogger logger, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, new object[] { arg0, arg1, arg2, arg3, arg4, arg5 });
    }

#if !METAPLAY_LOG_LEVEL_INFORMATION
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Information(this IMetaLogger logger, string format, params object[] args)
    {
        if (logger.IsInformationEnabled)
            logger.LogEvent(LogLevel.Information, ex: null, format, args);
    }

    // Warning

#if !METAPLAY_LOG_LEVEL_WARNING
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Warning(this IMetaLogger logger, string format, params object[] args)
    {
        if (logger.IsWarningEnabled)
            logger.LogEvent(LogLevel.Warning, ex: null, format, args);
    }

    // Error

#if !METAPLAY_LOG_LEVEL_ERROR
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Error(this IMetaLogger logger, string format, params object[] args)
    {
        if (logger.IsErrorEnabled)
            logger.LogEvent(LogLevel.Error, ex: null, format, args);
    }

#if !METAPLAY_LOG_LEVEL_ERROR
    [System.Diagnostics.Conditional("DISABLE")]
#endif
    public static void Error(this IMetaLogger logger, Exception ex, string format, params object[] args)
    {
        if (logger.IsErrorEnabled)
            logger.LogEvent(LogLevel.Error, ex, format, args);
    }
}
#pragma warning restore CA1050
