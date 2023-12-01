// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Model;
using System.Runtime.Serialization;

namespace Metaplay.Core.Guild
{
    [MetaSerializable]
    [ModelActionExecuteFlags(ModelActionExecuteFlags.LeaderSynchronized)]
    public abstract class GuildActionBase : ModelAction<IGuildModelBase>
    {
        [IgnoreDataMember] public EntityId InvokingPlayerId;
    }

    /// <summary>
    /// Base class for guild action types that client is allowed to enqueue.
    /// </summary>
    [MetaSerializable]
    [ModelActionExecuteFlags(ModelActionExecuteFlags.FollowerSynchronized)]
    public abstract class GuildClientActionBase : GuildActionBase
    {
    }

    /// <summary>
    /// Base class for all <see cref="ModelAction"/>s affecting <see cref="GuildModel"/>.
    ///
    /// The Execute() method receives the current <see cref="GuildModel"/> as an argument.
    /// Logging and client/server event listeners can be accessed from it.
    /// </summary>
    [MetaSerializable]
    public abstract class GuildActionCore<TModel> : GuildActionBase where TModel : IGuildModelBase
    {
        public GuildActionCore() { }

        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            return Execute((TModel)guild, commit);
        }

        /// <summary>
        /// See <see cref="GuildActionCore{TModel}"/>
        /// </summary>
        public abstract MetaActionResult Execute(TModel guild, bool commit);
    }

    /// <summary>
    /// Like to <see cref="GuildActionCore{TModel}"/>, but is allowed to originate from the client.
    /// </summary>
    [MetaSerializable]
    public abstract class GuildClientActionCore<TModel> : GuildClientActionBase where TModel : IGuildModelBase
    {
        public override MetaActionResult InvokeExecute(IGuildModelBase guild, bool commit)
        {
            return Execute((TModel)guild, commit);
        }

        public abstract MetaActionResult Execute(TModel guild, bool commit);
    }
}

#endif
