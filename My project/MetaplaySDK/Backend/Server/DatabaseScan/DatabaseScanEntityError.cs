// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;

namespace Metaplay.Server.DatabaseScan.User
{
    [MetaSerializable]
    public struct DatabaseScanEntityError
    {
        [MetaMember(1)] public EntityId EntityId;
        [MetaMember(2)] public MetaTime Timestamp;
        [MetaMember(3)] public string Description;

        public DatabaseScanEntityError(EntityId entityId, MetaTime timestamp, string description)
        {
            EntityId = entityId;
            Timestamp = timestamp;
            Description = description;
        }
    }
}
