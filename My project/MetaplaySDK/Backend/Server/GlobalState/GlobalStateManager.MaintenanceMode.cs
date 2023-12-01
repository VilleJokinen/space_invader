// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.TypeCodes;
using Metaplay.Core;
using System.Collections.Generic;
using System;
using Metaplay.Cloud.Entity;
using System.Threading.Tasks;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services;
using Newtonsoft.Json;
using System.Text;

namespace Metaplay.Server
{
    /// <summary>
    /// Publish a new ScheduledMaintenanceMode to <see cref="GlobalStateManager"/>, which then propagates it further
    /// to all <see cref="GlobalStateProxyActor"/>s in the cluster. GlobalStateManager responds with
    /// a <see cref="UpdateScheduledMaintenanceModeResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.ScheduledMaintenanceModeRequest, MessageDirection.ServerInternal)]
    public class UpdateScheduledMaintenanceModeRequest : MetaMessage
    {
        public ScheduledMaintenanceMode Mode { get; private set; }

        UpdateScheduledMaintenanceModeRequest() { }
        public UpdateScheduledMaintenanceModeRequest(ScheduledMaintenanceMode mode) { Mode = mode; }
    }

    /// <summary>
    /// Response to a <see cref="UpdateScheduledMaintenanceModeRequest"/>. Contains information whether the operation
    /// succeeded and an error message, if it didn't.
    /// </summary>
    [MetaMessage(MessageCodesCore.ScheduledMaintenanceModeResponse, MessageDirection.ServerInternal)]
    public class UpdateScheduledMaintenanceModeResponse : MetaMessage
    {
        public bool     IsSuccess       { get; private set; }
        public string   ErrorMessage    { get; private set; }

        public UpdateScheduledMaintenanceModeResponse() { }
        public UpdateScheduledMaintenanceModeResponse(bool isSuccess) { IsSuccess = isSuccess; }
        public UpdateScheduledMaintenanceModeResponse(string errorMessage) { IsSuccess = false; ErrorMessage = errorMessage; }
    }

    /// <summary>
    /// Representation of scheduled maintenance mode. If EstimationIsValid is true then the
    /// operator entered an estimated duration for the maintenance window in EstimatedDurationInMinutes,
    /// otherwise there is no estimation of how long the maintenance will take
    /// </summary>
    [MetaSerializable]
    public class ScheduledMaintenanceMode
    {
        [MetaMember(1)] public MetaTime StartAt { get; private set; }
        [MetaMember(2)] public int EstimatedDurationInMinutes { get; private set; }
        [MetaMember(3)] public bool EstimationIsValid { get; private set; }
        [MetaMember(4)] public List<ClientPlatform> PlatformExclusions { get; private set; }

        public bool IsInMaintenanceMode(MetaTime time) => time >= StartAt;

        public static ScheduledMaintenanceModeForClient GetEffectiveModeForClient(ScheduledMaintenanceMode globalModeMaybe, ClientPlatform clientPlatform)
        {
            if (globalModeMaybe == null)
            {
                // No scheduled maintenance
                return null;
            }

            if (globalModeMaybe.PlatformExclusions != null && globalModeMaybe.PlatformExclusions.Contains(clientPlatform))
            {
                // Excluded platform
                return null;
            }

            return new ScheduledMaintenanceModeForClient(globalModeMaybe.StartAt, globalModeMaybe.EstimatedDurationInMinutes, globalModeMaybe.EstimationIsValid);
        }
    }

    /// <summary>
    /// Represents status of scheduled maintenance. Returned to the Dashboard inside GlobalStatusResponse
    /// </summary>
    [MetaSerializable]
    public class MaintenanceStatus
    {
        [MetaMember(1)] public ScheduledMaintenanceMode ScheduledMaintenanceMode { get; private set; }
        [MetaMember(2)] public bool IsInMaintenance { get; private set; }

        public MaintenanceStatus() { }
        public MaintenanceStatus(ScheduledMaintenanceMode scheduledMaintenanceMode, bool isInMaintenance)
        {
            ScheduledMaintenanceMode = scheduledMaintenanceMode;
            IsInMaintenance = isInMaintenance;
        }
    }

    public abstract partial class GlobalStateManagerBase<TGlobalState>
    {
        static Prometheus.Gauge c_isInMaintenance = Prometheus.Metrics.CreateGauge("metaplay_in_maintenance", "Is the game server in maintenance mode. Only published by the node with GlobalStateManager.", new Prometheus.GaugeConfiguration { SuppressInitialValue = true });

        /// <summary>
        /// The contents of the maintenance mode hint file, stored in external storage (S3).
        /// </summary>
        class MaintenanceModeHint
        {
            public DateTime StartAt             { get; set; }
            public DateTime? EstimatedEndTime   { get; set; } = null;
            public string Reason                { get; set; } = null;

            public MaintenanceModeHint(DateTime startAt, DateTime? estimatedEndTime, string reason)
            {
                StartAt = startAt;
                EstimatedEndTime = estimatedEndTime;
                Reason = reason;
            }

            public static bool operator == (MaintenanceModeHint a, MaintenanceModeHint b)
            {
                if (ReferenceEquals(a, b))
                    return true;
                else if (a is null || b is null)
                    return false;
                return a.Equals(b);
            }
            public static bool operator != (MaintenanceModeHint a, MaintenanceModeHint b)
            {
                return !(a == b);
            }
            public bool Equals(MaintenanceModeHint other)
            {
                if (other is null)
                    return false;

                return StartAt == other.StartAt &&
                    EstimatedEndTime == other.EstimatedEndTime &&
                    Reason == other.Reason;
            }
            public override bool Equals(object obj)
            {
                return obj is MaintenanceModeHint other && Equals(other);
            }
            public override int GetHashCode() => Util.CombineHashCode(StartAt.GetHashCode(), EstimatedEndTime.GetHashCode(), Reason.GetHashCode());
        }

        MaintenanceModeHint LastWrittenMaintenanceModeHint = new MaintenanceModeHint(MetaTime.DateTimeEpoch, MetaTime.DateTimeEpoch, "");
        static readonly TimeSpan MaintenanceModeCheckInterval = TimeSpan.FromSeconds(5);
        internal class MaintenanceModeTimer { public static readonly MaintenanceModeTimer Instance = new MaintenanceModeTimer(); }

        void InitializeMaintenanceMode()
        {
            StartPeriodicTimer(TimeSpan.FromSeconds(5), MaintenanceModeCheckInterval, MaintenanceModeTimer.Instance);
        }

        async Task OnShutdownMaintenanceMode()
        {
            // Ensure that the server status hint file is up-to-date.
            MaintenanceModeHint maintenanceModeHint = CalculateMaintenanceModeHint();
            if (maintenanceModeHint == null)
            {
                // When we are in a graceful shutdown outside of a maintenance window we need to
                // let the outside world know that we are in a "maintenance mode".
                maintenanceModeHint = new MaintenanceModeHint(MetaTime.Now.ToDateTime(), null, "Shutdown");
            }
            await UpdateServerStatusHintFile(maintenanceModeHint);
        }

        /// <summary>
        /// Handle timer message that tells us to periodically check whether we are in
        /// maintenance mode or not.
        /// </summary>
        /// <param name="_"></param>
        [CommandHandler]
        async Task HandleMaintenanceModeTimer(MaintenanceModeTimer _)
        {
            MaintenanceModeHint maintenanceModeHint = CalculateMaintenanceModeHint();

            bool isInMaintenance = maintenanceModeHint != null;
            c_isInMaintenance.Set(isInMaintenance ? 1.0 : 0.0);

            // Potentially update the external server status hint file.
            await UpdateServerStatusHintFile(maintenanceModeHint);
        }

        /// <summary>
        /// Get status of the maintenance mode (if is it running or it's scheduled).
        /// </summary>
        MaintenanceStatus GetMaintenanceStatusAtNow()
        {
            MetaTime timeNow = MetaTime.Now;
            ScheduledMaintenanceMode scheduledMaintenanceMode = _state.ScheduledMaintenanceMode;
            bool isInMaintenanceMode = scheduledMaintenanceMode != null ? scheduledMaintenanceMode.IsInMaintenanceMode(timeNow) : false;
            return new MaintenanceStatus(scheduledMaintenanceMode, isInMaintenanceMode);
        }

        /// <summary>
        /// Calculate details about maintenance mode that we would like to put into the
        /// server status hint file. Returns null if server is not in maintenance.
        /// </summary>
        MaintenanceModeHint CalculateMaintenanceModeHint()
        {
            ScheduledMaintenanceMode mode = _state.ScheduledMaintenanceMode;
            bool shouldBeInMaintenance = (mode != null) ? mode.IsInMaintenanceMode(MetaTime.Now) : false;
            if (shouldBeInMaintenance)
            {
                MaintenanceModeHint details = new MaintenanceModeHint(mode.StartAt.ToDateTime(), null , "Scheduled");
                if (mode.EstimationIsValid)
                {
                    MetaTime estimatedTime = mode.StartAt + MetaDuration.FromMinutes(mode.EstimatedDurationInMinutes);
                    details.EstimatedEndTime = estimatedTime.ToDateTime();
                }
                return details;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Update server status hint file.
        /// </summary>
        /// <param name="maintenanceModeHint"></param>
        async Task UpdateServerStatusHintFile(MaintenanceModeHint maintenanceModeHint)
        {
            // If the details have changed since the last write then update the file.
            if (LastWrittenMaintenanceModeHint != maintenanceModeHint)
            {
                _log.Info("Updating server status hint file");

                // Convert to json.
                string jsonDetails = Newtonsoft.Json.JsonConvert.SerializeObject(
                    new
                    {
                        MaintenanceMode = maintenanceModeHint
                    },
                    Newtonsoft.Json.Formatting.None,
                    new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    }
                );

                try
                {
                    // Write.
                    // \todo: should this be cached? Each create opens a new S3Client, which can be take some time? But this does not happen often.
                    using (IBlobStorage storage = RuntimeOptionsRegistry.Instance.GetCurrent<BlobStorageOptions>().CreatePublicBlobStorage("Volatile"))
                    {
                        await storage.PutAsync("serverStatusHint.json", Encoding.UTF8.GetBytes(jsonDetails));
                    }

                    // Remember the last value written.
                    LastWrittenMaintenanceModeHint = maintenanceModeHint;
                }
                catch (Exception ex)
                {
                    // Ignore failures, this is not critical. Client would only use this in the case of an error.
                    _log.Error("Failed while writing server status hint file: {Exception}", ex);
                }
            }
        }

        [EntityAskHandler]
        async Task<UpdateScheduledMaintenanceModeResponse> HandleUpdateScheduledMaintenanceModeRequest(UpdateScheduledMaintenanceModeRequest mode)
        {
            if (mode.Mode != _state.ScheduledMaintenanceMode)
            {
                // Store new details
                _state.ScheduledMaintenanceMode = mode.Mode;

                // Persist & publish to proxies
                await PersistStateIntermediate();
                PublishMessage(EntityTopic.Member, mode);

                // Update maintenance mode hint if necessary
                MaintenanceModeHint maintenanceModeHint = CalculateMaintenanceModeHint();
                await UpdateServerStatusHintFile(maintenanceModeHint);
            }

            // Respond
            return new UpdateScheduledMaintenanceModeResponse(isSuccess: true);
        }
    }
}
