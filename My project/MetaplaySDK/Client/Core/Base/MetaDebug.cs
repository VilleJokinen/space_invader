// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Globalization;

namespace Metaplay.Core
{
    public class MetaAssertException : Exception
    {
        public string Format { get; }
        public object[] Args { get; }

        public MetaAssertException(string format, params object[] args)
            : base(FormatMessage(format, args))
        {
            Format = format;
            Args = args;
        }

        public override string ToString()
        {
            return "MetaAssertException: " + FormatMessage(Format, Args);
        }

        static string FormatMessage(string format, params object[] args)
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch
            {
                return "<exception formatting error>";
            }
        }
    }

    /// <summary>
    /// Logging vehicle for debugging purposes only. Only use for short-term ad hoc debugging.
    /// Don't use this in production.
    ///
    /// Works both on client and server, but does not track the source (eg, actor or class) of
    /// log events.
    /// </summary>
    public static class DebugLog
    {
        static void Log(LogLevel level, string format, params object[] args)
        {
            string message = $"<<<{level}>>> {LogTemplateFormatter.ToFlatString(format, args)}";

#if UNITY_2017_1_OR_NEWER
            switch (level)
            {
                // \note UnityEngine.Debug.Log* doesn't have Verbose and Debug variants - using the basic Log for those.
                case LogLevel.Verbose:      UnityEngine.Debug.Log(message);         break;
                case LogLevel.Debug:        UnityEngine.Debug.Log(message);         break;
                case LogLevel.Information:  UnityEngine.Debug.Log(message);         break;
                case LogLevel.Warning:      UnityEngine.Debug.LogWarning(message);  break;
                case LogLevel.Error:        UnityEngine.Debug.LogError(message);    break;
                // \note default case (shouldn't happen) uses Error level to make sure it is visible.
                default:                    UnityEngine.Debug.LogError(message);    break;
            }
#else
            Console.WriteLine(message);
#endif
        }

        public static void Verbose(string format, params object[] args) => Log(LogLevel.Verbose, format, args);
        public static void Debug(string format, params object[] args) => Log(LogLevel.Debug, format, args);
        public static void Info(string format, params object[] args) => Log(LogLevel.Information, format, args);
        public static void Information(string format, params object[] args) => Log(LogLevel.Information, format, args);
        public static void Warning(string format, params object[] args) => Log(LogLevel.Warning, format, args);
        public static void Error(string format, params object[] args) => Log(LogLevel.Error, format, args);
    }

    /// <summary>
    /// Provides set of Assert() functions to help debug logic code.
    /// </summary>
    public static class MetaDebug
    {
        static object[] s_emptyArgs = new object[] { };

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Assert(bool condition, string format)
        {
            if (!condition)
                AssertFail(format, s_emptyArgs);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Assert<T0>(bool condition, string format, T0 arg0)
        {
            if (!condition)
                AssertFail(format, new object[] { arg0 });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Assert<T0, T1>(bool condition, string format, T0 arg0, T1 arg1)
        {
            if (!condition)
                AssertFail(format, new object[] { arg0, arg1 });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Assert<T0, T1, T2>(bool condition, string format, T0 arg0, T1 arg1, T2 arg2)
        {
            if (!condition)
                AssertFail(format, new object[] { arg0, arg1, arg2 });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Assert<T0, T1, T2, T3>(bool condition, string format, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
        {
            if (!condition)
                AssertFail(format, new object[] { arg0, arg1, arg2, arg3 });
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void AssertFail(string format, params object[] args)
        {
            // \todo [petri] pass down assert failure handler from application?

#if NETCOREAPP // cloud
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, $"Assertion failed: {format}", args));
#endif

#if UNITY_2017_1_OR_NEWER
            UnityEngine.Debug.AssertFormat(false, format, args);
#else
            // \todo [petri] enable hard asserts in local server builds?
            //System.Diagnostics.Debug.Fail(string.Format(format, args));
#endif
            throw new MetaAssertException(format, args);
        }
    }
}
