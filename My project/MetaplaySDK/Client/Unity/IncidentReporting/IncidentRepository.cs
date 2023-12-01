// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Debugging;
using Metaplay.Core.Serialization;
using Metaplay.Core.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Metaplay.Unity.IncidentReporting
{
    /// <summary>
    /// Client-side repository for storing incident reports on disk until they are delivered to server.
    /// </summary>
    public class IncidentRepository
    {
        const int                   MaxPendingReports   = 5;  // Maximum number of pending incident reports to store on the client.
        const string                ReportFileSuffix    = ".mpi";

        public readonly struct PendingIncident
        {
            public readonly string IncidentId;
            public readonly string Type;
            public readonly string SubType;
            public readonly string Reason;

            public PendingIncident(string incidentId, string type, string subType, string reason)
            {
                IncidentId = incidentId;
                Type = type;
                SubType = subType;
                Reason = reason;
            }

            public ClientAvailableIncidentReport ToClientUploadProposal()
            {
                return new ClientAvailableIncidentReport(
                    incidentId: IncidentId,
                    type:       Type,
                    subType:    SubType,
                    reason:     Reason);
            }
        }

        LogChannel                  _log;

        readonly string             _directory;
        readonly object             _lock;
        PendingIncident[]           _pendingIncidents;
        Dictionary<string, byte[]>  _latestReportData;
        HashSet<string>             _pendingDeletes;
        TaskQueueExecutor           _executor;

        public IncidentRepository()
        {
            _log = MetaplaySDK.Logs.Incidents;
            _directory = Path.Combine(MetaplaySDK.PersistentDataPath, "MetaplayIncidentReports");
            _lock = new object();

            _pendingIncidents = Array.Empty<PendingIncident>();
            _latestReportData = new Dictionary<string, byte[]>();
            _pendingDeletes = new HashSet<string>();

            _executor = new TaskQueueExecutor(MetaTask.BackgroundScheduler);
            _executor.EnqueueAsync(async () => await InitializeAsync());
        }

        /// <summary>
        /// Returns all reports that have been written to the repository.
        /// </summary>
        public PendingIncident[] GetAll()
        {
            lock (_lock)
            {
                return _pendingIncidents;
            }
        }

        /// <summary>
        /// Adds the report to the repository. The writing is executed in the background and the incident may not become immediately visible in <see cref="GetAll"/>.
        /// In any case, the incident will be immediately accessible via <see cref="TryGetReportAsync(string)"/>
        /// <para>
        /// If <paramref name="isVisible"/> is <c>false</c>, the incident is not visible in <see cref="GetAll"/> until <br/>
        /// 1) the report with the same id is added again with <paramref name="isVisible"/> is <c>true</c>, or <br/>
        /// 2) writing to disk completes and application is restarted. <br/>
        /// This allows application to write a "minimal" pre-report and then amend it later.
        /// </para>
        /// </summary>
        public void AddOrUpdate(PlayerIncidentReport report, bool isVisible = true)
        {
            byte[] payload = MetaSerialization.SerializeTagged<PlayerIncidentReport>(report, MetaSerializationFlags.IncludeAll, logicVersion: null);
            PendingIncident pendingIncident = ReportToPendingIncident(report);

            lock (_lock)
            {
                // Add to uncommitted write cache
                _latestReportData[report.IncidentId] = payload;

                // Enqueue write to disk
                _executor.EnqueueAsync(async () => await WriteIncidentAsync(report.IncidentId, payload, isVisible, pendingIncident));
            }
        }

        /// <summary>
        /// Removes the report from the repository. Any pending write operations are cancelled. The filesystem operation is executed in the
        /// background, but the change is reflected in <see cref="GetAll()"/> immediately.
        /// </summary>
        public void Remove(string incidentId)
        {
            lock (_lock)
            {
                // Remove visibility
                List<PendingIncident> pendingIncidents = new List<PendingIncident>(_pendingIncidents);
                pendingIncidents.RemoveAll(pendingIncident => pendingIncident.IncidentId == incidentId);
                _pendingIncidents = pendingIncidents.ToArray();

                // "Cancel" any writes
                _latestReportData.Remove(incidentId);
                _pendingDeletes.Add(incidentId);

                // Enqueue disk update
                _executor.EnqueueAsync(async () => await DeleteIncidentAsync(incidentId));
            }
        }

        /// <summary>
        /// Returns the report or null if no such report exists. Reports may be deleted at any time.
        /// </summary>
        public async Task<PlayerIncidentReport> TryGetReportAsync(string incidentId)
        {
            // Check uncommitted data first
            lock (_lock)
            {
                if (_pendingDeletes.Contains(incidentId))
                    return null;

                if (_latestReportData.TryGetValue(incidentId, out byte[] payloadInWriteQueue))
                {
                    return MetaSerialization.DeserializeTagged<PlayerIncidentReport>(payloadInWriteQueue, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                }
            }

            // Check persisted storage
            string filePath = GetReportPath(incidentId);
            try
            {
                byte[] payloadOnPersistedStorage = await FileUtil.ReadAllBytesAsync(filePath);
                return MetaSerialization.DeserializeTagged<PlayerIncidentReport>(payloadOnPersistedStorage, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
            }
            catch
            {
            }

            return null;
        }

        async Task InitializeAsync()
        {
            List<PendingIncident> pendingIncidents = new List<PendingIncident>();

            // Small delay start scanning after application has started up.
            await MetaTask.Delay(1000);

            // Read all from the folder.
            await DirectoryUtil.EnsureDirectoryExistsAsync(_directory);
            string[] existingReportPaths = await DirectoryUtil.GetDirectoryFilesAsync(_directory);
            foreach (string filePath in existingReportPaths)
            {
                if (!filePath.EndsWith(ReportFileSuffix))
                    continue;

                string incidentId = Path.GetFileNameWithoutExtension(filePath);

                try
                {
                    byte[] payload = await FileUtil.ReadAllBytesAsync(filePath);
                    PlayerIncidentReport report = MetaSerialization.DeserializeTagged<PlayerIncidentReport>(payload, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);
                    pendingIncidents.Add(ReportToPendingIncident(report));
                }
                catch (Exception ex)
                {
                    try
                    {
                        // Cannot parse, so delete it and replace it with an internal failure.
                        await FileUtil.DeleteAsync(filePath);

                        PlayerIncidentReport replacementIncident = IncidentReportFactory.CreateReportForBrokenReport(incidentId, ex);
                        byte[] payload = MetaSerialization.SerializeTagged<PlayerIncidentReport>(replacementIncident, MetaSerializationFlags.IncludeAll, logicVersion: null);
                        await FileUtil.WriteAllBytesAsync(filePath, payload);

                        pendingIncidents.Add(ReportToPendingIncident(replacementIncident));
                    }
                    catch
                    {
                        // Error in error handling. Just ignore.
                    }
                }
            }

            // Sort by occurredAt (encoded in incidentId)
            pendingIncidents.Sort((a, b) => a.IncidentId.CompareTo(b.IncidentId));

            // Purge any old reports. Old reports are at the beginning of the list.
            int numToDelete = Math.Max(0, pendingIncidents.Count - MaxPendingReports);
            for (int ndx = 0; ndx < numToDelete; ++ndx)
            {
                string filePath = GetReportPath(pendingIncidents[ndx].IncidentId);
                try
                {
                    await FileUtil.DeleteAsync(filePath);
                }
                catch
                {
                    // ignored.
                }
            }
            pendingIncidents.RemoveRange(0, numToDelete);

            lock (_lock)
            {
                _pendingIncidents = pendingIncidents.ToArray();
            }

            _log.Info("Incident Repository loaded with {NumIncidents} incidents. Removed {NumToDelete} old reports.", pendingIncidents.Count, numToDelete);
        }

        async Task WriteIncidentAsync(string incidentId, byte[] payload, bool isVisible, PendingIncident pendingIncident)
        {
            byte[] latestData;
            lock (_lock)
            {
                // If data is removed, it means the report deletion has been issued after write, and we
                // should not continue with this write.
                if (!_latestReportData.TryGetValue(incidentId, out latestData))
                    return;
                // If latest data has changed, no point in writing this one. It means
                // we have later data in queue.
                if (!ReferenceEquals(payload, latestData))
                    return;
            }

            try
            {
                string filePath = GetReportPath(incidentId);
                await FileUtil.WriteAllBytesAsync(filePath, payload);
            }
            catch (Exception ex)
            {
                _log.Warning("Failed to write incident report: {Error}", ex);
                return;
            }
            _log.Debug("Report {IncidentId} written to persisted storage.", incidentId);

            // Make report visible, and remove any old reports.
            if (isVisible)
            {
                List<PendingIncident> incidentsToDelete = new List<PendingIncident>();

                lock (_lock)
                {
                    List<PendingIncident> pendingIncidents = new List<PendingIncident>(_pendingIncidents);
                    pendingIncidents.RemoveAll(pendingIncident => pendingIncident.IncidentId == incidentId);
                    pendingIncidents.Add(pendingIncident);
                    pendingIncidents.Sort((a, b) => a.IncidentId.CompareTo(b.IncidentId));

                    int numToDelete = Math.Max(0, pendingIncidents.Count - MaxPendingReports);
                    for (int ndx = 0; ndx < numToDelete; ++ndx)
                        incidentsToDelete.Add(pendingIncidents[ndx]);

                    pendingIncidents.RemoveRange(0, numToDelete);
                    _pendingIncidents = pendingIncidents.ToArray();
                }

                foreach (PendingIncident incidentToDelete in incidentsToDelete)
                {
                    string filePath = GetReportPath(incidentToDelete.IncidentId);
                    try
                    {
                        await FileUtil.DeleteAsync(filePath);
                    }
                    catch
                    {
                        // ignored.
                    }
                }
            }

            // Remove the written data from the uncommitted queue
            lock (_lock)
            {
                if (_latestReportData.TryGetValue(incidentId, out latestData) && ReferenceEquals(payload, latestData))
                    _latestReportData.Remove(incidentId);
            }
        }

        async Task DeleteIncidentAsync(string incidentId)
        {
            string filePath = GetReportPath(incidentId);
            try
            {
                await FileUtil.DeleteAsync(filePath);
            }
            catch
            {
                // ignored.
            }
            _log.Debug("Report {IncidentId} purged from persisted storage.", incidentId);

            lock (_lock)
            {
                _pendingDeletes.Remove(incidentId);
            }
        }

        string GetReportPath(string incidentId) => Path.Combine(_directory, $"{incidentId}{ReportFileSuffix}");

        static PendingIncident ReportToPendingIncident(PlayerIncidentReport report)
        {
            return new PendingIncident(
                incidentId:     report.IncidentId,
                type:           report.Type,
                subType:        report.SubType,
                reason:         PlayerIncidentUtil.TruncateReason(report.GetReason()));
        }
    }
}
