// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;

namespace Metaplay.Core.Debugging
{
    /// <summary>
    /// Client-announced pending incident report.
    /// </summary>
    [MetaSerializable]
    public class ClientAvailableIncidentReport
    {
        [MetaMember(1)] public string   IncidentId      { get; private set; }
        [MetaMember(2)] public string   Type            { get; private set; }
        [MetaMember(3)] public string   SubType         { get; private set; }
        [MetaMember(4)] public string   Reason          { get; private set; }

        ClientAvailableIncidentReport() { }
        public ClientAvailableIncidentReport(string incidentId, string type, string subType, string reason)
        {
            IncidentId = incidentId;
            Type = type;
            SubType = subType;
            Reason = reason;
        }
    }

    /// <summary>
    /// Client informs the server of available player incidents that have happened in the past and have not yet been uploaded.
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerAvailableIncidentReports, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerAvailableIncidentReports : MetaMessage
    {
        public ClientAvailableIncidentReport[] IncidentHeaders { get; private set; }

        PlayerAvailableIncidentReports() { }
        public PlayerAvailableIncidentReports(ClientAvailableIncidentReport[] incidentHeaders) => IncidentHeaders = incidentHeaders;
    }

    /// <summary>
    /// Server requests the client to upload the given incident reports.
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerRequestIncidentReportUploads, MessageDirection.ServerToClient)]
    public class PlayerRequestIncidentReportUploads : MetaMessage
    {
        public List<string> IncidentIds { get; private set; }

        PlayerRequestIncidentReportUploads() { }
        public PlayerRequestIncidentReportUploads(List<string> incidentIds) => IncidentIds = incidentIds;
    }

    /// <summary>
    /// Client uploads a single incident report to the server.
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerUploadIncidentReport, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class PlayerUploadIncidentReport : MetaMessage
    {
        public const int MaxCompressedPayloadSize   = 800 * 1024;
        public const int MaxUncompressedPayloadSize = 4 * 1024 * 1024;
        public const int MaxLogEntriesTotalUtf8Size = 3 * 1024 * 1024;

        public string   IncidentId  { get; private set; }
        public byte[]   Payload     { get; private set; }   // Deflate-compressed, TaggedSerialized<PlayerIncidentReport>

        public PlayerUploadIncidentReport() { }
        public PlayerUploadIncidentReport(string incidentId, byte[] payload)
        {
            if (payload.Length > MaxCompressedPayloadSize)
                throw new InvalidOperationException($"Too large payload ({payload.Length}, max={MaxCompressedPayloadSize}) for PlayerUploadIncidentReport");

            IncidentId  = incidentId;
            Payload     = payload;
        }
    }

    /// <summary>
    /// Server acknowledges the upload of an incident report. The client should then delete it.
    /// </summary>
    [MetaMessage(MessageCodesCore.PlayerAckIncidentReportUpload, MessageDirection.ServerToClient)]
    public class PlayerAckIncidentReportUpload : MetaMessage
    {
        public string IncidentId { get; private set; }

        public PlayerAckIncidentReportUpload() { }
        public PlayerAckIncidentReportUpload(string incidentId) { IncidentId = incidentId; }
    }
}
