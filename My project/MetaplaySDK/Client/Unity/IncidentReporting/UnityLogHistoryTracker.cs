// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Debugging;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Metaplay.Unity.IncidentReporting
{
    /// <summary>
    /// Maintains a history of log messages.
    /// </summary>
    public class UnityLogHistoryTracker : IDisposable
    {
        const int                    LogHistorySize             = 200;
        bool                        _started                    = false;
        object                      _logRingBufferLock          = new object();
        Queue<ClientLogEntry>       _logRingBuffer              = new Queue<ClientLogEntry>(LogHistorySize + 1);

        public delegate void OnExceptionLogEntryDelegate(string logString, string stackTrace, List<ClientLogEntry> logHistory);

        /// <summary>
        /// Invoked when Exception is logged. May be called from any thread.
        /// </summary>
        public event OnExceptionLogEntryDelegate OnExceptionLogEntry;

        public UnityLogHistoryTracker()
        {
        }

        public void Start()
        {
            if (_started)
                return;

            _started = true;
            Application.logMessageReceivedThreaded += HandleLog;
        }

        public void Dispose()
        {
            if (!_started)
                return;

            _started = false;
            Application.logMessageReceivedThreaded -= HandleLog;
        }

        public List<ClientLogEntry> GetLogHistory()
        {
            List<ClientLogEntry> entries;

            // Get current log buffer.

            lock (_logRingBufferLock)
            {
                entries = new List<ClientLogEntry>(_logRingBuffer);
            }

            // Possibly truncate the log to not exceed PlayerUploadIncidentReport.MaxLogEntriesTotalUtf8Size.
            // The purpose of this is to prefer truncating the log instead of exceeding MaxUncompressedPayloadSize
            // and thus dropping the whole incident report.
            //
            // Starting from the most recent entry, find out how many entries we can keep
            // without the total length of the entries exceeding a limit. Then if needed,
            // remove some of the oldest entries.

            int entriesTotalLength = 0;
            for (int i = entries.Count-1; i >= 0; i--)
            {
                ClientLogEntry entry = entries[i];

                if (entry.Message != null)
                    entriesTotalLength += Encoding.UTF8.GetByteCount(entry.Message);
                if (entry.StackTrace != null)
                    entriesTotalLength += Encoding.UTF8.GetByteCount(entry.StackTrace);

                if (entriesTotalLength > PlayerUploadIncidentReport.MaxLogEntriesTotalUtf8Size)
                {
                    entries.RemoveRange(index: 0, count: i+1);
                    break;
                }
            }

            return entries;
        }

        static ClientLogEntryType MapLogType(LogType logType)
        {
            switch (logType)
            {
                case LogType.Error:     return ClientLogEntryType.Error;
                case LogType.Assert:    return ClientLogEntryType.Assert;
                case LogType.Warning:   return ClientLogEntryType.Warning;
                case LogType.Log:       return ClientLogEntryType.Log;
                case LogType.Exception: return ClientLogEntryType.Exception;
                default:                return ClientLogEntryType.Log;
            }
        }

        void HandleLog(string logString, string stackTrace, LogType type)
        {
            // Append entry to ring buffer
            bool                    includeStackTrace   = (type == LogType.Exception || type == LogType.Error);
            ClientLogEntry          entry               = new ClientLogEntry(MetaTime.Now, MapLogType(type), logString, includeStackTrace ? stackTrace : null);
            bool                    reportException     = false;
            List<ClientLogEntry>    exceptionLogs       = null;

            lock (_logRingBufferLock)
            {
                _logRingBuffer.Enqueue(entry);
                if (_logRingBuffer.Count > LogHistorySize)
                    _logRingBuffer.Dequeue();

                if (type == LogType.Exception)
                {
                    reportException = true;
                    exceptionLogs = new List<ClientLogEntry>(_logRingBuffer);
                }
            }

            // For exceptions, trigger event to allow immediate incident reporting
            if (reportException)
                OnExceptionLogEntry?.Invoke(logString, stackTrace, exceptionLogs);
        }
    }
}
