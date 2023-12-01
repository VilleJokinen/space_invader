// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Guild;
using Metaplay.Core.Guild.Messages.Core;
using System;

namespace Metaplay.Core.Client
{
    /// <summary>
    /// The base interface for Guild entity client context. See also convenience helper <see cref="IGuildClientContext{TGuildModel}"/>.
    /// </summary>
    public interface IGuildClientContext : IEntityClientContext
    {
        /// <summary>
        /// The player ID if the current player in the guild.
        /// </summary>
        EntityId PlayerId { get; }
        IGuildModelBase CommittedModel { get; }

        void EnqueueAction(GuildActionBase action);
        bool HandleGuildTimelineUpdateMessage(GuildTimelineUpdateMessage msg);
        void HandleGuildTransactionResponse(GuildActionBase action);
        void SetClientListeners(Action<IGuildModelBase> applyFn);
    }

    /// <summary>
    /// The base interface for Guild entity client context. Typed convenience wrapper of <see cref="IGuildClientContext"/>.
    /// </summary>
    public interface IGuildClientContext<TGuildModel> : IGuildClientContext
        where TGuildModel : IGuildModelBase
    {
        new TGuildModel CommittedModel { get; }
    }
}

#endif
