// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Client.Messages;
using Metaplay.Core;
using Metaplay.Core.Debugging;
using Metaplay.Core.Message;
using Metaplay.Core.Network;
using Metaplay.Core.Player;
using Metaplay.Core.Session;
using Metaplay.Unity.ConnectionStates;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Unity.IncidentReporting
{
    /// <summary>
    /// Client-side incident reporting service. Keeps rotating buffer of latest Unity logs.
    /// </summary>
    public class IncidentTracker
    {
        LogChannel              _log;
        PeriodThrottle          _incidentThrottle;          // Throttle to avoid incident report spam

        UnitySystemInfo         _unitySystemInfo;
        UnityPlatformInfo       _unityPlatformInfo;
        IncidentGameConfigInfo  _latestGameConfigInfo;
        string                  _latestTlsPeerDescription;
        SessionToken            _sessionToken;
        bool                    _connectionErrorReportedForCurrentConnectionAttempt;

        UnityLogHistoryTracker  _logHistoryTracker;

        List<PlayerEventIncidentRecorded> _pendingAnalyticsEvents = new List<PlayerEventIncidentRecorded>();
        object _pendingAnalyticsEventsLock = new object();

        public IncidentTracker()
        {
            _log = MetaplaySDK.Logs.Incidents;
            _incidentThrottle = new PeriodThrottle(MetaDuration.FromSeconds(60), maxEventsPerDuration: 5, MetaTime.Now); // 5 events per minute
            _unitySystemInfo = UnitySystemInfo.Collect();
            _unityPlatformInfo = UnityPlatformInfo.Collect();

            _logHistoryTracker = new UnityLogHistoryTracker();
            _logHistoryTracker.OnExceptionLogEntry += OnExceptionLogEntry;
            _logHistoryTracker.Start();

            MetaplaySDK.MessageDispatcher.AddListener<ConnectedToServer>(OnConnectedToServer);
            MetaplaySDK.MessageDispatcher.AddListener<SessionProtocol.SessionStartSuccess>(OnSessionStartSuccess);
        }

        public void UpdateEarly()
        {
            // Update dynamically changing values of unitySystemInfo and unityPlatformInfo
            // \todo [petri] These can in theory be used while being updated, so should protect them with a lock to be extra-safe
            _unitySystemInfo.UpdateDynamic();
            _unityPlatformInfo.UpdateDynamic();
        }

        public void UpdateAfterConnection()
        {
            // Create an incident report for the issue
            if (MetaplaySDK.Connection.State is ConnectionStates.ErrorState errorState && IsReportableErrorState(errorState))
            {
                if (!_connectionErrorReportedForCurrentConnectionAttempt)
                {
                    _connectionErrorReportedForCurrentConnectionAttempt = true;

                    BeginReportNetworkError(errorState, MetaplaySDK.Connection.Endpoint, MetaplaySDK.Connection.LatestTlsPeerDescription);
                }
            }
            else
            {
                _connectionErrorReportedForCurrentConnectionAttempt = false;
            }
        }

        public IEnumerable<PlayerEventIncidentRecorded> GetAndClearPendingAnalyticsEvents()
        {
            lock (_pendingAnalyticsEventsLock)
            {
                if (_pendingAnalyticsEvents.Count == 0)
                {
                    // Avoid allocation in common case.
                    return Enumerable.Empty<PlayerEventIncidentRecorded>();
                }
                else
                {
                    List<PlayerEventIncidentRecorded> result = _pendingAnalyticsEvents;
                    _pendingAnalyticsEvents = new List<PlayerEventIncidentRecorded>();
                    return result;
                }
            }
        }

        public void Dispose()
        {
            _logHistoryTracker?.Dispose();
            _logHistoryTracker = null;
        }

        /// <summary>
        /// Issues an incident report for the unhandled exception. Thread safe.
        /// </summary>
        public void ReportUnhandledException(Exception ex)
        {
            DoReportUnhandledException(exceptionMessage: ex.Message, stackTrace: ex.StackTrace, _logHistoryTracker.GetLogHistory());
        }

        /// <summary>
        /// Issues an incident report for WatchdogDeadlineExceededError. Thread safe.
        /// </summary>
        public void ReportWatchdogDeadlineExceededError()
        {
            // Throttle errors
            if (!_incidentThrottle.TryTrigger(MetaTime.Now))
            {
                _log.Warning("Dropping WatchdogDeadlineExceeded incident report due to throttling");
                return;
            }

            AddIncidentAndAnalyticsEvent(IncidentReportFactory.CreateReportForWatchdogDeadlineExceededError(
                _logHistoryTracker.GetLogHistory(),
                _unitySystemInfo,
                _unityPlatformInfo,
                _latestGameConfigInfo,
                CollectApplicationIncidentInfo(),
                _latestTlsPeerDescription));
        }

        public void ReportServerStatusHintCorrupt(byte[] bytes, int length)
        {
            if (!_incidentThrottle.TryTrigger(MetaTime.Now))
            {
                _log.Warning("Dropping ServerStatusHintCorrupt incident report due to throttling");
                return;
            }

            AddIncidentAndAnalyticsEvent(IncidentReportFactory.CreateReportForServerStatusHintCorrupt(
                bytes,
                length,
                _logHistoryTracker.GetLogHistory(),
                _unitySystemInfo,
                _unityPlatformInfo,
                _latestGameConfigInfo,
                CollectApplicationIncidentInfo(),
                _latestTlsPeerDescription));
        }

        public void ReportSessionPingPongDurationThresholdExceeded(LoginDebugDiagnostics debugDiagnostics, MetaDuration roundtripEstimate, ServerGateway serverGateway, int pingId, MetaDuration timeSincePing)
        {
            if (!_incidentThrottle.TryTrigger(MetaTime.Now))
            {
                _log.Warning("Dropping SessionPingPongDurationThresholdExceeded incident report due to throttling");
                return;
            }

            AddIncidentAndAnalyticsEvent(IncidentReportFactory.CreateReportForSessionPingPongDurationThresholdExceeded(
                _logHistoryTracker.GetLogHistory(),
                _unitySystemInfo,
                _unityPlatformInfo,
                _latestGameConfigInfo,
                CollectApplicationIncidentInfo(),
                debugDiagnostics,
                roundtripEstimate,
                serverGateway,
                _latestTlsPeerDescription,
                _sessionToken,
                pingId,
                timeSincePing));
        }

        /// <summary>
        /// Issues a session start failure error report. If reports are throttled, no report is created and this method returns <c>null</c>.
        /// </summary>
        public PlayerIncidentReport.SessionStartFailed ReportSessionStartFailure(ConnectionStates.ErrorState error, ServerEndpoint endpoint, string tlsPeerDescription, NetworkDiagnosticReport networkReport)
        {
            // Suppress next connection error as it will be reported already
            _connectionErrorReportedForCurrentConnectionAttempt = true;

            // Throttle errors
            MetaTime now = MetaTime.Now;
            if (!_incidentThrottle.TryTrigger(now))
            {
                _log.Warning("Dropping SessionStartFailed incident report due to throttling");
                return null;
            }

            PlayerIncidentReport.SessionStartFailed report = IncidentReportFactory.CreateReportForSessionStartFailed(
                _logHistoryTracker.GetLogHistory(),
                _unitySystemInfo,
                _unityPlatformInfo,
                _latestGameConfigInfo,
                CollectApplicationIncidentInfo(),
                networkReport,
                error,
                endpoint,
                _latestTlsPeerDescription);
            AddIncidentAndAnalyticsEvent(report);
            return report;
        }

        public void ReportIncidentReportTooLarge(PlayerIncidentReport originalReport)
        {
            // Throttle errors
            MetaTime now = MetaTime.Now;
            if (!_incidentThrottle.TryTrigger(now))
            {
                _log.Warning("Dropping IncidentReportTooLarge incident report due to throttling");
                return;
            }

            AddIncidentAndAnalyticsEvent(IncidentReportFactory.CreateReportForIncidentReportTooLarge(
                _unitySystemInfo,
                _unityPlatformInfo,
                _latestGameConfigInfo,
                CollectApplicationIncidentInfo(),
                originalReport));
        }

        void BeginReportNetworkError(ConnectionStates.ErrorState error, ServerEndpoint endpoint, string tlsPeerDescription)
        {
            // Throttle errors
            MetaTime now = MetaTime.Now;
            if (!_incidentThrottle.TryTrigger(now))
            {
                _log.Warning("Dropping NetworkError incident report due to throttling");
                return;
            }

            // Try to get network diagnostic report
            NetworkDiagnosticReport networkReport;
            bool                    doAsyncNetworkDiagReport;

            if (error is IHasNetworkDiagnosticReport errorWithReport)
            {
                if (errorWithReport.NetworkDiagnosticReport == null)
                {
                    // This error should have a diagnostics report but it was not attached.
                    // This means the diagnostics report could not be computed when the
                    // error was raised.
                    // We want to compute the diagnostics report, but it takes time and we
                    // don't want to delay writing the incident in case app is closed. So, write
                    // the incident without the report, and then when/if the network diagnostics
                    // complete, update it.
                    networkReport = null;
                    doAsyncNetworkDiagReport = true;
                }
                else
                {
                    networkReport = errorWithReport.NetworkDiagnosticReport;
                    doAsyncNetworkDiagReport = false;
                }
            }
            else
            {
                networkReport = null;
                doAsyncNetworkDiagReport = false;
            }

            PlayerIncidentReport.TerminalNetworkError incident = new PlayerIncidentReport.TerminalNetworkError(
                id:                     IncidentReportFactory.CreateIncidentId(now),
                occurredAt:             now,
                logEntries:             _logHistoryTracker.GetLogHistory(),
                systemInfo:             _unitySystemInfo,
                platformInfo:           _unityPlatformInfo,
                gameConfigInfo:         _latestGameConfigInfo,
                applicationInfo:        CollectApplicationIncidentInfo(),
                errorType:              error.GetType().Name,
                networkError:           PrettyPrint.Verbose(error).ToString(),
                reasonOverride:         error.TryGetReasonOverrideForIncidentReport(),
                endpoint:               endpoint,
                networkReachability:    _unityPlatformInfo?.InternetReachability,
                networkReport:          networkReport,
                tlsPeerDescription:     tlsPeerDescription);

            if (!doAsyncNetworkDiagReport)
            {
                // Complete report (with network diagnostics). Write and we are done
                AddIncidentAndAnalyticsEvent(incident);
            }
            else
            {
                // Partial report (missing network diagnostics). Write partial, wait for diagnostics, rewrite and report
                MetaplaySDK.IncidentRepository.AddOrUpdate(incident, isVisible: false);
                AddAnalyticsEvent(incident);

                MetaplaySDK.StartNewNetworkDiagnosticsReport((newNetworkReport) =>
                {
                    PlayerIncidentReport.TerminalNetworkError completeIncident = new PlayerIncidentReport.TerminalNetworkError(
                        id:                     incident.IncidentId,
                        occurredAt:             incident.OccurredAt,
                        logEntries:             incident.ClientLogEntries,
                        systemInfo:             incident.ClientSystemInfo,
                        platformInfo:           incident.ClientPlatformInfo,
                        gameConfigInfo:         incident.GameConfigInfo,
                        applicationInfo:        incident.ApplicationInfo,
                        errorType:              incident.ErrorType,
                        networkError:           incident.NetworkError,
                        reasonOverride:         incident.ReasonOverride,
                        endpoint:               incident.Endpoint,
                        networkReachability:    incident.NetworkReachability,
                        networkReport:          newNetworkReport,
                        tlsPeerDescription:     incident.TlsPeerDescription);

                    MetaplaySDK.IncidentRepository.AddOrUpdate(completeIncident, isVisible: true);
                });
            }
        }

        void OnConnectedToServer(ConnectedToServer _)
        {
            _latestGameConfigInfo = MetaplaySDK.Connection.LatestGameConfigInfo.ToIncidentInfo();
            _latestTlsPeerDescription = MetaplaySDK.Connection.LatestTlsPeerDescription;
        }

        void OnSessionStartSuccess(SessionProtocol.SessionStartSuccess loginSuccess)
        {
            _sessionToken = loginSuccess.SessionToken;
        }

        void OnExceptionLogEntry(string logString, string stackTrace, List<ClientLogEntry> logHistory)
        {
            DoReportUnhandledException(exceptionMessage: logString, stackTrace: stackTrace, logHistory);
        }

        void DoReportUnhandledException(string exceptionMessage, string stackTrace, List<ClientLogEntry> logHistory)
        {
            if (!_incidentThrottle.TryTrigger(MetaTime.Now))
            {
                _log.Warning("Dropping UnhandledException incident report due to throttling");
                return;
            }

            AddIncidentAndAnalyticsEvent(IncidentReportFactory.CreateUnhandledException(
                exceptionMessage,
                stackTrace,
                logHistory,
                _unitySystemInfo,
                _unityPlatformInfo,
                CollectApplicationIncidentInfo(),
                _latestGameConfigInfo));
        }

        void AddIncidentAndAnalyticsEvent(PlayerIncidentReport report)
        {
            MetaplaySDK.IncidentRepository.AddOrUpdate(report);
            AddAnalyticsEvent(report);
        }

        void AddAnalyticsEvent(PlayerIncidentReport report)
        {
            PlayerEventIncidentRecorded ev = CreateIncidentAnalyticsEvent(report);
            lock (_pendingAnalyticsEventsLock)
                _pendingAnalyticsEvents.Add(ev);
        }

        PlayerEventIncidentRecorded CreateIncidentAnalyticsEvent(PlayerIncidentReport report)
        {
            string reason = PlayerIncidentUtil.TruncateReason(report.GetReason());
            string fingerprint = PlayerIncidentUtil.ComputeFingerprint(report.Type, report.SubType, reason);
            return new PlayerEventIncidentRecorded(report.IncidentId, report.OccurredAt, report.Type, report.SubType, reason, fingerprint);
        }

        static bool IsReportableErrorState(ConnectionStates.ErrorState state)
        {
            switch (state)
            {
                case ConnectionStates.TerminalError.InMaintenance _:
                    // no point to send reports if in Maintenance
                    return false;

                case ConnectionStates.TerminalError.LogicVersionMismatch _:
                    // These are usually not incidents. They are often expected, especially after a game update.
                    return false;

                case ConnectionStates.TransientError.AppTooLongSuspended _:
                    // not really an error. App was just brought back to foreground.
                    return false;

                case ConnectionStates.TransientError.SessionLostInBackground _:
                    // not really an error. App was just brought back to foreground.
                    return false;

                case ClientTerminatedConnectionConnectionError _:
                    // not an error, client decided to terminate the connection
                    return false;

                default:
                    return true;
            }
        }

        static IncidentApplicationInfo CollectApplicationIncidentInfo()
        {
            return new IncidentApplicationInfo(
                buildVersion:                   MetaplaySDK.BuildVersion,
                deviceGuid:                     MetaplaySDK.DeviceGuid,
                activeLanguage:                 MetaplaySDK.ActiveLanguage?.LanguageId.Value,
                highestSupportedLogicVersion:   MetaplayCore.Options.SupportedLogicVersions.MaxVersion
                );
        }
    }
}
