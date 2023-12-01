// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.Database;
using Metaplay.Server.GameConfig;
using Metaplay.Server.ServerAnalyticsEvents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    /// <summary>
    /// Phase defines the state of the experiment in its lifecycle state machine:<br/>
    /// <code>
    /// <![CDATA[
    ///       .------------------ Testing
    ///       |                      |
    ///       |                      V
    ///       |         Paused <-> Ongoing
    ///       |            '--.   .--'
    ///       V               V   V
    ///  [ deleted ]        Concluded
    /// ]]>
    /// </code>
    /// </summary>
    [MetaSerializable]
    public enum PlayerExperimentPhase
    {
        /// <summary>
        /// Experiment is testing phase. It is active but only for the players that
        /// have been specifically assigned into the experiment with a special
        /// testing flag. Players are not automatically enrolled into the experiment.
        /// </summary>
        Testing = 1,

        /// <summary>
        /// Experiment is enabled and players in the experiment will have patched
        /// gameconfigs. Players are automatically enrolled into the experiment as
        /// configured.
        /// </summary>
        Ongoing = 2,

        /// <summary>
        /// Experiment is paused and players in the experiment has no effect on the
        /// players, except for player specifically assigned into the experiment with
        /// a special testing flag. Players are not automatically enrolled into the
        /// experiment.
        /// </summary>
        Paused = 3,

        /// <summary>
        /// Experiment has been concluded. It no longer has effect on players and
        /// it may be removed from gameconfigs if not already removed.
        /// </summary>
        Concluded = 4
    }

    public enum PlayerExperimentSubject
    {
        Player,
        Tester,
    }

    [MetaSerializable]
    public class PlayerExperimentGlobalState : IPlayerFilter
    {
        [MetaSerializable]
        public class VariantState
        {
            /// <summary>
            /// Weight of this variant in the weighted assignment. The probability of being assigned into this group
            /// is the ratio of the Weight over the total weight of all variants (including control group).
            /// </summary>
            [MetaMember(1)] public int Weight { get; internal set; }

            /// <summary>
            /// Whether the variant info is missing or otherwise invalid in the game config. If this is set, the variant
            /// is not active. Players in this variant will not see config changes, and no new player will be assigned into
            /// this variant.
            /// </summary>
            [MetaMember(2)] public bool IsConfigMissing { get; internal set; }

            /// <summary>
            /// Whether the variant is explicitly disabled by admin. Players in this variant will not see config changes,
            /// and no new player will be assigned into this variant.
            /// </summary>
            [MetaMember(3)] public bool IsDisabledByAdmin { get; internal set; }

            public bool IsActive() => !IsConfigMissing && !IsDisabledByAdmin;

            VariantState()
            {
            }
            public VariantState(int weight, bool isDisabledByAdmin)
            {
                Weight = weight;
                IsConfigMissing = false;
                IsDisabledByAdmin = isDisabledByAdmin;
            }
        }
        [MetaSerializable]
        public enum EnrollTriggerType
        {
            /// <summary>
            /// Any player may be enrolled into the experiment upon login.
            /// </summary>
            Login = 0,

            /// <summary>
            /// Only the new players (logging in for the first time) may be enrolled.
            /// </summary>
            NewPlayers = 1,
        }

        /// <summary>
        /// True, if assigning players into the experiment should stop after reaching <see cref="MaxCapacity"/>.
        /// </summary>
        [MetaMember(2)] public bool     HasCapacityLimit            { get; internal set; }

        /// <summary>
        /// Number of players after which assigning into the experiment ends.
        /// </summary>
        [MetaMember(3)] public int      MaxCapacity                 { get; internal set; }

        /// <summary>
        /// The (approximate) number of players that have been assigned into this experiment.
        /// </summary>
        [MetaMember(4)] public int      NumPlayersInExperiment      { get; internal set; }

        /// <summary>
        /// The ratio of population as parts-per-1000 that are selected as the sample population
        /// for the experiment.
        /// </summary>
        [MetaMember(5)] public int      RolloutRatioPermille        { get; internal set; }

        /// <summary>
        /// Nonce for this experiment. Used for stable random assignment.
        /// </summary>
        [MetaMember(6)] public uint     ExperimentNonce             { get; private set; }

        /// <summary>
        /// Weight of the control group. See <see cref="VariantState.Weight"/>.
        /// </summary>
        [MetaMember(8)] public int      ControlWeight               { get; internal set; }

        /// <summary>
        /// States of the variants.
        /// </summary>
        [MetaMember(9)] public OrderedDictionary<ExperimentVariantId, VariantState> Variants { get; internal set; } = new OrderedDictionary<ExperimentVariantId, VariantState>();

        /// <summary>
        /// Targeting player condition. Only players matching the condition are assigned to this experiment.
        /// </summary>
        [MetaMember(16)] public PlayerCondition TargetCondition { get; internal set; }

        /// <summary>
        /// Trigger which invokes the assignment of players into this experiment.
        /// </summary>
        [MetaMember(11)] public EnrollTriggerType EnrollTrigger     { get; internal set; }

        /// <summary>
        /// <inheritdoc cref="PlayerExperimentPhase"/>
        /// </summary>
        [MetaMember(12)] public PlayerExperimentPhase LifecyclePhase { get; internal set; }

        /// <summary>
        /// Is the experiment game config missing. If so, the experiment cannot be active.
        /// </summary>
        [MetaMember(13)] public bool     IsConfigMissing            { get; internal set; }

        /// <summary>
        /// The set of players that are Testers for this experiment. Tester in an experiment receives changes also in Testing and Paused phases. Being a tester in
        /// any non-concluded experiment causes the client to receive patch-sets for all non-concluded experiments.
        /// </summary>
        [MetaMember(14)] public OrderedSet<EntityId> TesterPlayerIds { get; internal set; } = new OrderedSet<EntityId>();

        /// <summary>
        /// Counter that increases on every Tester change. Used to make detect when changes propagate through GSPs to players for synchronization.
        /// </summary>
        [MetaMember(15)] public uint    TesterEpoch                 { get; internal set; }

        /// <summary>
        /// If true then rollout has been disabled and no further players should be enrolled.
        /// </summary>
        [MetaMember(17)] public bool    IsRolloutDisabled           { get; internal set; }

        // Only for migration use
        [MetaMember(1)] public bool     LegacyIsRolloutEnabled  { get; internal set; }
        [MetaMember(7)] public int      LegacyLifecyclePhase    { get; internal set; }
        [MetaMember(10)] public List<PlayerSegmentId> LegacyTargetSegments { get; internal set; }

        public PlayerFilterCriteria PlayerFilter => new PlayerFilterCriteria(explicitPlayerIds: null, condition: TargetCondition);

        public bool HasAnyNonControlVariantActive()
        {
            foreach (VariantState variant in Variants.Values)
            {
                if (variant.IsActive())
                    return true;
            }
            return false;
        }


        PlayerExperimentGlobalState() { }
        public PlayerExperimentGlobalState(bool hasCapacityLimit, int maxCapacity, int rolloutRatioPermille, uint experimentNonce, int controlWeight, OrderedDictionary<ExperimentVariantId, VariantState> variants)
        {
            HasCapacityLimit = hasCapacityLimit;
            MaxCapacity = maxCapacity;
            NumPlayersInExperiment = 0;
            RolloutRatioPermille = rolloutRatioPermille;
            ExperimentNonce = experimentNonce;
            ControlWeight = controlWeight;
            Variants = variants;
            EnrollTrigger = EnrollTriggerType.Login;
            LifecyclePhase = PlayerExperimentPhase.Testing;
            IsConfigMissing = false;
            IsRolloutDisabled = false;
        }

        public static uint GenerateNonceU32()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(4);
            return BitConverter.ToUInt32(bytes);
        }

        public void MigrateDataFrom5To6()
        {
            switch (LegacyLifecyclePhase)
            {
                case 0:
                {
                    // new
                    LifecyclePhase = 0; // PlayerExperimentPhase.Inactive;
                    break;
                }

                case 1:
                {
                    // Enabled
                    if (LegacyIsRolloutEnabled)
                        LifecyclePhase = PlayerExperimentPhase.Ongoing;
                    else
                        LifecyclePhase = PlayerExperimentPhase.Paused;
                    break;
                }

                case 2:
                {
                    // Retired
                    LifecyclePhase = PlayerExperimentPhase.Concluded;
                    break;
                }

                case 3:
                {
                    // Invalid
                    LifecyclePhase = PlayerExperimentPhase.Paused;
                    break;
                }
            }
        }

        public void MigrateDataFrom6To7()
        {
            // Inactive is removed and is now just Testing
            if (LifecyclePhase == 0)
                LifecyclePhase = PlayerExperimentPhase.Testing;
        }

        public void MigrateDataFrom8To9()
        {
            if (LegacyTargetSegments != null)
            {
                if (LegacyTargetSegments.Any())
                    TargetCondition = new PlayerSegmentBasicCondition(propertyRequirements: null, requireAnySegment: LegacyTargetSegments, requireAllSegments: null);
                LegacyTargetSegments = null;
            }
        }
    }

    [MetaSerializable]
    [MetaBlockedMembers(7)]
    public class PlayerExperimentGlobalStatistics
    {
        [MetaSerializable]
        public class VariantStats
        {
            /// <summary>
            /// The (approximate) number of players that have been assigned into each variant.
            /// </summary>
            [MetaMember(1)] public int NumPlayersInVariant;

            /// <summary>
            /// The few lastest players assigned into this variant.
            /// </summary>
            [MetaMember(2)] public List<EntityId> LegacyPlayerSample;

            /// <summary>
            /// Last known AnalyticsId of the variant.
            /// </summary>
            [MetaMember(3)] public string AnalyticsId;

            public VariantStats()
            {
                NumPlayersInVariant = 0;
                LegacyPlayerSample = new List<EntityId>();
            }
        }
        [MetaSerializable]
        public class WhyInvalidInfo
        {
            [MetaMember(1)] public bool IsConfigDataMissing;
            [MetaMember(2)] public bool HasNoNonControlVariants;

            WhyInvalidInfo() { }
            public WhyInvalidInfo(bool isConfigDataMissing, bool hasNoNonControlVariants)
            {
                IsConfigDataMissing = isConfigDataMissing;
                HasNoNonControlVariants = hasNoNonControlVariants;
            }
        }

        /// <summary>
        /// The few lastest players assigned into this experiment.
        /// </summary>
        [MetaMember(1)] public List<EntityId> LegacyPlayerSample;

        /// <summary>
        /// Per-variant statistics. Note that this is neither super- or subset of the State Variants:
        /// Statistics are created on demand when players are assigned into variants. Hence state may have
        /// variants for which there are no statistics yet. If a variant is deleted, the state is deleted,
        /// but statistics are not. Hence statistics may have variants, the state does not.
        /// </summary>
        [MetaMember(2)] public OrderedDictionary<ExperimentVariantId, VariantStats> Variants { get; private set; }

        /// <summary>
        /// Last known displayName of the experiment.
        /// </summary>
        [MetaMember(3)] public string DisplayName;

        /// <summary>
        /// Last known Description of the experiment.
        /// </summary>
        [MetaMember(4)] public string Description;

        /// <summary>
        /// Reason why the experiment is Invalid (cannot be active), or null if could be active.
        /// </summary>
        [MetaMember(5)] public WhyInvalidInfo WhyInvalid;

        /// <summary>
        /// Timestamp when the experiment was first seen.
        /// </summary>
        [MetaMember(6)] public MetaTime CreatedAt;

        /// <summary>
        /// The latest timestamp when the experiment was put into Ongoing phase. Null if never (as of yet).
        /// </summary>
        [MetaMember(8)] public MetaTime? OngoingMostRecentlyAt;

        /// <summary>
        /// The earliest timestamp when the experiment was put into Ongoing phase. Null if never (as of yet).
        /// </summary>
        [MetaMember(13)] public MetaTime? OngoingFirstTimeAt;

        /// <summary>
        /// The duration the experiment has been in Ongoing phase, excluding the time since OngoingMostRecentlyAt if we are Ongoing currently.
        /// i.e. If time is split to spans  III-OOOO-PPP-OOO, where I is uninitalized, O ongoing, P paused, this duration would only count the
        /// duration of the first OOOO-span.
        /// </summary>
        [MetaMember(14)] public MetaDuration OngoingDurationBeforeCurrentSpan;

        /// <summary>
        /// The latest timestamp when the experiment was concluded. Null if never (as of yet).
        /// </summary>
        [MetaMember(9)] public MetaTime? ConcludedMostRecentlyAt;

        /// <summary>
        /// The earliest timestamp when the experiment was concluded. Null if never (as of yet).
        /// </summary>
        [MetaMember(15)] public MetaTime? ConcludedFirstTimeAt;

        /// <summary>
        /// The latest timestamp when the experiment reached the defined capacity. Null if never (as of yet).
        /// </summary>
        [MetaMember(10)] public MetaTime? ReachedCapacityMostRecentlyAt;

        /// <summary>
        /// The earliest timestamp when the experiment reached the defined capacity. Null if never (as of yet).
        /// </summary>
        [MetaMember(16)] public MetaTime? ReachedCapacityFirstTimeAt;

        /// <summary>
        /// Last known AnalyticsId of the experiment.
        /// </summary>
        [MetaMember(11)] public string ExperimentAnalyticsId;

        /// <summary>
        /// The latest timestamp when the experiment was moved into Testing phase. Null if never (as of yet).
        /// </summary>
        [MetaMember(12)] public MetaTime? TestingMostRecentlyAt;

        /// <summary>
        /// The earliest timestamp when the experiment was moved into Testing phase. Null if never (as of yet).
        /// </summary>
        [MetaMember(17)] public MetaTime? TestingFirstTimeAt;

        /// <summary>
        /// The latest timestamp when the experiment was moved into Paused phase. Null if never (as of yet).
        /// </summary>
        [MetaMember(18)] public MetaTime? PausedMostRecentlyAt;

        /// <summary>
        /// The earliest timestamp when the experiment was moved into Paused phase. Null if never (as of yet).
        /// </summary>
        [MetaMember(19)] public MetaTime? PausedFirstTimeAt;

        PlayerExperimentGlobalStatistics()
        {
        }
        public static PlayerExperimentGlobalStatistics CreateNew()
        {
            MetaTime now = MetaTime.Now;
            return new PlayerExperimentGlobalStatistics()
            {
                LegacyPlayerSample = new List<EntityId>(),
                Variants = new OrderedDictionary<ExperimentVariantId, VariantStats>(),
                DisplayName = null,
                Description = null,
                WhyInvalid = null,
                CreatedAt = now,
                TestingFirstTimeAt = now, // Experiments start in Testing mode, so let's set the timer already
            };
        }
    }

    /// <summary>
    /// Request for retrieving Statistics of an PlayerExperiment. GlobalStateManager responds with a <see cref="GlobalStateExperimentStateResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStateExperimentCombinationsRequest, MessageDirection.ServerInternal)]
    public class GlobalStateExperimentCombinationsRequest : MetaMessage
    {
        // The experiment the user is currently viewing, depending on the state of the experiment this can indicate
        // that the user can only disable or enable the experiment, the response will reflect this.
        public PlayerExperimentId CurrentExperiment { get; private set; }

        public GlobalStateExperimentCombinationsRequest() { }
        public GlobalStateExperimentCombinationsRequest(PlayerExperimentId currentExperiment)
        {
            CurrentExperiment = currentExperiment;
        }
    }

    [MetaMessage(MessageCodesCore.GlobalStateExperimentCombinationsResponse, MessageDirection.ServerInternal)]
    public class GlobalStateExperimentCombinationsResponse : MetaMessage
    {
        public int CurrentCombinations { get; set; }
        public int NewCombinations { get; set; }
        public bool ExceedsThreshold { get; set; }

        public GlobalStateExperimentCombinationsResponse()
        {
        }

        public GlobalStateExperimentCombinationsResponse(int currentCombinations, int newCombinations, bool exceedsExceedsThreshold)
        {
            CurrentCombinations = currentCombinations;
            NewCombinations = newCombinations;
            ExceedsThreshold = exceedsExceedsThreshold;
        }
    }

    /// <summary>
    /// Request for retrieving Statistics of an PlayerExperiment. GlobalStateManager responds with a <see cref="GlobalStateExperimentStateResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStateExperimentStateRequest, MessageDirection.ServerInternal)]
    public class GlobalStateExperimentStateRequest : MetaMessage
    {
        public PlayerExperimentId PlayerExperimentId { get; private set; }

        GlobalStateExperimentStateRequest() { }
        public GlobalStateExperimentStateRequest(PlayerExperimentId playerExperimentId)
        {
            PlayerExperimentId = playerExperimentId;
        }
    }
    [MetaMessage(MessageCodesCore.GlobalStateExperimentStateResponse, MessageDirection.ServerInternal)]
    public class GlobalStateExperimentStateResponse : MetaMessage
    {
        /// <summary>
        /// null on failure.
        /// </summary>
        public PlayerExperimentGlobalState State { get; private set; }

        /// <summary>
        /// null on failure.
        /// </summary>
        public PlayerExperimentGlobalStatistics Statistics { get; private set; }

        [IgnoreDataMember]
        public bool IsSuccess => Statistics != null;

        GlobalStateExperimentStateResponse() { }
        public GlobalStateExperimentStateResponse(PlayerExperimentGlobalState state, PlayerExperimentGlobalStatistics statistics)
        {
            State = state;
            Statistics = statistics;
        }
    }

    /// <summary>
    /// GSP -> GSM cast message to update experiment statistics
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStatePlayerExperimentAssignmentInfoUpdate, MessageDirection.ServerInternal)]
    public class GlobalStatePlayerExperimentAssignmentInfoUpdate : MetaMessage
    {
        public OrderedDictionary<ExperimentVariantPair, int> SizeDeltas { get; private set; }

        GlobalStatePlayerExperimentAssignmentInfoUpdate() { }
        public GlobalStatePlayerExperimentAssignmentInfoUpdate(OrderedDictionary<ExperimentVariantPair, int> sizeDeltas)
        {
            SizeDeltas = sizeDeltas;
        }
    }

    /// <summary>
    /// Tell <see cref="GlobalStateManager"/> to discard its Player Experiment Samples. Sent by <see cref="StatsCollectorManager"/>
    /// after it has migrated the statistics into its own state.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStateForgetPlayerExperimentSamples, MessageDirection.ServerInternal)]
    public class GlobalStateForgetPlayerExperimentSamples : MetaMessage
    {
        public static readonly GlobalStateForgetPlayerExperimentSamples Instance = new GlobalStateForgetPlayerExperimentSamples();
    }

    /// <summary>
    /// Controller -> GSM ask-message to edit Experiment config. GlobalStateManager responds with a <see cref="GlobalStateModifyExperimentResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStateModifyExperimentRequest, MessageDirection.ServerInternal)]
    public class GlobalStateModifyExperimentRequest : MetaMessage
    {
        public PlayerExperimentId                               PlayerExperimentId      { get; private set; }
        public bool?                                            HasCapacityLimit        { get; private set; }
        public int?                                             MaxCapacity             { get; private set; }
        public int?                                             RolloutRatioPermille    { get; private set; }
        public OrderedDictionary<ExperimentVariantId, int>      VariantWeights          { get; private set; }
        public OrderedDictionary<ExperimentVariantId, bool>     VariantIsDisabled       { get; private set; }
        public PlayerCondition                                  TargetCondition         { get; private set; }
        public PlayerExperimentGlobalState.EnrollTriggerType?   EnrollTrigger           { get; private set; }
        public bool?                                            IsRolloutDisabled       { get; private set; }

        GlobalStateModifyExperimentRequest() { }
        public GlobalStateModifyExperimentRequest(
            PlayerExperimentId playerExperimentId,
            bool? hasCapacityLimit,
            int? maxCapacity,
            int? rolloutRatioPermille,
            OrderedDictionary<ExperimentVariantId, int> variantWeights,
            OrderedDictionary<ExperimentVariantId, bool> variantIsDisabled,
            PlayerCondition targetCondition,
            PlayerExperimentGlobalState.EnrollTriggerType? enrollTrigger,
            bool? isRolloutDisabled)
        {
            PlayerExperimentId = playerExperimentId;
            HasCapacityLimit = hasCapacityLimit;
            MaxCapacity = maxCapacity;
            RolloutRatioPermille = rolloutRatioPermille;
            VariantWeights = variantWeights;
            VariantIsDisabled = variantIsDisabled;
            TargetCondition = targetCondition;
            EnrollTrigger = enrollTrigger;
            IsRolloutDisabled = isRolloutDisabled;
        }
    }
    [MetaMessage(MessageCodesCore.GlobalStateModifyExperimentResponse, MessageDirection.ServerInternal)]
    public class GlobalStateModifyExperimentResponse : MetaMessage
    {
        public string ErrorStringOrNull { get; private set; }

        GlobalStateModifyExperimentResponse() { }
        public GlobalStateModifyExperimentResponse(string errorStringOrNull)
        {
            ErrorStringOrNull = errorStringOrNull;
        }

        public static GlobalStateModifyExperimentResponse CreateError(string errorString)
        {
            if (errorString is null)
                throw new ArgumentNullException(nameof(errorString));
            return new GlobalStateModifyExperimentResponse(errorString);
        }
    }

    /// <summary>
    /// Controller -> GSM ask-message to edit Experiment Phase. GlobalStateManager responds with a <see cref="GlobalStateSetExperimentPhaseResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStateSetExperimentPhaseRequest, MessageDirection.ServerInternal)]
    public class GlobalStateSetExperimentPhaseRequest : MetaMessage
    {
        public PlayerExperimentId       PlayerExperimentId      { get; private set; }
        public PlayerExperimentPhase    Phase                   { get; private set; }

        /// <summary>
        /// If set to true, the following dangerous or unexpected transitions are allowed:
        /// * [Inactive, Testing] -> Concluded
        /// * [Ongoing, Paused] -> [ Testing ]
        /// * Concluded -> [ Ongoing, Paused, Testing ]
        ///
        /// Transitions to Inactive are not allowed since Inactive experiments are subject to auto-removal. Remove experiment instead.
        /// </summary>
        public bool                     Force                   { get; private set; }

        GlobalStateSetExperimentPhaseRequest() { }
        public GlobalStateSetExperimentPhaseRequest(PlayerExperimentId playerExperimentId, PlayerExperimentPhase phase, bool force)
        {
            PlayerExperimentId = playerExperimentId;
            Phase = phase;
            Force = force;
        }
    }
    [MetaMessage(MessageCodesCore.GlobalStateSetExperimentPhaseResponse, MessageDirection.ServerInternal)]
    public class GlobalStateSetExperimentPhaseResponse : MetaMessage
    {
        public PlayerExperimentPhase    PreviousPhase       { get; private set; }
        public string                   ErrorStringOrNull   { get; private set; }

        GlobalStateSetExperimentPhaseResponse() { }
        public GlobalStateSetExperimentPhaseResponse(PlayerExperimentPhase previousPhase, string errorStringOrNull)
        {
            PreviousPhase = previousPhase;
            ErrorStringOrNull = errorStringOrNull;
        }

        public static GlobalStateSetExperimentPhaseResponse CreateError(string errorString)
        {
            if (errorString is null)
                throw new ArgumentNullException(nameof(errorString));
            return new GlobalStateSetExperimentPhaseResponse(default, errorString);
        }
    }

    /// <summary>
    /// Controller -> GSM ask-message to fetch Experiment States. GlobalStateManager responds with a <see cref="GlobalStateGetAllExperimentsResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStateGetAllExperimentsRequest, MessageDirection.ServerInternal)]
    public class GlobalStateGetAllExperimentsRequest : MetaMessage
    {
        public static readonly GlobalStateGetAllExperimentsRequest Instance = new GlobalStateGetAllExperimentsRequest();
        GlobalStateGetAllExperimentsRequest() { }
    }
    [MetaMessage(MessageCodesCore.GlobalStateGetAllExperimentsResponse, MessageDirection.ServerInternal)]
    public class GlobalStateGetAllExperimentsResponse : MetaMessage
    {
        [MetaSerializable]
        public struct ExperimentListEntry
        {
            [MetaMember(1)] public PlayerExperimentId                               ExperimentId;
            [MetaMember(2)] public PlayerExperimentPhase                            Phase;
            [MetaMember(3)] public string                                           DisplayName;
            [MetaMember(4)] public string                                           Description;
            [MetaMember(5)] public PlayerExperimentGlobalStatistics.WhyInvalidInfo  WhyInvalid;
            [MetaMember(6)] public int                                              TotalPlayerCount;
            [MetaMember(7)] public MetaTime?                                        PhaseStartedAt;
            [MetaMember(8)] public MetaDuration?                                    OngoingDurationBeforeCurrentSpan;

            public ExperimentListEntry(
                PlayerExperimentId experimentId,
                PlayerExperimentPhase phase,
                string displayName,
                string description,
                PlayerExperimentGlobalStatistics.WhyInvalidInfo whyInvalid,
                int totalPlayerCount,
                MetaTime? phaseStartedAt,
                MetaDuration? ongoingDurationBeforeCurrentSpan)
            {
                ExperimentId = experimentId;
                Phase = phase;
                DisplayName = displayName;
                Description = description;
                WhyInvalid = whyInvalid;
                TotalPlayerCount = totalPlayerCount;
                PhaseStartedAt = phaseStartedAt;
                OngoingDurationBeforeCurrentSpan = ongoingDurationBeforeCurrentSpan;
            }
        }

        public List<ExperimentListEntry> Entries { get; private set; }

        GlobalStateGetAllExperimentsResponse() { }
        public GlobalStateGetAllExperimentsResponse(List<ExperimentListEntry> entries)
        {
            Entries = entries;
        }
    }

    /// <summary>
    /// Controller -> GSM ask-message to delete a specific Experiment. GlobalStateManager responds with a <see cref="GlobalStateDeleteExperimentResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStateDeleteExperimentRequest, MessageDirection.ServerInternal)]
    public class GlobalStateDeleteExperimentRequest : MetaMessage
    {
        public PlayerExperimentId       PlayerExperimentId      { get; private set; }

        GlobalStateDeleteExperimentRequest() { }
        public GlobalStateDeleteExperimentRequest(PlayerExperimentId playerExperimentId)
        {
            PlayerExperimentId = playerExperimentId;
        }
    }
    [MetaMessage(MessageCodesCore.GlobalStateDeleteExperimentResponse, MessageDirection.ServerInternal)]
    public class GlobalStateDeleteExperimentResponse : MetaMessage
    {
        public string                   ErrorStringOrNull   { get; private set; }

        GlobalStateDeleteExperimentResponse() { }
        public GlobalStateDeleteExperimentResponse(string errorStringOrNull)
        {
            ErrorStringOrNull = errorStringOrNull;
        }
    }

    /// <summary>
    /// Controller -> GSM ask-message to edit a player's Is-Tester flag in a certain Experiment. GlobalStateManager responds with a <see cref="GlobalStateEditExperimentTestersResponse"/>.
    /// Note that this message also contains the VariantId of the Experiment. The variant is validated but not used for anything. This allows Controller to validate assignment with
    /// the most-up-to-date game config.
    /// </summary>
    [MetaMessage(MessageCodesCore.GlobalStateEditExperimentTestersRequest, MessageDirection.ServerInternal)]
    public class GlobalStateEditExperimentTestersRequest : MetaMessage
    {
        public PlayerExperimentId       PlayerExperimentId      { get; private set; }
        public ExperimentVariantId      ExperimentVariantId     { get; private set; }
        public EntityId                 PlayerId                { get; private set; }
        public bool                     IsATester               { get; private set; }

        GlobalStateEditExperimentTestersRequest() { }
        public GlobalStateEditExperimentTestersRequest(PlayerExperimentId playerExperimentId, ExperimentVariantId experimentVariantId, EntityId playerId, bool isATester)
        {
            PlayerExperimentId = playerExperimentId;
            ExperimentVariantId = experimentVariantId;
            PlayerId = playerId;
            IsATester = isATester;
        }
    }
    [MetaMessage(MessageCodesCore.GlobalStateEditExperimentTestersResponse, MessageDirection.ServerInternal)]
    public class GlobalStateEditExperimentTestersResponse : MetaMessage
    {
        /// <summary>
        /// On success, the TesterEpoch where the change has been applied.
        /// </summary>
        public uint                     TesterEpoch         { get; private set; }
        public string                   ErrorStringOrNull   { get; private set; }

        GlobalStateEditExperimentTestersResponse() { }
        public GlobalStateEditExperimentTestersResponse(uint testerEpoch, string errorStringOrNull)
        {
            TesterEpoch = testerEpoch;
            ErrorStringOrNull = errorStringOrNull;
        }
    }

    public abstract partial class GlobalStateManagerBase<TGlobalState>
    {
        const int ControlVariantDefaultWeight = 1;
        const int NonControlVariantDefaultWeight = 1;

        [EntityAskHandler]
        GlobalStateExperimentStateResponse HandleGlobalStateExperimentStateRequest(GlobalStateExperimentStateRequest request)
        {
            PlayerExperimentGlobalState state = _state.PlayerExperiments.GetValueOrDefault(request.PlayerExperimentId);
            PlayerExperimentGlobalStatistics stats = _state.PlayerExperimentsStats.GetValueOrDefault(request.PlayerExperimentId);
            if (state == null || stats == null)
                return new GlobalStateExperimentStateResponse(null, null);
            else
                return new GlobalStateExperimentStateResponse(state, stats);
        }

        [EntityAskHandler]
        GlobalStateExperimentCombinationsResponse HandleGlobalStateExperimentCombinationsRequest(GlobalStateExperimentCombinationsRequest request)
        {
            PlayerExperimentGlobalState state = _state.PlayerExperiments.GetValueOrDefault(request.CurrentExperiment);

            int combinations = 1;

            var runningExperiments =
                _state.PlayerExperiments.Where(x => x.Value.LifecyclePhase == PlayerExperimentPhase.Ongoing);
            foreach ((PlayerExperimentId _, PlayerExperimentGlobalState value) in runningExperiments)
                combinations *= value.Variants.Count + 1;

            int newCombinations = combinations;

            if (state?.LifecyclePhase == PlayerExperimentPhase.Ongoing)
                newCombinations = combinations / (state.Variants.Count + 1);
            else if (state?.LifecyclePhase is PlayerExperimentPhase.Paused or PlayerExperimentPhase.Testing)
                newCombinations = combinations * (state.Variants.Count + 1);

            PlayerExperimentOptions playerExperimentOptions = RuntimeOptionsRegistry.Instance.GetCurrent<PlayerExperimentOptions>();

            return new GlobalStateExperimentCombinationsResponse(combinations, newCombinations, newCombinations >= playerExperimentOptions.PlayerExperimentCombinationThreshold);
        }

        [MessageHandler]
        void HandleGlobalStateForgetPlayerExperimentSamples(GlobalStateForgetPlayerExperimentSamples _)
        {
            _log.Info("Forgetting player experiment player samples");

            foreach ((PlayerExperimentId experimentId, PlayerExperimentGlobalStatistics globalExperimentStats) in _state.PlayerExperimentsStats)
            {
                globalExperimentStats.LegacyPlayerSample = null;
                foreach ((ExperimentVariantId variantId, PlayerExperimentGlobalStatistics.VariantStats variantGlobalStats) in globalExperimentStats.Variants)
                    variantGlobalStats.LegacyPlayerSample = null;
            }
        }

        [MessageHandler]
        void HandleGlobalStatePlayerExperimentAssignmentInfoUpdate(GlobalStatePlayerExperimentAssignmentInfoUpdate message)
        {
            bool shouldUpdateExperimentStatesImmediately = false;

            // Handle size updates for variants
            Dictionary<PlayerExperimentId, int> experimentTotalDelta = new Dictionary<PlayerExperimentId, int>();
            foreach (((PlayerExperimentId experimentId, ExperimentVariantId variantId), int delta) in message.SizeDeltas)
            {
                if (!_state.PlayerExperiments.TryGetValue(experimentId, out PlayerExperimentGlobalState state))
                {
                    _log.Warning("Got update for unknown experiment {ExperimentId}", experimentId);
                    continue;
                }
                if (!_state.PlayerExperimentsStats.TryGetValue(experimentId, out PlayerExperimentGlobalStatistics statistics))
                {
                    _log.Warning("Got update for unknown experiment {ExperimentId}", experimentId);
                    continue;
                }

                PlayerExperimentGlobalStatistics.VariantStats variantStats;
                if (!statistics.Variants.TryGetValue(variantId, out variantStats))
                {
                    variantStats = new PlayerExperimentGlobalStatistics.VariantStats();
                    statistics.Variants.Add(variantId, variantStats);
                }
                variantStats.NumPlayersInVariant += delta;

                int experimentIterativeDelta = experimentTotalDelta.GetValueOrDefault(experimentId, defaultValue: 0);
                experimentTotalDelta[experimentId] = experimentIterativeDelta + delta;
            }

            // Handle size updates for experiments
            foreach ((PlayerExperimentId experimentId, int totalDelta) in experimentTotalDelta)
            {
                PlayerExperimentGlobalState state = _state.PlayerExperiments[experimentId];
                int oldSize = state.NumPlayersInExperiment;
                int newSize = state.NumPlayersInExperiment + totalDelta;

                state.NumPlayersInExperiment = newSize;

                if (state.HasCapacityLimit && oldSize < state.MaxCapacity && newSize >= state.MaxCapacity)
                {
                    // Reached the limit. Keep records and inform Proxies immediately (so that they stop assigning new players into the groups).
                    MetaTime now = MetaTime.Now;
                    _state.PlayerExperimentsStats[experimentId].ReachedCapacityFirstTimeAt ??= now;
                    _state.PlayerExperimentsStats[experimentId].ReachedCapacityMostRecentlyAt = now;
                    shouldUpdateExperimentStatesImmediately = true;
                }
            }

            if (shouldUpdateExperimentStatesImmediately)
                PublishGameConfigOrExperimentUpdate();
        }

        [EntityAskHandler]
        GlobalStateModifyExperimentResponse HandleGlobalStateModifyExperimentRequest(GlobalStateModifyExperimentRequest request)
        {
            // validate

            if (!_state.PlayerExperiments.TryGetValue(request.PlayerExperimentId, out PlayerExperimentGlobalState experiment))
                return GlobalStateModifyExperimentResponse.CreateError("No such experiment.");

            if (!_state.PlayerExperimentsStats.TryGetValue(request.PlayerExperimentId, out PlayerExperimentGlobalStatistics experimentStats))
                return GlobalStateModifyExperimentResponse.CreateError("No such experiment (stats).");

            if (request.RolloutRatioPermille.HasValue && (request.RolloutRatioPermille.Value < 0 || request.RolloutRatioPermille.Value > 1000))
                return GlobalStateModifyExperimentResponse.CreateError("Invalid rollout ratio.");

            if (request.VariantWeights != null)
            {
                foreach ((ExperimentVariantId key, int weight) in request.VariantWeights)
                {
                    if (weight < 0)
                        return GlobalStateModifyExperimentResponse.CreateError("Invalid weight.");
                    if (key != null && !experiment.Variants.ContainsKey(key))
                        return GlobalStateModifyExperimentResponse.CreateError("Invalid variant. No such variant.");
                }
            }

            if (request.VariantIsDisabled != null)
            {
                foreach ((ExperimentVariantId key, bool _isDisabled) in request.VariantIsDisabled)
                {
                    if (key == null)
                        return GlobalStateModifyExperimentResponse.CreateError("Invalid variant. Cannot disable control variant.");
                    if (!experiment.Variants.ContainsKey(key))
                        return GlobalStateModifyExperimentResponse.CreateError("Invalid variant. No such variant.");
                }
            }

            // apply

            bool hadReachedTheLimit = experiment.HasCapacityLimit && experiment.NumPlayersInExperiment >= experiment.MaxCapacity;

            experiment.HasCapacityLimit     = request.HasCapacityLimit      ?? experiment.HasCapacityLimit;
            experiment.MaxCapacity          = request.MaxCapacity           ?? experiment.MaxCapacity;
            experiment.RolloutRatioPermille = request.RolloutRatioPermille  ?? experiment.RolloutRatioPermille;
            experiment.EnrollTrigger        = request.EnrollTrigger         ?? experiment.EnrollTrigger;
            experiment.TargetCondition      = request.TargetCondition;
            experiment.IsRolloutDisabled    = request.IsRolloutDisabled     ?? experiment.IsRolloutDisabled;

            if (request.VariantWeights != null)
            {
                foreach ((ExperimentVariantId key, int weight) in request.VariantWeights)
                {
                    if (key != null)
                        experiment.Variants[key].Weight = weight;
                    else
                        experiment.ControlWeight = weight;
                }
            }

            if (request.VariantIsDisabled != null)
            {
                foreach ((ExperimentVariantId key, bool isDisabled) in request.VariantIsDisabled)
                    experiment.Variants[key].IsDisabledByAdmin = isDisabled;
            }

            bool hasReachedTheLimit = experiment.HasCapacityLimit && experiment.NumPlayersInExperiment >= experiment.MaxCapacity;
            if (hasReachedTheLimit && !hadReachedTheLimit)
            {
                // Reached the limit by moving the limit under the current counter. Update values anyways.
                MetaTime now = MetaTime.Now;
                experimentStats.ReachedCapacityFirstTimeAt ??= now;
                experimentStats.ReachedCapacityMostRecentlyAt = now;
            }

            PublishGameConfigOrExperimentUpdate();

            return new GlobalStateModifyExperimentResponse(errorStringOrNull: null);
        }

        [EntityAskHandler]
        async Task<GlobalStateSetExperimentPhaseResponse> HandleGlobalStateSetExperimentPhaseRequest(GlobalStateSetExperimentPhaseRequest request)
        {
            if (!_state.PlayerExperiments.TryGetValue(request.PlayerExperimentId, out PlayerExperimentGlobalState experiment))
                return GlobalStateSetExperimentPhaseResponse.CreateError("no such experiment");
            if (experiment.LifecyclePhase == request.Phase)
                return GlobalStateSetExperimentPhaseResponse.CreateError("experiment already in the desired phase");

            // Check legality

            switch ((experiment.LifecyclePhase, request.Phase))
            {
                // legal transitions.

                case (PlayerExperimentPhase.Testing, PlayerExperimentPhase.Ongoing):
                case (PlayerExperimentPhase.Paused, PlayerExperimentPhase.Ongoing):
                    break;

                case (PlayerExperimentPhase.Ongoing, PlayerExperimentPhase.Paused):
                    break;

                case (PlayerExperimentPhase.Ongoing, PlayerExperimentPhase.Concluded):
                case (PlayerExperimentPhase.Paused, PlayerExperimentPhase.Concluded):
                    break;

                // force-only transitions

                case (PlayerExperimentPhase.Testing, PlayerExperimentPhase.Concluded):
                {
                    if (!request.Force)
                        return GlobalStateSetExperimentPhaseResponse.CreateError("Concluding a never-used experiment is unlikely to be useful. To continue, specify Force=true.");
                    break;
                }

                case (PlayerExperimentPhase.Concluded, PlayerExperimentPhase.Ongoing):
                case (PlayerExperimentPhase.Concluded, PlayerExperimentPhase.Paused):
                case (PlayerExperimentPhase.Concluded, PlayerExperimentPhase.Testing):
                {
                    if (!request.Force)
                        return GlobalStateSetExperimentPhaseResponse.CreateError("Resurrecting a concluded experiment is potentially a dangerous operation. To continue, specify Force=true.");
                    break;
                }

                case (PlayerExperimentPhase.Ongoing, PlayerExperimentPhase.Testing):
                case (PlayerExperimentPhase.Paused, PlayerExperimentPhase.Testing):
                {
                    if (!request.Force)
                        return GlobalStateSetExperimentPhaseResponse.CreateError("Transitioning a running experiment backwards into Testing is potentially a dangerous operation. To continue, specify Force=true. You should Conclude the experiment instead and create an new one.");
                    break;
                }

                // error transitions

                default:
                    return GlobalStateSetExperimentPhaseResponse.CreateError($"Unknown transition: from {experiment.LifecyclePhase}, to {request.Phase}.");
            }

            // Update stats

            MetaTime now = MetaTime.Now;
            switch (request.Phase)
            {
                case PlayerExperimentPhase.Testing:
                    _state.PlayerExperimentsStats[request.PlayerExperimentId].TestingFirstTimeAt ??= now;
                    _state.PlayerExperimentsStats[request.PlayerExperimentId].TestingMostRecentlyAt = now;
                    break;

                case PlayerExperimentPhase.Ongoing:
                    _state.PlayerExperimentsStats[request.PlayerExperimentId].OngoingFirstTimeAt ??= now;
                    _state.PlayerExperimentsStats[request.PlayerExperimentId].OngoingMostRecentlyAt = now;
                    break;

                case PlayerExperimentPhase.Paused:
                    _state.PlayerExperimentsStats[request.PlayerExperimentId].PausedFirstTimeAt ??= now;
                    _state.PlayerExperimentsStats[request.PlayerExperimentId].PausedMostRecentlyAt = now;
                    break;

                case PlayerExperimentPhase.Concluded:
                    _state.PlayerExperimentsStats[request.PlayerExperimentId].ConcludedFirstTimeAt ??= now;
                    _state.PlayerExperimentsStats[request.PlayerExperimentId].ConcludedMostRecentlyAt = now;
                    break;
            }

            if (experiment.LifecyclePhase == PlayerExperimentPhase.Ongoing)
            {
                // ongoing endeded, keep track of total ongoing time as well
                _state.PlayerExperimentsStats[request.PlayerExperimentId].OngoingDurationBeforeCurrentSpan += (now - (_state.PlayerExperimentsStats[request.PlayerExperimentId].OngoingMostRecentlyAt ?? now));
            }

            // Apply

            PlayerExperimentPhase previousPhase = experiment.LifecyclePhase;
            experiment.LifecyclePhase = request.Phase;

            // Effects

            // Experiment Phases were (forcibly) modified. Sync with config again.
            // \todo: clean this up. See todo above PrepareSyncExperimentsWithConfig.
            MetaDatabase db = MetaDatabase.Get();
            PersistedStaticGameConfig staticArchive = await db.TryGetAsync<PersistedStaticGameConfig>(_state.StaticGameConfigId.ToString());
            ConfigArchive staticConfig = ConfigArchive.FromBytes(staticArchive.ArchiveBytes);

            SyncExperimentsPlan syncPlan = PrepareSyncExperimentsWithConfig(FullGameConfig.CreateSoloUnpatched(staticConfig));
            try
            {
                await ActivatePlannedExperimentChangeAsync(syncPlan, uploadPatchesEvenIfNoChanges: false);
            }
            catch (Exception ex)
            {
                _log.Error("Failed to active experiments: {Error}", ex);
                return GlobalStateSetExperimentPhaseResponse.CreateError($"Failed to active experiments: {ex}");
            }

            PublishGameConfigOrExperimentUpdate();

            return new GlobalStateSetExperimentPhaseResponse(previousPhase, errorStringOrNull: null);
        }

        [EntityAskHandler]
        GlobalStateGetAllExperimentsResponse HandleGlobalStateGetAllExperimentsRequest(GlobalStateGetAllExperimentsRequest _)
        {
            List<GlobalStateGetAllExperimentsResponse.ExperimentListEntry> entries = new List<GlobalStateGetAllExperimentsResponse.ExperimentListEntry>();

            foreach ((PlayerExperimentId experimentId, PlayerExperimentGlobalState experimentState) in _state.PlayerExperiments)
            {
                PlayerExperimentGlobalStatistics experimentStats = _state.PlayerExperimentsStats[experimentId];

                MetaTime? phaseStartedAt;
                switch (experimentState.LifecyclePhase)
                {
                    case PlayerExperimentPhase.Testing:
                        phaseStartedAt = experimentStats.TestingMostRecentlyAt;
                        break;

                    case PlayerExperimentPhase.Ongoing:
                        phaseStartedAt = experimentStats.OngoingMostRecentlyAt;
                        break;

                    case PlayerExperimentPhase.Paused:
                        phaseStartedAt = experimentStats.PausedMostRecentlyAt;
                        break;

                    case PlayerExperimentPhase.Concluded:
                        phaseStartedAt = experimentStats.ConcludedMostRecentlyAt;
                        break;

                    default:
                        _log.Warning("Unknown experiment lifecycle phase {Phase}", experimentState.LifecyclePhase);
                        phaseStartedAt = null;
                        break;
                }

                entries.Add(new GlobalStateGetAllExperimentsResponse.ExperimentListEntry(
                    experimentId:                       experimentId,
                    phase:                              experimentState.LifecyclePhase,
                    displayName:                        experimentStats.DisplayName,
                    description:                        experimentStats.Description,
                    whyInvalid:                         experimentStats.WhyInvalid,
                    totalPlayerCount:                   experimentStats.Variants.Sum(variant => variant.Value.NumPlayersInVariant),
                    phaseStartedAt:                     phaseStartedAt,
                    ongoingDurationBeforeCurrentSpan:   experimentStats.OngoingDurationBeforeCurrentSpan
                ));
            }

            return new GlobalStateGetAllExperimentsResponse(entries);
        }

        [EntityAskHandler]
        GlobalStateDeleteExperimentResponse HandleGlobalStateDeleteExperimentRequest(GlobalStateDeleteExperimentRequest request)
        {
            // validate

            if (!_state.PlayerExperiments.TryGetValue(request.PlayerExperimentId, out PlayerExperimentGlobalState experiment))
                return new GlobalStateDeleteExperimentResponse(errorStringOrNull: "no such experiment");

            // apply

            _state.PlayerExperiments.Remove(request.PlayerExperimentId);
            _state.PlayerExperimentsStats.Remove(request.PlayerExperimentId);

            PublishGameConfigOrExperimentUpdate();

            return new GlobalStateDeleteExperimentResponse(errorStringOrNull: null);
        }

        [EntityAskHandler]
        GlobalStateEditExperimentTestersResponse HandleGlobalStateEditExperimentTestersRequest(GlobalStateEditExperimentTestersRequest request)
        {
            // validate

            if (!_state.PlayerExperiments.TryGetValue(request.PlayerExperimentId, out PlayerExperimentGlobalState experiment))
                return new GlobalStateEditExperimentTestersResponse(testerEpoch: 0, errorStringOrNull: "no such experiment");
            if (request.ExperimentVariantId != null && !experiment.Variants.ContainsKey(request.ExperimentVariantId))
                return new GlobalStateEditExperimentTestersResponse(testerEpoch: 0, errorStringOrNull: "no such variant");

            // apply

            bool wasATester = experiment.TesterPlayerIds.Contains(request.PlayerId);
            if (wasATester && !request.IsATester)
            {
                experiment.TesterPlayerIds.Remove(request.PlayerId);
                experiment.TesterEpoch++;
            }
            else if (!wasATester && request.IsATester)
            {
                experiment.TesterPlayerIds.Add(request.PlayerId);
                experiment.TesterEpoch++;
            }

            PublishGameConfigOrExperimentUpdate();

            return new GlobalStateEditExperimentTestersResponse(experiment.TesterEpoch, errorStringOrNull: null);
        }

        struct SyncExperimentsPlan
        {
            public OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalState> PlayerExperiments;
            public OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalStatistics> PlayerExperimentsStats;
            public GameConfigSpecializationPatches PatchesForPlayer;
            public GameConfigSpecializationPatches PatchesForTesters;

            public SyncExperimentsPlan(
                OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalState> playerExperiments,
                OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalStatistics>  playerExperimentsStats,
                GameConfigSpecializationPatches patchesForPlayer,
                GameConfigSpecializationPatches patchesForTesters)
            {
                PlayerExperiments = playerExperiments;
                PlayerExperimentsStats = playerExperimentsStats;
                PatchesForPlayer = patchesForPlayer;
                PatchesForTesters = patchesForTesters;
            }
        }

        /// <summary>
        /// Computes the changes to experiments the game config change would cause, BUT DOES NOT APPLY THEM.
        /// </summary>
        // \todo: make this take Static and Dynamic config versions, so that this then fetches (and combines) the
        //        final config. This would check the config combination is valid. Also figure out a way to return
        //        errors and warnings, like "this would interrupt experiment Foo" so that we can show them in the
        //        dash BEFORE the action is taken.
        SyncExperimentsPlan PrepareSyncExperimentsWithConfig(FullGameConfig fullGameConfig)
        {
            // Copy state which we then mutate according to game config changes.
            OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalState> playerExperiments = MetaSerialization.CloneTagged(_state.PlayerExperiments, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);
            OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalStatistics> playerExperimentsStats = MetaSerialization.CloneTagged(_state.PlayerExperimentsStats, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);

            void CreateNewExperimentStates()
            {
                foreach (PlayerExperimentInfo experimentInfo in fullGameConfig.ServerConfig.PlayerExperiments.Values)
                {
                    if (playerExperiments.ContainsKey(experimentInfo.ExperimentId))
                        continue;

                    // New experiment. We may put in some dummy values, the values will be fixed in SyncExperimentStates.

                    PlayerExperimentGlobalState experimentState = new PlayerExperimentGlobalState(
                        hasCapacityLimit:               false,
                        maxCapacity:                    1200,   // default to 1200.
                        rolloutRatioPermille:           1000,   // default to 100%.
                        experimentNonce:                PlayerExperimentGlobalState.GenerateNonceU32(),
                        controlWeight:                  ControlVariantDefaultWeight,
                        variants:                       new OrderedDictionary<ExperimentVariantId, PlayerExperimentGlobalState.VariantState>());

                    playerExperiments.Add(experimentInfo.ExperimentId, experimentState);
                    playerExperimentsStats.Add(experimentInfo.ExperimentId, PlayerExperimentGlobalStatistics.CreateNew());
                }
            }

            void SyncExperimentStates()
            {
                foreach ((PlayerExperimentId experimentId, PlayerExperimentGlobalState experimentState) in playerExperiments)
                {
                    PlayerExperimentGlobalStatistics experimentStats = playerExperimentsStats[experimentId];

                    if (fullGameConfig.ServerConfig.PlayerExperiments.TryGetValue(experimentId, out PlayerExperimentInfo experimentInfo))
                    {
                        if (experimentState.IsConfigMissing)
                            _log.Info("Player Experiment {PlayerExperimentId} config was restored.", experimentId);

                        experimentState.IsConfigMissing = false;

                        // Sync variants: Check data exists for existing variants. Add variant state for new configured variants.

                        foreach ((ExperimentVariantId variantId, PlayerExperimentGlobalState.VariantState variantState) in experimentState.Variants)
                        {
                            bool wasMissing = variantState.IsConfigMissing;
                            bool isMissing = !experimentInfo.Variants.ContainsKey(variantId);

                            variantState.IsConfigMissing = isMissing;

                            if (isMissing && !variantState.IsDisabledByAdmin)
                            {
                                // This will warn over an over unless the config is disabled.
                                _log.Warning("Player Experiment {PlayerExperimentId} is missing config for variant {MissingVariant}. Variant cannot be active until config is corrected.", experimentId, variantId);
                            }
                            else if (!isMissing && wasMissing)
                            {
                                _log.Info("Player Experiment {PlayerExperimentId} variant {MissingVariant} config corrected. Variant may be active again.", experimentId, variantId);
                            }
                        }

                        // Check for new variants

                        foreach (ExperimentVariantId newVariantId in experimentInfo.Variants.Keys)
                        {
                            if (experimentState.Variants.ContainsKey(newVariantId))
                                continue;

                            bool isVariantDisabled;
                            switch (experimentState.LifecyclePhase)
                            {
                                case PlayerExperimentPhase.Testing:
                                {
                                    // Experiment is not yet configured (just testing), so let's default to enabled
                                    isVariantDisabled = false;
                                    break;
                                }

                                case PlayerExperimentPhase.Ongoing:
                                case PlayerExperimentPhase.Paused:
                                {
                                    // Experiment is configured. Add a variant but disable it so that it does not affect existing distribution.
                                    isVariantDisabled = true;
                                    break;
                                }

                                case PlayerExperimentPhase.Concluded:
                                default:
                                {
                                    // Experiment is concluded. We allow adding variants (to be consistent with other phases) but this doesn't really
                                    // make any difference. Let's Disable by default, maybe that is more expected behavior.
                                    isVariantDisabled = true;
                                    break;
                                }
                            }

                            // Add experiment state. Default `isDisabledByAdmin` to a safe value
                            experimentState.Variants.Add(newVariantId, new PlayerExperimentGlobalState.VariantState(
                                weight:             NonControlVariantDefaultWeight,
                                isDisabledByAdmin:  isVariantDisabled));
                        }

                        // Sync stats

                        experimentStats.DisplayName = experimentInfo.DisplayName;
                        experimentStats.Description = experimentInfo.Description;
                        experimentStats.ExperimentAnalyticsId = experimentInfo.ExperimentAnalyticsId;

                        // Sync variant data. Note that there might be other variants and there might already be the "new" variant,
                        // if a variant was removed and re-added. Statistics survive such operation. We also add the variants in all
                        // phases. Adding empty statistics is harmless but makes reading the data easier.

                        if (!experimentStats.Variants.TryGetValue(null, out PlayerExperimentGlobalStatistics.VariantStats controlVariantStats))
                        {
                            controlVariantStats = new PlayerExperimentGlobalStatistics.VariantStats();
                            experimentStats.Variants.Add(null, controlVariantStats);
                        }
                        controlVariantStats.AnalyticsId = experimentInfo.ControlVariantAnalyticsId;

                        foreach ((ExperimentVariantId variantId, PlayerExperimentInfo.Variant variantInfo) in experimentInfo.Variants)
                        {
                            if (!experimentStats.Variants.TryGetValue(variantId, out PlayerExperimentGlobalStatistics.VariantStats variantStats))
                            {
                                variantStats = new PlayerExperimentGlobalStatistics.VariantStats();
                                experimentStats.Variants.Add(variantId, variantStats);
                            }
                            variantStats.AnalyticsId = variantInfo.AnalyticsId;
                        }
                    }
                    else
                    {
                        // No config: Log and mark everything as missing.

                        if (!experimentState.IsConfigMissing)
                            _log.Info("Player Experiment {PlayerExperimentId} config was removed.", experimentId);

                        experimentState.IsConfigMissing = true;
                        foreach (PlayerExperimentGlobalState.VariantState variant in experimentState.Variants.Values)
                            variant.IsConfigMissing = true;
                    }
                }
            }

            void UpdateWhyInvalid()
            {
                foreach ((PlayerExperimentId experimentId, PlayerExperimentGlobalState experimentState) in playerExperiments)
                {
                    PlayerExperimentGlobalStatistics experimentStats = playerExperimentsStats[experimentId];
                    bool isConfigMissing = experimentState.IsConfigMissing;
                    bool hasNoNonControlVariants = !experimentState.HasAnyNonControlVariantActive();

                    if (isConfigMissing || hasNoNonControlVariants)
                    {
                        if (isConfigMissing)
                            _log.Debug("Player Experiment {PlayerExperimentId} is not in a valid state: missing config.", experimentId);
                        else if (hasNoNonControlVariants)
                            _log.Debug("Player Experiment {PlayerExperimentId} is not in a valid state: there are no valid or enabled non-control variants.", experimentId);

                        experimentStats.WhyInvalid = new PlayerExperimentGlobalStatistics.WhyInvalidInfo(
                            isConfigDataMissing:        isConfigMissing,
                            hasNoNonControlVariants:    hasNoNonControlVariants);
                    }
                    else
                    {
                        _log.Info("Player Experiment {PlayerExperimentId} config conflict was resolved.", experimentId);
                        experimentStats.WhyInvalid = null;
                    }
                }
            }

            CreateNewExperimentStates();
            SyncExperimentStates();
            UpdateWhyInvalid();

            // Build the patches
            GameConfigSpecializationPatches specializationPatchesForPlayers = GameConfigSpecializationPatchesBuilder.TryBuildNonEmpty(
                serverConfig:   fullGameConfig.ServerConfig,
                activeSubset:   GlobalState.GetVisibleExperimentsInGameConfigOrder(playerExperiments, fullGameConfig, PlayerExperimentSubject.Player),
                outBuildStats:  out GameConfigSpecializationPatchesBuilder.BuildStats buildStatsForPlayers);

            GameConfigSpecializationPatches specializationPatchesForTesters = GameConfigSpecializationPatchesBuilder.TryBuildNonEmpty(
                serverConfig:   fullGameConfig.ServerConfig,
                activeSubset:   GlobalState.GetVisibleExperimentsInGameConfigOrder(playerExperiments, fullGameConfig, PlayerExperimentSubject.Tester),
                outBuildStats:  out GameConfigSpecializationPatchesBuilder.BuildStats buildStatsForTesters);

            _log.Debug("Specialization patches for players contain {NumVariant} variants. In total, {NumTotalBytes} bytes.", buildStatsForPlayers.NumVariants, buildStatsForPlayers.TotalNumBytes);
            _log.Debug("Specialization patches for testers contain {NumVariant} variants. In total, {NumTotalBytes} bytes.", buildStatsForTesters.NumVariants, buildStatsForTesters.TotalNumBytes);

            return new SyncExperimentsPlan(
                playerExperiments:      playerExperiments,
                playerExperimentsStats: playerExperimentsStats,
                patchesForPlayer:       specializationPatchesForPlayers,
                patchesForTesters:      specializationPatchesForTesters);
        }

        /// <summary>
        /// Publish the SharedGameConfig patches into CDN (under GameConfig/SharedGameConfigPatches/) if needed and
        /// updates the current versions in state. On failure, throws without making the experiment change.
        /// </summary>
        async Task ActivatePlannedExperimentChangeAsync(SyncExperimentsPlan syncPlan, bool uploadPatchesEvenIfNoChanges)
        {
            ContentHash playerVersion = syncPlan.PatchesForPlayer?.Version ?? ContentHash.None;
            ContentHash testerVersion = syncPlan.PatchesForTesters?.Version ?? ContentHash.None;

            // Update resources to CDN. We must have a non-null version, and it must be different than existing. If the new version
            // if null, there is nothing to upload.
            if (syncPlan.PatchesForPlayer != null
                && (playerVersion != _state.SharedGameConfigPatchesForPlayersContentHash || uploadPatchesEvenIfNoChanges))
            {
                await ServerConfigDataProvider.Instance.PublicBlobStorage.PutAsync($"SharedGameConfigPatches/{playerVersion}", syncPlan.PatchesForPlayer.ToBytes());
            }
            // Same for the Tester set. Additionally we can skip the upload if the Player set was (or will be) indentical, in which
            // cases the upload is already done.
            if (syncPlan.PatchesForTesters != null
                && (testerVersion != _state.SharedGameConfigPatchesForTestersContentHash || uploadPatchesEvenIfNoChanges)
                && testerVersion != playerVersion)
            {
                await ServerConfigDataProvider.Instance.PublicBlobStorage.PutAsync($"SharedGameConfigPatches/{testerVersion}", syncPlan.PatchesForTesters.ToBytes());
            }

            _state.PlayerExperiments = syncPlan.PlayerExperiments;
            _state.PlayerExperimentsStats = syncPlan.PlayerExperimentsStats;
            _state.SharedGameConfigPatchesForPlayersContentHash = playerVersion;
            _state.SharedGameConfigPatchesForTestersContentHash = testerVersion;

            EmitAnalyticsEventsAfterExperimentChange();
        }

        void EmitAnalyticsEventsAfterExperimentChange()
        {
            // Emit events for all known experiments, and variants of the active experiments
            foreach ((PlayerExperimentId experimentId, PlayerExperimentGlobalState experimentState) in _state.PlayerExperiments)
            {
                PlayerExperimentGlobalStatistics experimentStats = _state.PlayerExperimentsStats[experimentId];
                bool isActive = experimentState.LifecyclePhase == PlayerExperimentPhase.Ongoing;
                bool isCapacityReached = experimentState.HasCapacityLimit && experimentState.NumPlayersInExperiment >= experimentState.MaxCapacity;

                // Event for experiment
                EmitAnalyticsEvent(new ServerEventExperimentInfo(
                    experimentId:           experimentId,
                    experimentAnalyticsId:  experimentStats.ExperimentAnalyticsId,
                    isActive:               isActive,
                    isRollingOut:           isActive && !isCapacityReached,
                    displayName:            experimentStats.DisplayName,
                    description:            experimentStats.Description));

                // Event for variants
                if (isActive)
                {
                    foreach ((ExperimentVariantId variantId, PlayerExperimentGlobalState.VariantState variantState) in experimentState.Variants)
                        EmitAnalyticsEvent(new ServerEventExperimentVariantInfo(experimentId, experimentStats.ExperimentAnalyticsId, variantId, experimentStats.Variants[variantId].AnalyticsId));
                }
            }
        }
    }

    public abstract partial class GlobalState
    {
        public static List<PlayerExperimentId> GetVisibleExperimentsInGameConfigOrder(OrderedDictionary<PlayerExperimentId, PlayerExperimentGlobalState> experimentStates, FullGameConfig baselineFullGameConfig, PlayerExperimentSubject subject)
        {
            List<PlayerExperimentId> experimentIds = new List<PlayerExperimentId>();

            foreach (PlayerExperimentId experimentId in baselineFullGameConfig.ServerConfig.PlayerExperiments.Keys)
            {
                if (!experimentStates.TryGetValue(experimentId, out PlayerExperimentGlobalState experimentState))
                    continue;
                if (experimentState.IsConfigMissing)
                    continue;
                if (!experimentState.HasAnyNonControlVariantActive())
                    continue;

                bool isVisibleExperiment;
                switch (experimentState.LifecyclePhase)
                {
                    case PlayerExperimentPhase.Testing:
                    {
                        // In testing phase, experiments are visible only for testers.
                        isVisibleExperiment = subject == PlayerExperimentSubject.Tester;
                        break;
                    }

                    case PlayerExperimentPhase.Ongoing:
                    case PlayerExperimentPhase.Paused:
                    {
                        // In Ongoing and Paused experiment is visibile to everybody. However, in Paused, the experiment
                        // is _ACTIVE_ only for Testers.
                        isVisibleExperiment = true;
                        break;
                    }

                    default:
                    {
                        // Concluded experiments are not visible.
                        isVisibleExperiment = false;
                        break;
                    }
                }
                if (!isVisibleExperiment)
                    continue;

                experimentIds.Add(experimentId);
            }

            return experimentIds;
        }
    }
}
