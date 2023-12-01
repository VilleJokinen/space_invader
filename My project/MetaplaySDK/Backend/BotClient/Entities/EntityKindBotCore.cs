// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;

namespace Metaplay.BotClient
{
    /// <summary>
    /// Register SDK core BotClient EntityKinds. These are only used on the BotClient.
    /// </summary>
    [EntityKindRegistry(60, 64)]
    public static class EntityKindBotCore
    {
        public static readonly EntityKind   BotClient       = EntityKind.FromValue(62);
        public static readonly EntityKind   BotCoordinator  = EntityKind.FromValue(63);
    }
}
