// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;

namespace Metaplay.Server.GameConfig
{
    /// <summary>
    /// Version of an GameConfig, in a format suitable for a client-server shared Game Entity.
    /// </summary>
    [MetaSerializable]
    public struct ServerEntityGameConfigVersion
    {
        [MetaMember(1)] public MetaGuid BaselineStaticGameConfigId;
        [MetaMember(2)] public MetaGuid BaselineDynamicGameConfigId;
        [MetaMember(3)] public GameConfigSpecializationKey SpecializationKeyForServer;
        [MetaMember(4)] public GameConfigSpecializationKey SpecializationKeyForClient;
        [MetaMember(5)] public ContentHash SpecializationPatchesVersion;

        public ServerEntityGameConfigVersion(MetaGuid baselineStaticGameConfigId, MetaGuid baselineDynamicGameConfigId, GameConfigSpecializationKey specializationKeyForServer, GameConfigSpecializationKey specializationKeyForClient, ContentHash specializationPatchesVersion)
        {
            BaselineStaticGameConfigId = baselineStaticGameConfigId;
            BaselineDynamicGameConfigId = baselineDynamicGameConfigId;
            SpecializationKeyForServer = specializationKeyForServer;
            SpecializationKeyForClient = specializationKeyForClient;
            SpecializationPatchesVersion = specializationPatchesVersion;
        }
    }
}
