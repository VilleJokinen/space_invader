// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

namespace Metaplay.Core
{
    /// <summary>
    /// Register shared code SDK core EntityKinds. Game-specific EntityKinds should be registered
    /// in either <c>EntityKindGame</c> (for EntityKinds shared between client and server),
    /// or <c>EntityKindCloudGame</c> (for server-only EntityKinds).
    /// </summary>
    [EntityKindRegistry(1, 10)]
    public static class EntityKindCore
    {
        public static readonly EntityKind Player    = EntityKind.FromValue(1);
        public static readonly EntityKind Session   = EntityKind.FromValue(2);
        public static readonly EntityKind Guild     = EntityKind.FromValue(3);
        public static readonly EntityKind Division  = EntityKind.FromValue(4);
    }
}
