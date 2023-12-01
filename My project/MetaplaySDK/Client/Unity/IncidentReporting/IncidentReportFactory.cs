// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Debugging;
using Metaplay.Core.Message;
using Metaplay.Core.Network;
using Metaplay.Core.Serialization;
using Metaplay.Core.Session;
using System;
using System.Collections.Generic;

namespace Metaplay.Unity.IncidentReporting
{
    public static class IncidentReportFactory
    {
        /// <summary>
        /// Try to extract the name of the exception from Unity log message for an Exception.
        /// The input should be of the form 'InvalidOperationException: Message in the exception.'
        /// Returns 'Unknown' if cannot be matched.
        /// </summary>
        static string GetExceptionName(string message)
        {
            int offset = message.IndexOf(':');
            if (offset != -1 && offset < 100) // assume all exception names are shorter than 100 chars
                return message.Substring(0, offset);
            else
                return "Unknown";
        }

        /// <summary>
        /// Create an incident id based on the time of its occurrence. First 8 bytes are the milliseconds-since-epoch, followed by 8 bytes of random.
        /// </summary>
        public static string CreateIncidentId(MetaTime occurredAt) =>
            PlayerIncidentUtil.EncodeIncidentId(occurredAt, RandomPCG.CreateNew().NextULong());

        public static PlayerIncidentReport.UnhandledExceptionError CreateUnhandledException(string exceptionMessage, string stackTrace, List<ClientLogEntry> logHistory, UnitySystemInfo unitySystemInfo, UnityPlatformInfo unityPlatformInfo, IncidentApplicationInfo applicationInfo, IncidentGameConfigInfo latestGameConfigInfo)
        {
            // Limit very long stack traces to 8kB
            stackTrace = Util.ShortenString(stackTrace, 8 * 1024);

            MetaTime occurredAt = MetaTime.Now;
            return new PlayerIncidentReport.UnhandledExceptionError(
                id:                     CreateIncidentId(occurredAt),
                occurredAt:             occurredAt,
                logEntries:             logHistory,
                systemInfo:             unitySystemInfo,
                platformInfo:           unityPlatformInfo,
                gameConfigInfo:         latestGameConfigInfo,
                applicationInfo:        applicationInfo,
                exceptionName:          GetExceptionName(exceptionMessage),
                exceptionMessage:       exceptionMessage,
                stackTrace:             stackTrace);
        }

        public static PlayerIncidentReport.TerminalNetworkError CreateReportForBrokenReport(string incidentId, Exception error)
        {
            // \note: "network"error instead of unhandled exception since this is targeted for metaplay people
            return new PlayerIncidentReport.TerminalNetworkError(
                id:                     incidentId,
                occurredAt:             PlayerIncidentUtil.GetOccurredAtFromIncidentId(incidentId),
                logEntries:             new List<ClientLogEntry>() { new ClientLogEntry(MetaTime.Now, ClientLogEntryType.Exception, "Failed to parse incident", error.ToString() )},
                systemInfo:             null, // \note not available in broken-report reports
                platformInfo:           null,
                gameConfigInfo:         null,
                applicationInfo:        null,
                errorType:              "Metaplay.InternalReportBroken",
                networkError:           "Metaplay.InternalReportBroken",
                reasonOverride:         null,
                endpoint:               null,
                networkReachability:    null,
                networkReport:          null,
                tlsPeerDescription:     null);
        }

        public static PlayerIncidentReport.TerminalNetworkError CreateReportForWatchdogDeadlineExceededError(List<ClientLogEntry> logHistory, UnitySystemInfo unitySystemInfo, UnityPlatformInfo unityPlatformInfo, IncidentGameConfigInfo latestGameConfigInfo, IncidentApplicationInfo applicationInfo, string latestTlsPeerDescription)
        {
            MetaTime occurredAt = MetaTime.Now;
            return new PlayerIncidentReport.TerminalNetworkError(
                id:                     CreateIncidentId(occurredAt),
                occurredAt:             occurredAt,
                logEntries:             logHistory,
                systemInfo:             unitySystemInfo,
                platformInfo:           unityPlatformInfo,
                gameConfigInfo:         latestGameConfigInfo,
                applicationInfo:        applicationInfo,
                errorType:              "Metaplay.InternalWatchdogDeadlineExceeded",
                networkError:           "Metaplay.InternalWatchdogDeadlineExceeded",
                reasonOverride:         null,
                endpoint:               null,
                networkReachability:    unityPlatformInfo?.InternetReachability,
                networkReport:          null,
                tlsPeerDescription:     latestTlsPeerDescription);
        }

        public static PlayerIncidentReport.TerminalNetworkError CreateReportForServerStatusHintCorrupt(byte[] bytes, int length, List<ClientLogEntry> logHistory, UnitySystemInfo unitySystemInfo, UnityPlatformInfo unityPlatformInfo, IncidentGameConfigInfo latestGameConfigInfo, IncidentApplicationInfo applicationInfo, string latestTlsPeerDescription)
        {
            // \note: "network"error instead of unhandled exception since this is targeted for metaplay people
            MetaTime occurredAt = MetaTime.Now;
            List<ClientLogEntry> augmentedLogs = new List<ClientLogEntry>();
            augmentedLogs.Add(new ClientLogEntry(occurredAt, ClientLogEntryType.Warning, "bytes: " + Convert.ToBase64String(bytes, 0, length), null));
            augmentedLogs.AddRange(logHistory);
            return new PlayerIncidentReport.TerminalNetworkError(
                id:                     CreateIncidentId(occurredAt),
                occurredAt:             occurredAt,
                logEntries:             augmentedLogs,
                systemInfo:             unitySystemInfo,
                platformInfo:           unityPlatformInfo,
                gameConfigInfo:         latestGameConfigInfo,
                applicationInfo:        applicationInfo,
                errorType:              "Metaplay.InternalServerStatusHintCorrupt",
                networkError:           "Metaplay.InternalServerStatusHintCorrupt",
                reasonOverride:         null,
                endpoint:               null,
                networkReachability:    unityPlatformInfo?.InternetReachability,
                networkReport:          null,
                tlsPeerDescription:     latestTlsPeerDescription);
        }

        public static PlayerIncidentReport.SessionCommunicationHanged CreateReportForSessionPingPongDurationThresholdExceeded(List<ClientLogEntry> logHistory, UnitySystemInfo unitySystemInfo, UnityPlatformInfo unityPlatformInfo, IncidentGameConfigInfo latestGameConfigInfo, IncidentApplicationInfo applicationInfo, LoginDebugDiagnostics debugDiagnostics, MetaDuration roundtripEstimate, ServerGateway serverGateway, string tlsPeerDescription, SessionToken sessionToken, int pingId, MetaDuration elapsedSinceCommunication)
        {
            MetaTime occurredAt = MetaTime.Now;
            return new PlayerIncidentReport.SessionCommunicationHanged(
                id:                         CreateIncidentId(occurredAt),
                occurredAt:                 occurredAt,
                logEntries:                 logHistory,
                systemInfo:                 unitySystemInfo,
                platformInfo:               unityPlatformInfo,
                gameConfigInfo:             latestGameConfigInfo,
                applicationInfo:            applicationInfo,
                issueType:                  "Metaplay.SessionPingPongDurationThresholdExceeded",
                issueInfo:                  $"Metaplay.SessionPingPongDurationThresholdExceeded(pingId: {pingId})",
                debugDiagnostics:           debugDiagnostics,
                roundtripEstimate:          roundtripEstimate,
                serverGateway:              serverGateway,
                networkReachability:        unityPlatformInfo?.InternetReachability,
                tlsPeerDescription:         tlsPeerDescription,
                pingId:                     pingId,
                sessionToken:               sessionToken,
                elapsedSinceCommunication:  elapsedSinceCommunication);
        }

        public static PlayerIncidentReport.SessionStartFailed CreateReportForSessionStartFailed(List<ClientLogEntry> logHistory, UnitySystemInfo unitySystemInfo, UnityPlatformInfo unityPlatformInfo, IncidentGameConfigInfo latestGameConfigInfo, IncidentApplicationInfo applicationInfo, NetworkDiagnosticReport networkReport, ConnectionStates.ErrorState error, ServerEndpoint endpoint, string tlsPeerDescription)
        {
            MetaTime occurredAt = MetaTime.Now;
            return new PlayerIncidentReport.SessionStartFailed(
                id:                     CreateIncidentId(occurredAt),
                occurredAt:             occurredAt,
                logEntries:             logHistory,
                systemInfo:             unitySystemInfo,
                platformInfo:           unityPlatformInfo,
                gameConfigInfo:         latestGameConfigInfo,
                applicationInfo:        applicationInfo,
                errorType:              error.GetType().Name,
                networkError:           PrettyPrint.Verbose(error).ToString(),
                reasonOverride:         error.TryGetReasonOverrideForIncidentReport(),
                endpoint:               endpoint,
                networkReachability:    unityPlatformInfo?.InternetReachability,
                networkReport:          networkReport,
                tlsPeerDescription:     tlsPeerDescription);
        }

        public static PlayerIncidentReport.UnhandledExceptionError CreateReportForIncidentReportTooLarge(UnitySystemInfo unitySystemInfo, UnityPlatformInfo unityPlatformInfo, IncidentGameConfigInfo latestGameConfigInfo, IncidentApplicationInfo applicationInfo, PlayerIncidentReport originalReport)
        {
            // Try to gather info from original error
            string fakeStackTrace;
            try
            {
                byte[] reportAsBytes = MetaSerialization.SerializeTagged<PlayerIncidentReport>(originalReport, MetaSerializationFlags.IncludeAll, logicVersion: null);
                fakeStackTrace = "Metaplay.IncidentReportTooLarge\n"
                               + $"Type: {originalReport.Type}\n"
                               + $"SubType: {originalReport.SubType}\n"
                               + $"Serialized size: {reportAsBytes.Length}\n"
                               + $"Log entries: {originalReport.ClientLogEntries?.Count ?? 0}\n";
            }
            catch
            {
                fakeStackTrace = "Metaplay.IncidentReportTooLarge\n";
            }

            MetaTime occuredAt = MetaTime.Now;
            return new PlayerIncidentReport.UnhandledExceptionError(
                id:                     CreateIncidentId(occuredAt),
                occurredAt:             occuredAt,
                logEntries:             new List<ClientLogEntry>(),
                systemInfo:             unitySystemInfo,
                platformInfo:           unityPlatformInfo,
                gameConfigInfo:         latestGameConfigInfo,
                applicationInfo:        applicationInfo,
                exceptionName:          "Metaplay.IncidentReportTooLarge",
                exceptionMessage:       "",
                stackTrace:             fakeStackTrace);
        }
    }
}
