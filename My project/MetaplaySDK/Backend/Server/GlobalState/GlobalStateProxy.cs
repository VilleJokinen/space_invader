// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Player;
using Metaplay.Server.GameConfig;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    /// <summary>
    /// Currently active client compatibility settings in use. Updated by <see cref="GlobalStateProxyActor"/>.
    /// </summary>
    public class ActiveClientCompatibilitySettings : IAtomicValue<ActiveClientCompatibilitySettings>
    {
        public readonly ClientCompatibilitySettings ClientCompatibilitySettings;

        public ActiveClientCompatibilitySettings(ClientCompatibilitySettings clientCompatibilitySettings)
        {
            ClientCompatibilitySettings = clientCompatibilitySettings;
        }

        public bool Equals(ActiveClientCompatibilitySettings other)
        {
            // \todo [petri] is this good enough?
            return ReferenceEquals(ClientCompatibilitySettings, other.ClientCompatibilitySettings);
        }

        public override bool Equals(object obj) => obj is ActiveClientCompatibilitySettings other && Equals(other);
        public override int GetHashCode() => ClientCompatibilitySettings.GetHashCode();
    }

    /// <summary>
    /// Computed assignment policy that determines whether player may be assigned into an experiment this very moment.
    /// This only exists for enabled, running Experiments.
    /// </summary>
    public readonly struct PlayerExperimentAssignmentPolicy
    {
        /// <summary>
        /// <inheritdoc cref="PlayerExperimentGlobalState.ControlWeight"/>
        /// </summary>
        public readonly int             ControlWeight;

        /// <summary>
        /// <inheritdoc cref="PlayerExperimentGlobalState.Variants"/>
        /// </summary>
        public readonly OrderedDictionary<ExperimentVariantId, PlayerExperimentGlobalState.VariantState> Variants;

        /// <summary>
        /// Will new player be automatically assigned into this experiment.
        /// </summary>
        public readonly bool            IsRolloutEnabled;

        /// <summary>
        /// Is assigning into the experiment currently disabled due to desired population
        /// size having been reached already.
        /// </summary>
        public readonly bool            IsCapacityReached;

        /// <summary>
        /// <inheritdoc cref="PlayerExperimentGlobalState.RolloutRatioPermille"/>
        /// Note that within this sample population, the eligibility is then determined with <see cref="EligibilityFilter"/>.
        /// </summary>
        public readonly int             RolloutRatioPermille;

        /// <summary>
        /// <inheritdoc cref="PlayerExperimentGlobalState.ExperimentNonce"/>
        /// </summary>
        public readonly uint            ExperimentNonce;

        /// <summary>
        /// Determines the eligibility of a player in the Sample Population to be assigned into this
        /// experiment. Note that the sample population is controlled with <see cref="RolloutRatioPermille"/>.
        /// </summary>
        public readonly PlayerFilterCriteria EligibilityFilter;

        /// <summary>
        /// Determines the if only the new players (logging in for the first time) may be assigned into this
        /// experiment. If not set, all players may be assigned into the experiment.
        /// </summary>
        public readonly bool            EnrollOnlyNewPlayers;

        /// <summary>
        /// Determines the if experiment is disabled (i.e. has no effect, and all players in experiment appear as if
        /// they were not in the experiment), except for players that are marked as testers.
        /// </summary>
        public readonly bool            IsOnlyForTester;

        /// <summary>
        /// <inheritdoc cref="PlayerExperimentGlobalState.TesterPlayerIds"/>
        /// </summary>
        public readonly OrderedSet<EntityId> TesterPlayerIds;

        public PlayerExperimentAssignmentPolicy(
            int controlWeight,
            OrderedDictionary<ExperimentVariantId, PlayerExperimentGlobalState.VariantState> variants,
            bool isOnlyForTester,
            bool isRolloutEnabled,
            bool isCapacityReached,
            int rolloutRatioPermille,
            uint experimentNonce,
            PlayerFilterCriteria eligibilityFilter,
            bool enrollOnlyNewPlayers,
            OrderedSet<EntityId> testerPlayerIds)
        {
            ControlWeight = controlWeight;
            Variants = variants;
            IsOnlyForTester = isOnlyForTester;
            IsRolloutEnabled = isRolloutEnabled;
            IsCapacityReached = isCapacityReached;
            RolloutRatioPermille = rolloutRatioPermille;
            ExperimentNonce = experimentNonce;
            EligibilityFilter = eligibilityFilter;
            EnrollOnlyNewPlayers = enrollOnlyNewPlayers;
            TesterPlayerIds = testerPlayerIds;
        }

        /// <summary>
        /// Determines if a player is in the sample population for the experiment. Note that within this sample population,
        /// the eligibility may then be determined with <see cref="EligibilityFilter"/>.
        /// </summary>
        public bool IsPlayerInExperimentSamplePopulation(EntityId playerId)
        {
            uint subject = (uint)playerId.Value;
            return KeyedStableWeightedCoinflip.FlipACoin(ExperimentNonce, subject, RolloutRatioPermille);
        }

        /// <summary>
        /// Chooses a random variant respecting the variant weights.
        /// </summary>
        public ExperimentVariantId GetRandomVariant()
        {
            int totalWeight = ControlWeight;
            foreach ((ExperimentVariantId variantId, PlayerExperimentGlobalState.VariantState variant) in Variants)
            {
                if (!variant.IsActive())
                    continue;
                totalWeight += variant.Weight;
            }

            int remainingCdf = RandomPCG.CreateNew().NextInt(maxExclusive: totalWeight);
            foreach ((ExperimentVariantId variantId, PlayerExperimentGlobalState.VariantState variant) in Variants)
            {
                if (!variant.IsActive())
                    continue;
                remainingCdf -= variant.Weight;
                if (remainingCdf < 0)
                    return variantId;
            }
            return null; // control
        }
    }

    /// <summary>
    /// Currently active GameConfig versions in use. Updated by the <see cref="GlobalStateProxyActor"/>.
    /// </summary>
    public class ActiveGameConfig : IAtomicValue<ActiveGameConfig>
    {
        /// <summary>
        /// Node-local identifier.
        /// </summary>
        readonly int                                                                            AtomicValueVersion;

        /// <summary>
        /// The import resources of the active config.
        /// </summary>
        public readonly FullGameConfigImportResources                                           FullConfigImportResources;

        /// <summary>
        /// The config id of the baseline static config.
        /// </summary>
        public readonly MetaGuid                                                                BaselineStaticGameConfigId;

        /// <summary>
        /// The config id of the baseline dynamic config.
        /// </summary>
        public readonly MetaGuid                                                                BaselineDynamicGameConfigId;

        /// <summary>
        /// The baseline config that is active.
        /// </summary>
        public readonly FullGameConfig                                                          BaselineGameConfig;

        /// <summary>
        /// All tester players. A player in this set is a Tester in some Experiment.
        /// </summary>
        public readonly OrderedSet<EntityId>                                                    AllTesters;

        /// <summary>
        /// Contains the runtime state of FullGameConfig.ServerConfig.PlayerExperiments. This always contains A (NON-STRICT) SUBSET
        /// of Experiments in the Config.
        /// </summary>
        public readonly OrderedDictionary<PlayerExperimentId, PlayerExperimentAssignmentPolicy> VisibleExperimentsForPlayers;

        /// <summary>
        /// Same as <see cref="VisibleExperimentsForPlayers"/>, except this is the set visibile for Tester players.
        /// </summary>
        public readonly OrderedDictionary<PlayerExperimentId, PlayerExperimentAssignmentPolicy> VisibleExperimentsForTesters;

        /// <summary>
        /// The archive version of the config specialization patches for client consumption. This is essentially
        /// `BaselineGameConfig.ServerConfig.PlayerExperiments[].Variants[].ConfigPatch`, but packaged
        /// for more convenient delivery.
        /// </summary>
        public readonly ContentHash                                                             SharedGameConfigPatchesForPlayersContentHash;

        /// <summary>
        /// Same as <see cref="SharedGameConfigPatchesForPlayersContentHash"/>, except this is the version for Testers.
        /// </summary>
        public readonly ContentHash                                                             SharedGameConfigPatchesForTestersContentHash;

        /// <summary>
        /// Contains the experiment tester epoch for all known experiments. See <see cref="PlayerExperimentGlobalState.TesterEpoch"/>.
        /// </summary>
        public readonly OrderedDictionary<PlayerExperimentId, uint>                             ExperimentTesterEpochs;

        /// <summary>
        /// Contains the experiment state for all experiments in config. This is a subset of all known experiments.
        /// </summary>
        public readonly OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalState>      AllExperimentsInConfig;

        /// <summary>
        /// Content hash of the SharedGameConfig exposed to clients. Can differ from the hash of <c>BaselineGameConfig.SharedConfig</c>
        /// due to stripping of server-only data.
        /// </summary>
        public readonly ContentHash                                                             ClientSharedGameConfigContentHash;

        /// <summary>
        /// Contains the available delivery sources (i.e. CDN resources) for <c>BaselineGameConfig.SharedConfig</c>.
        /// </summary>
        public readonly ArchiveDeliverySourceSet                                                BaselineGameConfigSharedConfigDeliverySources;

        /// <summary>
        /// Timestamp of when this config was made active
        /// </summary>
        public readonly MetaTime                                                                ActiveSince;

        public ActiveGameConfig(
            int atomicValueVersion,
            FullGameConfigImportResources fullConfigImportResources,
            MetaGuid baselineStaticGameConfigId,
            MetaGuid baselineDynamicGameConfigId,
            FullGameConfig baselineGameConfig,
            OrderedSet<EntityId> allTesters,
            OrderedDictionary<PlayerExperimentId, PlayerExperimentAssignmentPolicy> visibleExperimentsForPlayers,
            OrderedDictionary<PlayerExperimentId, PlayerExperimentAssignmentPolicy> visibleExperimentsForTesters,
            ContentHash sharedGameConfigPatchesForPlayersContentHash,
            ContentHash sharedGameConfigPatchesForTestersContentHash,
            OrderedDictionary<PlayerExperimentId, uint> experimentTesterEpochs,
            OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalState> allExperimentsInConfig,
            ContentHash clientSharedGameConfigContentHash,
            ArchiveDeliverySourceSet baselineGameConfigSharedConfigDeliverySources,
            MetaTime activeSince)
        {
            AtomicValueVersion = atomicValueVersion;
            FullConfigImportResources = fullConfigImportResources;
            BaselineStaticGameConfigId = baselineStaticGameConfigId;
            BaselineDynamicGameConfigId = baselineDynamicGameConfigId;
            BaselineGameConfig = baselineGameConfig;
            AllTesters = allTesters;
            VisibleExperimentsForPlayers = visibleExperimentsForPlayers;
            VisibleExperimentsForTesters = visibleExperimentsForTesters;
            SharedGameConfigPatchesForPlayersContentHash = sharedGameConfigPatchesForPlayersContentHash;
            SharedGameConfigPatchesForTestersContentHash = sharedGameConfigPatchesForTestersContentHash;
            ExperimentTesterEpochs = experimentTesterEpochs;
            AllExperimentsInConfig = allExperimentsInConfig;
            ClientSharedGameConfigContentHash = clientSharedGameConfigContentHash;
            BaselineGameConfigSharedConfigDeliverySources = baselineGameConfigSharedConfigDeliverySources;
            ActiveSince = activeSince;
        }

        public FullGameConfig GetSpecializedGameConfig(GameConfigSpecializationKey specializationKey)
        {
            return ServerGameConfigProvider.Instance.GetSpecializedGameConfig(
                staticConfigId:     BaselineStaticGameConfigId,
                dynamicContentId:   BaselineDynamicGameConfigId,
                importResources:    FullConfigImportResources,
                specializationKey:  specializationKey);
        }

        public bool IsPlayerTesterInAnyExperiment(EntityId playerId) => AllTesters.Contains(playerId);

        public OrderedDictionary<PlayerExperimentId, PlayerExperimentAssignmentPolicy> GetVisibleExperimentsFor(PlayerExperimentSubject subject)
        {
            if (subject == PlayerExperimentSubject.Player)
                return VisibleExperimentsForPlayers;
            else
                return VisibleExperimentsForTesters;
        }

        public bool Equals(ActiveGameConfig other)
        {
            return AtomicValueVersion == other.AtomicValueVersion;
        }

        public override bool Equals(object obj) => obj is ActiveGameConfig other && Equals(other);
        public override int GetHashCode() => AtomicValueVersion.GetHashCode();
    }

    /// <summary>
    /// Currently active localization versions for each language. Updated by <see cref="GlobalStateProxyActor"/>.
    /// </summary>
    public class ActiveLocalizationVersions : IAtomicValue<ActiveLocalizationVersions>
    {
        // \note: using OrderedDictionary to allow for (failing) lookups with null language
        public readonly OrderedDictionary<LanguageId, ContentHash> Versions;

        public ActiveLocalizationVersions(OrderedDictionary<LanguageId, ContentHash> versions)
        {
            // Take copy just to be safe
            Versions = versions == null ? new OrderedDictionary<LanguageId, ContentHash>() : new OrderedDictionary<LanguageId, ContentHash>(versions);
        }

        public bool Equals(ActiveLocalizationVersions other)
        {
            if (other is null)
                return false;

            return Versions.Equals(other.Versions);
        }

        public override bool Equals(object obj) => obj is ActiveLocalizationVersions other && Equals(other);
        public override int GetHashCode() => Versions.GetHashCode();
    }

    /// <summary>
    /// Currently active scheduled maintenance mode. Updated by <see cref="GlobalStateProxyActor"/>.
    /// </summary>
    public class ActiveScheduledMaintenanceMode : IAtomicValue<ActiveScheduledMaintenanceMode>
    {
        public readonly ScheduledMaintenanceMode Mode;

        public ActiveScheduledMaintenanceMode(ScheduledMaintenanceMode mode)
        {
            Mode = mode;
        }

        public bool Equals(ActiveScheduledMaintenanceMode other)
        {
            if (other is null)
                return false;

            return Mode == other.Mode;
        }

        public bool IsPlatformExcluded(ClientPlatform platform)
        {
            if (Mode?.PlatformExclusions == null)
                return false;
            return Mode.PlatformExclusions.Contains(platform);
        }

        public override bool Equals(object obj) => obj is ActiveScheduledMaintenanceMode other && Equals(other);
        public override int GetHashCode() => Mode.GetHashCode();
    }

    /// <summary>
    /// Currently active cluster-wide nonce. Do not print this value to logs. This value can be used as a key to
    /// a hash function to irrevesible mangle personal information while keeping it (probabilistically) unique.
    /// Updated by <see cref="GlobalStateProxyActor"/>.
    /// </summary>
    public class ActiveSharedClusterNonce : IAtomicValue<ActiveSharedClusterNonce>
    {
        readonly byte[] _nonceBytes;

        /// <summary>
        /// 32 bytes (256 bits) of random, shared between all nodes.
        /// </summary>
        public ReadOnlySpan<byte> Nonce => _nonceBytes;

        public ActiveSharedClusterNonce(byte[] nonceBytes)
        {
            _nonceBytes = new byte[nonceBytes.Length];
            nonceBytes.CopyTo(_nonceBytes, 0);
        }

        public bool Equals(ActiveSharedClusterNonce other)
        {
            if (other is null)
                return false;

            return Nonce.SequenceEqual(other.Nonce);
        }

        public override bool Equals(object obj) => obj is ActiveSharedClusterNonce other && Equals(other);
        public override int GetHashCode() => 0; // \todo Proper hash. Probably not much used for IAtomicValues though
    }

    /// <summary>
    /// Currently active Developers.
    /// </summary>
    public class ActiveDevelopers : IAtomicValue<ActiveDevelopers>
    {
        /// <summary>
        /// All developer players. A player is in this set if they are marked as a developer.
        /// </summary>
        public readonly OrderedSet<EntityId> DeveloperPlayers;

        public ActiveDevelopers(OrderedSet<EntityId> allDevelopers)
        {
            DeveloperPlayers = allDevelopers;
        }

        public bool IsPlayerDeveloper(EntityId playerId) => DeveloperPlayers.Contains(playerId);

        public bool Equals(ActiveDevelopers other)
        {
            if (other is null)
                return false;

            return DeveloperPlayers == other.DeveloperPlayers;
        }

        public override bool Equals(object obj) => obj is ActiveDevelopers other && Equals(other);
        public override int GetHashCode() => DeveloperPlayers.GetHashCode();
    }

    [EntityConfig]
    internal sealed class GlobalStateProxyConfig : EphemeralEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.GlobalStateProxy;
        public override Type                EntityActorType         => typeof(GlobalStateProxyActor);
        public override IShardingStrategy   ShardingStrategy        => new ServiceShardingStrategy(EntityKindCloudCore.GlobalStateProxy);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Actor for proxying the <see cref="GlobalState"/> on all nodes in the server cluster. Subscribes
    /// to <see cref="GlobalStateManager"/> to get the latest state and the following stream of updates.
    /// </summary>
    public class GlobalStateProxyActor : EphemeralEntityActor
    {
        public class StatisticsTick
        {
            public static StatisticsTick Instance { get; } = new StatisticsTick();
            private StatisticsTick() { }
        }

        protected override sealed AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        EntitySubscription                          _subscription;
        GlobalState                                 _state;
        OrderedDictionary<LanguageId, ContentHash>  _localizationVersions = new OrderedDictionary<LanguageId, ContentHash>();

        static volatile bool    s_isInMaintenance   = false;
        public static bool      IsInMaintenance     => s_isInMaintenance;

        public static AtomicValuePublisher<ActiveClientCompatibilitySettings>   ActiveClientCompatibilitySettings   = new AtomicValuePublisher<ActiveClientCompatibilitySettings>();
        public static AtomicValuePublisher<ActiveGameConfig>                    ActiveGameConfig                    = new AtomicValuePublisher<ActiveGameConfig>();
        public static AtomicValuePublisher<ActiveBroadcastSet>                  ActiveBroadcastState                = new AtomicValuePublisher<ActiveBroadcastSet>();
        public static AtomicValuePublisher<ActiveLocalizationVersions>          ActiveLocalizationVersions          = new AtomicValuePublisher<ActiveLocalizationVersions>();
        public static AtomicValuePublisher<ActiveScheduledMaintenanceMode>      ActiveScheduledMaintenanceMode      = new AtomicValuePublisher<ActiveScheduledMaintenanceMode>();
        public static AtomicValuePublisher<ActiveSharedClusterNonce>            ActiveSharedClusterNonce            = new AtomicValuePublisher<ActiveSharedClusterNonce>();
        public static AtomicValuePublisher<ActiveDevelopers>                    ActiveDevelopers                    = new AtomicValuePublisher<ActiveDevelopers>();

        private static ConcurrentDictionary<int, int>                           BroadcastsConsumed                  = new ConcurrentDictionary<int, int>();
        private static OrderedDictionary<ExperimentVariantPair, int>            PlayerExperimentSizeDeltas          = new OrderedDictionary<ExperimentVariantPair, int>(); // monitored by PlayerExperimentSizeDeltasLock
        private static object                                                   PlayerExperimentSizeDeltasLock      = new object();

        static int s_runningActiveGameConfigVersion = 1;

        public GlobalStateProxyActor(EntityId entityId) : base(entityId) { }

        protected override async Task Initialize()
        {
            _log.Info("Subscribing to GlobalStateManager..");
            (EntitySubscription subscription, GlobalStateSnapshot initialState) = await SubscribeToAsync<GlobalStateSnapshot>(GlobalStateManager.EntityId, EntityTopic.Member, null);
            _subscription = subscription;

            // Update all states
            await UpdateGlobalState(initialState.GlobalState.Deserialize(resolver: null, logicVersion: null));

            // Start tick timers
            StartPeriodicTimer(TimeSpan.FromSeconds(1), ActorTick.Instance);
            StartPeriodicTimer(TimeSpan.FromSeconds(10), StatisticsTick.Instance);
        }

        protected override async Task OnShutdown()
        {
            // Unsubscribe from GlobalStateManager
            // \todo [petri] this doesn't work properly, so disabled for now (might be that responses are not routed to entities which are shutting down?)
            //await UnsubscribeFromAsync(_subscription);
            _subscription = null;

            await base.OnShutdown();
        }

        protected override Task OnSubscriptionLost(EntitySubscription subscription)
        {
            _log.Warning("Lost subscription to GlobalStateManager ({Actor})", subscription.ActorRef);
            _subscription = null;

            return Task.CompletedTask;
        }

        /// <summary>
        /// Periodically send collected statistics to GSM
        /// </summary>
        /// <param name="_"></param>
        [CommandHandler]
        public void HandleStatisticsTick(StatisticsTick _)
        {
            SendBroadcastConsumedCountInfo();
            SendPlayerExperimentAssignmentCounts();
        }

        void SendBroadcastConsumedCountInfo()
        {
            // Only send counts if there have been any since the last tick
            if (BroadcastsConsumed.Count > 0)
            {
                // Take a count of the number of times each broadcast has been consumed, clearing
                // the counts at the same time
                Dictionary<int, int> broadcastsToSend = new Dictionary<int, int>();
                foreach ((int broadcastId, int _) in BroadcastsConsumed)
                {
                    // A straightforwad Remove should never fail here because no-one else is removing
                    // keys from the dictionary, but use a TryRemove to be safe
                    int value;
                    if (BroadcastsConsumed.TryRemove(broadcastId, out value))
                        broadcastsToSend.Add(broadcastId, value);
                }

                // Send counts to GSM
                CastMessage(GlobalStateManager.EntityId, new BroadcastConsumedCountInfo(broadcastsToSend));
            }
        }

        void SendPlayerExperimentAssignmentCounts()
        {
            // Consume size changes
            OrderedDictionary<ExperimentVariantPair, int> resultDeltas = null;
            lock(PlayerExperimentSizeDeltasLock)
            {
                if (PlayerExperimentSizeDeltas.Count > 0)
                {
                    // copy source and clear it.
                    resultDeltas = new OrderedDictionary<ExperimentVariantPair, int>(PlayerExperimentSizeDeltas);
                    PlayerExperimentSizeDeltas.Clear();
                }
            }

            if (resultDeltas != null)
                CastMessage(GlobalStateManager.EntityId, new GlobalStatePlayerExperimentAssignmentInfoUpdate(resultDeltas));
        }

        [CommandHandler]
        public async Task HandleActorTick(ActorTick _)
        {
            // If lost subscription to GlobalStateManager, retry subscription
            if (_subscription == null)
            {
                _log.Warning("No subscription to GlobalStateManager, retrying");
                try
                {
                    (EntitySubscription subscription, GlobalStateSnapshot snapshot) = await SubscribeToAsync<GlobalStateSnapshot>(GlobalStateManager.EntityId, EntityTopic.Member, null);
                    _subscription = subscription;

                    // Store the state
                    GlobalState newState = snapshot.GlobalState.Deserialize(resolver: null, logicVersion: null);
                    _log.Info("Re-established subscription to GlobalStateManager: {NewState}", PrettyPrint.Verbose(newState));
                    await UpdateGlobalState(newState);
                }
                catch (Exception ex)
                {
                    _log.Warning("Failed to re-subscribe to GlobalStateManager: {Exception}", ex);
                }
            }

            // Update time-dependent state (maintenance mode and global broadcasts)
            UpdateTimeDependentState();
        }

        [MessageHandler]
        public void HandleUpdateClientCompatibilitySettingsRequest(UpdateClientCompatibilitySettingsRequest updateConfig)
        {
            // Store new config & save
            ClientCompatibilitySettings settings = updateConfig.Settings;
            _log.Info("Updating ClientCompatibilitySettings to {ClientCompatibilitySettings}", PrettyPrint.Verbose(settings));
            _state.ClientCompatibilitySettings = settings;

            // Start using latest version
            ActiveClientCompatibilitySettings.TryUpdate(new ActiveClientCompatibilitySettings(settings));
        }

        [MessageHandler]
        public void HandleUpdateScheduledMaintenanceModeRequest(UpdateScheduledMaintenanceModeRequest updateMode)
        {
            // Store new config & save
            ScheduledMaintenanceMode mode = updateMode.Mode;
            if (mode != null)
                _log.Info("Setting ScheduledMaintenanceMode to {ScheduledMaintenanceMode}, estimated duration {EstimatedDurationInMinutes}", mode.StartAt.ToString(), mode.EstimationIsValid ? mode.EstimatedDurationInMinutes.ToString(CultureInfo.InvariantCulture) + " minutes" : "not set");
            else
                _log.Info("Cancelling ScheduledMaintenanceMode");
            _state.ScheduledMaintenanceMode = mode;

            // Publish the state locally
            ActiveScheduledMaintenanceMode.TryUpdate(new ActiveScheduledMaintenanceMode(_state.ScheduledMaintenanceMode));
        }

        [MessageHandler]
        public async Task HandleUpdateGameConfig(GlobalStateUpdateGameConfig update)
        {
            // Store archive version
            _state.StaticGameConfigId = update.StaticConfigId;
            _state.SharedGameConfigDeliverables = update.SharedConfigDeliverables;
            //_state.DynamicGameConfigId = update.DynamicConfigId;
            _state.PlayerExperiments = update.PlayerExperiments;
            _state.SharedGameConfigPatchesForPlayersContentHash = update.SharedPatchesForPlayersContentHash;
            _state.SharedGameConfigPatchesForTestersContentHash = update.SharedPatchesForTestersContentHash;
            _state.LatestGameConfigUpdate = update.Timestamp;

            // Update published state based on modified archive
            await UpdateActiveGameConfigs();
        }

        [MessageHandler]
        public void HandleUpdateLocalizationsConfigVersion(GlobalStateUpdateLocalizationsConfigVersion update)
        {
            // Store archive version
            _state.ActiveLocalizationsId     = update.LocalizationsId;
            _state.LocalizationsDeliverables = update.Deliverables;

            // Update language versions to latest ones
            UpdateActiveLocalizationVersions();
        }

        [MessageHandler]
        public void HandleAddBroadcastMessage(AddBroadcastMessage addBroadcast)
        {
            BroadcastMessageParams broadcastParams = addBroadcast.BroadcastParams;
            _log.Debug("New BroadcastMessage: {Message}", PrettyPrint.Verbose(broadcastParams));
            _state.BroadcastMessages.Add(broadcastParams.Id, new BroadcastMessage(broadcastParams, new BroadcastMessageStats()));

            // Update set of active broadcasts
            UpdateActiveBroadcastStates();
        }

        [MessageHandler]
        public void HandleUpdateBroadcastMessage(UpdateBroadcastMessage updateBroadcast)
        {
            BroadcastMessageParams broadcastParams = updateBroadcast.BroadcastParams;
            _log.Debug("Update BroadcastMessage: {Message}", PrettyPrint.Verbose(broadcastParams));
            _state.BroadcastMessages[broadcastParams.Id].Params = broadcastParams;

            // Update set of active broadcasts
            UpdateActiveBroadcastStates();
        }

        [MessageHandler]
        public void HandleDeleteBroadcastMessage(DeleteBroadcastMessage deleteBroadcast)
        {
            _log.Debug("Delete broadcast #{BroadcastId}", deleteBroadcast.BroadcastId);
            _state.BroadcastMessages.Remove(deleteBroadcast.BroadcastId);

            // Update set of active broadcasts
            UpdateActiveBroadcastStates();
        }

        [MessageHandler]
        public void HandleSetDeveloperPlayerRequest(SetDeveloperPlayerRequest request)
        {
            _log.Debug("Set developer player: {Message}", PrettyPrint.Verbose(request));

            if (request.IsDeveloper)
            {
                if (_state.DeveloperPlayerIds.Contains(request.PlayerId))
                    return;
                _state.DeveloperPlayerIds.Add(request.PlayerId);
            }
            else
            {
                if (!_state.DeveloperPlayerIds.Contains(request.PlayerId))
                    return;
                _state.DeveloperPlayerIds.Remove(request.PlayerId);
            }

            // Update published state
            UpdateActiveDevelopers();
        }

        public static void PlayerConsumedBroadcast(BroadcastMessageParams broadcast)
        {
            BroadcastsConsumed.AddOrUpdate(broadcast.Id, 1, (key, oldValue) => oldValue + 1);
        }

        async Task UpdateActiveGameConfigs()
        {
            static OrderedDictionary<PlayerExperimentId, PlayerExperimentAssignmentPolicy> ComputeExperimentPolicies(GlobalState state, FullGameConfig baselineFullGameConfig, PlayerExperimentSubject subject)
            {
                OrderedDictionary<PlayerExperimentId, PlayerExperimentAssignmentPolicy> policies = new OrderedDictionary<PlayerExperimentId, PlayerExperimentAssignmentPolicy>();
                foreach (PlayerExperimentId experimentId in GlobalState.GetVisibleExperimentsInGameConfigOrder(state.PlayerExperiments, baselineFullGameConfig, subject))
                {
                    PlayerExperimentGlobalState experimentState = state.PlayerExperiments[experimentId];

                    PlayerExperimentAssignmentPolicy policy = new PlayerExperimentAssignmentPolicy(
                        controlWeight:                  experimentState.ControlWeight,
                        variants:                       experimentState.Variants,
                        isOnlyForTester:                experimentState.LifecyclePhase == PlayerExperimentPhase.Testing || experimentState.LifecyclePhase == PlayerExperimentPhase.Paused,
                        isRolloutEnabled:               experimentState.LifecyclePhase == PlayerExperimentPhase.Ongoing && !experimentState.IsRolloutDisabled,
                        isCapacityReached:              experimentState.HasCapacityLimit && experimentState.NumPlayersInExperiment >= experimentState.MaxCapacity,
                        rolloutRatioPermille:           experimentState.RolloutRatioPermille,
                        experimentNonce:                experimentState.ExperimentNonce,
                        eligibilityFilter:              experimentState.PlayerFilter,
                        enrollOnlyNewPlayers:           experimentState.EnrollTrigger == PlayerExperimentGlobalState.EnrollTriggerType.NewPlayers,
                        testerPlayerIds:                new OrderedSet<EntityId>(experimentState.TesterPlayerIds)); // \note: TesterPlayerIds is a copy, currently, but let's make a defensive copy here just in case.

                    policies.Add(experimentId, policy);
                }
                return policies;
            }
            static ArchiveDeliverySourceSet ToDeliverySourceSet(ConfigArchiveDeliverables deliverables)
            {
                List<ArchiveDeliverySource> preferred = new List<ArchiveDeliverySource>();

                // \todo: create sources for delta encoded versions

                ArchiveDeliverySource fallback = new ArchiveDeliverySource(deliverables.Version);

                return new ArchiveDeliverySourceSet(preferred, fallback);
            }

            MetaGuid dynamicId = MetaGuid.None;
            FullGameConfigImportResources fullConfigImportResources = await ServerGameConfigProvider.Instance.GetImportResourcesAsync(_state.StaticGameConfigId, dynamicId);
            FullGameConfig baselineGameConfig = ServerGameConfigProvider.Instance.GetBaselineGameConfig(_state.StaticGameConfigId, dynamicId, fullConfigImportResources);

            _log.Info("Updated to StaticGameConfig version {StaticGameConfigVersion}", _state.StaticGameConfigId);

            // Compute the set of players that are testers-in-some-experiment. These players use the -ForTesters variants for experiments. Other
            // (normal) players use the -ForPlayers variants.
            OrderedSet<EntityId> allTesters = new OrderedSet<EntityId>();
            foreach (PlayerExperimentId experimentId in GlobalState.GetVisibleExperimentsInGameConfigOrder(_state.PlayerExperiments, baselineGameConfig, PlayerExperimentSubject.Tester))
            {
                PlayerExperimentGlobalState experimentState = _state.PlayerExperiments[experimentId];
                foreach (EntityId testerPlayerId in experimentState.TesterPlayerIds)
                    allTesters.Add(testerPlayerId);
            }

            // Compute policies for the both sets of experiments
            OrderedDictionary<PlayerExperimentId, PlayerExperimentAssignmentPolicy> experimentsPoliciesForPlayers = ComputeExperimentPolicies(_state, baselineGameConfig, PlayerExperimentSubject.Player);
            OrderedDictionary<PlayerExperimentId, PlayerExperimentAssignmentPolicy> experimentsPoliciesForTesters = ComputeExperimentPolicies(_state, baselineGameConfig, PlayerExperimentSubject.Tester);

            OrderedDictionary<PlayerExperimentId, uint> experimentTesterEpochs = _state.PlayerExperiments.ToOrderedDictionary(keySelector: kv => kv.Key, elementSelector: kv => kv.Value.TesterEpoch);
            OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalState> allExperimentsInConfig = baselineGameConfig.ServerConfig.PlayerExperiments.Keys.ToOrderedDictionary(experimentId => experimentId, experimentId => _state.PlayerExperiments[experimentId]);

            // Publish to local listeners
            ActiveGameConfig.TryUpdate(new ActiveGameConfig(
                atomicValueVersion:                             Interlocked.Increment(ref s_runningActiveGameConfigVersion),
                fullConfigImportResources:                      fullConfigImportResources,
                baselineStaticGameConfigId:                     _state.StaticGameConfigId,
                baselineDynamicGameConfigId:                    dynamicId,
                baselineGameConfig:                             baselineGameConfig,
                allTesters:                                     allTesters,
                visibleExperimentsForPlayers:                   experimentsPoliciesForPlayers,
                visibleExperimentsForTesters:                   experimentsPoliciesForTesters,
                sharedGameConfigPatchesForPlayersContentHash:   _state.SharedGameConfigPatchesForPlayersContentHash,
                sharedGameConfigPatchesForTestersContentHash:   _state.SharedGameConfigPatchesForTestersContentHash,
                experimentTesterEpochs:                         experimentTesterEpochs,
                allExperimentsInConfig:                         allExperimentsInConfig,
                clientSharedGameConfigContentHash:              _state.SharedGameConfigDeliverables.Version,
                baselineGameConfigSharedConfigDeliverySources:  ToDeliverySourceSet(_state.SharedGameConfigDeliverables),
                activeSince:                                    _state.LatestGameConfigUpdate));
        }

        void UpdateActiveLocalizationVersions()
        {
            // Publish to local entities
            ActiveLocalizationVersions newVersions = new ActiveLocalizationVersions(_state.LocalizationsDeliverables.Languages);
            ActiveLocalizationVersions.TryUpdate(newVersions);
        }

        void UpdateMaintenanceModeActive()
        {
            ScheduledMaintenanceMode mode = _state.ScheduledMaintenanceMode;
            bool shouldBeInMaintenance = (mode != null) ? mode.IsInMaintenanceMode(MetaTime.Now) : false;
            if (s_isInMaintenance != shouldBeInMaintenance)
            {
                _log.Info(shouldBeInMaintenance ? "ENTERING MAINTENANCE MODE" : "LEAVING MAINTENANCE MODE");
                s_isInMaintenance = shouldBeInMaintenance;
            }
        }

        void UpdateActiveBroadcastStates()
        {
            // \note Classifies the broadcasts based on current time
            ActiveBroadcastState.TryUpdate(new ActiveBroadcastSet(MetaTime.Now, _state.BroadcastMessages.Values));
        }

        void UpdateActiveDevelopers()
        {
            ActiveDevelopers.TryUpdate(new ActiveDevelopers(new OrderedSet<EntityId>(_state.DeveloperPlayerIds)));
        }

        void UpdateTimeDependentState()
        {
            // Update maintenance mode status
            UpdateMaintenanceModeActive();

            // Update currently active broadcasts
            UpdateActiveBroadcastStates();
        }

        /// <summary>
        /// Update to a new state received from <see cref="GlobalStateManager"/> and update any locally dependent publishers of said state.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        async Task UpdateGlobalState(GlobalState state)
        {
            _state = state;

            // Start publishing active LogicVersion configs
            ActiveClientCompatibilitySettings.TryUpdate(new ActiveClientCompatibilitySettings(_state.ClientCompatibilitySettings));

            // Initialize active GameConfigs (if has valid version)
            await UpdateActiveGameConfigs();

            // Initialize Localization versions
            UpdateActiveLocalizationVersions();

            // Initialise scheduled maintenance window
            ActiveScheduledMaintenanceMode.TryUpdate(new ActiveScheduledMaintenanceMode(_state.ScheduledMaintenanceMode));

            // Shared random
            ActiveSharedClusterNonce.TryUpdate(new ActiveSharedClusterNonce(_state.SharedClusterNonce));

            // Update current state
            UpdateTimeDependentState();

            // Update developers
            UpdateActiveDevelopers();
        }

        /// <summary>
        /// Adds assignment change into GSP's update queue. This is used to keep track on the number of players assigned into the experiment in order to stop assignment when Capacity limit is reached.
        /// </summary>
        public static void PlayerAssignmentIntoExperimentChanged(EntityId playerId, PlayerExperimentId experimentId, ExperimentVariantId addedIntoGroupId, bool wasRemovedFromGroup, ExperimentVariantId removedFromGroupId)
        {
            // Track groups sizes.

            lock (PlayerExperimentSizeDeltasLock)
            {
                // remove old
                if (wasRemovedFromGroup)
                {
                    ExperimentVariantPair key = new ExperimentVariantPair(experimentId, removedFromGroupId);
                    int existing = PlayerExperimentSizeDeltas.GetValueOrDefault(key, defaultValue: 0);
                    PlayerExperimentSizeDeltas[key] = existing - 1;
                }

                // add new
                {
                    ExperimentVariantPair key = new ExperimentVariantPair(experimentId, addedIntoGroupId);
                    int existing = PlayerExperimentSizeDeltas.GetValueOrDefault(key, defaultValue: 0);
                    PlayerExperimentSizeDeltas[key] = existing + 1;
                }
            }
        }
    }
}
