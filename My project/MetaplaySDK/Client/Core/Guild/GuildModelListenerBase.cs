// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

namespace Metaplay.Core.Guild
{
    /// <summary>
    /// Server-side guild event listener interface, used by Metaplay core callbacks.
    /// Game-specific callbacks should go to game-specific <c>IGuildModelServerListener</c>.
    /// </summary>
    public interface IGuildModelServerListenerCore
    {
        /// <summary>
        /// Data returned by GuildActor.CreateGuildDiscoveryInfo has changed and should
        /// be updated. This allows guild recommender and search results to be updated
        /// quickly. If this listener is not called, the discovery data is updated only
        /// eventually.
        /// </summary>
        void GuildDiscoveryInfoChanged();

        /// <summary>
        /// Called after Member was removed from the Members with the intention of
        /// kicking the player. This call delivers the kick reason to the player and
        /// informs the kicked player's session (if any) that the guild has kicked it.
        /// </summary>
        void PlayerKicked(EntityId kickedPlayerId, GuildMemberBase kickedMember, EntityId kickingPlayerId, IGuildMemberKickReason kickReasonOrNull);

        /// <summary>
        /// Called after the player data of a guild member has been updated by <see cref="GuildMemberPlayerDataBase.ApplyOnMember(GuildMemberBase)"/>.
        /// This is also called when a new member joins the guild.
        /// </summary>
        void MemberPlayerDataUpdated(EntityId memberId);

        /// <summary>
        /// Called after the member's role has changed.
        /// </summary>
        void MemberRoleChanged(EntityId memberId);

        /// <summary>
        /// Called after the member leaves or is removed from the guild.
        /// </summary>
        void MemberRemoved(EntityId memberId);
    }

    /// <summary>
    /// Empty implementation of <see cref="IGuildModelServerListenerCore"/>.
    /// </summary>
    public class EmptyGuildModelServerListenerCore : IGuildModelServerListenerCore
    {
        public static readonly EmptyGuildModelServerListenerCore Instance = new EmptyGuildModelServerListenerCore();

        public void GuildDiscoveryInfoChanged() { }
        public void PlayerKicked(EntityId kickedPlayerId, GuildMemberBase kickedMember, EntityId kickingPlayerId, IGuildMemberKickReason kickReasonOrNull) { }
        public void MemberPlayerDataUpdated(EntityId memberId) { }
        public void MemberRoleChanged(EntityId memberId) { }
        public void MemberRemoved(EntityId memberId) { }
    }

    /// <summary>
    /// Client-side guild event listener interface, used by Metaplay core callbacks.
    /// Game-specific callbacks should go to game-specific <c>IGuildModelClientListener</c>.
    /// </summary>
    public interface IGuildModelClientListenerCore
    {
        /// <summary>
        /// <inheritdoc cref="IGuildModelServerListenerCore.MemberPlayerDataUpdated(EntityId)"/>
        /// </summary>
        void MemberPlayerDataUpdated(EntityId memberId);

        /// <inheritdoc cref="IGuildModelServerListenerCore.MemberRoleChanged(EntityId)"/>
        void MemberRoleChanged(EntityId memberId);

        void GuildNameAndDescriptionUpdated();
    }

    /// <summary>
    /// Empty implementation of <see cref="IGuildModelClientListenerCore"/>.
    /// </summary>
    public class EmptyGuildModelClientListenerCore : IGuildModelClientListenerCore
    {
        public static readonly EmptyGuildModelClientListenerCore Instance = new EmptyGuildModelClientListenerCore();

        public void MemberPlayerDataUpdated(EntityId memberId) { }
        public void MemberRoleChanged(EntityId memberId) { }
        public void GuildNameAndDescriptionUpdated() { }
    }
}

#endif
