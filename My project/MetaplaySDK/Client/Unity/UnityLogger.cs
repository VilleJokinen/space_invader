// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Metaplay.Unity
{
    /// <summary>
    /// Implementation of <see cref="IMetaLogger"/> which forwards the log messages
    /// to Unity's logger. Allows specifying colors to use and overriding log levels for
    /// each log channel.
    /// </summary>
    public class UnityLogger : MetaLoggerBase
    {
        Dictionary<LogLevel, string> _logLevelColors;

        static Dictionary<LogLevel, Action<string>> _logFuncs = new Dictionary<LogLevel, Action<string>>
        {
            { LogLevel.Error, Debug.LogError },
            { LogLevel.Warning, Debug.LogWarning },
            { LogLevel.Information, Debug.Log },
            { LogLevel.Debug, Debug.Log },
            { LogLevel.Verbose, Debug.Log },
        };

        public UnityLogger()
        {
            // Enable colors only in editor
#if UNITY_EDITOR
            bool isProSkin = EditorGUIUtility.isProSkin;
            _logLevelColors = new Dictionary<LogLevel, string>
            {
                { LogLevel.Error, "red" },
                { LogLevel.Warning, "yellow" },
                { LogLevel.Information, null },
                { LogLevel.Debug, isProSkin ? "#a0a0ff" : "blue" },
                { LogLevel.Verbose, "gray" }
            };
#else
            _logLevelColors = new Dictionary<LogLevel, string>
            {
                { LogLevel.Error, null },
                { LogLevel.Warning, null },
                { LogLevel.Information, null },
                { LogLevel.Debug, null },
                { LogLevel.Verbose, null }
            };
#endif
        }

        // Log levels are handled in MetaplayLogs, to allow specifying lower overrides for specific channels.
        public override sealed bool IsLevelEnabled(LogLevel level) => true;

        public override sealed void LogEvent(LogLevel level, Exception ex, string format, params object[] args)
        {
            string str = LogTemplateFormatter.ToFlatString(format, args);
            string color = _logLevelColors[level];
            if (color != null)
                str = WrapWithColor(str, color);
            _logFuncs[level](str);

            if (ex != null)
                Debug.LogException(ex);
        }

        static string WrapWithColor(string str, string color)
        {
            // Only color first line, because Unity only supports per-line colors
            int eolNdx = str.IndexOf("\n", StringComparison.Ordinal);
            if (eolNdx != -1)
                return $"<color={color}>{str.Substring(0, eolNdx)}</color>\n{str.Substring(eolNdx + 1)}";
            else
                return $"<color={color}>{str}</color>";
        }
    }
}
