// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Network;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.Session;
using System;
using System.Collections.Generic;

namespace Metaplay.Core.Debugging
{
    /// <summary>
    /// Type of client log event (basically Unity's log level).
    /// </summary>
    [MetaSerializable]
    public enum ClientLogEntryType
    {
        Log         = 0,
        Warning     = 1,
        Error       = 2,
        Assert      = 3,
        Exception   = 4,
    }

    /// <summary>
    /// Single entry in the client log with relevant metadata. Collected from Unity via its log callback.
    /// </summary>
    [MetaSerializable]
    public class ClientLogEntry
    {
        [MetaMember(1)] public MetaTime             Timestamp   { get; private set; }   // Timestamp of log event
        [MetaMember(3)] public ClientLogEntryType   Level       { get; private set; }   // Log level
        [MetaMember(2)] public string               Message     { get; private set; }   // Message payload
        [MetaMember(4)] public string               StackTrace  { get; private set; }   // Stack trace (only for errors and exceptions)

        public ClientLogEntry() { }
        public ClientLogEntry(MetaTime timestamp, ClientLogEntryType level, string message, string stackTrace)
        {
            Timestamp   = timestamp;
            Level       = level;
            Message     = message;
            StackTrace  = stackTrace;
        }
    }

    [MetaSerializable]
    public class UnitySystemInfo
    {
        [MetaMember(1)] public float    BatteryLevel                    { get; set; } // dynamic
        [MetaMember(2)] public string   DeviceModel                     { get; set; }
        [MetaMember(3)] public string   DeviceType                      { get; set; }
        //[MetaMember(4)] public string   DeviceUniqueIdentifier          { get; set; } // disabled, as seems sensitive
        [MetaMember(5)] public int      GraphicsDeviceId                { get; set; }
        [MetaMember(6)] public string   GraphicsDeviceName              { get; set; }
        [MetaMember(7)] public string   GraphicsDeviceType              { get; set; }
        [MetaMember(8)] public string   GraphicsDeviceVendor            { get; set; }
        [MetaMember(9)] public int      GraphicsDeviceVendorId          { get; set; }
        [MetaMember(10)] public string  GraphicsDeviceVersion           { get; set; }
        [MetaMember(11)] public int     GraphicsDeviceMemoryMegabytes   { get; set; }
        [MetaMember(12)] public string  OperatingSystem                 { get; set; }
        [MetaMember(13)] public string  OperatingSystemFamily           { get; set; }
        [MetaMember(14)] public int     ProcessorCount                  { get; set; }
        [MetaMember(15)] public int     ProcessorFrequencyMhz           { get; set; }
        [MetaMember(16)] public string  ProcessorType                   { get; set; }
        // \todo [petri] add various supportsXxx members?
        [MetaMember(17)] public int     SystemMemoryMegabytes           { get; set; }

        [MetaMember(50)] public int     ScreenWidth                     { get; set; } // dynamic
        [MetaMember(51)] public int     ScreenHeight                    { get; set; } // dynamic
        [MetaMember(52)] public float   ScreenDPI                       { get; set; } // dynamic
        [MetaMember(53)] public string  ScreenOrientation               { get; set; } // dynamic
        [MetaMember(54)] public bool    IsFullScreen                    { get; set; } // dynamic

#if UNITY_2018_1_OR_NEWER
        public static UnitySystemInfo Collect()
        {
            return new UnitySystemInfo
            {
                BatteryLevel                    = UnityEngine.SystemInfo.batteryLevel,
                DeviceModel                     = UnityEngine.SystemInfo.deviceModel,
                DeviceType                      = UnityEngine.SystemInfo.deviceType.ToString(),
                GraphicsDeviceId                = UnityEngine.SystemInfo.graphicsDeviceID,
                GraphicsDeviceName              = UnityEngine.SystemInfo.graphicsDeviceName,
                GraphicsDeviceType              = UnityEngine.SystemInfo.graphicsDeviceType.ToString(),
                GraphicsDeviceVendor            = UnityEngine.SystemInfo.graphicsDeviceVendor,
                GraphicsDeviceVendorId          = UnityEngine.SystemInfo.graphicsDeviceVendorID,
                GraphicsDeviceVersion           = UnityEngine.SystemInfo.graphicsDeviceVersion,
                GraphicsDeviceMemoryMegabytes   = UnityEngine.SystemInfo.graphicsMemorySize,
                OperatingSystem                 = UnityEngine.SystemInfo.operatingSystem,
                OperatingSystemFamily           = UnityEngine.SystemInfo.operatingSystemFamily.ToString(),
                ProcessorCount                  = UnityEngine.SystemInfo.processorCount,
                ProcessorFrequencyMhz           = UnityEngine.SystemInfo.processorFrequency,
                ProcessorType                   = UnityEngine.SystemInfo.processorType,
                SystemMemoryMegabytes           = UnityEngine.SystemInfo.systemMemorySize,

                ScreenWidth                     = UnityEngine.Screen.width,
                ScreenHeight                    = UnityEngine.Screen.height,
                ScreenDPI                       = UnityEngine.Screen.dpi,
                ScreenOrientation               = EnumToStringCache<UnityEngine.ScreenOrientation>.ToString(UnityEngine.Screen.orientation),
                IsFullScreen                    = UnityEngine.Screen.fullScreen,
            };
        }

        public void UpdateDynamic()
        {
            BatteryLevel        = UnityEngine.SystemInfo.batteryLevel;
            ScreenWidth         = UnityEngine.Screen.width;
            ScreenHeight        = UnityEngine.Screen.height;
            ScreenDPI           = UnityEngine.Screen.dpi;
            ScreenOrientation   = EnumToStringCache<UnityEngine.ScreenOrientation>.ToString(UnityEngine.Screen.orientation);
            IsFullScreen        = UnityEngine.Screen.fullScreen;
        }
#endif
    }

    [MetaSerializable]
    public class UnityPlatformInfo
    {
        [MetaMember(1)] public string   BuildGuid               { get; set; }
        [MetaMember(2)] public string   Platform                { get; set; }
        [MetaMember(3)] public string   InternetReachability    { get; set; } // dynamic
        [MetaMember(4)] public bool     IsEditor                { get; set; }
        [MetaMember(5)] public string   ApplicationVersion      { get; set; }
        [MetaMember(6)] public string   UnityVersion            { get; set; }
        [MetaMember(7)] public string   SystemLanguage          { get; set; } // dynamic
        [MetaMember(8)] public string   InstallMode             { get; set; }
        [MetaMember(9)] public bool     IsGenuine               { get; set; }

        public UnityPlatformInfo() { }

#if UNITY_2018_1_OR_NEWER
        public static UnityPlatformInfo Collect()
        {
            return new UnityPlatformInfo
            {
                BuildGuid               = UnityEngine.Application.buildGUID,
                Platform                = UnityEngine.Application.platform.ToString(),
                InternetReachability    = EnumToStringCache<UnityEngine.NetworkReachability>.ToString(UnityEngine.Application.internetReachability),
                IsEditor                = UnityEngine.Application.isEditor,
                ApplicationVersion      = UnityEngine.Application.version,
                UnityVersion            = UnityEngine.Application.unityVersion,
                SystemLanguage          = EnumToStringCache<UnityEngine.SystemLanguage>.ToString(UnityEngine.Application.systemLanguage),
                InstallMode             = UnityEngine.Application.installMode.ToString(),
                IsGenuine               = UnityEngine.Application.genuine,
            };
        }

        public void UpdateDynamic()
        {
            InternetReachability    = EnumToStringCache<UnityEngine.NetworkReachability>.ToString(UnityEngine.Application.internetReachability);
            SystemLanguage          = EnumToStringCache<UnityEngine.SystemLanguage>.ToString(UnityEngine.Application.systemLanguage);
        }
#endif
    }

    /// <summary>
    /// Info about game configs in use when the incident occurred.
    /// This is similar to ConnectionGameConfigInfo in MetaplayConnection,
    /// but is intentionally separate from it to emphasize the serializability
    /// compatibility needs of this one.
    /// </summary>
    [MetaSerializable]
    public class IncidentGameConfigInfo
    {
        [MetaMember(1)] public ContentHash                  SharedConfigBaselineVersion;
        [MetaMember(2)] public ContentHash                  SharedConfigPatchesVersion;
        [MetaMember(3)] public List<ExperimentVariantPair>  ExperimentMemberships;

        IncidentGameConfigInfo() { }
        public IncidentGameConfigInfo(ContentHash sharedConfigBaselineVersion, ContentHash sharedConfigPatchesVersion, List<ExperimentVariantPair> experimentMemberships)
        {
            SharedConfigBaselineVersion = sharedConfigBaselineVersion;
            SharedConfigPatchesVersion = sharedConfigPatchesVersion;
            ExperimentMemberships = experimentMemberships;
        }
    }

    [MetaSerializable]
    public sealed class IncidentApplicationInfo
    {
        [MetaMember(1)] public BuildVersion BuildVersion                    { get; set; }
        [MetaMember(2)] public string       DeviceGuid                      { get; set; }
        [MetaMember(3)] public string       ActiveLanguage                  { get; set; }
        [MetaMember(4)] public int          HighestSupportedLogicVersion    { get; set; }

        IncidentApplicationInfo() { }
        public IncidentApplicationInfo(BuildVersion buildVersion, string deviceGuid, string activeLanguage, int highestSupportedLogicVersion)
        {
            BuildVersion = buildVersion;
            DeviceGuid = deviceGuid;
            ActiveLanguage = activeLanguage;
            HighestSupportedLogicVersion = highestSupportedLogicVersion;
        }
    }

    /// <summary>
    /// Report of a client-side incident that has happened.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(100, 200)] // reserve tagIds 100..199
    public abstract class PlayerIncidentReport
    {
        [MetaMember(100)] public string                     IncidentId          { get; private set; }
        [MetaMember(102)] public MetaTime                   OccurredAt          { get; private set; }
        [MetaMember(103)] public List<ClientLogEntry>       ClientLogEntries    { get; private set; }
        [MetaMember(104)] public UnitySystemInfo            ClientSystemInfo    { get; private set; }
        [MetaMember(105)] public UnityPlatformInfo          ClientPlatformInfo  { get; private set; }
        [MetaMember(106)] public IncidentGameConfigInfo     GameConfigInfo      { get; private set; }
        [MetaMember(107)] public IncidentApplicationInfo    ApplicationInfo     { get; private set; }

        public DateTime UploadedAt { get; set; } // filled in by server when responding to dashboard requests
        public DateTime DeletionDateTime { get; set; } // filled in by server when responding to dashboard requests

        public abstract string Type     { get; }
        public abstract string SubType  { get; }

        public abstract string GetReason();

        public PlayerIncidentReport() { }
        public PlayerIncidentReport(
            string incidentId,
            MetaTime occurredAt,
            List<ClientLogEntry> logEntries,
            UnitySystemInfo systemInfo,
            UnityPlatformInfo platformInfo,
            IncidentGameConfigInfo gameConfigInfo,
            IncidentApplicationInfo applicationInfo)
        {
            IncidentId          = incidentId;
            OccurredAt          = occurredAt;
            ClientLogEntries    = logEntries;
            ClientSystemInfo    = systemInfo;
            ClientPlatformInfo  = platformInfo;
            GameConfigInfo      = gameConfigInfo;
            ApplicationInfo     = applicationInfo;
        }

        /// <summary>
        /// Client was unable to form a connection to the server, even after several attempts.
        /// </summary>
        [MetaSerializableDerived(1)]
        public class TerminalNetworkError : PlayerIncidentReport
        {
            [MetaMember(4)] public string                   ErrorType           { get; private set; } // Type of network error (name of ConnectionStates class)
            [MetaMember(1)] public string                   NetworkError        { get; private set; } // Network error (ConnectionStates.TerminalError as string)
            [MetaMember(7)] public string                   ReasonOverride      { get; private set; } // Optional string to use instead of NetworkError for GetReason().
            [MetaMember(2)] public ServerEndpoint           Endpoint            { get; private set; } // Server endpoint used for connecting
            [MetaMember(3)] public string                   NetworkReachability { get; private set; } // Unity's Application.internetReachability
            [MetaMember(5)] public NetworkDiagnosticReport  NetworkReport       { get; private set; } // Optional network diagnostic report
            [MetaMember(6)] public string                   TlsPeerDescription  { get; private set; } // Information about the TLS peer used in Server connection

            public override string Type     => nameof(TerminalNetworkError);
            public override string SubType  => ErrorType;

            public override string GetReason() => ReasonOverride ?? NetworkError;

            public TerminalNetworkError() { }
            public TerminalNetworkError(
                string id,
                MetaTime occurredAt,
                List<ClientLogEntry> logEntries,
                UnitySystemInfo systemInfo,
                UnityPlatformInfo platformInfo,
                IncidentGameConfigInfo gameConfigInfo,
                IncidentApplicationInfo applicationInfo,
                string errorType,
                string networkError,
                string reasonOverride,
                ServerEndpoint endpoint,
                string networkReachability,
                NetworkDiagnosticReport networkReport,
                string tlsPeerDescription)
                : base(
                    id,
                    occurredAt,
                    logEntries,
                    systemInfo,
                    platformInfo,
                    gameConfigInfo,
                    applicationInfo)
            {
                ErrorType           = errorType;
                NetworkError        = networkError;
                ReasonOverride      = reasonOverride;
                Endpoint            = endpoint;
                NetworkReachability = networkReachability;
                NetworkReport       = networkReport;
                TlsPeerDescription  = tlsPeerDescription;
            }
        }

        /// <summary>
        /// An unhandled exception was thrown on the client.
        /// </summary>
        [MetaSerializableDerived(2)]
        public class UnhandledExceptionError : PlayerIncidentReport
        {
            [MetaMember(3)] public string   ExceptionName       { get; private set; }
            [MetaMember(1)] public string   ExceptionMessage    { get; private set; }
            [MetaMember(2)] public string   StackTrace          { get; private set; }

            public override string Type     => nameof(UnhandledExceptionError);
            public override string SubType  => ExceptionName;

            public override string GetReason() => StackTrace.Split('\n')[0].Trim();

            public UnhandledExceptionError() { }
            public UnhandledExceptionError(string id,
                MetaTime occurredAt,
                List<ClientLogEntry> logEntries,
                UnitySystemInfo systemInfo,
                UnityPlatformInfo platformInfo,
                IncidentGameConfigInfo gameConfigInfo,
                IncidentApplicationInfo applicationInfo,
                string exceptionName,
                string exceptionMessage,
                string stackTrace)
                : base(
                    id,
                    occurredAt,
                    logEntries,
                    systemInfo,
                    platformInfo,
                    gameConfigInfo,
                    applicationInfo)
            {
                ExceptionName       = exceptionName;
                ExceptionMessage    = exceptionMessage;
                StackTrace          = stackTrace;
            }
        }

        /// <summary>
        /// Session communication does not seem to be advancing, despite
        /// connections and session handshakes succeeding.
        /// </summary>
        /// <remarks>
        /// This report is produced both on the client
        /// (with IssueType Metaplay.SessionPingPongDurationThresholdExceeded)
        /// and on the server
        /// (with IssueType Metaplay.ServerSideSessionPingDurationThresholdExceeded).
        ///
        /// Some of the report's properties are only set in the client-produced reports.
        /// </remarks>
        [MetaSerializableDerived(3)]
        public class SessionCommunicationHanged : PlayerIncidentReport
        {
            [MetaMember(1)]  public string                  IssueType                   { get; private set; }
            [MetaMember(2)]  public string                  IssueInfo                   { get; private set; }
            [MetaMember(9)]  public LoginDebugDiagnostics   DebugDiagnostics            { get; private set; }
            [MetaMember(10)] public MetaDuration            RoundtripEstimate           { get; private set; }
            [MetaMember(11)] public ServerGateway           ServerGateway               { get; private set; }
            [MetaMember(4)]  public string                  NetworkReachability         { get; private set; } // Unity's Application.internetReachability
            [MetaMember(12)] public string                  TlsPeerDescription          { get; private set; }
            // \todo [nuutti] Do we want network report? Need to get it asynchronously.
            //[MetaMember(5)] public NetworkDiagnosticReport  NetworkReport              { get; private set; } // Optional network diagnostic report
            [MetaMember(6)]  public SessionToken            SessionToken                { get; private set; }
            /// <summary>
            /// The id of the ping that didn't get a pong (on client),
            /// or that was expected but wasn't received (on server).
            /// </summary>
            [MetaMember(7)]  public int                     PingId                      { get; private set; }
            /// <summary>
            /// Time elapsed since the ping was sent (on client),
            /// or since the session was resumed (on server).
            /// </summary>
            [MetaMember(8)]  public MetaDuration            ElapsedSinceCommunication   { get; private set; }

            public override string Type     => nameof(SessionCommunicationHanged);
            public override string SubType  => IssueType;

            public override string GetReason() => IssueInfo;

            SessionCommunicationHanged(){ }
            public SessionCommunicationHanged(
                string id,
                MetaTime occurredAt,
                List<ClientLogEntry> logEntries,
                UnitySystemInfo systemInfo,
                UnityPlatformInfo platformInfo,
                IncidentGameConfigInfo gameConfigInfo,
                IncidentApplicationInfo applicationInfo,
                string issueType,
                string issueInfo,
                LoginDebugDiagnostics debugDiagnostics,
                MetaDuration roundtripEstimate,
                ServerGateway serverGateway,
                string networkReachability,
                //NetworkDiagnosticReport networkReport,
                string tlsPeerDescription,
                SessionToken sessionToken,
                int pingId,
                MetaDuration elapsedSinceCommunication)
                : base(
                      id,
                      occurredAt,
                      logEntries,
                      systemInfo,
                      platformInfo,
                      gameConfigInfo,
                      applicationInfo)
            {
                IssueType = issueType;
                IssueInfo = issueInfo;
                DebugDiagnostics = debugDiagnostics;
                RoundtripEstimate = roundtripEstimate;
                ServerGateway = serverGateway;
                NetworkReachability = networkReachability;
                //NetworkReport = networkReport;
                TlsPeerDescription = tlsPeerDescription;
                SessionToken = sessionToken;
                PingId = pingId;
                ElapsedSinceCommunication = elapsedSinceCommunication;
            }
        }

        /// <summary>
        /// Session start failed due to client-side issue.
        /// </summary>
        /// <remarks>
        /// This is very similar to TerminalNetworkError but is intentionally a separate type to allow for
        /// quickly assessing the impact of the error and to allow independent evolution of the errors.
        /// </remarks>
        [MetaSerializableDerived(4)]
        public class SessionStartFailed : PlayerIncidentReport
        {
            [MetaMember(4)] public string                   ErrorType           { get; private set; } // Type of network error (name of ConnectionStates class)
            [MetaMember(1)] public string                   NetworkError        { get; private set; } // Network error (ConnectionStates.TerminalError as string)
            [MetaMember(7)] public string                   ReasonOverride      { get; private set; } // Optional string to use instead of NetworkError for GetReason().
            [MetaMember(2)] public ServerEndpoint           Endpoint            { get; private set; } // Server endpoint used for connecting
            [MetaMember(3)] public string                   NetworkReachability { get; private set; } // Unity's Application.internetReachability
            [MetaMember(5)] public NetworkDiagnosticReport  NetworkReport       { get; private set; } // Network diagnostic report
            [MetaMember(6)] public string                   TlsPeerDescription  { get; private set; } // Information about the TLS peer used in Server connection

            public override string Type     => nameof(SessionStartFailed);
            public override string SubType  => ErrorType;

            public override string GetReason() => ReasonOverride ?? NetworkError;

            SessionStartFailed() { }
            public SessionStartFailed(
                string id,
                MetaTime occurredAt,
                List<ClientLogEntry> logEntries,
                UnitySystemInfo systemInfo,
                UnityPlatformInfo platformInfo,
                IncidentGameConfigInfo gameConfigInfo,
                IncidentApplicationInfo applicationInfo,
                string errorType,
                string networkError,
                string reasonOverride,
                ServerEndpoint endpoint,
                string networkReachability,
                NetworkDiagnosticReport networkReport,
                string tlsPeerDescription)
                : base(
                    id,
                    occurredAt,
                    logEntries,
                    systemInfo,
                    platformInfo,
                    gameConfigInfo,
                    applicationInfo)
            {
                ErrorType           = errorType;
                NetworkError        = networkError;
                ReasonOverride      = reasonOverride;
                Endpoint            = endpoint;
                NetworkReachability = networkReachability;
                NetworkReport       = networkReport;
                TlsPeerDescription  = tlsPeerDescription;
            }
        }
    }

    public static class PlayerIncidentUtil
    {
        public const uint PushThrottlingKeyflipKey = 0x1247A312;

        /// <summary>
        /// Extract the occurred-at of an incident from its id. The first 8 bytes encode the MetaTime milliseconds-since-epoch.
        /// </summary>
        /// <param name="incidentId"></param>
        /// <returns></returns>
        public static MetaTime GetOccurredAtFromIncidentId(string incidentId)
        {
            byte[] bytes = Util.ParseHexString(incidentId.Substring(0, 16));
            long ms = 0;
            for (int ndx = 0; ndx < 8; ndx++)
                ms |= (long)bytes[ndx] << (56 - 8 * ndx);
            return MetaTime.FromMillisecondsSinceEpoch(ms);
        }

        /// <summary>
        /// Extract the suffix payload its id which are encoded into the last 8 bytes.
        /// </summary>
        /// <param name="incidentId"></param>
        /// <returns></returns>
        public static ulong GetSuffixFromIncidentId(string incidentId)
        {
            byte[] bytes = Util.ParseHexString(incidentId.Substring(16, 16));
            ulong suffix = 0;
            for (int ndx = 0; ndx < 8; ndx++)
                suffix |= (ulong)bytes[ndx] << (8 * ndx);
            return suffix;
        }

        public static string EncodeIncidentId(MetaTime occurredAt, ulong suffix)
        {
            byte[] bytes = new byte[16];

            // First 8 bytes are the occurredAt milliseconds-since-epoch
            long ms = occurredAt.MillisecondsSinceEpoch;
            for (int ndx = 0; ndx < 8; ndx++)
                bytes[ndx] = (byte)(ms >> (56 - 8 * ndx));

            // Last 8 bytes are the suffix (usually random).
            for (int ndx = 0; ndx < 8; ndx++)
                bytes[8 + ndx] = (byte)(suffix >> (8 * ndx));

            return Util.ToHexString(bytes);
        }

        public static string TruncateReason(string reason)
        {
            const int MaxReasonLength = 256;
            return (reason.Length > MaxReasonLength) ? reason.Substring(0, MaxReasonLength) : reason;
        }

        public static string ComputeFingerprint(string type, string subType, string reason) =>
            Util.ComputeMD5($"{type}/{subType}/{reason}");

        public class IncidentReportTooLargeException : Exception
        {
            public IncidentReportTooLargeException(string message) : base(message)
            {
            }
        }

        /// <summary>
        /// If incident could not be compressed to fit into delivery limits, throws <see cref="IncidentReportTooLargeException"/>. Otherwise returns compressed payload.
        /// </summary>
        public static byte[] CompressIncidentForNetworkDelivery(PlayerIncidentReport report)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            byte[] reportPayload = MetaSerialization.SerializeTagged<PlayerIncidentReport>(report, MetaSerializationFlags.SendOverNetwork, logicVersion: null);
            if (reportPayload.Length > PlayerUploadIncidentReport.MaxUncompressedPayloadSize)
                throw new IncidentReportTooLargeException($"Metaplay uncompressed incident report too large: reportSize={reportPayload.Length} (max is {PlayerUploadIncidentReport.MaxUncompressedPayloadSize})");

            byte[] compressedPayload = CompressUtil.DeflateCompress(reportPayload);
            if (compressedPayload.Length > PlayerUploadIncidentReport.MaxCompressedPayloadSize)
                throw new IncidentReportTooLargeException($"Metaplay compressed incident report too large: compressedSize={compressedPayload.Length} (max is {PlayerUploadIncidentReport.MaxCompressedPayloadSize}).");

            return compressedPayload;
        }

        public static PlayerIncidentReport DecompressNetworkDeliveredIncident(byte[] payload, out int uncompressedPayloadSize)
        {
            if (payload.Length > PlayerUploadIncidentReport.MaxCompressedPayloadSize)
                throw new ArgumentException($"Compressed incident is too large. Payload was {payload.Length} bytes which is more than MaxCompressedPayloadSize: {PlayerUploadIncidentReport.MaxCompressedPayloadSize}");

            byte[] uncompressed = CompressUtil.DeflateDecompress(payload, maxDecompressedSize: PlayerUploadIncidentReport.MaxUncompressedPayloadSize);
            PlayerIncidentReport report = MetaSerialization.DeserializeTagged<PlayerIncidentReport>(uncompressed, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            if (report == null)
                throw new ArgumentException("Incident report is null. Not allowed.");

            uncompressedPayloadSize = uncompressed.Length;
            return report;
        }
    }
}
