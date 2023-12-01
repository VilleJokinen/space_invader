// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Utility;
using Metaplay.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    [RuntimeOptions("PushNotification", isStatic: true, "Configuration options for sending push notifications.")]
    public class PushNotificationOptions : RuntimeOptionsBase
    {
        [MetaDescription("Enables push notifications via Firebase Cloud Messaging.")]
        public bool         Enabled                     { get; private set; } = false;
        [MetaDescription("The file path to the Firebase credentials file.")]
        public string       FirebaseCredentialsPath     { get; private set; } = null;

        public override Task OnLoadedAsync()
        {
            // \todo [petri] #options resolve credentials here?

            return Task.CompletedTask;
        }
    }

    [RuntimeOptions("System", isStatic: true, "Configuration options for the game server.")]
    public class SystemOptions : RuntimeOptionsBase
    {
        /// <summary> Default port on which to listen to for incoming connections from clients. </summary>
        public const int DefaultClientListenPort = 9339;

        [MetaDescription("The list of ports to listen on for incoming client connections.")]
        public int[]        ClientPorts                 { get; private set; }
        [MetaDescription("The file path to the statically built `GameConfig`.")]
        public string       StaticGameConfigPath        { get; private set; } = "./GameConfig/StaticGameConfig.mpa";
        [MetaDescription("Enables in-memory deduplication of game config content across different experiment specializations.")]
        public bool         EnableGameConfigInMemoryDeduplication { get; private set; } = true;
        // \note: For API Compatibility
        [MetaDescription("When enabled, the server must have a valid `GameConfig` to start.")]
        public bool         MustHaveGameConfigToStart => true;
        [MetaDescription("The path to the statically built `Localization` file or folder.")]
        public string       StaticLocalizationsPath     { get; private set; } = "./GameConfig/Localizations.mpa";
        [MetaDescription("If path in `StaticLocalizationsPath` should be read as a folder. Otherwise assumed to be a file.")]
        public bool         StaticLocalizationsIsFolder { get; private set; } = false;
        // \note: For API Compatibility
        [MetaDescription("When enabled, the server must have a valid `Localization` to start.")]
        public bool         MustHaveLocalizationToStart => true;
        [MetaDescription("When enabled, the `ActiveLogicVersion` is automatically upgraded to the latest supported version when the server starts.")]
        public bool         AutoUpgradeLogicVersion     { get; private set; } = IsLocalEnvironment; // auto-upgrade locally
        [MetaDescription("When enabled, allows bots to login and register accounts on the server.")]
        public bool         AllowBotAccounts            { get; private set; } = !IsProductionEnvironment;
        [MetaDescription("The default delay before permanently deleting a player account. This can be overidden in the LiveOps Dashboard.")]
        public TimeSpan     PlayerDeletionDefaultDelay  { get; private set; } = TimeSpan.FromDays(7);
        [MetaDescription("The UTC hour when the database sweep to permanently remove deleted players begins.")]
        public TimeSpan     PlayerDeletionSweepTimeOfDay{ get; private set; } = TimeSpan.FromHours(2);
        [MetaDescription("The maximum amount of time that incident reports are stored before being purged.")]
        public TimeSpan     IncidentReportRetentionPeriod { get; private set; } = (IsProductionEnvironment || IsStagingEnvironment) ? TimeSpan.FromDays(14) : TimeSpan.FromDays(30);
        [MetaDescription("The maximum number of incident reports to be purged in a single purge cycle.")]
        public int          IncidentReportPurgeMaxItems { get; private set; } = 5000;
        [MetaDescription("The maximum amount of time that the LiveOps Dashboard audit log events are stored before being purged.")]
        public TimeSpan     AuditLogRetentionPeriod     { get; private set; } = TimeSpan.FromDays(365);
        [MetaDescription("Maximum size allowed for a notification campaign's or broadcast's target players list. This doesn't affect segment targeting. A value of 0 disables the target players list.")]
        public int          MaxTargetPlayersListSize { get; private set; } = 1000;

        public override Task OnLoadedAsync()
        {
            // Defaults

            if (ClientPorts == null)
                ClientPorts = new int[] { DefaultClientListenPort };

            // Validate the options

            if (!(PlayerDeletionSweepTimeOfDay.TotalHours >= 0 && PlayerDeletionSweepTimeOfDay.TotalHours < 24))
                throw new InvalidOperationException("PlayerDeletionSweepTimeOfDay must be a valid hour of the day");

            if (MetaplayCore.Options.FeatureFlags.EnableLocalizations)
            {
                if (string.IsNullOrEmpty(StaticLocalizationsPath))
                    throw new InvalidOperationException("FeatureFlags.EnableLocalizations is true, but System:StaticLocalizationsPath is not defined!");

                if (!string.IsNullOrEmpty(StaticLocalizationsPath))
                {
                    if (!StaticLocalizationsIsFolder)
                    {
                        if (!File.Exists(StaticLocalizationsPath))
                        {
                            if (MustHaveLocalizationToStart)
                                throw new InvalidOperationException($"System:StaticLocalizationsPath is set to {StaticLocalizationsPath}, but no such file exists.");
                            Log.Warning("System:StaticLocalizationsPath is set to {StaticGameConfigPath}, but no such file exists. Continuing anyway.", StaticGameConfigPath);
                        }
                    }
                    else
                    {
                        if (!Directory.Exists(StaticLocalizationsPath))
                        {
                            if (MustHaveLocalizationToStart)
                                throw new InvalidOperationException($"System:StaticLocalizationsPath is set to {StaticLocalizationsPath}, but no such directory exists.");
                            Log.Warning("System:StaticLocalizationsPath is set to {StaticGameConfigPath}, but no such directory exists. Continuing anyway.", StaticGameConfigPath);
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(StaticGameConfigPath) && !File.Exists(StaticGameConfigPath))
            {
                if (MustHaveGameConfigToStart)
                    throw new InvalidOperationException($"System:StaticGameConfigPath is set to {StaticGameConfigPath}, but no such file exists.");
                Log.Warning("System:StaticGameConfigPath is set to {StaticGameConfigPath}, but no such file exists. Continuing anyway.", StaticGameConfigPath);
            }

            return Task.CompletedTask;
        }
    }

    [RuntimeOptions("ClientConnection", isStatic: false, "Configuration options regarding the connections with game clients.")]
    public class ClientConnectionOptions : RuntimeOptionsBase
    {
        [MetaDescription("Whether to compress large packets in the connection. This affects both server-to-client and client-to-server packets.")]
        public bool EnableWireCompression { get; private set; } = true;
    }

    [RuntimeOptions("GoogleSheets", isStatic: false, "Configuration options for Google Sheets integration.")]
    public class GoogleSheetOptions : RuntimeOptionsBase
    {
        [MetaDescription("The path to the Google credentials JSON file.")]
        public string CredentialsPath { get; private set; } = null;

        [Sensitive]
        [MetaDescription("Google credentials as a JSON string. Overrides `CredentialsPath` when specified.")]
        public string CredentialsJson { get; private set; } = null;

        public async override Task OnLoadedAsync()
        {
            if (CredentialsJson == null && !string.IsNullOrEmpty(CredentialsPath))
            {
                CredentialsJson = await SecretUtil.ResolveSecretAsync(Log, CredentialsPath);
            }
        }
    }

    [RuntimeOptions("GameConfigBuild", isStatic: false, "Configuration options for building the Game Config.")]
    public class GameConfigBuildOptions : RuntimeOptionsBase
    {
        [MetaDescription("Prevent configs with warnings from being published.")]
        public bool TreatWarningsAsErrors { get; private set; } = false;
    }

    [RuntimeOptions("Segmentation", isStatic: false, "Configuration options for estimating the player segment sizes.")]
    public class SegmentationOptions : RuntimeOptionsBase
    {
        [MetaDescription("How often the segments are sampled. Segment size estimates are created by averaging a number of samples.")]
        public TimeSpan SizeSamplingInterval    { get; private set; } = TimeSpan.FromMinutes(6);

        [MetaDescription("The number of players to sample per segment.")]
        public int SizeSampleCount              { get; private set; } = 100;

        [MetaDescription("The maximum number of samples to store.")]
        public int MaxSampleSetsToStore         { get; private set; } = 100;

        [MetaDescription("The maximum age of a sample. Samples older than this value are no longer used in the estimate.")]
        public TimeSpan MaxSizeSampleAge        { get; private set; } = TimeSpan.FromDays(2);
    }

    [RuntimeOptions("PlayerExperiments", isStatic: false, "Configuration options for player experiments.")]
    public class PlayerExperimentOptions : RuntimeOptionsBase
    {
        [MetaDescription("The threshold where we start showing warnings in the experiment configuration view.")]
        public int PlayerExperimentCombinationThreshold { get; private set; } = 400;
    }
}
