// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Core.Message
{
    /// <summary>
    /// Debug diagnostics sent in login message
    /// </summary>
    [MetaSerializable]
    public class LoginDebugDiagnostics
    {
        [MetaMember(5)] public MetaTime                                 Timestamp;
        [MetaMember(1)] public LoginSessionDebugDiagnostics             Session;
        [MetaMember(6)] public LoginServerConnectionDebugDiagnostics    ServerConnection;
        [MetaMember(7)] public LoginTransportDebugDiagnostics           Transport;
        [MetaMember(8)] public LoginIncidentReportDebugDiagnostics      IncidentReport;
        [MetaMember(9)] public LoginMainLoopDebugDiagnostics            MainLoop;
        [MetaMember(2)] public MetaDuration?                            CurrentPauseDuration;
        [MetaMember(11)] public MetaDuration?                           DurationSincePauseEnd;
        [MetaMember(3)] public MetaDuration?                            DurationSinceConnectionUpdate;
        [MetaMember(4)] public MetaDuration?                            DurationSincePlayerContextUpdate;
        /// <summary>
        /// Whether client intends to send a SessionPing after each session resumption.
        /// Used to debug certain kinds of connection issues. False in particular for bot clients.
        /// </summary>
        [MetaMember(12)] public bool                                    ExpectSessionResumptionPing;

        [MetaMember(10)] public string                                  DiagnosticsError;
    }

    [MetaSerializable]
    public class LoginSessionDebugDiagnostics
    {
        /// <summary> Client's <see cref="SessionParticipantState.NumSent"/>. </summary>
        [MetaMember(1)] public int NumSent                                  { get; set; }
        /// <summary> Client's <see cref="SessionParticipantState.RememberedSent"/>.Count. </summary>
        [MetaMember(2)] public int NumRememberedSent                        { get; set; }
        /// <summary> Serialization type codes of the first few MetaMessages the client has sent and server hasn't yet acknowledged. </summary>
        [MetaMember(6)] public int[] FirstNRememberedSentMessageTypeCodes   { get; set; }
        /// <summary> Total size of the serialized Operations in any PlayerFlushActions messages pending in the session send queue </summary>
        [MetaMember(3)] public int TotalPendingFlushActionsOperationsBytes  { get; set; }
        /// <summary> Total length of Checksums arrays in any PlayerFlushActions messages pending in the session send queue </summary>
        [MetaMember(4)] public int TotalPendingFlushActionsChecksums        { get; set; }
        /// <summary> Type name of the previous IMessageTransport.Error that caused transport failure </summary>
        [MetaMember(5)] public string PreviousTransportErrorName            { get; set; }

        public string[] FirstNRememberedSentMessageTypeNames =>
            FirstNRememberedSentMessageTypeCodes?
            .Select(typeCode =>
            {
                if (MetaMessageRepository.Instance == null)
                    return "<uninit>";
                else if (MetaMessageRepository.Instance.TryGetFromTypeCode(typeCode, out MetaMessageSpec spec))
                    return spec.Name;
                else
                    return "<none>";
            })
            .ToArray();
    }

    [MetaSerializable]
    public class LoginServerConnectionDebugDiagnostics
    {
        public LoginServerConnectionDebugDiagnostics Clone() => (LoginServerConnectionDebugDiagnostics)MemberwiseClone();

        [MetaMember(1)] public int TransportsCreated;
        [MetaMember(2)] public int SessionMessageEnqueuesAttempted;
        [MetaMember(3)] public int SessionMessageImmediateSendEnqueues;
        [MetaMember(4)] public long FirstSessionMessageSentAtMS;
        public MetaTime FirstSessionMessageSentAt => MetaTime.FromMillisecondsSinceEpoch(FirstSessionMessageSentAtMS);
        [MetaMember(5)] public int SessionMessagesDelayedSendEnqueues;
        [MetaMember(6)] public int SessionMessagesEnqueues;
        [MetaMember(7)] public int SessionMessagesDelayedSent;
        [MetaMember(8)] public int StreamClosedErrors;
        [MetaMember(9)] public int StreamIOFailedErrors;
        [MetaMember(10)] public int StreamExecutorErrors;
        [MetaMember(11)] public int ConnectTimeoutErrors;
        [MetaMember(12)] public int HeaderTimeoutErrors;
        [MetaMember(13)] public int ReadTimeoutErrors;
        [MetaMember(14)] public int WriteTimeoutErrors;
        [MetaMember(15)] public int OtherErrors;
        [MetaMember(16)] public int HellosSent;
        [MetaMember(17)] public int InitialLoginsSent;
        [MetaMember(18)] public int ResumptionLoginsSent;
        [MetaMember(19)] public int HellosReceived;
        [MetaMember(20)] public int LoginSuccessesReceived;
        [MetaMember(21)] public long LastLoginSuccessReceivedAtMS;
        public MetaTime LastLoginSuccessReceivedAt => MetaTime.FromMillisecondsSinceEpoch(LastLoginSuccessReceivedAtMS);
        [MetaMember(22)] public int SessionMessagesReceived;
        [MetaMember(23)] public int SessionPayloadMessagesReceived;
    }

    [MetaSerializable]
    public class LoginTransportDebugDiagnostics
    {
        public LoginTransportDebugDiagnostics Clone() => (LoginTransportDebugDiagnostics)MemberwiseClone();

        [MetaMember(1)] public int WritesStarted;
        [MetaMember(2)] public int WritesCompleted;
        [MetaMember(3)] public int ReadsStarted;
        [MetaMember(4)] public int ReadsCompleted;
        [MetaMember(5)] public int MetaMessageEnqueuesAttempted;
        [MetaMember(6)] public int MetaMessageUnconnectedEnqueuesAttempted;
        [MetaMember(7)] public int MetaMessageDisposedEnqueuesAttempted;
        [MetaMember(8)] public int MetaMessagePacketSizesExceeded;
        [MetaMember(9)] public int MetaMessageClosingEnqueuesAttempted;
        [MetaMember(10)] public int MetaMessagesEnqueued;
        [MetaMember(11)] public int PacketEnqueuesAttempted;
        [MetaMember(12)] public int PacketsEnqueued;
        [MetaMember(13)] public int BytesEnqueued;
        [MetaMember(14)] public int MetaMessagesWritten;
        [MetaMember(15)] public int PacketsWritten;
        [MetaMember(16)] public int BytesWritten;
        [MetaMember(17)] public int BytesRead;
        [MetaMember(18)] public int PacketsRead;
        [MetaMember(19)] public int MetaMessagesRead;
        [MetaMember(20)] public int MetaMessagesReceived;
    }

    [MetaSerializable]
    public class LoginIncidentReportDebugDiagnostics
    {
        public LoginIncidentReportDebugDiagnostics Clone() => (LoginIncidentReportDebugDiagnostics)MemberwiseClone();

        [MetaMember(1)] public int CurrentPendingIncidents;
        [MetaMember(2)] public int CurrentRequestedIncidents;
        [MetaMember(3)] public int TotalUploadRequestMessages;
        [MetaMember(4)] public int TotalRequestedIncidents;
        [MetaMember(5)] public int AcknowledgedIncidents;
        [MetaMember(6)] public int UploadsAttempted;
        [MetaMember(7)] public int UploadUnavailable;
        [MetaMember(8)] public int UploadException;
        [MetaMember(9)] public int UploadTooLarge;
        [MetaMember(10)] public int UploadsSent;
    }

    [MetaSerializable]
    public class LoginMainLoopDebugDiagnostics
    {
        public LoginMainLoopDebugDiagnostics Clone() => (LoginMainLoopDebugDiagnostics)MemberwiseClone();

        [MetaMember(1)] public int UpdatesStarted;
        [MetaMember(2)] public int UpdatesEndedPrematurely;
        [MetaMember(3)] public int UpdatesEndedNormally;
    }
}
