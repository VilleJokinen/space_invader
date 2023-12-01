// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using System;

namespace Metaplay.Core.Guild
{
    /// <summary>
    /// Represents a read-only view to a "foreign" guild and all associated data needed for maintaining such
    /// a view. Foreign guild is a guild that the user is not a member of. For the guild the user is a member
    /// of, see <see cref="GuildClientContext"/>.
    ///
    /// <para>
    /// Unlike the <see cref="GuildClientContext"/>, which has lifetime tied to player's guild membership and
    /// is automatically managed, ForeignGuildContext has more dynamic lifetime and lifecycle. Hence care must
    /// be taken to manually close ForeignGuildContext when it is no longer needed, either by Disposing it or
    /// using GuildClient.EndViewGuild.
    /// </para>
    /// <para>
    /// See also convenience helper <see cref="ForeignGuildContext{TGuildModel}"/>
    /// </para>
    /// </summary>
    public class ForeignGuildContext
    {
        /// <summary>
        /// The model of the foreign guild.
        /// </summary>
        public IGuildModelBase Model { get; }

        /// <summary>
        /// The Id of the update channel used to communicate this guild's changes.
        /// </summary>
        public int ChannelId { get; }

        GuildClient _client;

        /// <summary>
        /// Invoked when this view has been closed by the server. This data
        /// will no longer be updated.
        /// </summary>
        public event Action ViewClosedByServer;

        internal ForeignGuildContext(GuildClient client, IGuildModelBase model, int channelId)
        {
            Model = model;
            ChannelId = channelId;
            _client = client;
        }

        /// <summary>
        /// Used to signal that the user is no longer interested in the guild context.
        /// This closes any external resources related to the Context. On Client, this
        /// closes the update channel established between the client and the server.
        /// </summary>
        public void Dispose()
        {
            _client.OnForeignGuildContextDisposed(this);
        }

        /// <summary>
        /// Called by SDK when the Guild View is closed due to server. The Context dead and is no longer
        /// kept up to date.
        /// </summary>
        internal void OnViewClosedByServer()
        {
            ViewClosedByServer?.Invoke();
        }
    }

    /// <summary>
    /// Represents a read-only view to a "foreign" guild. Convenience wrapper of <see cref="ForeignGuildContext"/>.
    /// </summary>
    public class ForeignGuildContext<TGuildModel> : ForeignGuildContext
        where TGuildModel : IGuildModelBase
    {
        /// <inheritdoc cref="ForeignGuildContext.Model"/>
        public new TGuildModel Model => (TGuildModel)base.Model;

        internal ForeignGuildContext(GuildClient client, TGuildModel model, int channelId)
            : base(client, model, channelId)
        {
        }
    }
}

#endif
