// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.GuildDiscovery;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System.Collections.Generic;

namespace Metaplay.Core.Guild.Messages.Core
{
    /// <summary>
    /// Request to create a Guild. Will be responded with <see cref="GuildCreateResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildCreateRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class GuildCreateRequest : MetaMessage
    {
        public GuildCreationRequestParamsBase CreationParams { get; private set; }

        public GuildCreateRequest() { }
        public GuildCreateRequest(GuildCreationRequestParamsBase creationParams)
        {
            CreationParams = creationParams;
        }
    }

    /// <summary>
    /// Response to GuildCreateRequest. If connection is lost soon after the Request, client
    /// might receive a Response for Request made in a previous session.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildCreateResponse, MessageDirection.ServerToClient)]
    public class GuildCreateResponse : MetaMessage
    {
        public EntitySerializedState?   GuildState      { get; private set; }
        public int                      GuildChannelId  { get; private set; }

        public GuildCreateResponse() { }
        public GuildCreateResponse(EntitySerializedState? guildState, int guildChannelId)
        {
            GuildState = guildState;
            GuildChannelId = guildChannelId;
        }
    }

    /// <summary>
    /// Request leaving from the guild. This can never observably fail and the client
    /// may speculate the effects. There is no response.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildLeaveRequest, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class GuildLeaveRequest : MetaMessage
    {
        public int ChannelId { get; private set; }

        public GuildLeaveRequest() { }
        public GuildLeaveRequest(int channelId)
        {
            ChannelId = channelId;
        }
    }

    /// <summary>
    /// Request to execute certain action on guild
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildEnqueueActionsRequest, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class GuildEnqueueActionsRequest : MetaMessage
    {
        public int                                      ChannelId   { get; private set; }
        public MetaSerialized<List<GuildActionBase>>    Actions     { get; private set; }

        public GuildEnqueueActionsRequest() { }
        public GuildEnqueueActionsRequest(int channelId, MetaSerialized<List<GuildActionBase>> actions)
        {
            ChannelId = channelId;
            Actions = actions;
        }
    }

    /// <summary>
    /// Announcement from server that guild model has moved forward in time by executing the specified
    /// actions and ticks.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildUpdate, MessageDirection.ServerToClient, hasExplicitMembers: true)]
    public class GuildTimelineUpdateMessage : MetaMessage
    {
        [MetaSerializable]
        public struct Operation
        {
            [MetaMember(1)] public GuildActionBase  Action              { get; private set; } // if null, then Tick
            [MetaMember(2)] public EntityId         InvokingPlayerId    { get; private set; } // if None, then server invoked. Otherwise, the invoking member

            public Operation(GuildActionBase actionOrNull, EntityId invokingPlayerId)
            {
                Action = actionOrNull;
                InvokingPlayerId = invokingPlayerId;
            }
        }

        [PrettyPrint(PrettyPrintFlag.SizeOnly)]
        [MetaMember(1)] public MetaSerialized<List<Operation>>  Operations      { get; private set; }
        [MetaMember(2)] public uint                             FinalChecksum   { get; private set; }
        [MetaMember(3)] public int                              StartTick       { get; private set; } // Tick of the first Operation
        [MetaMember(4)] public int                              StartOperation  { get; private set; } // Operation index of the first Operation
        [MetaMember(5)] public int                              GuildChannelId  { get; private set; }

        public GuildTimelineUpdateMessage() { }
        public GuildTimelineUpdateMessage(MetaSerialized<List<Operation>> operations, int startTick, int startOperation, uint finalChecksum, int guildChannelId)
        {
            Operations = operations;
            StartTick = startTick;
            StartOperation = startOperation;
            FinalChecksum = finalChecksum;
            GuildChannelId = guildChannelId;
        }
    }

    /// <summary>
    /// Request to join a Guild. Will be responded with <see cref="GuildJoinResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildJoinRequest, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class GuildJoinRequest : MetaMessage
    {
        [MetaSerializable]
        public enum JoinMode
        {
            Normal = 0,
            InviteCode = 1,
        }

        public JoinMode         Mode        { get; private set; }
        public EntityId         GuildId     { get; private set; }
        public int              InviteId    { get; private set; }
        public GuildInviteCode  InviteCode  { get; private set; }

        GuildJoinRequest() { }
        public GuildJoinRequest(JoinMode mode, EntityId guildId, int inviteId, GuildInviteCode inviteCode)
        {
            Mode = mode;
            GuildId = guildId;
            InviteId = inviteId;
            InviteCode = inviteCode;
        }
    }

    /// <summary>
    /// Response to GuildJoinRequest.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildJoinResponse, MessageDirection.ServerToClient)]
    public class GuildJoinResponse : MetaMessage
    {
        public EntitySerializedState?   GuildState      { get; private set; } // null if refused
        public int                      GuildChannelId  { get; private set; }

        GuildJoinResponse() { }
        public GuildJoinResponse(EntitySerializedState? guildState, int guildChannelId)
        {
            GuildState = guildState;
            GuildChannelId = guildChannelId;
        }

        public static GuildJoinResponse CreateRefusal() => new GuildJoinResponse(null, 0);
    }

    /// <summary>
    /// Notification from server to client that player's current guild been changed to another guild, or
    /// that the player is no longer in a guild.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildSwitchedMessage, MessageDirection.ServerToClient)]
    public class GuildSwitchedMessage : MetaMessage
    {
        public EntitySerializedState?   GuildState      { get; private set; }
        public int                      GuildChannelId  { get; private set; }

        GuildSwitchedMessage() { }
        public GuildSwitchedMessage(EntitySerializedState? guildState, int guildChannelId)
        {
            GuildState = guildState;
            GuildChannelId = guildChannelId;
        }
    }

    /// <summary>
    /// Request to discover some guilds. Will be responded with <see cref="GuildDiscoveryResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildDiscoveryRequest, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class GuildDiscoveryRequest : MetaMessage
    {
        public GuildDiscoveryRequest() { }
    }

    /// <summary>
    /// Response to GuildDiscoveryRequest.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildDiscoveryResponse, MessageDirection.ServerToClient)]
    public class GuildDiscoveryResponse : MetaMessage
    {
        public List<GuildDiscoveryInfoBase> GuildInfos;

        public GuildDiscoveryResponse() { }
        public GuildDiscoveryResponse(List<GuildDiscoveryInfoBase> guildInfos)
        {
            GuildInfos = guildInfos;
        }
    }

    /// <summary>
    /// Request to search guild matching criteria. Will be responded with <see cref="GuildSearchResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildSearchRequest, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class GuildSearchRequest : MetaMessage
    {
        public GuildSearchParamsBase SearchParams;

        public GuildSearchRequest() { }
        public GuildSearchRequest(GuildSearchParamsBase searchParams)
        {
            SearchParams = searchParams;
        }
    }

    /// <summary>
    /// Response to GuildSearchRequest.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildSearchResponse, MessageDirection.ServerToClient)]
    public class GuildSearchResponse : MetaMessage
    {
        public bool IsError; // True, if search failed. In this case no results are returned.
        public List<GuildDiscoveryInfoBase> GuildInfos;
        // \todo: pagination. Add NextPageToken

        public GuildSearchResponse() { }
        public GuildSearchResponse(bool isError, List<GuildDiscoveryInfoBase> guildInfos)
        {
            IsError = isError;
            GuildInfos = guildInfos;
        }
    }

    /// <summary>
    /// Request to execute a GuildTransaction. Responded with <see cref="GuildTransactionResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildTransactionRequest, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class GuildTransactionRequest : MetaMessage
    {
        public int                                  GuildChannelId  { get; private set; }
        public MetaSerialized<IGuildTransaction>    Transaction     { get; private set; }

        public GuildTransactionRequest() { }
        public GuildTransactionRequest(MetaSerialized<IGuildTransaction> transaction, int guildChannelId)
        {
            GuildChannelId = guildChannelId;
            Transaction = transaction;
        }
    }

    [MetaMessage(MessageCodesCore.GuildTransactionResponse, MessageDirection.ServerToClient)]
    public class GuildTransactionResponse : MetaMessage
    {
        public MetaSerialized<PlayerTransactionFinalizingActionBase>    PlayerAction            { get; private set; }
        public MetaSerialized<GuildActionBase>                          GuildAction             { get; private set; }
        public int                                                      PlayerActionTrackingId  { get; private set; }

        GuildTransactionResponse() { }
        public GuildTransactionResponse(MetaSerialized<PlayerTransactionFinalizingActionBase> playerAction, MetaSerialized<GuildActionBase> guildAction, int playerActionTrackingId)
        {
            PlayerAction = playerAction;
            GuildAction = guildAction;
            PlayerActionTrackingId = playerActionTrackingId;
        }
    }

    /// <summary>
    /// Request to begin viewing a Guild. Will be responded with <see cref="GuildViewResponse"/>.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildBeginViewRequest, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class GuildBeginViewRequest : MetaMessage
    {
        public EntityId GuildId { get; private set; }
        public int      QueryId { get; private set; }

        public GuildBeginViewRequest() { }
        public GuildBeginViewRequest(EntityId guildId, int queryId)
        {
            GuildId = guildId;
            QueryId = queryId;
        }
    }

    /// <summary>
    /// Response to GuildViewRequest.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildViewResponse, MessageDirection.ServerToClient)]
    public class GuildViewResponse : MetaMessage
    {
        [MetaSerializable]
        public enum StatusCode
        {
            Success = 0,
            Refused = 1,
        }

        public StatusCode               Status              { get; private set; }
        public EntitySerializedState    GuildState          { get; private set; }
        public int                      GuildChannelId      { get; private set; }
        public int                      QueryId             { get; private set; }

        GuildViewResponse() { }
        public GuildViewResponse(StatusCode status, EntitySerializedState guildState, int guildChannelId, int queryId)
        {
            Status = status;
            GuildState = guildState;
            GuildChannelId = guildChannelId;
            QueryId = queryId;
        }

        public static GuildViewResponse CreateRefusal(int queryId) => new GuildViewResponse(status: StatusCode.Refused, default, 0, queryId);
        public static GuildViewResponse CreateSuccess(EntitySerializedState guildState, int guildChannelId, int queryId) => new GuildViewResponse(status: StatusCode.Success, guildState, guildChannelId, queryId);
    }

    /// <summary>
    /// Request to end viewing a Guild.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildEndViewRequest, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class GuildEndViewRequest : MetaMessage
    {
        public int GuildChannelId { get; private set; }

        public GuildEndViewRequest() { }
        public GuildEndViewRequest(int guildChannelId)
        {
            GuildChannelId = guildChannelId;
        }
    }

    /// <summary>
    /// Announcement from server that a guild view has been closed.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildViewEnded, MessageDirection.ServerToClient)]
    public class GuildViewEnded : MetaMessage
    {
        public int GuildChannelId { get; private set; }

        public GuildViewEnded() { }
        public GuildViewEnded(int guildChannelId)
        {
            GuildChannelId = guildChannelId;
        }
    }

    /// <summary>
    /// Request to create a new invitation.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildCreateInvitationRequest, MessageDirection.ClientToServer), MessageRoutingRuleCurrentGuild]
    public class GuildCreateInvitationRequest : MetaMessage
    {
        public int              QueryId         { get; private set; }
        public GuildInviteType  Type            { get; private set; }
        public MetaDuration?    ExpiresAfter    { get; private set; } // If null, the invite has no expiration time.
        public int              NumMaxUsages    { get; private set; } // If set to 0, there is no limit.

        GuildCreateInvitationRequest() { }
        public GuildCreateInvitationRequest(int queryId, GuildInviteType type, MetaDuration? expiresAfter, int numMaxUsages)
        {
            QueryId = queryId;
            Type = type;
            ExpiresAfter = expiresAfter;
            NumMaxUsages = numMaxUsages;
        }
    }

    /// <summary>
    /// Response to <see cref="GuildCreateInvitationRequest"/>
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildCreateInvitationResponse, MessageDirection.ServerToClient)]
    public class GuildCreateInvitationResponse : MetaMessage
    {
        [MetaSerializable]
        public enum StatusCode
        {
            Success = 0,

            /// <summary>
            /// Kicked during operation, or otherwise stale request
            /// </summary>
            NotAMember = 1,

            /// <summary>
            /// GuildModel.HasPermissionToInvite returned false
            /// </summary>
            NotAllowed = 2,

            /// <summary>
            /// Player has too many invites, must revoke some of the first or wait for them to expire
            /// </summary>
            TooManyInvites = 3,

            /// <summary>
            /// Too many requests too frequently. Try again later.
            /// </summary>
            RateLimited = 4,
        }

        public int              QueryId             { get; private set; }
        public StatusCode       Status              { get; private set; }
        public int              InviteId            { get; private set; } // Id of the created invite

        GuildCreateInvitationResponse() { }
        GuildCreateInvitationResponse(int queryId, StatusCode status, int inviteId)
        {
            QueryId = queryId;
            Status = status;
            InviteId = inviteId;
        }

        public static GuildCreateInvitationResponse CreateRefusal(int queryId, StatusCode error) => new GuildCreateInvitationResponse(queryId, error, default);
        public static GuildCreateInvitationResponse CreateSuccess(int queryId, int inviteId) => new GuildCreateInvitationResponse(queryId, StatusCode.Success, inviteId);
    }

    /// <summary>
    /// Request to revoke an existing invitation.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildRevokeInvitationRequest, MessageDirection.ClientToServer), MessageRoutingRuleCurrentGuild]
    public class GuildRevokeInvitationRequest : MetaMessage
    {
        public int InviteId { get; private set; }

        GuildRevokeInvitationRequest() { }
        public GuildRevokeInvitationRequest(int inviteId)
        {
            InviteId = inviteId;
        }
    }

    /// <summary>
    /// Request to inspect whether an invitation code is still valid, and the invitation contents.
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildInspectInvitationRequest, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class GuildInspectInvitationRequest : MetaMessage
    {
        public int              QueryId     { get; private set; }
        public GuildInviteCode  InviteCode  { get; private set; }

        GuildInspectInvitationRequest() { }
        public GuildInspectInvitationRequest(int queryId, GuildInviteCode inviteCode)
        {
            QueryId = queryId;
            InviteCode = inviteCode;
        }
    }

    /// <summary>
    /// Response to <see cref="GuildInspectInvitationRequest"/>
    /// </summary>
    [MetaMessage(MessageCodesCore.GuildInspectInvitationResponse, MessageDirection.ServerToClient)]
    public class GuildInspectInvitationResponse : MetaMessage
    {
        [MetaSerializable]
        public enum StatusCode
        {
            Success = 0,

            /// <summary>
            /// Invite is not valid or is no longer valid
            /// </summary>
            InvalidOrExpired = 1,

            /// <summary>
            /// Too many requests too frequently. Try again later.
            /// </summary>
            RateLimited = 2,
        }

        public int                                      QueryId                 { get; private set; }
        public StatusCode                               Status                  { get; private set; }
        public int                                      InviteId                { get; private set; }
        public MetaSerialized<GuildDiscoveryInfoBase>   GuildDiscoveryInfo      { get; private set; }
        public MetaSerialized<GuildInviterAvatarBase>   InviterAvatar           { get; private set; }

        GuildInspectInvitationResponse() { }
        GuildInspectInvitationResponse(int queryId, StatusCode status, int inviteId, MetaSerialized<GuildDiscoveryInfoBase> guildDiscoveryInfo, MetaSerialized<GuildInviterAvatarBase> inviterAvatar)
        {
            QueryId = queryId;
            Status = status;
            InviteId = inviteId;
            GuildDiscoveryInfo = guildDiscoveryInfo;
            InviterAvatar = inviterAvatar;
        }

        public static GuildInspectInvitationResponse CreateInvalidOrExpired(int queryId) => new GuildInspectInvitationResponse(queryId, StatusCode.InvalidOrExpired, 0, default, default);
        public static GuildInspectInvitationResponse CreateSuccess(int queryId, int inviteId, MetaSerialized<GuildDiscoveryInfoBase> guildDiscoveryInfo, MetaSerialized<GuildInviterAvatarBase> inviterAvatar)
            => new GuildInspectInvitationResponse(queryId, StatusCode.Success, inviteId, guildDiscoveryInfo, inviterAvatar);
        public static GuildInspectInvitationResponse CreateRateLimited(int queryId) => new GuildInspectInvitationResponse(queryId, StatusCode.RateLimited, 0, default, default);
    }
}

#endif
