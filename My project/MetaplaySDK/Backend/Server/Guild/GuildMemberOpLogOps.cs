// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Guild;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;

// These ops are private, so we use Metaplay.Server namespace (private)

namespace Metaplay.Server.Guild
{
    /// <summary>
    /// <inheritdoc cref="GuildMemberGuildOpLogEntry"/>
    /// This operation executes GuildAction on Guild.
    /// </summary>
    [MetaSerializableDerived(1)]
    public sealed class GuildOpRunGuildAction : GuildMemberGuildOpLogEntry
    {
        [MetaMember(1)] public MetaSerialized<GuildActionBase> Action;

        public GuildOpRunGuildAction() { }
        public GuildOpRunGuildAction(MetaSerialized<GuildActionBase> action)
        {
            Action = action;
        }
    }

    /// <summary>
    /// <inheritdoc cref="GuildMemberPlayerOpLogEntry"/>
    /// This operation executes completed transaction actions on Player.
    /// </summary>
    [MetaSerializableDerived(1)]
    public sealed class PlayerGuildOpTransactionCommitted : GuildMemberPlayerOpLogEntry
    {
        [MetaMember(1)] public MetaSerialized<PlayerActionBase>                         InitiatingAction;
        [MetaMember(2)] public MetaSerialized<PlayerTransactionFinalizingActionBase>    FinalizingAction;

        public PlayerGuildOpTransactionCommitted() { }
        public PlayerGuildOpTransactionCommitted(MetaSerialized<PlayerActionBase> initiatingAction, MetaSerialized<PlayerTransactionFinalizingActionBase> finalizingAction)
        {
            InitiatingAction = initiatingAction;
            FinalizingAction = finalizingAction;
        }
    }
}

#endif
