// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Metaplay.Unity.DefaultIntegration
{
    /// <summary>
    /// Note: if you modify these (in the stock SDK), please also
    /// update the SDK documentation!
    /// See <see cref="GenerateDocumentationTable"/> below.
    ///
    /// Registry for info about connection errors.
    /// These are used by <see cref="MetaplayClient"/> when
    /// handling a connection loss.
    /// See <see cref="MetaplayClient.HandleConnectionError"/>
    /// and <see cref="ConnectionLostEvent"/>.
    /// </summary>
    /// <remarks>
    /// Note about the naming of the MetaplayClientErrorInfo
    /// members here:
    /// "Unknown" is used when the error state
    /// (as seen by MetaplayClient) itself specifies "unknown",
    /// as with <see cref="TlsErrorUnknown"/>.
    /// "Unhandled" is used when the handling of the specific
    /// error state is missing from MetaplayClient, as with
    /// <see cref="TlsErrorUnhandled"/>.
    ///
    /// This convention does not apply to the documentation strings,
    /// where for simplicity just "unknown" is used.
    /// </remarks>
    public static class MetaplayClientConnectionErrors
    {
        // The allocation of the numeric code among the technical error scenarios
        // is pretty arbitrary and exists for organizatory purposes. The scheme
        // used here is to group the codes based on MetaplayConnection's ConnectionState
        // representing the error, with the most significant digits identifying the
        // type of the ConnectionState, and the two least significant digits
        // discriminating the error scenario further, in an error-specific manner.

        // 10xx: TransientError.Closed
        public static readonly MetaplayClientErrorInfo Closed_HasCompletedSessionInit                       = new MetaplayClientErrorInfo(1000, "closed_session_init_done",                             ConnectionLostReason.ConnectionLost,            "Connection was lost (during session).");
        public static readonly MetaplayClientErrorInfo Closed_HasCompletedHandshake                         = new MetaplayClientErrorInfo(1001, "closed_handshake_done",                                ConnectionLostReason.CouldNotConnect,           "Connection was lost (before session had been started).");
        public static readonly MetaplayClientErrorInfo Closed_HasNotCompletedHandshake                      = new MetaplayClientErrorInfo(1002, "closed_handshake_not_done",                            ConnectionLostReason.CouldNotConnect,           "Connection was lost (before any packets had been received from the server).");
        public static readonly MetaplayClientErrorInfo Closed_ProbeFailed                                   = new MetaplayClientErrorInfo(1003, "closed_probe_failed",                                  ConnectionLostReason.NoInternetConnection,      "Connection was lost (no internet connection).");
        public static readonly MetaplayClientErrorInfo Closed_ProbeMissing                                  = new MetaplayClientErrorInfo(1004, "closed_probe_missing",                                 ConnectionLostReason.CouldNotConnect,           "Connection was lost (internet connectivity not known).");

        // 11xx: TransientError.Timeout
        public static readonly MetaplayClientErrorInfo TimeoutConnect_HasCompletedSessionInit               = new MetaplayClientErrorInfo(1100, "timeout_connect_session_init_done",                    ConnectionLostReason.CouldNotConnect,           "Reconnection attempt timed out (during session).");
        public static readonly MetaplayClientErrorInfo TimeoutConnect_HasCompletedHandshake                 = new MetaplayClientErrorInfo(1101, "timeout_connect_handshake_done",                       ConnectionLostReason.CouldNotConnect,           "Reconnection attempt timed out (during session).");
        public static readonly MetaplayClientErrorInfo TimeoutConnect_HasNotCompletedHandshake              = new MetaplayClientErrorInfo(1102, "timeout_connect_handshake_not_done",                   ConnectionLostReason.CouldNotConnect,           "Connection attempt timed out.");
        public static readonly MetaplayClientErrorInfo TimeoutConnect_ProbeFailed                           = new MetaplayClientErrorInfo(1103, "timeout_connect_probe_failed",                         ConnectionLostReason.NoInternetConnection,      "Connection attempt timed out (no internet connection).");
        public static readonly MetaplayClientErrorInfo TimeoutConnect_ProbeMissing                          = new MetaplayClientErrorInfo(1104, "timeout_connect_probe_missing",                        ConnectionLostReason.CouldNotConnect,           "Connection attempt timed out (internet connectivity not known).");
        public static readonly MetaplayClientErrorInfo TimeoutResourceFetch                                 = new MetaplayClientErrorInfo(1112, "timeout_resource_fetch",                               ConnectionLostReason.CouldNotConnect,           "Resource download timed out.");
        public static readonly MetaplayClientErrorInfo TimeoutResourceFetch_ProbeFailed                     = new MetaplayClientErrorInfo(1113, "timeout_resource_fetch_probe_failed",                  ConnectionLostReason.NoInternetConnection,      "Resource download timed out (no internet connection).");
        public static readonly MetaplayClientErrorInfo TimeoutResourceFetch_ProbeMissing                    = new MetaplayClientErrorInfo(1114, "timeout_resource_fetch_probe_missing",                 ConnectionLostReason.CouldNotConnect,           "Resource download timed out (internet connectivity not known).");
        public static readonly MetaplayClientErrorInfo TimeoutStream_HasCompletedSessionInit                = new MetaplayClientErrorInfo(1120, "timeout_stream_session_init_done",                     ConnectionLostReason.ConnectionLost,            "Connection timed out (during session).");
        public static readonly MetaplayClientErrorInfo TimeoutStream_HasCompletedHandshake                  = new MetaplayClientErrorInfo(1121, "timeout_stream_handshake_done",                        ConnectionLostReason.ConnectionLost,            "Connection timed out (before session had been started).");
        public static readonly MetaplayClientErrorInfo TimeoutStream_HasNotCompletedHandshake               = new MetaplayClientErrorInfo(1122, "timeout_stream_handshake_not_done",                    ConnectionLostReason.ConnectionLost,            "Connection timed out (before any packets had been received from the server).");
        public static readonly MetaplayClientErrorInfo TimeoutUnhandled                                     = new MetaplayClientErrorInfo(1190, "timeout_unhandled",                                    ConnectionLostReason.CouldNotConnect,           "Unknown kind of connection timeout.");

        // 12xx: TransientError.ClusterNotReady
        public static readonly MetaplayClientErrorInfo ClusterStarting                                      = new MetaplayClientErrorInfo(1200, "cluster_starting",                                     ConnectionLostReason.ServerMaintenance,         "Backend is starting up and does not yet accept connections.");
        public static readonly MetaplayClientErrorInfo ClusterShuttingDown                                  = new MetaplayClientErrorInfo(1201, "cluster_shutting_down",                                ConnectionLostReason.ServerMaintenance,         "Backend is shutting down and does not accept connections anymore.");
        public static readonly MetaplayClientErrorInfo ClusterNotReadyUnhandled                             = new MetaplayClientErrorInfo(1209, "cluster_not_ready_unhandled",                          ConnectionLostReason.ServerMaintenance,         "Backend is refusing connections for an unknown reason.");

        // 13xx: TransientError.ConfigFetchFailed
        public static readonly MetaplayClientErrorInfo ResourceFetchFailed                                  = new MetaplayClientErrorInfo(1302, "resource_fetch_failed",                                ConnectionLostReason.CouldNotConnect,           "Failed to download resources.");
        public static readonly MetaplayClientErrorInfo ResourceFetchFailed_ProbeFailed                      = new MetaplayClientErrorInfo(1303, "resource_fetch_failed_probe_failed",                   ConnectionLostReason.NoInternetConnection,      "Failed to download resources (no internet connection).");
        public static readonly MetaplayClientErrorInfo ResourceFetchFailed_ProbeMissing                     = new MetaplayClientErrorInfo(1304, "resource_fetch_failed_probe_missing",                  ConnectionLostReason.CouldNotConnect,           "Failed to download resources (internet connectivity not known).");
        public static readonly MetaplayClientErrorInfo ResourceActivationFailed                             = new MetaplayClientErrorInfo(1310, "resource_activation_failed",                           ConnectionLostReason.InternalError,             "Failed to apply resources. Likely a game config deserialization failure.");
        public static readonly MetaplayClientErrorInfo ResourceLoadFailedUnhandled                          = new MetaplayClientErrorInfo(1390, "resource_load_failed_unhandled",                       ConnectionLostReason.InternalError,             "Unknown kind of resource loading failure.");

        // 14xx: TransientError.FailedToResumeSession
        public static readonly MetaplayClientErrorInfo FailedToResumeSession                                = new MetaplayClientErrorInfo(1400, "failed_to_resume_session",                             ConnectionLostReason.ConnectionLost,            "Failed to resume a game session after a disconnect. Likely the session has already expired, or another device connected to the same player account.");

        // 15xx: TransientError.SessionForceTerminated
        public static readonly MetaplayClientErrorInfo SessionTerminatedReceivedAnotherConnection           = new MetaplayClientErrorInfo(1500, "force_terminated_received_another_connection",         ConnectionLostReason.ConnectionLost,            "Another client connected to the same player account.");
        public static readonly MetaplayClientErrorInfo SessionTerminatedKickedByAdminAction                 = new MetaplayClientErrorInfo(1501, "force_terminated_kicked_by_admin_action",              ConnectionLostReason.ConnectionLost,            "A game admin performed an action that requires the client to reconnect.");
        public static readonly MetaplayClientErrorInfo SessionTerminatedInternalServerError                 = new MetaplayClientErrorInfo(1502, "force_terminated_internal_server_error",               ConnectionLostReason.InternalError,             "An internal server error occurred.");
        public static readonly MetaplayClientErrorInfo SessionTerminatedUnknown                             = new MetaplayClientErrorInfo(1503, "force_terminated_unknown",                             ConnectionLostReason.InternalError,             "An unknown server error occurred.");
        public static readonly MetaplayClientErrorInfo SessionTerminatedClientTimeTooFarBehind              = new MetaplayClientErrorInfo(1504, "force_terminated_client_time_too_far_behind",          ConnectionLostReason.ConnectionLost,            "The client-controlled PlayerModel timeline fell too far behind the real time.");
        public static readonly MetaplayClientErrorInfo SessionTerminatedClientTimeTooFarAhead               = new MetaplayClientErrorInfo(1505, "force_terminated_client_time_too_far_ahead",           ConnectionLostReason.InternalError,             "The client-controlled PlayerModel timeline advanced too far beyond the real time.");
        public static readonly MetaplayClientErrorInfo SessionTerminatedSessionTooLong                      = new MetaplayClientErrorInfo(1506, "force_terminated_session_too_long",                    ConnectionLostReason.ConnectionLost,            "Maximum game session duration was exceeded (configured in server option `Session:MaximumSessionLength`).");
        public static readonly MetaplayClientErrorInfo SessionTerminatedPlayerBanned                        = new MetaplayClientErrorInfo(1507, "force_terminated_player_banned",                       ConnectionLostReason.PlayerIsBanned,            "The player got banned during the game session.");
        public static readonly MetaplayClientErrorInfo SessionTerminatedMaintenanceModeStarted              = new MetaplayClientErrorInfo(1508, "force_terminated_maintenance_mode_started",            ConnectionLostReason.ServerMaintenance,         "Backend maintenance mode mode started during the game session.");
        public static readonly MetaplayClientErrorInfo SessionTerminatedPauseDeadlineExceeded               = new MetaplayClientErrorInfo(1509, "force_terminated_pause_deadline_exceeded",             ConnectionLostReason.ConnectionLost,            "The client was paused for too long.");
        /// <summary>
        /// Note: not an individual error code; used as the base code to which the
        /// concrete <see cref="Metaplay.Core.Message.SessionForceTerminateReason"/>'s
        /// type code is added.
        /// </summary>
        public static readonly MetaplayClientErrorInfo SessionTerminatedUnhandled_Base                      = new MetaplayClientErrorInfo(1550, "force_terminated_unhandled",                           ConnectionLostReason.InternalError,             "The server terminated the session for an unknown reason.");
        public static readonly int                     SessionTerminatedUnhandled_MaxCode                   = 1598;
        public static readonly MetaplayClientErrorInfo SessionTerminatedUnhandledInvalid                    = new MetaplayClientErrorInfo(1599, "force_terminated_unhandled_invalid",                   ConnectionLostReason.InternalError,             "The server terminated the session for an unknown reason.");

        // 16xx: TransientError.ProtocolError
        public static readonly MetaplayClientErrorInfo ProtocolErrorUnexpectedLoginMessage                  = new MetaplayClientErrorInfo(1600, "protocol_error_unexpected_login_message",              ConnectionLostReason.InternalError,             "The client detected a connection protocol violation by the server.");
        public static readonly MetaplayClientErrorInfo ProtocolErrorMissingServerHello                      = new MetaplayClientErrorInfo(1610, "protocol_error_missing_server_hello",                  ConnectionLostReason.InternalError,             "The client detected a connection protocol violation by the server.");
        public static readonly MetaplayClientErrorInfo ProtocolErrorSessionStartFailed                      = new MetaplayClientErrorInfo(1620, "protocol_error_session_start_failed",                  ConnectionLostReason.InternalError,             "The server failed to initialize the session. Internal server error.");
        public static readonly MetaplayClientErrorInfo ProtocolErrorSessionProtocolError                    = new MetaplayClientErrorInfo(1630, "protocol_error_session_protocol_error",                ConnectionLostReason.InternalError,             "The client detected a connection protocol violation by the server.");
        public static readonly MetaplayClientErrorInfo ProtocolErrorUnhandled                               = new MetaplayClientErrorInfo(1690, "protocol_error_unhandled",                             ConnectionLostReason.InternalError,             "The client detected a connection protocol violation by the server.");

        // 17xx: TransientError.SessionLostInBackground
        public static readonly MetaplayClientErrorInfo SessionLostInBackground                              = new MetaplayClientErrorInfo(1700, "session_lost_in_background",                           ConnectionLostReason.ConnectionLost,            "The connection was lost while the client was paused.");

        // 18xx: TransientError.InternalWatchdogDeadlineExceeded
        public static readonly MetaplayClientErrorInfo TransportWatchdogExceeded_HasCompletedSessionInit    = new MetaplayClientErrorInfo(1800, "transport_watchdog_exceeded_session_init_done",        ConnectionLostReason.InternalError,             "A connection watchdog timer was triggered. Internal client error.");
        public static readonly MetaplayClientErrorInfo TransportWatchdogExceeded_HasCompletedHandshake      = new MetaplayClientErrorInfo(1801, "transport_watchdog_exceeded_handshake_done",           ConnectionLostReason.InternalError,             "A connection watchdog timer was triggered. Internal client error.");
        public static readonly MetaplayClientErrorInfo TransportWatchdogExceeded_HasNotCompletedHandshake   = new MetaplayClientErrorInfo(1802, "transport_watchdog_exceeded_handshake_not_done",       ConnectionLostReason.InternalError,             "A connection watchdog timer was triggered. Internal client error.");
        public static readonly MetaplayClientErrorInfo TransportWatchdogExceeded_ProbeFailed                = new MetaplayClientErrorInfo(1803, "transport_watchdog_exceeded_probe_failed",             ConnectionLostReason.InternalError,             "A connection watchdog timer was triggered. Internal client error.");
        public static readonly MetaplayClientErrorInfo TransportWatchdogExceeded_ProbeMissing               = new MetaplayClientErrorInfo(1804, "transport_watchdog_exceeded_probe_missing",            ConnectionLostReason.InternalError,             "A connection watchdog timer was triggered. Internal client error.");
        public static readonly MetaplayClientErrorInfo ResetupWatchdogExceeded                              = new MetaplayClientErrorInfo(1810, "resetup_watchdog_exceeded",                            ConnectionLostReason.InternalError,             "A connection watchdog timer was triggered. Internal client error.");
        public static readonly MetaplayClientErrorInfo ConnectionWatchdogExceededUnhandled                  = new MetaplayClientErrorInfo(1890, "connection_watchdog_exceeded_unhandled",               ConnectionLostReason.InternalError,             "A connection watchdog timer was triggered. Internal client error.");

        // 19xx: TransientError.AppTooLongSuspended
        public static readonly MetaplayClientErrorInfo AppTooLongSuspended                                  = new MetaplayClientErrorInfo(1900, "app_too_long_suspended",                               ConnectionLostReason.ConnectionLost,            "The client was paused for too long, or spent too long without running the update loop.");

        // 20xx: TransientError.TlsError
        public static readonly MetaplayClientErrorInfo TlsErrorUnknown                                      = new MetaplayClientErrorInfo(2000, "tls_error_unknown",                                    ConnectionLostReason.CouldNotConnect,           "TLS error.");
        public static readonly MetaplayClientErrorInfo TlsErrorNotAuthenticated                             = new MetaplayClientErrorInfo(2001, "tls_error_not_authenticated",                          ConnectionLostReason.CouldNotConnect,           "TLS error.");
        public static readonly MetaplayClientErrorInfo TlsErrorFailureWhileAuthenticating                   = new MetaplayClientErrorInfo(2002, "tls_error_failure_while_authenticating",               ConnectionLostReason.CouldNotConnect,           "TLS error.");
        public static readonly MetaplayClientErrorInfo TlsErrorNotEncrypted                                 = new MetaplayClientErrorInfo(2003, "tls_error_not_encrypted",                              ConnectionLostReason.CouldNotConnect,           "TLS error.");
        public static readonly MetaplayClientErrorInfo TlsErrorUnhandled                                    = new MetaplayClientErrorInfo(2009, "tls_error_unhandled",                                  ConnectionLostReason.CouldNotConnect,           "TLS error.");

        // 21xx: TerminalError.WireProtocolVersionMismatch
        public static readonly MetaplayClientErrorInfo WireProtocolVersionClientTooOld                      = new MetaplayClientErrorInfo(2100, "wire_protocol_version_client_too_old",                 ConnectionLostReason.ClientVersionTooOld,       "The client is too old for the server.");
        public static readonly MetaplayClientErrorInfo WireProtocolVersionClientTooNew                      = new MetaplayClientErrorInfo(2101, "wire_protocol_version_client_too_new",                 ConnectionLostReason.ServerMaintenance,         "The client is too new for the server.");
        public static readonly MetaplayClientErrorInfo WireProtocolVersionInvalidMismatch                   = new MetaplayClientErrorInfo(2109, "wire_protocol_version_invalid_mismatch",               ConnectionLostReason.ServerMaintenance,         "Unexpected client-server version mismatch.");

        // 22xx: TerminalError.InvalidGameMagic
        public static readonly MetaplayClientErrorInfo InvalidGameMagic                                     = new MetaplayClientErrorInfo(2200, "invalid_game_magic",                                   ConnectionLostReason.InternalError,             "The server reported a mismatching \"game magic\" code. The client likely connected to something that isn't a Metaplay game server, or belongs to a different game.");

        // 23xx: TerminalError.InMaintenance
        public static readonly MetaplayClientErrorInfo InMaintenance                                        = new MetaplayClientErrorInfo(2300, "in_maintenance",                                       ConnectionLostReason.ServerMaintenance,         "Backend is in maintenance.");

        // 24xx: TerminalError.LogicVersionMismatch
        public static readonly MetaplayClientErrorInfo LogicVersionClientTooOld                             = new MetaplayClientErrorInfo(2400, "logic_version_client_too_old",                         ConnectionLostReason.ClientVersionTooOld,       "The client is too old for the server.");
        public static readonly MetaplayClientErrorInfo LogicVersionClientTooNew                             = new MetaplayClientErrorInfo(2401, "logic_version_client_too_new",                         ConnectionLostReason.ServerMaintenance,         "The client is too new for the server.");
        public static readonly MetaplayClientErrorInfo LogicVersionInvalidMismatch                          = new MetaplayClientErrorInfo(2402, "logic_version_invalid_mismatch",                       ConnectionLostReason.ServerMaintenance,         "Unexpected client-server version mismatch.");

        // 25xx: TerminalError.CommitIdMismatch
        public static readonly MetaplayClientErrorInfo CommitIdMismatch                                     = new MetaplayClientErrorInfo(2500, "commit_id_mismatch",                                   ConnectionLostReason.InternalError,             "The client and the server have mismatching commit ids. This is only reported if the game has been configured to check for matching commit ids.");

        // 26xx: TerminalError.WireFormatError
        public static readonly MetaplayClientErrorInfo WireFormatError_HasCompletedSessionInit              = new MetaplayClientErrorInfo(2600, "wire_format_error_session_init_done",                  ConnectionLostReason.InternalError,             "The client failed to parse a packet that was sent by the server.");
        public static readonly MetaplayClientErrorInfo WireFormatError_HasCompletedHandshake                = new MetaplayClientErrorInfo(2601, "wire_format_error_handshake_done",                     ConnectionLostReason.InternalError,             "The client failed to parse a packet that was sent by the server.");
        public static readonly MetaplayClientErrorInfo WireFormatError_HasNotCompletedHandshake             = new MetaplayClientErrorInfo(2602, "wire_format_error_handshake_not_done",                 ConnectionLostReason.InternalError,             "The client failed to parse a packet that was sent by the server.");

        // 27xx: TerminalError.NoNetworkConnectivity
        public static readonly MetaplayClientErrorInfo NoNetworkConnectivity                                = new MetaplayClientErrorInfo(2700, "no_network_connectivity",                              ConnectionLostReason.NoInternetConnection,      "No internet connection.");

        // 28xx: TerminalError.ClientSideConnectionError
        public static readonly MetaplayClientErrorInfo DeviceLocalStorageWriteError                         = new MetaplayClientErrorInfo(2800, "device_storage_write_error",                           ConnectionLostReason.DeviceLocalStorageError,   "Could not write account credentials to the client device's storage.");
        public static readonly MetaplayClientErrorInfo ClientSideConnectionError                            = new MetaplayClientErrorInfo(2801, "client_side_connection_error",                         ConnectionLostReason.InternalError,             "Unexpected client-side connection management error.");

        // 29xx: TerminalError.PlayerIsBanned
        public static readonly MetaplayClientErrorInfo PlayerIsBanned                                       = new MetaplayClientErrorInfo(2900, "player_is_banned",                                     ConnectionLostReason.PlayerIsBanned,            "The player is banned.");

        // 30xx: TerminalError.ServerPlayerDeserializationFailure
        public static readonly MetaplayClientErrorInfo ServerPlayerDeserializationFailure                   = new MetaplayClientErrorInfo(3000, "player_deserialization_failure",                       ConnectionLostReason.InternalError,             "The server-side persisted player state failed to be deserialized.");

        // 31xx: TerminalError.LoginProtocolVersionMismatch
        public static readonly MetaplayClientErrorInfo LoginProtocolVersionClientTooOld                      = new MetaplayClientErrorInfo(3100, "login_protocol_version_client_too_old",               ConnectionLostReason.ClientVersionTooOld,       "The client is too old for the server.");
        public static readonly MetaplayClientErrorInfo LoginProtocolVersionClientTooNew                      = new MetaplayClientErrorInfo(3101, "login_protocol_version_client_too_new",               ConnectionLostReason.ServerMaintenance,         "The client is too new for the server.");
        public static readonly MetaplayClientErrorInfo LoginProtocolVersionInvalidMismatch                   = new MetaplayClientErrorInfo(3102, "login_protocol_version_invalid_mismatch",             ConnectionLostReason.ServerMaintenance,         "Unexpected client-server version mismatch.");

        // 80xx: SocialAuthenticateForceReconnectConnectionError
        public static readonly MetaplayClientErrorInfo SocialAuthenticateForceReconnect                     = new MetaplayClientErrorInfo(8000, "social_authentication_force_reconnect",                ConnectionLostReason.ConnectionLost,            "The client chose to switch to another player account via an external authentication mechanism (e.g. Facebook Login), and needs to reconnect to access the new player account.");

        // 81xx: SocialAuthenticateForceReconnectConnectionError
        public static readonly MetaplayClientErrorInfo PlayerChecksumMismatch                               = new MetaplayClientErrorInfo(8100, "player_checksum_mismatch",                             ConnectionLostReason.InternalError,             "A player state mismatch was detected between the client and the server.");

        // 82xx: ClientTerminatedConnection
        public static readonly MetaplayClientErrorInfo ClientTerminatedConnection                           = new MetaplayClientErrorInfo(8200, "client_terminated_connection",                         ConnectionLostReason.ConnectionLost,            "The client terminated the connection due to the usage of a debug/development utility which requires it.");

        // 90xx: TerminalError.Unknown
        public static readonly MetaplayClientErrorInfo ExplicitUnknown                                      = new MetaplayClientErrorInfo(9000, "unknown",                                              ConnectionLostReason.InternalError,             "Unknown error.");

        // 91xx: error wasn't handled in MetaplayClient.HandleConnectionError
        public static readonly MetaplayClientErrorInfo Unhandled                                            = new MetaplayClientErrorInfo(9100, "unhandled",                                            ConnectionLostReason.InternalError,             "Unknown error.");

#if UNITY_EDITOR
#pragma warning disable MP_WGL_00 // "Feature is poorly supported in WebGL" (regarding blocking file IO). False positive, this is editor-only.
        /// <summary>
        /// Write a file with a markdown table containing the
        /// <see cref="MetaplayClientErrorInfo"/>s for documentation purposes.
        /// \todo Not properly "productized" - you'll need to hack a way
        ///       to invoke this method (add a MenuItem or whatever),
        ///       and copy the contents of the resulting file to the correct
        ///       document (currently, connection-management.md).
        /// </summary>
        public static void GenerateDocumentationTable()
        {
            string fileName = $"{nameof(MetaplayClientConnectionErrors)}.md";

            List<List<string>> rows = new List<List<string>>();

            void AddRow(params object[] cells)
            {
                rows.Add(cells.Select(c => Util.ObjectToStringInvariant(c).Replace("|", "\\|")).ToList());
            }

            // Header
            AddRow("TechnicalErrorCode", "TechnicalErrorString", "Player-Facing Reason", "Description");

            // Loop through all the MetaplayClientErrorInfo members and add as rows
            foreach (MemberInfo member in typeof(MetaplayClientConnectionErrors).GetProperties(BindingFlags.Public | BindingFlags.Static).Cast<MemberInfo>()
                                          .Concat(typeof(MetaplayClientConnectionErrors).GetFields(BindingFlags.Public | BindingFlags.Static))
                                          .Where(member => member.GetDataMemberType() == typeof(MetaplayClientErrorInfo)))
            {
                MetaplayClientErrorInfo info = (MetaplayClientErrorInfo)member.GetDataMemberGetValueOnDeclaringType()(null);
                AddRow(info.TechnicalCode, info.TechnicalString, info.Reason, info.DocString);
            }

            // Figure out max cell length in each column, for nice table formatting
            List<int> columnLengths = Enumerable.Range(0, rows[0].Count)
                                      .Select(colNdx => rows.Select(r => r[colNdx]).Max(c => c.Length))
                                      .ToList();

            // Kludgeish: after header row, add a row with dashes in each "cell", to get the proper table syntax.
            // Note that this doesn't actually represent a row in the table, it's just part of the syntax.
            rows.Insert(1, columnLengths.Select(len => new string('-', len)).ToList());

            // Output the rows in table syntax.

            StringBuilder output = new StringBuilder();

            output.AppendLine("<!-- Note: This markdown table is generated by the C# MetaplayClientConnectionErrors.GenerateDocumentationTable. -->");
            output.AppendLine();

            foreach (List<string> row in rows)
            {
                foreach ((string cell, int columnLength) in row.Zip(columnLengths, ValueTuple.Create))
                {
                    output.Append("| ");
                    output.Append(cell);
                    output.Append(" ");
                    output.Append(new string(' ', columnLength - cell.Length));
                }
                output.Append("|");
                output.AppendLine();
            }

            File.WriteAllText(fileName, output.ToString());

            DebugLog.Info($"{nameof(MetaplayClientConnectionErrors)} doc table written to {Path.GetFullPath(fileName)}");
        }
#pragma warning restore MP_WGL_00
#endif
    }

    public readonly struct MetaplayClientErrorInfo
    {
        /// <inheritdoc cref="ConnectionLostEvent.TechnicalErrorCode"/>
        public readonly int                     TechnicalCode;
        /// <inheritdoc cref="ConnectionLostEvent.TechnicalErrorString"/>
        public readonly string                  TechnicalString;
        /// <inheritdoc cref="ConnectionLostEvent.Reason"/>
        public readonly ConnectionLostReason    Reason;

        /// <summary>
        /// A description about the error, included in SDK documentation.
        /// </summary>
        public readonly string                  DocString;

        public MetaplayClientErrorInfo(int technicalCode, string technicalString, ConnectionLostReason reason, string docString)
        {
            TechnicalCode = technicalCode;
            TechnicalString = technicalString;
            Reason = reason;
            DocString = docString;
        }
    }
}
