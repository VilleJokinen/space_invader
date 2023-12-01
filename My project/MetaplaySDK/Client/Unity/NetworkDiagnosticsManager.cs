// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Metaplay.Core.Network;
using Metaplay.Core.Tasks;

namespace Metaplay.Unity
{
    public class NetworkDiagnosticsManager
    {
        public NetworkDiagnosticReport LastNetworkDiagnosticReport { get; private set; } = null;

        Task _reportTask;

        public void StartNewReport(string gameServerHost4, string gameServerHost6, List<int> gameServerPorts, bool gameServerUseTls, string cdnHostname4, string cdnHostname6, TimeSpan? timeout = null, Action<NetworkDiagnosticReport> callback = null)
        {
            CancelLastReport();

            UnityEngine.Debug.Log($"Starting new network diagnostics report with game4={gameServerHost4}, game6={gameServerHost6}, ports={string.Join(",", gameServerPorts)}, tls={gameServerUseTls}, cdn4={cdnHostname4}, cdn6={cdnHostname6}");

            (NetworkDiagnosticReport report, Task reportTask) = NetworkDiagnostics.GenerateReport(
                gameServerHost4,
                gameServerHost6,
                gameServerPorts,
                gameServerUseTls,
                cdnHostname4,
                cdnHostname6,
                timeout: TimeSpan.FromSeconds(5));

            if (callback != null)
                reportTask.ContinueWithCtx(_ => callback(report));

            _reportTask = reportTask;
            LastNetworkDiagnosticReport = report;
            // TODO: upload report after it's done?
        }

        public void CancelLastReport()
        {
            if (_reportTask != null && !_reportTask.IsCompleted)
            {
                // \todo [petri] Cannot Dispose() without canceling and awaiting first, fix with proper cancellation semantics?
                //_reportTask.Dispose();
                _reportTask = null;
                LastNetworkDiagnosticReport = null;
            }
        }
    }
}
