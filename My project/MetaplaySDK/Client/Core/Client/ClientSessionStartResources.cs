// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Localization;
using Metaplay.Core.Message;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Player;
using System;

namespace Metaplay.Core.Client
{
    /// <summary>
    /// Client-side state of the resource negoatiation during session handshake.
    /// </summary>
    public class ClientSessionNegotiationResources
    {
        public OrderedDictionary<ClientSlot, ConfigArchive> ConfigArchives = new OrderedDictionary<ClientSlot, ConfigArchive>();
        public OrderedDictionary<ClientSlot, GameConfigSpecializationPatches> PatchArchives  = new OrderedDictionary<ClientSlot, GameConfigSpecializationPatches>();
        public LocalizationLanguage                                           ActiveLanguage;

        public SessionProtocol.SessionResourceProposal ToResourceProposal()
        {
            OrderedDictionary<ClientSlot, ContentHash> configVersions = new OrderedDictionary<ClientSlot, ContentHash>();
            OrderedDictionary<ClientSlot, ContentHash> patchVersions = new OrderedDictionary<ClientSlot, ContentHash>();

            foreach ((ClientSlot slot, ConfigArchive config) in ConfigArchives)
                configVersions[slot] = config.Version;
            foreach ((ClientSlot slot, GameConfigSpecializationPatches patch) in PatchArchives)
                patchVersions[slot] = patch.Version;

            SessionProtocol.SessionResourceProposal proposal = new SessionProtocol.SessionResourceProposal(
                configVersions:                 configVersions,
                patchVersions:                  patchVersions,
                clientActiveLanguage:           ActiveLanguage?.LanguageId,
                clientLocalizationVersion:      ActiveLanguage?.Version ?? ContentHash.None
                );
            return proposal;
        }
    }

    /// <summary>
    /// Client-side result of resource negoatiation completed during session handshake.
    /// </summary>
    public class ClientSessionStartResources
    {
        public readonly int LogicVersion;
        public readonly OrderedDictionary<ClientSlot, ISharedGameConfig> GameConfigs;
        public readonly OrderedDictionary<ClientSlot, ContentHash> GameConfigBaselineVersions;
        public readonly OrderedDictionary<ClientSlot, ContentHash> GameConfigPatchVersions;

        public ClientSessionStartResources(int logicVersion, OrderedDictionary<ClientSlot, ISharedGameConfig> gameConfigs, OrderedDictionary<ClientSlot, ContentHash> gameConfigBaselineVersions, OrderedDictionary<ClientSlot, ContentHash> gameConfigPatchVersions)
        {
            LogicVersion = logicVersion;
            GameConfigs = gameConfigs;
            GameConfigBaselineVersions = gameConfigBaselineVersions;
            GameConfigPatchVersions = gameConfigPatchVersions;
        }

        public static ClientSessionStartResources SpecializeResources(SessionProtocol.SessionStartSuccess sessionStartSuccess, ClientSessionNegotiationResources negotiationResources, Func<ConfigArchive, (GameConfigSpecializationPatches, GameConfigSpecializationKey)?, ISharedGameConfig> gameConfigImporter)
        {
            OrderedDictionary<ClientSlot, ISharedGameConfig> gameConfigs = new OrderedDictionary<ClientSlot, ISharedGameConfig>();
            OrderedDictionary<ClientSlot, ContentHash> gameConfigBaselineVersions = new OrderedDictionary<ClientSlot, ContentHash>();
            OrderedDictionary<ClientSlot, ContentHash> gameConfigPatchVersions = new OrderedDictionary<ClientSlot, ContentHash>();

            foreach ((ClientSlot slot, ConfigArchive configArchive) in negotiationResources.ConfigArchives)
            {
                gameConfigBaselineVersions[slot] = configArchive.Version;

                // Get experiment assignment of the entity in the slot. Except Player which uses fixed location.
                OrderedDictionary<PlayerExperimentId, ExperimentVariantId> assignment = null;
                if (slot == ClientSlotCore.Player)
                {
                    assignment = sessionStartSuccess.ActiveExperiments?.ToOrderedDictionary(experiment => experiment.ExperimentId, experiment => experiment.VariantId);

                    // normalize empty lists into null.
                    if (assignment != null && assignment.Count == 0)
                        assignment = null;
                }
                else
                {
                    foreach (EntityInitialState entityState in sessionStartSuccess.EntityStates)
                    {
                        if (entityState.ContextData.ClientSlot != slot)
                            continue;
                        assignment = entityState.State.TryGetNonEmptyExperimentAssignment();
                        break;
                    }
                }

                GameConfigSpecializationPatches patches = negotiationResources.PatchArchives.GetValueOrDefault(slot);

                // Assignment but no patches is not ok. No assignment but with patches is ok (assignment changed to empty during handshake).
                if (assignment != null && patches == null)
                    throw new InvalidOperationException($"In slot {slot} got experiment assignment but no patches");

                if (assignment != null)
                {
                    GameConfigSpecializationKey specializationKey = patches.CreateKeyFromAssignment(assignment);

                    gameConfigs[slot] = gameConfigImporter(configArchive, (patches, specializationKey));
                    gameConfigPatchVersions[slot] = patches.Version;
                }
                else
                {
                    gameConfigs[slot] = gameConfigImporter(configArchive, null);
                }
            }

            return new ClientSessionStartResources(sessionStartSuccess.LogicVersion, gameConfigs, gameConfigBaselineVersions, gameConfigPatchVersions);
        }
    }
}
