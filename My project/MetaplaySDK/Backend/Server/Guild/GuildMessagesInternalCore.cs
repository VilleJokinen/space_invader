// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Guild;
using Metaplay.Core.Guild.Messages.Core;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using Metaplay.Server.GuildDiscovery;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Metaplay.Server.Guild.InternalMessages
{
    //
    // This file contains Server-internal messages that are part of Metaplay core. To add
    // game-specific server-internal messages, see GuildMessagesInternal.cs
    //

    [MetaSerializableDerived(MessageCodesCore.InternalOwnedGuildAssociationRef)]
    public class InternalOwnedGuildAssociationRef : AssociatedEntityRefBase
    {
        public int                                                  MemberInstanceId                { get; private set; }
        public int                                                  LastPlayerOpEpoch               { get; private set; }
        public int                                                  CommittedPlayerOpEpoch          { get; private set; }
        public int                                                  CommittedGuildOpEpoch           { get; private set; }
        public OrderedDictionary<int, GuildMemberGuildOpLogEntry>   CommittedPendingGuildOps        { get; private set; }
        public GuildMemberPlayerDataBase                            PlayerLoginData                 { get; private set; }

        [IgnoreDataMember] public EntityId PlayerId => SourceEntity;
        [IgnoreDataMember] public EntityId GuildId => AssociatedEntity;
        public override ClientSlot GetClientSlot() => ClientSlotCore.Guild;

        InternalOwnedGuildAssociationRef() { }
        public InternalOwnedGuildAssociationRef(EntityId playerId, EntityId guildId, int memberInstanceId, int lastPlayerOpEpoch, int committedPlayerOpEpoch, int committedGuildOpEpoch, OrderedDictionary<int, GuildMemberGuildOpLogEntry> committedPendingGuildOps, GuildMemberPlayerDataBase playerLoginData) : base(playerId, guildId)
        {
            MemberInstanceId = memberInstanceId;
            LastPlayerOpEpoch = lastPlayerOpEpoch;
            CommittedPlayerOpEpoch = committedPlayerOpEpoch;
            CommittedGuildOpEpoch = committedGuildOpEpoch;
            CommittedPendingGuildOps = committedPendingGuildOps;
            PlayerLoginData = playerLoginData;
        }
    }

    [MetaSerializableDerived(MessageCodesCore.InternalGuildMemberSubscribeRefused)]
    public class InternalGuildMemberSubscribeRefused : InternalEntitySubscribeRefusedBase
    {
        [MetaSerializable]
        public enum ResultCode
        {
            PendingPlayerOps = 2,
            Kicked = 3,
            GuildOpEpochSkip = 4,
        }
        [MetaMember(1)] public ResultCode                                           Result              { get; private set; }
        [MetaMember(2)] public OrderedDictionary<int, GuildMemberPlayerOpLogEntry>  PendingPlayerOps    { get; private set; }
        [MetaMember(3)] public int                                                  GuildOpSkipTo       { get; private set; }

        public override string Message => $"Session subscribe refused with {Result}";

        InternalGuildMemberSubscribeRefused() { }
        InternalGuildMemberSubscribeRefused(ResultCode result)
        {
            Result = result;
        }

        public static InternalGuildMemberSubscribeRefused CreatePendingPlayerOps(OrderedDictionary<int, GuildMemberPlayerOpLogEntry> pendingPlayerOps)
        {
            InternalGuildMemberSubscribeRefused refused = new InternalGuildMemberSubscribeRefused(ResultCode.PendingPlayerOps);
            refused.PendingPlayerOps = pendingPlayerOps;
            return refused;
        }
        public static InternalGuildMemberSubscribeRefused CreateKicked() => new InternalGuildMemberSubscribeRefused(ResultCode.Kicked);
        public static InternalGuildMemberSubscribeRefused CreateGuildOpEpochSkip(int seenEpoch)
        {
            InternalGuildMemberSubscribeRefused refused = new InternalGuildMemberSubscribeRefused(ResultCode.GuildOpEpochSkip);
            refused.GuildOpSkipTo = seenEpoch;
            return refused;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildLeaveRequest, MessageDirection.ServerInternal)]
    public class InternalGuildLeaveRequest : MetaMessage
    {
        public EntityId                                             PlayerId                { get; private set; }
        public int                                                  MemberInstanceId        { get; private set; }
        public int                                                  CommittedPlayerOpEpoch  { get; private set; }
        public OrderedDictionary<int, GuildMemberGuildOpLogEntry>   PendingGuildOps         { get; private set; }
        public bool                                                 ForceLeave              { get; private set; }

        InternalGuildLeaveRequest() { }
        public InternalGuildLeaveRequest(EntityId playerId, int memberInstanceId, int committedPlayerOpEpoch, OrderedDictionary<int, GuildMemberGuildOpLogEntry> pendingGuildOps, bool forceLeave)
        {
            PlayerId = playerId;
            MemberInstanceId = memberInstanceId;
            CommittedPlayerOpEpoch = committedPlayerOpEpoch;
            PendingGuildOps = pendingGuildOps;
            ForceLeave = forceLeave;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildLeaveResponse, MessageDirection.ServerInternal)]
    public class InternalGuildLeaveResponse : MetaMessage
    {
        [MetaSerializable]
        public enum ResultCode
        {
            Ok = 0,
            PendingPlayerOps = 1,
            Kicked = 2,
        }

        public ResultCode                                           Result                  { get; private set; }
        public OrderedDictionary<int, GuildMemberPlayerOpLogEntry>  PendingPlayerOps        { get; private set; }

        public InternalGuildLeaveResponse() { }
        InternalGuildLeaveResponse(ResultCode result, OrderedDictionary<int, GuildMemberPlayerOpLogEntry> pendingPlayerOps)
        {
            Result = result;
            PendingPlayerOps = pendingPlayerOps;
        }

        public static InternalGuildLeaveResponse CreateOk() => new InternalGuildLeaveResponse(ResultCode.Ok, null);
        public static InternalGuildLeaveResponse CreatePendingPlayerOps(OrderedDictionary<int, GuildMemberPlayerOpLogEntry> pendingPlayerOps) => new InternalGuildLeaveResponse(ResultCode.PendingPlayerOps, pendingPlayerOps);
        public static InternalGuildLeaveResponse CreateKicked() => new InternalGuildLeaveResponse(ResultCode.Kicked, null);
    }

    [MetaMessage(MessageCodesCore.InternalGuildPlayerDashboardInfoRequest, MessageDirection.ServerInternal)]
    public class InternalGuildPlayerDashboardInfoRequest : MetaMessage
    {
        public EntityId                     PlayerId                { get; private set; }

        public InternalGuildPlayerDashboardInfoRequest() { }
        public InternalGuildPlayerDashboardInfoRequest(EntityId playerId)
        {
            PlayerId = playerId;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildPlayerDashboardInfoResponse, MessageDirection.ServerInternal)]
    public class InternalGuildPlayerDashboardInfoResponse : MetaMessage
    {
        public bool                         IsMember                { get; private set; }
        public string                       DisplayName             { get; private set; }
        public GuildMemberRole              Role                    { get; private set; }

        public InternalGuildPlayerDashboardInfoResponse() { }
        public InternalGuildPlayerDashboardInfoResponse(bool isMember, string displayName, GuildMemberRole role)
        {
            IsMember = isMember;
            DisplayName = displayName;
            Role = role;
        }

        public static InternalGuildPlayerDashboardInfoResponse CreateForRefusal() => new InternalGuildPlayerDashboardInfoResponse(false, null, default);
        public static InternalGuildPlayerDashboardInfoResponse CreateForSuccess(string displayName, GuildMemberRole role) => new InternalGuildPlayerDashboardInfoResponse(true, displayName, role);
    }

    [MetaMessage(MessageCodesCore.InternalGuildDiscoveryGuildDataRequest, MessageDirection.ServerInternal)]
    public class InternalGuildDiscoveryGuildDataRequest : MetaMessage
    {
        public static readonly InternalGuildDiscoveryGuildDataRequest Instance = new InternalGuildDiscoveryGuildDataRequest();
        public InternalGuildDiscoveryGuildDataRequest() { }
    }

    [MetaMessage(MessageCodesCore.InternalGuildDiscoveryGuildDataResponse, MessageDirection.ServerInternal)]
    public class InternalGuildDiscoveryGuildDataResponse : MetaMessage
    {
        public GuildDiscoveryInfoBase           PublicDiscoveryInfo     { get; private set; }
        public GuildDiscoveryServerOnlyInfoBase ServerOnlyDiscoveryInfo { get; private set; }
        public bool                             IsErrorTemporary        { get; private set; }

        public bool IsSuccess() => PublicDiscoveryInfo != null;

        public InternalGuildDiscoveryGuildDataResponse() { }
        public static InternalGuildDiscoveryGuildDataResponse CreateForPermanentRefusal() => new InternalGuildDiscoveryGuildDataResponse() { IsErrorTemporary = false };
        public static InternalGuildDiscoveryGuildDataResponse CreateForTemporaryRefusal() => new InternalGuildDiscoveryGuildDataResponse() { IsErrorTemporary = true };
        public static InternalGuildDiscoveryGuildDataResponse CreateForSuccess(GuildDiscoveryInfoBase publicDiscoveryInfo, GuildDiscoveryServerOnlyInfoBase serverOnlyDiscoveryInfo)
        {
            return new InternalGuildDiscoveryGuildDataResponse() { PublicDiscoveryInfo = publicDiscoveryInfo, ServerOnlyDiscoveryInfo = serverOnlyDiscoveryInfo };
        }
    }

    /// <summary>
    /// Guild viewer subscribes to a Guild.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalGuildViewerSubscribeRequest, MessageDirection.ServerInternal)]
    public class InternalGuildViewerSubscribeRequest : MetaMessage
    {
        static public readonly InternalGuildViewerSubscribeRequest Instance = new InternalGuildViewerSubscribeRequest();
        public InternalGuildViewerSubscribeRequest() { }
    }

    [MetaMessage(MessageCodesCore.InternalGuildViewerSubscribeResponse, MessageDirection.ServerInternal)]
    public class InternalGuildViewerSubscribeResponse : MetaMessage
    {
        public EntitySerializedState? GuildState  { get; private set; }

        InternalGuildViewerSubscribeResponse() { }
        public InternalGuildViewerSubscribeResponse(EntitySerializedState? guildState)
        {
            GuildState = guildState;
        }

        public static InternalGuildViewerSubscribeResponse CreateForRefusal() => new InternalGuildViewerSubscribeResponse(null);
        public bool IsRefusal() => GuildState == null;
    }

    /// <summary>
    /// Player updates it's member data in a guild
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalGuildMemberPlayerDataUpdate, MessageDirection.ServerInternal)]
    public class InternalGuildMemberPlayerDataUpdate : MetaMessage
    {
        public EntityId                     PlayerId            { get; private set; }
        public int                          MemberInstanceId    { get; private set; }
        public GuildMemberPlayerDataBase    PlayerData          { get; private set; }

        public InternalGuildMemberPlayerDataUpdate() { }
        public InternalGuildMemberPlayerDataUpdate(EntityId playerId, int memberInstanceId, GuildMemberPlayerDataBase playerData)
        {
            PlayerId = playerId;
            MemberInstanceId = memberInstanceId;
            PlayerData = playerData;
        }
    }

    public static class InternalGuildTransactionPlayerSync
    {
        [MetaMessage(MessageCodesCore.InternalGuildTransactionPlayerSyncBegin, MessageDirection.ServerInternal)]
        public class Begin : MetaMessage
        {
            public MetaSerialized<IGuildTransaction>    Transaction         { get; private set; }
            public bool                                 ForcePlayerCancel   { get; private set; }
            public int                                  GuildIncarnation    { get; private set; }
            public int                                  PubsubPiggingId     { get; private set; }

            public Begin() { }
            public Begin(MetaSerialized<IGuildTransaction> transaction, bool forcePlayerCancel, int guildIncarnation, int pubsubPiggingId)
            {
                Transaction = transaction;
                ForcePlayerCancel = forcePlayerCancel;
                GuildIncarnation = guildIncarnation;
                PubsubPiggingId = pubsubPiggingId;
            }
        }
        [MetaMessage(MessageCodesCore.InternalGuildTransactionPlayerSyncPlanned, MessageDirection.ServerInternal)]
        public class Planned : MetaMessage
        {
            [MetaSerializable]
            public enum ResultCode
            {
                Ok = 0,
                Cancel = 1,
                InternalError = 2,
            }

            public ResultCode                                               Result                      { get; private set; }
            public ITransactionPlan                                         PlayerPlan                  { get; private set; }
            public int                                                      MemberInstanceId            { get; private set; }
            public int                                                      LastPlayerOpEpoch           { get; private set; }
            public MetaSerialized<PlayerTransactionFinalizingActionBase>    EarlyCancelAction           { get; private set; }
            public int                                                      CancelTrackingId            { get; private set; }
            public string                                                   ErrorString                 { get; private set; }

            public Planned()
            {
            }
            Planned(ResultCode result, ITransactionPlan playerPlan, int memberInstanceId, int lastPlayerOpEpoch, MetaSerialized<PlayerTransactionFinalizingActionBase> earlyCancelAction, int cancelTrackingId, string errorString)
            {
                Result = result;
                PlayerPlan = playerPlan;
                MemberInstanceId = memberInstanceId;
                LastPlayerOpEpoch = lastPlayerOpEpoch;
                EarlyCancelAction = earlyCancelAction;
                CancelTrackingId = cancelTrackingId;
                ErrorString = errorString;
            }

            public static Planned CreateOk(ITransactionPlan playerPlan, int memberInstanceId, int lastPlayerOpEpoch) => new Planned(ResultCode.Ok, playerPlan, memberInstanceId, lastPlayerOpEpoch, default, 0, null);
            public static Planned CreateCancel(MetaSerialized<PlayerTransactionFinalizingActionBase> earlyCancelAction, int cancelTrackingId) => new Planned(ResultCode.Cancel, null, 0, 0, earlyCancelAction, cancelTrackingId, null);
            public static Planned CreateInternalError(string errorString) => new Planned(ResultCode.InternalError, null, 0, 0, default, 0, errorString);
        }
        [MetaMessage(MessageCodesCore.InternalGuildTransactionPlayerSyncCommit, MessageDirection.ServerInternal)]
        public class Commit : MetaMessage
        {
            public bool                                                 IsCancel                    { get; private set; }
            public ITransactionPlan                                     GuildPlan                   { get; private set; }
            public ITransactionPlan                                     ServerPlan                  { get; private set; }
            public OrderedDictionary<int, GuildMemberPlayerOpLogEntry>  PreceedingPlayerOps         { get; private set; }
            public int                                                  ExpectedPlayerOpEpoch       { get; private set; }

            public Commit() {}
            Commit(bool isCancel, ITransactionPlan guildPlan, ITransactionPlan serverPlan, OrderedDictionary<int, GuildMemberPlayerOpLogEntry> preceedingPlayerOps, int expectedPlayerOpEpoch)
            {
                IsCancel = isCancel;
                GuildPlan = guildPlan;
                ServerPlan = serverPlan;
                PreceedingPlayerOps = preceedingPlayerOps;
                ExpectedPlayerOpEpoch = expectedPlayerOpEpoch;
            }

            public static Commit CreateOk(ITransactionPlan guildPlan, ITransactionPlan serverPlan, OrderedDictionary<int, GuildMemberPlayerOpLogEntry> preceedingPlayerOps, int expectedPlayerOpEpoch) => new Commit(false, guildPlan, serverPlan, preceedingPlayerOps, expectedPlayerOpEpoch);
            public static Commit CreateCancel(OrderedDictionary<int, GuildMemberPlayerOpLogEntry> preceedingPlayerOps) => new Commit(true, null, null, preceedingPlayerOps, 0);
        }
        [MetaMessage(MessageCodesCore.InternalGuildTransactionPlayerSyncCommitted, MessageDirection.ServerInternal)]
        public class Committed : MetaMessage
        {
            public MetaSerialized<PlayerTransactionFinalizingActionBase>    Action              { get; private set; }
            public int                                                      ActionTrackingId    { get; private set; }

            Committed() { }
            public Committed(MetaSerialized<PlayerTransactionFinalizingActionBase> action, int actionTrackingId)
            {
                Action = action;
                ActionTrackingId = actionTrackingId;
            }
        }
    }

    public static class InternalGuildTransactionGuildSync
    {
        [MetaMessage(MessageCodesCore.InternalGuildTransactionGuildSyncBegin, MessageDirection.ServerInternal)]
        public class Begin : MetaMessage
        {
            public EntityId                             PlayerId            { get; private set; }
            public int                                  MemberInstanceId    { get; private set; }
            public int                                  LastPlayerOpEpoch   { get; private set; }
            public MetaSerialized<IGuildTransaction>    Transaction         { get; private set; }
            public ITransactionPlan                     PlayerPlan          { get; private set; }
            public ITransactionPlan                     ServerPlan          { get; private set; }
            public int                                  PubsubPiggingId     { get; private set; }

            public Begin() { }
            public Begin(EntityId playerId, int memberInstanceId, int lastPlayerOpEpoch, MetaSerialized<IGuildTransaction> transaction, ITransactionPlan playerPlan, ITransactionPlan serverPlan, int pubsubPiggingId)
            {
                PlayerId = playerId;
                MemberInstanceId = memberInstanceId;
                LastPlayerOpEpoch = lastPlayerOpEpoch;
                Transaction = transaction;
                PlayerPlan = playerPlan;
                ServerPlan = serverPlan;
                PubsubPiggingId = pubsubPiggingId;
            }
        }

        [MetaMessage(MessageCodesCore.InternalGuildTransactionGuildSyncPlannedAndCommitted, MessageDirection.ServerInternal)]
        public class PlannedAndCommitted : MetaMessage
        {
            [MetaSerializable]
            public enum ResultCode
            {
                Ok = 0,
                Cancel = 1,
                InternalError = 2,
            }

            public ResultCode                                           Result                      { get; private set; }
            public ITransactionPlan                                     GuildPlan                   { get; private set; }
            public MetaSerialized<GuildActionBase>                      GuildFinalizingAction       { get; private set; }
            public OrderedDictionary<int, GuildMemberPlayerOpLogEntry>  PreceedingPlayerOps         { get; private set; }
            public int                                                  ExpectedPlayerOpEpoch       { get; private set; }
            public string                                               ErrorString                 { get; private set; }

            public PlannedAndCommitted()
            {
            }
            PlannedAndCommitted(ResultCode result, ITransactionPlan guildPlan, MetaSerialized<GuildActionBase> guildFinalizingAction, OrderedDictionary<int, GuildMemberPlayerOpLogEntry> preceedingPlayerOps, int expectedPlayerOpEpoch, string errorString)
            {
                Result = result;
                GuildPlan = guildPlan;
                GuildFinalizingAction = guildFinalizingAction;
                PreceedingPlayerOps = preceedingPlayerOps;
                ExpectedPlayerOpEpoch = expectedPlayerOpEpoch;
                ErrorString = errorString;
            }

            public static PlannedAndCommitted CreateOk(ITransactionPlan guildPlan, MetaSerialized<GuildActionBase> guildFinalizingAction, OrderedDictionary<int, GuildMemberPlayerOpLogEntry> preceedingPlayerOps, int expectedPlayerOpEpoch) => new PlannedAndCommitted(ResultCode.Ok, guildPlan, guildFinalizingAction, preceedingPlayerOps, expectedPlayerOpEpoch, null);
            public static PlannedAndCommitted CreateCancel(OrderedDictionary<int, GuildMemberPlayerOpLogEntry> preceedingPlayerOps) => new PlannedAndCommitted(ResultCode.Cancel, null, default, preceedingPlayerOps, 0, null);
            public static PlannedAndCommitted CreateInternalError(string errorString) => new PlannedAndCommitted(ResultCode.InternalError, null, default, null, 0, errorString);
        }
    }

    public static class InternalGuildJoinGuildSync
    {
        [MetaMessage(MessageCodesCore.InternalGuildJoinGuildSyncBegin, MessageDirection.ServerInternal)]
        public class Begin : MetaMessage
        {
            public EntityId                     PlayerId            { get; private set; }
            public GuildJoinRequest             OriginalRequest     { get; private set; }
            public GuildMemberPlayerDataBase    PlayerData          { get; private set; }

            Begin() { }
            public Begin(EntityId playerId, GuildJoinRequest originalRequest, GuildMemberPlayerDataBase playerData)
            {
                PlayerId = playerId;
                OriginalRequest = originalRequest;
                PlayerData = playerData;
            }
        }
        [MetaMessage(MessageCodesCore.InternalGuildJoinGuildSyncPreflightDone, MessageDirection.ServerInternal)]
        public class PreflightDone : MetaMessage
        {
            [MetaSerializable]
            public enum ResultCode
            {
                Ok = 0,
                Reject = 1,
                TryAgain = 2,
            }

            public ResultCode   Result                  { get; private set; }
            public int          MemberInstanceId        { get; private set; }

            public PreflightDone() { }
            PreflightDone(ResultCode result, int memberInstanceId)
            {
                Result = result;
                MemberInstanceId = memberInstanceId;
            }

            public static PreflightDone CreateOk(int memberInstanceId) => new PreflightDone(ResultCode.Ok, memberInstanceId);
            public static PreflightDone CreateReject() => new PreflightDone(ResultCode.Reject, 0);
            public static PreflightDone CreateTryAgain() => new PreflightDone(ResultCode.TryAgain, 0);
        }
        [MetaMessage(MessageCodesCore.InternalGuildJoinGuildSyncPlayerCommitted, MessageDirection.ServerInternal)]
        public class PlayerCommitted : MetaMessage
        {
            public PlayerCommitted() { }
        }
        [MetaMessage(MessageCodesCore.InternalGuildJoinGuildSyncGuildCommitted, MessageDirection.ServerInternal)]
        public class GuildCommitted : MetaMessage
        {
            public GuildCommitted() { }
        }
    }

    /// <summary>
    /// Player initializes just created guild.
    /// </summary>
    public static class InternalGuildSetupSync
    {
        [MetaMessage(MessageCodesCore.InternalGuildSetupSyncBegin, MessageDirection.ServerInternal)]
        public class Begin : MetaMessage
        {
            public GuildCreationParamsBase CreationParams { get; private set; }

            public Begin() { }
            public Begin(GuildCreationParamsBase creationParams)
            {
                CreationParams = creationParams;
            }
        }

        [MetaMessage(MessageCodesCore.InternalGuildSetupSyncSetupResponse, MessageDirection.ServerInternal)]
        public class SetupResponse : MetaMessage
        {
            public bool     IsSuccess           { get; private set; }
            public int      MemberInstanceId    { get; private set; }

            public SetupResponse() { }
            public SetupResponse(bool isSuccess, int memberInstanceId)
            {
                IsSuccess = isSuccess;
                MemberInstanceId = memberInstanceId;
            }
        }
        [MetaMessage(MessageCodesCore.InternalGuildSetupSyncPlayerCommitted, MessageDirection.ServerInternal)]
        public class PlayerCommitted : MetaMessage
        {
            public EntityId                     PlayerId            { get; private set; }
            public GuildMemberPlayerDataBase    PlayerData          { get; private set; }
            public int                          GuildIncarnation    { get; private set; }

            public PlayerCommitted() { }
            public PlayerCommitted(EntityId playerId, GuildMemberPlayerDataBase playerData, int guildIncarnation)
            {
                PlayerId = playerId;
                PlayerData = playerData;
                GuildIncarnation = guildIncarnation;
            }
        }
        [MetaMessage(MessageCodesCore.InternalGuildSetupSyncGuildCommitted, MessageDirection.ServerInternal)]
        public class GuildCommitted : MetaMessage
        {
            public GuildCommitted() { }
        }
    }

    /// <summary>
    /// Request from client (via session) to execute certain action on guild
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalGuildEnqueueActionsRequest, MessageDirection.ServerInternal)]
    public class InternalGuildEnqueueActionsRequest : MetaMessage
    {
        public EntityId                                 PlayerId    { get; private set; }
        public MetaSerialized<List<GuildActionBase>>    Actions     { get; private set; }

        InternalGuildEnqueueActionsRequest() { }
        public InternalGuildEnqueueActionsRequest(EntityId playerId, MetaSerialized<List<GuildActionBase>> actions)
        {
            PlayerId = playerId;
            Actions = actions;
        }
    }

    /// <summary>
    /// Request from member player to execute certain action on guild
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalGuildEnqueueMemberActionRequest, MessageDirection.ServerInternal)]
    public class InternalGuildEnqueueMemberActionRequest : MetaMessage
    {
        public EntityId                         PlayerId            { get; private set; }
        public int                              MemberInstanceId    { get; private set; }
        public MetaSerialized<GuildActionBase>  Action              { get; private set; }

        public InternalGuildEnqueueMemberActionRequest() { }
        public InternalGuildEnqueueMemberActionRequest(EntityId playerId, int memberInstanceId, MetaSerialized<GuildActionBase> action)
        {
            PlayerId = playerId;
            MemberInstanceId = memberInstanceId;
            Action = action;
        }
    }

    /// <summary>
    /// Request from member player to execute certain ops on guild. After messages are committed, guild replies with InternalPlayerPendingGuildOpsCommitted
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalGuildRunPendingGuildOpsRequest, MessageDirection.ServerInternal)]
    public class InternalGuildRunPendingGuildOpsRequest : MetaMessage
    {
        public EntityId                                                 PlayerId            { get; private set; }
        public int                                                      MemberInstanceId    { get; private set; }
        public OrderedDictionary<int, GuildMemberGuildOpLogEntry>       Ops                 { get; private set; }

        public InternalGuildRunPendingGuildOpsRequest() { }
        public InternalGuildRunPendingGuildOpsRequest(EntityId playerId, int memberInstanceId, OrderedDictionary<int, GuildMemberGuildOpLogEntry> ops)
        {
            PlayerId = playerId;
            MemberInstanceId = memberInstanceId;
            Ops = ops;
        }
    }

    /// <summary>
    /// Notification from player to guild that ops have been committed up to certain epoch
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalGuildPlayerOpsCommitted, MessageDirection.ServerInternal)]
    public class InternalGuildPlayerOpsCommitted : MetaMessage
    {
        public EntityId PlayerId                { get; private set; }
        public int      MemberInstanceId        { get; private set; }

        /// <summary>
        /// Inclusive upper bound. Player has committed all ops BEFORE and ON this epoch.
        /// </summary>
        public int      CommittedOpEpoch        { get; private set; }

        public InternalGuildPlayerOpsCommitted() { }
        public InternalGuildPlayerOpsCommitted(EntityId playerId, int memberInstanceId, int committedOpEpoch)
        {
            PlayerId = playerId;
            MemberInstanceId = memberInstanceId;
            CommittedOpEpoch = committedOpEpoch;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildPeekKickedStateRequest, MessageDirection.ServerInternal)]
    public class InternalGuildPeekKickedStateRequest : MetaMessage
    {
        public EntityId PlayerId                { get; private set; }
        public int      MemberInstanceId        { get; private set; }
        public int      CommittedPlayerOpEpoch  { get; private set; }

        public InternalGuildPeekKickedStateRequest() { }
        public InternalGuildPeekKickedStateRequest(EntityId playerId, int memberInstanceId, int committedPlayerOpEpoch)
        {
            PlayerId = playerId;
            MemberInstanceId = memberInstanceId;
            CommittedPlayerOpEpoch = committedPlayerOpEpoch;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildPeekKickedStateResponse, MessageDirection.ServerInternal)]
    public class InternalGuildPeekKickedStateResponse : MetaMessage
    {
        [MetaSerializable]
        public enum ResultCode
        {
            NotKicked = 0,
            NotAMember = 1,
            PendingPlayerOps = 2,
            Kicked = 3,
        }

        public ResultCode                                           Result                      { get; private set; }
        public OrderedDictionary<int, GuildMemberPlayerOpLogEntry>  PendingPlayerOps            { get; private set; }
        public IGuildMemberKickReason                               KickReasonOrNull            { get; private set; }

        public InternalGuildPeekKickedStateResponse() { }
        InternalGuildPeekKickedStateResponse(ResultCode result, OrderedDictionary<int, GuildMemberPlayerOpLogEntry> pendingPlayerOps, IGuildMemberKickReason kickReasonOrNull)
        {
            Result = result;
            PendingPlayerOps = pendingPlayerOps;
            KickReasonOrNull = kickReasonOrNull;
        }

        public static InternalGuildPeekKickedStateResponse CreateNotKicked() => new InternalGuildPeekKickedStateResponse(ResultCode.NotKicked, null, null);
        public static InternalGuildPeekKickedStateResponse CreateNotAMember() => new InternalGuildPeekKickedStateResponse(ResultCode.NotAMember, null, null);
        public static InternalGuildPeekKickedStateResponse CreatePendingPlayerOps(OrderedDictionary<int, GuildMemberPlayerOpLogEntry> pendingPlayerOps) => new InternalGuildPeekKickedStateResponse(ResultCode.PendingPlayerOps, pendingPlayerOps, null);
        public static InternalGuildPeekKickedStateResponse CreateKicked(IGuildMemberKickReason reasonOrNull) => new InternalGuildPeekKickedStateResponse(ResultCode.Kicked, null, reasonOrNull);
    }

    [MetaMessage(MessageCodesCore.InternalGuildPlayerClearKickedState, MessageDirection.ServerInternal)]
    public class InternalGuildPlayerClearKickedState : MetaMessage
    {
        public EntityId PlayerId                { get; private set; }
        public int      MemberInstanceId        { get; private set; }

        public InternalGuildPlayerClearKickedState() { }
        public InternalGuildPlayerClearKickedState(EntityId playerId, int memberInstanceId)
        {
            PlayerId = playerId;
            MemberInstanceId = memberInstanceId;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildAdminKickMember, MessageDirection.ServerInternal)]
    public class InternalGuildAdminKickMember : MetaMessage
    {
        public EntityId PlayerId    { get; private set; }

        public InternalGuildAdminKickMember() { }
        public InternalGuildAdminKickMember(EntityId playerId)
        {
            PlayerId = playerId;
        }
    }

    /// <summary>
    /// Request that the guilds changes their name and description
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalGuildAdminChangeDisplayNameAndDetailsRequest, MessageDirection.ServerInternal)]
    public class InternalGuildChangeDisplayNameAndDescriptionRequest : MetaMessage
    {
        public GuildEventInvokerInfo Invoker { get; private set; }
        public string NewDisplayName { get; private set; }
        public string NewDescription { get; private set; }
        public bool ValidateOnly { get; private set; }

        public InternalGuildChangeDisplayNameAndDescriptionRequest() { }
        public InternalGuildChangeDisplayNameAndDescriptionRequest(GuildEventInvokerInfo invoker, string newDisplayName, string newDescription, bool validateOnly)
        {
            Invoker = invoker;
            NewDisplayName = newDisplayName;
            NewDescription = newDescription;
            ValidateOnly = validateOnly;
        }
    }
    [MetaMessage(MessageCodesCore.InternalGuildAdminChangeDisplayNameAndDetailsReponse, MessageDirection.ServerInternal)]
    public class InternalGuildChangeDisplayNameAndDescriptionResponse : MetaMessage
    {
        public bool DisplayNameWasValid { get; private set; }
        public bool DescriptionWasValid { get; private set; }
        public bool ChangeWasCommitted { get; private set; }

        public InternalGuildChangeDisplayNameAndDescriptionResponse() { }
        public InternalGuildChangeDisplayNameAndDescriptionResponse(bool displayNameWasValid, bool descriptionWasValid, bool changeWasCommitted)
        {
            DisplayNameWasValid = displayNameWasValid;
            DescriptionWasValid = descriptionWasValid;
            ChangeWasCommitted = changeWasCommitted;
        }
    }

    /// <summary>
    /// Guild timeline update from guild -> session.
    /// </summary>
    [MetaMessage(MessageCodesCore.InternalSessionGuildTimelineUpdate, MessageDirection.ServerInternal)]
    public class InternalSessionGuildTimelineUpdate : MetaMessage
    {
        public MetaSerialized<List<GuildTimelineUpdateMessage.Operation>>   Operations      { get; private set; }
        public int                                                          StartTick       { get; private set; } // Tick of the first Operation
        public int                                                          StartOperation  { get; private set; } // Operation index of the first Operation
        public uint                                                         FinalChecksum   { get; private set; }

        public InternalSessionGuildTimelineUpdate() { }
        public InternalSessionGuildTimelineUpdate(MetaSerialized<List<GuildTimelineUpdateMessage.Operation>> operations, int startTick, int startOperation, uint finalChecksum)
        {
            Operations = operations;
            StartTick = startTick;
            StartOperation = startOperation;
            FinalChecksum = finalChecksum;
        }

        public GuildTimelineUpdateMessage CreateUpdateForClient(int guildChannelId) => new GuildTimelineUpdateMessage(Operations, StartTick, StartOperation, FinalChecksum, guildChannelId);
    }

    [MetaMessage(MessageCodesCore.InternalGuildMemberGdprExportRequest, MessageDirection.ServerInternal)]
    public class InternalGuildMemberGdprExportRequest : MetaMessage
    {
        public EntityId PlayerId    { get; private set; }

        public InternalGuildMemberGdprExportRequest() { }
        public InternalGuildMemberGdprExportRequest(EntityId playerId)
        {
            PlayerId = playerId;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildMemberGdprExportResponse, MessageDirection.ServerInternal)]
    public class InternalGuildMemberGdprExportResponse : MetaMessage
    {
        public bool     IsSuccess           { get; private set; }
        public string   ExportJsonString    { get; private set; }

        public InternalGuildMemberGdprExportResponse() { }
        public InternalGuildMemberGdprExportResponse(bool isSuccess, string exportJsonString)
        {
            IsSuccess = isSuccess;
            ExportJsonString = exportJsonString;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildImportModelDataRequest, MessageDirection.ServerInternal)]
    public class InternalGuildImportModelDataRequest : MetaMessage
    {
        public byte[] Payload { get; private set; }
        public int? SchemaVersion { get; private set; }

        public InternalGuildImportModelDataRequest() { }
        public InternalGuildImportModelDataRequest(byte[] payload, int? schemaVersion)
        {
            Payload = payload;
            SchemaVersion = schemaVersion;
        }
    }

    [MetaMessage(MessageCodesCore.InternalGuildImportModelDataResponse, MessageDirection.ServerInternal)]
    public class InternalGuildImportModelDataResponse : MetaMessage
    {
        public bool Success { get; private set; }
        public string FailureInfo { get; private set; }

        public InternalGuildImportModelDataResponse() { }
        public InternalGuildImportModelDataResponse(bool success, string failureInfo) { Success = success; FailureInfo = failureInfo; }
    }

    [MetaMessage(MessageCodesCore.InternalGuildAdminEditRolesRequest, MessageDirection.ServerInternal)]
    public class InternalGuildAdminEditRolesRequest : MetaMessage
    {
        public EntityId                                     TargetPlayerId  { get; private set; }
        public GuildMemberRole                              TargetNewRole   { get; private set; }
        public OrderedDictionary<EntityId, GuildMemberRole> ExpectedChanges { get; private set; }

        public InternalGuildAdminEditRolesRequest() { }
        public InternalGuildAdminEditRolesRequest(EntityId targetPlayerId, GuildMemberRole targetNewRole, OrderedDictionary<EntityId, GuildMemberRole> expectedChanges)
        {
            TargetPlayerId = targetPlayerId;
            TargetNewRole = targetNewRole;
            ExpectedChanges = expectedChanges;
        }
    }
    [MetaMessage(MessageCodesCore.InternalGuildAdminEditRolesResponse, MessageDirection.ServerInternal)]
    public class InternalGuildAdminEditRolesResponse : MetaMessage
    {
        public bool Success { get; private set; }

        public InternalGuildAdminEditRolesResponse() { }
        public InternalGuildAdminEditRolesResponse(bool success) { Success = success; }
    }

    [MetaMessage(MessageCodesCore.InternalGuildInspectInviteCodeRequest, MessageDirection.ServerInternal)]
    public class InternalGuildInspectInviteCodeRequest : MetaMessage
    {
        public int              InviteId    { get; private set; }
        public GuildInviteCode  InviteCode  { get; private set; }

        InternalGuildInspectInviteCodeRequest() { }
        public InternalGuildInspectInviteCodeRequest(int inviteId, GuildInviteCode inviteCode)
        {
            InviteId = inviteId;
            InviteCode = inviteCode;
        }
    }
    [MetaMessage(MessageCodesCore.InternalGuildInspectInviteCodeResponse, MessageDirection.ServerInternal)]
    public class InternalGuildInspectInviteCodeResponse : MetaMessage
    {
        public bool                                     Success         { get; private set; }
        public MetaSerialized<GuildDiscoveryInfoBase>   DiscoveryInfo   { get; private set; }
        public EntityId                                 PlayerId        { get; private set; }

        InternalGuildInspectInviteCodeResponse() { }
        public InternalGuildInspectInviteCodeResponse(bool success, MetaSerialized<GuildDiscoveryInfoBase> discoveryInfo, EntityId playerId)
        {
            Success = success;
            DiscoveryInfo = discoveryInfo;
            PlayerId = playerId;
        }
    }
}

#endif
