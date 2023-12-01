// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Client.Messages;
using Metaplay.Core;
using Metaplay.Core.Debugging;
using Metaplay.Core.Message;
using Metaplay.Core.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Unity.IncidentReporting
{
    /// <summary>
    /// Manages syncing new incidents from repository to server and deleting them after upload is complete.
    /// </summary>
    public class IncidentUploader
    {
        enum State
        {
            /// <summary>
            /// Waiting for incidents to become available.
            /// </summary>
            WaitingForIncidents,

            /// <summary>
            /// Waiting for server to reply to upload proposal.
            /// </summary>
            ProposingUploads,

            /// <summary>
            /// Starting uploading of next requested incidents.
            /// </summary>
            NextUpload,

            /// <summary>
            /// Upload logic is running in background.
            /// </summary>
            Uploading,

            /// <summary>
            /// Waiting for server to acknowledge upload.
            /// </summary>
            WaitingForUploadAck,

            /// <summary>
            /// No connection, nothing to do.
            /// </summary>
            NoConnection
        }

        MetaDuration StartDelayAfterSessionStart => MetaDuration.FromSeconds(5); // Minimum interval to avoid spamming the server
        MetaDuration UploadCooldown => MetaDuration.FromSeconds(10); // Minimum interval to avoid spamming the server
        MetaDuration UploadFailureCooldown => MetaDuration.FromSeconds(1); // Minimum interval avoid IO spam.

        LogChannel _log;

        object _lock;
        State _state;
        MetaTime _nextProposalAt;
        IncidentRepository.PendingIncident[] _proposedUploads;
        MetaTime _nextUploadAt;
        Queue<string> _incidentsToUpload;
        string _ongoingUploadIncident;
        LoginIncidentReportDebugDiagnostics _debugDiagnostics;

        public IncidentUploader()
        {
            _log = MetaplaySDK.Logs.Incidents;

            _state = State.NoConnection;
            _lock = new object();
            _debugDiagnostics = new LoginIncidentReportDebugDiagnostics();

            // Register message listeners
            MetaplaySDK.MessageDispatcher.AddListener<ConnectedToServer>(OnConnectedToServer);
            MetaplaySDK.MessageDispatcher.AddListener<DisconnectedFromServer>(OnDisconnectedFromServer);
            MetaplaySDK.MessageDispatcher.AddListener<PlayerRequestIncidentReportUploads>(OnPlayerRequestIncidentReportUploads);
            MetaplaySDK.MessageDispatcher.AddListener<PlayerAckIncidentReportUpload>(OnPlayerAckIncidentReportUpload);
        }

        public void LateUpdate(MetaTime timeNow)
        {
            // Complete state transition in locked scope, side-effects in unlocked scope.

            IncidentRepository.PendingIncident[] incidents = MetaplaySDK.IncidentRepository.GetAll();
            string nextIncident;
            lock (_lock)
            {
                _debugDiagnostics.CurrentPendingIncidents = incidents.Length;
                _debugDiagnostics.CurrentRequestedIncidents = _state == State.NextUpload ? _incidentsToUpload.Count : 0;

                switch (_state)
                {
                    case State.WaitingForIncidents:
                    {
                        if (incidents.Length == 0)
                            return;
                        if (timeNow < _nextProposalAt)
                            return;

                        _state = State.ProposingUploads;
                        _proposedUploads = incidents;

                        goto complete_waiting_for_incidents;
                    }

                    case State.ProposingUploads:
                    {
                        // nada, waiting for PlayerRequestIncidentReportUploads.
                        return;
                    }

                    case State.NextUpload:
                    {
                        if (timeNow < _nextUploadAt)
                        {
                            // \note: we first wait the cooldown, and only then check if there
                            //        was more work. This allows cooldown to work after last upload.
                            return;
                        }
                        if (_incidentsToUpload.Count == 0)
                        {
                            _state = State.WaitingForIncidents;
                            _nextProposalAt = MetaTime.Now;
                            return;
                        }

                        nextIncident = _incidentsToUpload.Dequeue();

                        _ongoingUploadIncident = nextIncident;
                        _state = State.Uploading;
                        _debugDiagnostics.UploadsAttempted++;

                        goto complete_next_upload;
                    }

                    case State.Uploading:
                    {
                        // nada, waiting for background task to complete
                        return;
                    }

                    case State.WaitingForUploadAck:
                    {
                        // nada, waiting for PlayerAckIncidentReportUpload
                        return;
                    }

                    case State.NoConnection:
                    {
                        // nada, waiting for ConnectedToServer
                        return;
                    }

                    default:
                        return;
                }
            }

            // unreachable

complete_waiting_for_incidents:
            _log.Debug("Informing the server of {NumPendingIncidents} pending incident reports", incidents.Length);
            MetaplaySDK.MessageDispatcher.SendMessage(new PlayerAvailableIncidentReports(_proposedUploads.Select(proposed => proposed.ToClientUploadProposal()).ToArray()));
            return;

complete_next_upload:
            _log.Debug("Uploading incident report {IncidentReport}. ({NumReportsPending} more requests pending)", nextIncident, _incidentsToUpload.Count);
            TryUploadIncidentOnBackground(nextIncident);
            return;
        }

        public LoginIncidentReportDebugDiagnostics GetLoginIncidentReportDebugDiagnostics()
        {
            lock (_lock)
            {
                return _debugDiagnostics.Clone();
            }
        }

        void OnConnectedToServer(ConnectedToServer _)
        {
            // When connection is established, start uploading. In Offline mode there is no
            // point in uploading, and we can pretend no connection was made.
            if (MetaplaySDK.Connection.Endpoint.IsOfflineMode)
                return;

            lock (_lock)
            {
                _state = State.WaitingForIncidents;
                _nextProposalAt = MetaTime.Now + StartDelayAfterSessionStart;
            }
        }

        void OnDisconnectedFromServer(DisconnectedFromServer _)
        {
            lock (_lock)
            {
                _state = State.NoConnection;
            }
        }

        void OnPlayerRequestIncidentReportUploads(PlayerRequestIncidentReportUploads uploadRequest)
        {
            HashSet<string> reportsToDelete = new HashSet<string>();
            lock (_lock)
            {
                _debugDiagnostics.TotalUploadRequestMessages++;
                _debugDiagnostics.TotalRequestedIncidents += uploadRequest.IncidentIds?.Count ?? 0;

                if (_state != State.ProposingUploads)
                    return;

                // Server replied which incidents it is interested in. This means the incidents not on the
                // list are not interesting and should be removed.

                HashSet<string> interestingReports = new HashSet<string>(uploadRequest.IncidentIds);
                foreach (IncidentRepository.PendingIncident proposedUpload in _proposedUploads)
                {
                    if (!interestingReports.Contains(proposedUpload.IncidentId))
                        reportsToDelete.Add(proposedUpload.IncidentId);
                }

                _state = State.NextUpload;
                _proposedUploads = null;
                _nextUploadAt = MetaTime.Now;
                _incidentsToUpload = new Queue<string>(uploadRequest.IncidentIds);
            }

            foreach (string incidentId in reportsToDelete)
                MetaplaySDK.IncidentRepository.Remove(incidentId);
        }

        void OnPlayerAckIncidentReportUpload(PlayerAckIncidentReportUpload ackUpload)
        {
            _log.Info("Acknowledge upload of incident report {IncidentId}, deleting it..", ackUpload.IncidentId);
            MetaplaySDK.IncidentRepository.Remove(ackUpload.IncidentId);

            lock (_lock)
            {
                if (_state != State.WaitingForUploadAck)
                    return;
                if (_ongoingUploadIncident != ackUpload.IncidentId)
                    return;

                _debugDiagnostics.AcknowledgedIncidents++;

                _state = State.NextUpload;
                _ongoingUploadIncident = null;
                _nextUploadAt = MetaTime.Now + UploadCooldown;
            }
        }

        void TryUploadIncidentOnBackground(string incidentId)
        {
            _ = MetaTask.Run(async () =>
            {
                PlayerIncidentReport incident = await MetaplaySDK.IncidentRepository.TryGetReportAsync(incidentId);
                if (incident == null)
                {
                    _log.Warning("Server requested incident report {IncidentId} is no longer available, ignored.", incidentId);

                    // Move to next upload
                    lock (_lock)
                    {
                        _debugDiagnostics.UploadUnavailable++;

                        if (_state == State.Uploading)
                            _state = State.NextUpload;
                        _nextUploadAt = MetaTime.Now + UploadFailureCooldown;
                    }
                    return;
                }

                try
                {
                    byte[] compressedPayload = PlayerIncidentUtil.CompressIncidentForNetworkDelivery(incident);

                    // Send report. Server replies with ack.
                    MetaplaySDK.MessageDispatcher.SendMessage(new PlayerUploadIncidentReport(incidentId, compressedPayload));

                    // Wait for the ack
                    lock (_lock)
                    {
                        _debugDiagnostics.UploadsSent++;

                        if (_state == State.Uploading)
                            _state = State.WaitingForUploadAck;
                    }
                }
                catch (PlayerIncidentUtil.IncidentReportTooLargeException tooLarge)
                {
                    _log.Warning("Failed to compress report: {Error}", tooLarge);
                    _debugDiagnostics.UploadTooLarge++;

                    // Too large incident report. Remove report now...
                    MetaplaySDK.IncidentRepository.Remove(incidentId);

                    // Move to next upload
                    lock (_lock)
                    {
                        if (_state == State.Uploading)
                            _state = State.NextUpload;
                        _nextUploadAt = MetaTime.Now + UploadFailureCooldown;
                    }

                    // ... and create a new incident for the too large incident.
                    MetaplaySDK.IncidentTracker.ReportIncidentReportTooLarge(incident);
                }
            });
        }
    }
}
