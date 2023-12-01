// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Debugging;
using Metaplay.Server.Database;
using Microsoft.EntityFrameworkCore;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    [RuntimeOptions("PlayerIncident", isStatic: true, "Configuration options for player Incidents.")]
    public class PlayerIncidentOptions : RuntimeOptionsBase
    {
        [MetaDescription("Enables uploading of player incident reports to the server.")]
        public bool     EnableUploads                               { get; private set; } = true;
        [MetaDescription("The percentage likelihood that any given `TerminalNetworkError` incident is uploaded.")]
        public int      UploadPercentageTerminalNetworkError        { get; private set; } = 100;
        [MetaDescription("The percentage likelihood that any given `UnhandledExceptionError` incident is uploaded.")]
        public int      UploadPercentageUnhandledExceptionError     { get; private set; } = 100;
        [MetaDescription("The percentage likelihood that any given `SessionCommunicationHanged` incident is uploaded.")]
        public int      UploadPercentageSessionCommunicationHanged  { get; private set; } = 100;
        [MetaDescription("The percentage likelihood that any given `SessionStartFailed` incident is uploaded.")]
        public int      UploadPercentageSessionStartFailed          { get; private set; } = 100;

        /// <summary>
        /// <inheritdoc cref="Metaplay.Core.Message.Handshake.ConnectionOptions.PushUploadPercentageSessionStartFailedIncidentReport"/>
        /// </summary>
        [MetaDescription("The percentage likelihood that an incident during session start is reported immediately.")]
        public int      PushUploadPercentageSessionStartFailed      { get; private set; } = 100;

        /// <summary>
        /// After a game session resumption, if the server expects a
        /// <see cref="Metaplay.Core.Message.SessionPing"/> from the client
        /// but does not receive it within this duration, the server
        /// reports a SessionCommunicationHanged incident (with SubType
        /// Metaplay.ServerSideSessionPingDurationThresholdExceeded).
        /// </summary>
        [MetaDescription("A time limit in which the client must acknowldege session resumption. Exceeding this limit results in an incident report.")]
        public TimeSpan SessionPingDurationIncidentThreshold        { get; private set; } = TimeSpan.FromSeconds(10);
        [MetaDescription("The maximum number of server-side `SessionCommunicationHanged` incidents generated per session.")]
        public int      MaxSessionPingDurationIncidentsPerSession   { get; private set; } = 3;
    }

    /// <summary>
    /// Database-persisted form of <see cref="Metaplay.Core.Debugging.PlayerIncidentReport"/>.
    /// </summary>
    [Table("PlayerIncidents")]
    [Index(nameof(PlayerId))]
    [Index(nameof(PersistedAt))]
    [Index(nameof(Fingerprint), nameof(PersistedAt))]
    public class PersistedPlayerIncident : IPersistedItem
    {
        [Key]
        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string               IncidentId      { get; set; }   // \note Also acts as index for OccurredAt (encoded at beginning of IncidentId)

        [Required]
        [PartitionKey]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        // \todo [petri] foreign key?
        public string               PlayerId        { get; set; }

        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string               Fingerprint     { get; set; }   // Uniquely identifying fingerprint for the kind of error (MD5 of Type, SubType and Reason)

        [Required]
        [MaxLength(128)]
        [Column(TypeName = "varchar(128)")]
        public string               Type            { get; set; }   // Type of incident (type of PlayerIncidentReport, eg, "TerminalNetworkError")

        [Required]
        [MaxLength(128)]
        [Column(TypeName = "varchar(128)")]
        public string               SubType         { get; set; }   // Sub-type of incident (eg, "NullReferenceError" or "SessionForceTerminated")

        [Required]
        [MaxLength(256)]
        [Column(TypeName = "varchar(256)")]
        public string               Reason          { get; set; }   // Uniquely identifying reason for incident (eg, "SessionForceTerminated { KickedByAdminAction }" or first line of stack trace)

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime             PersistedAt     { get; set; }

        [Required]
        public byte[]               Payload         { get; set; }   // TaggedSerialized<PlayerIncidentReport> (compressed using Compression)

        [Required]
        public CompressionAlgorithm Compression     { get; set; }   // Compression algorithm for Payload

        /// <summary>
        /// Extract the metadata of the incident.
        /// </summary>
        /// <returns></returns>
        public PlayerIncidentHeader ToHeader()
        {
            // \note Keep in sync with HeaderMemberNames, which is used
            //       to fetch just the required data from the database.
            return new PlayerIncidentHeader(
                IncidentId,
                EntityId.ParseFromString(PlayerId),
                Fingerprint,
                Type,
                SubType,
                Reason,
                PersistedAt);
        }

        public static readonly string[] HeaderMemberNames = new string[]
        {
            nameof(IncidentId),
            nameof(PlayerId),
            nameof(Fingerprint),
            nameof(Type),
            nameof(SubType),
            nameof(Reason),
            nameof(PersistedAt),
        };
    }

    /// <summary>
    /// Contains the metadata of a <see cref="PlayerIncidentReport"/>. Used when querying lists of incidents into dashboard.
    /// </summary>
    public class PlayerIncidentHeader
    {
        public string   IncidentId  { get; }
        public EntityId PlayerId    { get; }
        public string   Fingerprint { get; }
        public string   Type        { get; }
        public string   SubType     { get; }
        public string   Reason      { get; }
        public MetaTime OccurredAt  { get; }

        // Used only on server for showing in dashboard, copied from PersistedPlayerIncident when read from db
        public DateTime UploadedAt { get; }

        PlayerIncidentHeader() { }
        public PlayerIncidentHeader(string incidentId, EntityId playerId, string fingerprint, string type, string subType, string reason, DateTime uploadedAt)
        {
            IncidentId  = incidentId;
            PlayerId    = playerId;
            Fingerprint = fingerprint;
            Type        = type;
            SubType     = subType;
            Reason      = reason;
            OccurredAt  = PlayerIncidentUtil.GetOccurredAtFromIncidentId(incidentId);
            UploadedAt  = uploadedAt;
        }
    }

    /// <summary>
    /// Aggregate statistics about player incidents by type (used by when querying into dashboard).
    /// </summary>
    public class PlayerIncidentStatistics
    {
        public string   Fingerprint                 { get; private set; }
        public string   Type                        { get; private set; }
        public string   SubType                     { get; private set; }
        public string   Reason                      { get; private set; }
        public int      Count                       { get; private set; }
        public bool     CountIsLimitedByQuerySize   { get; set; }

        public PlayerIncidentStatistics() { }
        public PlayerIncidentStatistics(string fingerprint, string type, string subType, string reason, int count, bool countIsLimitedByQuerySize)
        {
            Fingerprint = fingerprint;
            Type = type;
            SubType = subType;
            Reason = reason;
            Count = count;
            CountIsLimitedByQuerySize = countIsLimitedByQuerySize;
        }
    }

    public static class PlayerIncidentStorage
    {
        static Prometheus.Counter c_incidentReportsPersisted = Prometheus.Metrics.CreateCounter("game_player_incident_reports_persisted", "Number of player incident reports persisted (by kind)", "kind");

        public static async Task PersistIncidentAsync(EntityId playerId, PlayerIncidentReport report, byte[] deflateCompressedReportPayload)
        {
            // \todo [petri] validate incidentId some more, as we use it as key in database

            // Truncate long incident reasons (just in case)
            string reason = PlayerIncidentUtil.TruncateReason(report.GetReason());

            // Persist in database
            PersistedPlayerIncident persisted = new PersistedPlayerIncident
            {
                IncidentId  = report.IncidentId,
                PlayerId    = playerId.ToString(),
                Fingerprint = PlayerIncidentUtil.ComputeFingerprint(report.Type, report.SubType, reason),
                Type        = report.Type,
                SubType     = report.SubType,
                Reason      = reason,
                PersistedAt = MetaTime.Now.ToDateTime(),
                Payload     = deflateCompressedReportPayload,
                Compression = CompressionAlgorithm.Deflate,
            };
            await MetaDatabase.Get().InsertOrIgnoreAsync(persisted);

            // \todo [petri] handle attachments?

            // Metrics
            // \todo [nuutti] Only report metrics if incident didn't already exist in database?
            c_incidentReportsPersisted.WithLabels(GetIncidentKindLabelForMetrics(report)).Inc();
        }

        /// <summary>
        /// Get the percentage of reports that should be uploaded for this specific type.
        /// </summary>
        public static int GetUploadPercentage(IMetaLogger log, ClientAvailableIncidentReport header)
        {
            PlayerIncidentOptions incidentOpts = RuntimeOptionsRegistry.Instance.GetCurrent<PlayerIncidentOptions>();

            if (!incidentOpts.EnableUploads)
                return 0;

            switch (header.Type)
            {
                case nameof(PlayerIncidentReport.TerminalNetworkError):
                    return incidentOpts.UploadPercentageTerminalNetworkError;

                case nameof(PlayerIncidentReport.UnhandledExceptionError):
                    return incidentOpts.UploadPercentageUnhandledExceptionError;

                case nameof(PlayerIncidentReport.SessionCommunicationHanged):
                    return incidentOpts.UploadPercentageSessionCommunicationHanged;

                case nameof(PlayerIncidentReport.SessionStartFailed):
                    return incidentOpts.UploadPercentageSessionStartFailed;

                default:
                    log.Warning("Received unknown PlayerIncident type: {PlayerIncidentType}", header.Type);
                    return 0;
            }
        }

        static string GetIncidentKindLabelForMetrics(PlayerIncidentReport report)
        {
            if (report is PlayerIncidentReport.SessionCommunicationHanged sessionCommunicationHanged)
            {
                // Include terse information about subtype (whether report
                // comes from client or server) and ping id.

                string subTypeStr;
                if (sessionCommunicationHanged.SubType == "Metaplay.SessionPingPongDurationThresholdExceeded")
                    subTypeStr = "ClientPingPong";
                else if (sessionCommunicationHanged.SubType == "Metaplay.ServerSideSessionPingDurationThresholdExceeded")
                    subTypeStr = "ServerPing";
                else
                    subTypeStr = "Unknown";

                int pingId = sessionCommunicationHanged.PingId;
                // \note Restrict ping id in metrics label, we don't want to spam metrics labels.
                string pingIdStr;
                if (pingId < 0)
                {
                    // Can come from a bad client
                    pingIdStr = "Negative";
                }
                else if (pingId > 10)
                {
                    // Can come from client or server - only number of incidents per session
                    // is restricted, not ping id.
                    pingIdStr = "Over10";
                }
                else
                    pingIdStr = pingId.ToString(CultureInfo.InvariantCulture);

                return $"{report.Type}_{subTypeStr}_{pingIdStr}";
            }
            else
                return report.Type;
        }
    }
}
