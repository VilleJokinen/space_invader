// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Metaplay.Server.AdminApi.Controllers.Exceptions;
using ActionResult = Microsoft.AspNetCore.Mvc.ActionResult;

namespace Metaplay.Server.AdminApi.Controllers
{
    public class ActivablesController : ActivablesControllerBase
    {
        public ActivablesController(ILogger<ActivablesController> logger, IActorRef adminApi) : base(logger, adminApi)
        {
        }

        /// <summary>
        /// API endpoint to get activables info.
        ///
        /// <paramref name="time"/> determines the time to use
        /// when resolving the phases of local-time schedules.
        /// If it's omitted, current UTC time is used.
        ///
        /// Usage: GET /api/activables
        /// </summary>
        [HttpGet("activables")]
        [RequirePermission(MetaplayPermissions.ApiActivablesView)]
        public async Task<ActionResult> GetActivablesAsync([FromQuery] string time)
        {
            GeneralActivablesQueryContext context = await GetGeneralActivablesQueryContextAsync(time).ConfigureAwait(false);

            OrderedDictionary<MetaActivableKindId, GeneralActivableKindData> activableDatas = MetaActivableRepository.Instance.AllKinds.Values.ToOrderedDictionary(
                kind => kind.Id,
                kind =>
                {
                    OrderedDictionary<object, GeneralActivableData> activables;
                    if (kind.GameConfigLibrary == null)
                        activables = new OrderedDictionary<object, GeneralActivableData>();
                    else
                    {
                        IGameConfigLibraryEntry library = kind.GameConfigLibrary.GetMemberValue(context.SharedGameConfig);
                        activables = library.EnumerateAll().ToOrderedDictionary(
                            activableKV => activableKV.Key,
                            activableKV =>
                            {
                                (object activableId, object activableInfoObj) = activableKV;
                                return CreateGeneralActivableData(activableId, (IMetaActivableConfigData)activableInfoObj, kind.Id, context);
                            });
                    }

                    return new GeneralActivableKindData(
                        activables,
                        kind.GetIncompleteIntegrationErrors());
                });

            return Ok(activableDatas);
        }

        /// <summary>
        /// API endpoint to get a single activable's info.
        ///
        /// <para><paramref name="kindIdStr"/> is the id for the activable kind.</para>
        ///
        /// <para><paramref name="activableIdStr"/> is the id for the activable entity.</para>
        ///
        /// <para><paramref name="time"/> determines the time to use
        /// when resolving the phases of local-time schedules.
        /// If it's omitted, current UTC time is used.</para>
        ///
        /// Usage: GET /api/activables/activable/{kindIdStr}/{activableIdStr}"
        /// </summary>
        [HttpGet("activables/activable/{kindIdStr}/{activableIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiActivablesView)]
        public async Task<ActionResult> GetActivableAsync(string kindIdStr, string activableIdStr, [FromQuery] string time)
        {
            if (string.IsNullOrWhiteSpace(kindIdStr))
                throw new MetaplayHttpException(400, "Failed to get activable!", $"Invalid activable kind id: {kindIdStr}");
            if (string.IsNullOrWhiteSpace(activableIdStr))
                throw new MetaplayHttpException(400, "Failed to get activable!", $"Invalid activable id: {activableIdStr}");

            MetaActivableKindId kindId = MetaActivableKindId.FromString(kindIdStr);

            GeneralActivablesQueryContext context = await GetGeneralActivablesQueryContextAsync(time).ConfigureAwait(false);
            if (MetaActivableRepository.Instance.AllKinds.TryGetValue(kindId, out MetaActivableRepository.KindSpec kind))
            {
                if (kind.GameConfigLibrary == null)
                    throw new MetaplayHttpException(400, "Failed to get activable!", $"Invalid activable kind: {kindIdStr}. Kind incomplete integration errors: {kind.GetIncompleteIntegrationErrors()}");
                else
                {
                    IStringId                activableId = StringIdUtil.CreateDynamic(kind.ConfigKeyType, activableIdStr);
                    IGameConfigLibraryEntry       library     = kind.GameConfigLibrary.GetMemberValue(context.SharedGameConfig);
                    IMetaActivableConfigData info;
                    try
                    {
                        info = (IMetaActivableConfigData)library.GetInfoByKey((object)activableId);
                    }
                    catch (KeyNotFoundException)
                    {
                        throw new MetaplayHttpException(404, "Activable not found!", $"Activable with id: {activableIdStr} not found.");
                    }

                    GeneralActivableData data = CreateGeneralActivableData(activableId, info, kindId, context);

                    return Ok(data);
                }
            }
            else
            {
                throw new MetaplayHttpException(404, "Activable kind not found!", $"Activable kind with id: {kindIdStr} not found.");
            }
        }

        [HttpGet("activables/{playerIdStr}")]
        [RequirePermission(MetaplayPermissions.ApiActivablesView)]
        async public Task<ActionResult> GetActivablesForPlayer(string playerIdStr)
        {
            PlayerActivablesQueryContext context = await GetPlayerActivablesQueryContextAsync(playerIdStr).ConfigureAwait(false);

            OrderedDictionary<MetaActivableKindId, PlayerActivableKindData> activableDatas = MetaActivableRepository.Instance.AllKinds.Values.ToOrderedDictionary(
                kind => kind.Id,
                kind =>
                {
                    OrderedDictionary<object, PlayerActivableData> activables;
                    if (kind.GameConfigLibrary == null || !kind.PlayerSubModel.TryGetMemberValue(context.PlayerModel, out IMetaActivableSet playerActivables))
                        activables = new OrderedDictionary<object, PlayerActivableData>();
                    else
                    {
                        IGameConfigLibraryEntry library = kind.GameConfigLibrary.GetMemberValue(context.SharedGameConfig);
                        activables = library.EnumerateAll().ToOrderedDictionary(
                            kv => kv.Key,
                            kv =>
                            {
                                IMetaActivableConfigData info = (IMetaActivableConfigData)kv.Value;
                                return CreatePlayerActivableData(info, playerActivables, context);
                            });
                    }

                    return new PlayerActivableKindData(
                        activables,
                        kind.GetIncompleteIntegrationErrors());
                });

            return Ok(activableDatas);
        }

        [HttpGet("activables/forcePhaseEnabled")]
        [RequirePermission(MetaplayPermissions.ApiActivablesView)]
        public ActionResult ForcePlayerActivablePhaseEnabled()
        {
            EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
            return Ok(new
            {
                ForcePlayerActivablePhaseEnabled = envOpts.EnableDevelopmentFeatures
            });
        }

        [HttpPost("activables/{playerIdStr}/forcePhase/{kindIdStr}/{activableIdStr}/{phaseStr}")]
        [RequirePermission(MetaplayPermissions.ApiPlayersForceActivablePhase)]
        public async Task ForcePlayerActivablePhaseRequest(string playerIdStr, string kindIdStr, string activableIdStr, string phaseStr)
        {
            EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
            if (!envOpts.EnableDevelopmentFeatures)
                throw new MetaplayHttpException(400, "Development features not enabled.", $"Forcing the phase of an activable is a development feature, and {nameof(EnvironmentOptions)}.{nameof(EnvironmentOptions.EnableDevelopmentFeatures)} is false.");

            MetaActivableKindId kindId = MetaActivableKindId.FromString(kindIdStr);
            if (!MetaActivableRepository.Instance.AllKinds.ContainsKey(kindId))
                throw new MetaplayHttpException(400, "No such activable kind.", $"No activable kind '{kindId}' exists.");

            EntityId                        playerId    = await ParsePlayerIdStrAndCheckForExistenceAsync(playerIdStr).ConfigureAwait(false);
            MetaActivableState.DebugPhase?  phase       = phaseStr == "none" ? null : EnumUtil.Parse<MetaActivableState.DebugPhase>(phaseStr);

            await TellEntityAsync(playerId, new InternalPlayerForceActivablePhaseMessage(kindId, activableIdStr, phase));
        }
    }
}
