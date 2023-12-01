// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;
using static System.FormattableString;

namespace Metaplay.Server.AdminApi.Controllers
{
    /// <summary>
    /// Controller to view and edit Player Experiment state
    /// </summary>
    public class PlayerExperimentsController : GameAdminApiController
    {
        public PlayerExperimentsController(ILogger<PlayerExperimentsController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// Audit log events
        /// </summary>
        [MetaSerializableDerived(MetaplayAuditLogEventCodes.ExperimentEdited)]
        public class ExperimentEdited : ExperimentEventPayloadBase
        {
            [MetaMember(3)]  public bool?                                            HasCapacityLimit        { get; private set; }
            [MetaMember(4)]  public int?                                             MaxCapacity             { get; private set; }
            [MetaMember(5)]  public int?                                             RolloutRatioPermille    { get; private set; }
            [MetaMember(6)]  public OrderedDictionary<string, int>                   VariantWeights          { get; private set; }
            [MetaMember(7)]  public List<PlayerSegmentId>                            LegacyTargetSegments    { get; private set; }
            [MetaMember(10)] public PlayerCondition                                  TargetCondition         { get; private set; }
            [MetaMember(8)]  public PlayerExperimentGlobalState.EnrollTriggerType?   EnrollTrigger           { get; private set; }
            [MetaMember(9)]  public OrderedDictionary<string, bool>                  VariantIsDisabled       { get; private set; }
            [MetaMember(11)] public bool?                                            IsRolloutDisabled       { get; private set; }

            ExperimentEdited() { }
            public ExperimentEdited(
                bool? hasCapacityLimit,
                int? maxCapacity,
                int? rolloutRatioPermille,
                OrderedDictionary<ExperimentVariantId, int> variantWeights,
                OrderedDictionary<ExperimentVariantId, bool> variantIsDisabled,
                PlayerCondition targetCondition,
                PlayerExperimentGlobalState.EnrollTriggerType? enrollTrigger,
                bool? isRolloutDisabled)
            {
                HasCapacityLimit = hasCapacityLimit;
                MaxCapacity = maxCapacity;
                RolloutRatioPermille = rolloutRatioPermille;
                if (variantWeights != null) VariantWeights = new OrderedDictionary<string, int>(variantWeights.Select((kv) => new KeyValuePair<string, int>(kv.Key?.ToString() ?? "$null", kv.Value)));
                TargetCondition = targetCondition;
                EnrollTrigger = enrollTrigger;
                if (variantIsDisabled != null) VariantIsDisabled = new OrderedDictionary<string, bool>(variantIsDisabled.Select((kv) => new KeyValuePair<string, bool>(kv.Key?.ToString() ?? "$null", kv.Value)));
                IsRolloutDisabled = isRolloutDisabled;
            }

            public override string EventTitle => "Experiment modified";
            public override string EventDescription
            {
                get
                {
                    List<string> changes = new List<string>();

                    if (HasCapacityLimit.HasValue)      changes.Add(Invariant($"HasCapacityLimit = {HasCapacityLimit}"));
                    if (MaxCapacity.HasValue)           changes.Add(Invariant($"MaxCapacity = {MaxCapacity}"));
                    if (RolloutRatioPermille.HasValue)  changes.Add(Invariant($"RolloutRatio = {RolloutRatioPermille / 10.0f:F1}%"));
                    if (VariantWeights != null)
                    {
                        IEnumerable<string> values = VariantWeights.AsEnumerable().Select((kv) =>
                        {
                            if (kv.Key == "$null")
                                return Invariant($"control: {kv.Value}");
                            else
                                return Invariant($"{kv.Key}: {kv.Value}");
                        });
                        changes.Add($"VariantWeights = {{{string.Join(", ", values)}}}");
                    }
                    if (VariantIsDisabled != null)
                    {
                        IEnumerable<string> values = VariantIsDisabled.AsEnumerable().Select((kv) =>
                        {
                            if (kv.Key == "$null")
                                return Invariant($"control: {kv.Value}");
                            else
                                return Invariant($"{kv.Key}: {kv.Value}");
                        });
                        changes.Add($"VariantIsDisabled = {{{string.Join(", ", values)}}}");
                    }
                    if (LegacyTargetSegments != null)
                    {
                        if (LegacyTargetSegments.Count > 0)
                            changes.Add($"TargetSegments = {{{string.Join(", ", LegacyTargetSegments)}}}");
                        else
                            changes.Add($"TargetSegments = <any player>");
                    }
                    if (TargetCondition != null)
                    {
                        string conditionDescription;
                        if (TargetCondition is PlayerSegmentBasicCondition basicCondition)
                            conditionDescription = basicCondition.Describe();
                        else
                            conditionDescription = TargetCondition.ToString();

                        changes.Add($"TargetCondition = {conditionDescription}");
                    }
                    if (EnrollTrigger.HasValue)         changes.Add(Invariant($"EnrollTrigger = {EnrollTrigger}"));
                    if (IsRolloutDisabled.HasValue)     changes.Add(Invariant($"IsRolloutDisabled = {IsRolloutDisabled}"));

                    return string.Join(", ", changes);
                }
            }
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.ExperimentPhaseChange)]
        public class ExperimentPhaseChanged : ExperimentEventPayloadBase
        {
            [MetaMember(1)] public string   NewPhase    { get; private set; }
            [MetaMember(2)] public string   OldPhase    { get; private set; }
            [MetaMember(3)] public bool     Force       { get; private set; }

            ExperimentPhaseChanged() { }
            public ExperimentPhaseChanged(string newPhase, string oldPhase, bool force)
            {
                NewPhase = newPhase;
                OldPhase = oldPhase;
                Force = force;
            }

            public override string EventTitle => "Experiment phase changed";
            public override string EventDescription => $"Phase {(Force ? "forcibly " : "")}set to \"{NewPhase}\" (was \"{OldPhase}\").";
        }

        [MetaSerializableDerived(MetaplayAuditLogEventCodes.ExperimentDeleted)]
        public class ExperimentDeleted : ExperimentEventPayloadBase
        {
            public ExperimentDeleted() { }

            public override string EventTitle => "Experiment deleted";
            public override string EventDescription => $"Experiment configuration was deleted.";
        }

        /// <summary>
        /// API endpoint to view experiment
        /// Usage:  GET /api/experiments
        /// Test:   curl http://localhost:5550/api/experiments
        /// </summary>
        [HttpGet("experiments")]
        [RequirePermission(MetaplayPermissions.ApiExperimentsView)]
        public async Task<IActionResult> GetExperiments()
        {
            ActiveGameConfig activeGameConfig = GlobalStateProxyActor.ActiveGameConfig.Get();
            if (activeGameConfig == null)
                throw new MetaplayHttpException(500, "Experiments not found.", "Cannot get current active game config.");

            GlobalStateGetAllExperimentsRequest       request              = GlobalStateGetAllExperimentsRequest.Instance;
            GlobalStateGetAllExperimentsResponse      response             = await AskEntityAsync<GlobalStateGetAllExperimentsResponse>(GlobalStateManager.EntityId, request);

            GlobalStateExperimentCombinationsRequest  combinationsRequest  = new GlobalStateExperimentCombinationsRequest();
            GlobalStateExperimentCombinationsResponse combinationsResponse = await AskEntityAsync<GlobalStateExperimentCombinationsResponse>(GlobalStateManager.EntityId, combinationsRequest);
            return Ok(new
            {
                Experiments = response.Entries,
                Combinations = combinationsResponse,
            });
        }

        /// <summary>
        /// API endpoint to view experiment state
        /// Usage:  GET /api/experiments/{EXPERIMENTID}
        /// Test:   curl http://localhost:5550/api/experiments/{EXPERIMENTID}
        /// </summary>
        [HttpGet("experiments/{experimentIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiExperimentsView)]
        public async Task<IActionResult> GetExperimentState(string experimentIdStr)
        {
            PlayerExperimentId experimentId = PlayerExperimentId.FromString(experimentIdStr);

            // Try to get up-to-date stats form GSM
            GlobalStateExperimentStateRequest  request  = new GlobalStateExperimentStateRequest(experimentId);
            GlobalStateExperimentStateResponse response = await AskEntityAsync<GlobalStateExperimentStateResponse>(GlobalStateManager.EntityId, request);

            GlobalStateExperimentCombinationsRequest  combinationsRequest  = new GlobalStateExperimentCombinationsRequest(experimentId);
            GlobalStateExperimentCombinationsResponse combinationsResponse = await AskEntityAsync<GlobalStateExperimentCombinationsResponse>(GlobalStateManager.EntityId, combinationsRequest);
            if (response.IsSuccess)
            {
                // Map null into "null" to make it JSON serializeable
                if (response.Statistics.Variants.ContainsKey(null))
                {
                    response.Statistics.Variants[ExperimentVariantId.FromString("null")] = response.Statistics.Variants[null];
                    response.Statistics.Variants.Remove(null);
                }

                return Ok(new
                {
                    State = response.State,
                    Stats = response.Statistics,
                    Combinations = combinationsResponse
                });
            }
            else
            {
                throw new MetaplayHttpException(500, "Experiment not found.", $"Could not retrieve data for experiment with ID {experimentIdStr}.");
            }
        }

        public struct EditExperimentConfigInput
        {
            [JsonProperty(Required = Required.AllowNull)] public bool?                                          HasCapacityLimit    { get; private set; }
            [JsonProperty(Required = Required.AllowNull)] public int?                                           MaxCapacity         { get; private set; }
            [JsonProperty(Required = Required.AllowNull)] public int?                                           RolloutRatioPermille{ get; private set; }
            [JsonProperty(Required = Required.AllowNull)] public OrderedDictionary<ExperimentVariantId, int>    VariantWeights      { get; private set; }
            [JsonProperty(Required = Required.AllowNull)] public OrderedDictionary<ExperimentVariantId, bool>   VariantIsDisabled   { get; private set; }
            [JsonProperty(Required = Required.AllowNull)] public PlayerCondition                                TargetCondition     { get; private set; }
            [JsonProperty(Required = Required.AllowNull)] public PlayerExperimentGlobalState.EnrollTriggerType? EnrollTrigger       { get; private set; }
            [JsonProperty(Required = Required.AllowNull)] public bool?                                          IsRolloutDisabled   { get; private set; }
        }

        /// <summary>
        /// API endpoint to edit experiment config
        /// Usage:  POST /api/experiments/{EXPERIMENTID}/config
        /// Test:   curl http://localhost:5550/api/experiments/{EXPERIMENTID}/config -X POST -H "Content-Type:application/json" -d '{"HasCapacityLimit": null, "MaxCapacity": null, "RolloutRatioPermille": null, "VariantWeights": null, "VariantIsDisabled":null}'
        /// </summary>
        [HttpPost("experiments/{experimentIdStr}/config")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiExperimentsEdit)]
        public async Task<IActionResult> EditExperimentConfig(string experimentIdStr)
        {
            PlayerExperimentId experimentId = PlayerExperimentId.FromString(experimentIdStr);
            EditExperimentConfigInput input = await ParseBodyAsync<EditExperimentConfigInput>();

            // Map null from "null" that is there for JSON serialization
            if (input.VariantWeights != null && input.VariantWeights.ContainsKey(ExperimentVariantId.FromString("null")))
            {
                input.VariantWeights[null] = input.VariantWeights[ExperimentVariantId.FromString("null")];
                input.VariantWeights.Remove(ExperimentVariantId.FromString("null"));
            }
            if (input.VariantIsDisabled != null && input.VariantIsDisabled.ContainsKey(ExperimentVariantId.FromString("null")))
            {
                input.VariantIsDisabled[null] = input.VariantIsDisabled[ExperimentVariantId.FromString("null")];
                input.VariantIsDisabled.Remove(ExperimentVariantId.FromString("null"));
            }

            GlobalStateModifyExperimentRequest request = new GlobalStateModifyExperimentRequest(
                playerExperimentId:     experimentId,
                hasCapacityLimit:       input.HasCapacityLimit,
                maxCapacity:            input.MaxCapacity,
                rolloutRatioPermille:   input.RolloutRatioPermille,
                variantWeights:         input.VariantWeights,
                variantIsDisabled:      input.VariantIsDisabled,
                targetCondition:        input.TargetCondition,
                enrollTrigger:          input.EnrollTrigger,
                isRolloutDisabled:      input.IsRolloutDisabled);
            GlobalStateModifyExperimentResponse response = await AskEntityAsync<GlobalStateModifyExperimentResponse>(GlobalStateManager.EntityId, request);
            if (response.ErrorStringOrNull == null)
            {
                await WriteAuditLogEventAsync(new ExperimentEventBuilder(experimentIdStr, new ExperimentEdited(
                    hasCapacityLimit:       input.HasCapacityLimit,
                    maxCapacity:            input.MaxCapacity,
                    rolloutRatioPermille:   input.RolloutRatioPermille,
                    variantWeights:         input.VariantWeights,
                    variantIsDisabled:      input.VariantIsDisabled,
                    targetCondition:        input.TargetCondition,
                    enrollTrigger:          input.EnrollTrigger,
                    isRolloutDisabled:      input.IsRolloutDisabled)));
                return Ok();
            }
            else
            {
                throw new MetaplayHttpException(400, "Failed to modify experiment config.", response.ErrorStringOrNull);
            }
        }

        public struct EditExperimentPhaseInput
        {
            [JsonProperty(Required = Required.Always)] public PlayerExperimentPhase     Phase   { get; private set; }

            /// <summary>
            /// <inheritdoc cref="GlobalStateSetExperimentPhaseRequest.Force"/>
            /// </summary>
            [JsonProperty(Required = Required.Always)] public bool                      Force   { get; private set; }
        }

        /// <summary>
        /// API endpoint to edit experiment phase
        /// Usage:  POST /api/experiments/{EXPERIMENTID}/phase
        /// Test:   curl http://localhost:5550/api/experiments/{EXPERIMENTID}/phase -X POST -H "Content-Type:application/json" -d '{"Phase":"Enabled", "Force": false}'
        /// </summary>
        [HttpPost("experiments/{experimentIdStr}/phase")]
        [Consumes("application/json")]
        [RequirePermission(MetaplayPermissions.ApiExperimentsEdit)]
        public async Task<IActionResult> EditExperimentPhase(string experimentIdStr)
        {
            PlayerExperimentId experimentId = PlayerExperimentId.FromString(experimentIdStr);
            EditExperimentPhaseInput input = await ParseBodyAsync<EditExperimentPhaseInput>();

            GlobalStateSetExperimentPhaseRequest request = new GlobalStateSetExperimentPhaseRequest(
                playerExperimentId:     experimentId,
                phase:                  input.Phase,
                force:                  input.Force);
            GlobalStateSetExperimentPhaseResponse response = await AskEntityAsync<GlobalStateSetExperimentPhaseResponse>(GlobalStateManager.EntityId, request);
            if (response.ErrorStringOrNull == null)
            {
                await WriteAuditLogEventAsync(new ExperimentEventBuilder(experimentIdStr, new ExperimentPhaseChanged(
                    newPhase:               input.Phase.ToString(),
                    oldPhase:               response.PreviousPhase.ToString(),
                    force:                  input.Force)));
                return Ok();
            }
            else
            {
                throw new MetaplayHttpException(400, "Failed to modify experiment phase.", response.ErrorStringOrNull);
            }
        }

        /// <summary>
        /// API endpoint to delete an experiment
        /// Usage:  DELETE /api/experiments/{EXPERIMENTID}
        /// Test:   curl http://localhost:5550/api/experiments/{EXPERIMENTID} -X DELETE
        /// </summary>
        [HttpDelete("experiments/{experimentIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiExperimentsEdit)]
        public async Task<IActionResult> DeleteExperiment(string experimentIdStr)
        {
            PlayerExperimentId experimentId = PlayerExperimentId.FromString(experimentIdStr);
            GlobalStateDeleteExperimentRequest request = new GlobalStateDeleteExperimentRequest(experimentId);
            GlobalStateDeleteExperimentResponse response = await AskEntityAsync<GlobalStateDeleteExperimentResponse>(GlobalStateManager.EntityId, request);
            if (response.ErrorStringOrNull == null)
            {
                await WriteAuditLogEventAsync(new ExperimentEventBuilder(experimentIdStr, new ExperimentDeleted()));
                return Ok();
            }
            else
            {
                throw new MetaplayHttpException(400, "Failed to delete experiment.", response.ErrorStringOrNull);
            }
        }
    }
}
