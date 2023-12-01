// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace Metaplay.Core.Guild
{
    [MetaSerializable]
    public enum GuildLifecyclePhase
    {
        /// <summary>
        /// Guild is just created and is waiting for the creator to set up the guild
        /// settings. On initialization, guild becomes WaitingForLeader
        /// </summary>
        WaitingForSetup = 0,

        /// <summary>
        /// Guild is initialized and is waiting for the creator player to join.
        /// On join, the creator will become the leader and guild becomes Running.
        /// </summary>
        WaitingForLeader = 1,

        /// <summary>
        /// Guild has members, and is running.
        /// </summary>
        Running = 2,

        /// <summary>
        /// Guild has been closed. Guild has no members and it cannot be joined to.
        /// </summary>
       Closed = 3,
    }

    public enum GuildMemberRoleEvent
    {
        /// <summary>
        /// New Member is about to be adder. The member is not yet in Members list.
        /// </summary>
        MemberAdd,

        /// <summary>
        /// A Member has been removed. The member is not longer in the Members list.
        /// </summary>
        MemberRemove,

        /// <summary>
        /// A Member is about to be edited. The Role is not yet updated in Members list
        /// </summary>
        MemberEdit
    }

    [MetaSerializable]
    public class GuildPendingMemberKickState
    {
        [MetaMember(1)] public MetaTime                                             IssuedAt;
        [MetaMember(2)] public IGuildMemberKickReason                               ReasonOrNull;
        [MetaMember(3)] public OrderedDictionary<int, GuildMemberPlayerOpLogEntry>  PendingPlayerOps;
        [MetaMember(4)] public int                                                  MemberInstanceId;

        public GuildPendingMemberKickState() { }
        public GuildPendingMemberKickState(MetaTime issuedAt, IGuildMemberKickReason reasonOrNull, OrderedDictionary<int, GuildMemberPlayerOpLogEntry> pendingPlayerOps, int memberInstanceId)
        {
            IssuedAt = issuedAt;
            ReasonOrNull = reasonOrNull;
            PendingPlayerOps = pendingPlayerOps;
            MemberInstanceId = memberInstanceId;
        }
    }

    public interface IGuildModelBase : IMultiplayerModel<IGuildModelBase>
    {
        AnalyticsEventHandler<IGuildModelBase, GuildEventBase>                  AnalyticsEventHandler   { get; set; }
        ContextWrappingAnalyticsEventHandler<IGuildModelBase, GuildEventBase>   EventStream             { get; }
        IGuildModelServerListenerCore                                           ServerListenerCore      { get; set; }
        IGuildModelClientListenerCore                                           ClientListenerCore      { get; set; }

        GuildLifecyclePhase                             LifecyclePhase  { get; set; }

        EntityId                                        GuildId         { get; set; }
        string                                          DisplayName     { get; set; }
        string                                          Description     { get; set; }
        int                                             RunningMemberInstanceId { get; set; }
        OrderedDictionary<EntityId, GuildPendingMemberKickState>    PendingKicks            { get; set; }
        bool                                            IsNameSearchValid   { get; set; }
        int                                             RunningInviteId { get; set; }

        int                                             MaxNumMembers   { get; }
        int                                             MemberCount     { get; }
        GuildEventLog                                   EventLog        { get; }

        bool                                            TryGetMember                        (EntityId memberPlayerId, out GuildMemberBase member);
        IEnumerable<EntityId>                           EnumerateMembers                    ();

        /// <summary>
        /// Creates the <see cref="IGuildMemberPrivateState"/> that is delivered to the client in Join (and Create) and
        /// in the initial state.
        /// </summary>
        new GuildMemberPrivateStateBase                 GetMemberPrivateState               (EntityId memberPlayerId);

        /// <summary>
        /// Adds a new player to Members. The <paramref name="memberPlayerId"/> is guaranteed to be a new member.
        /// This method is not expected to change roles. Do not call directly, use
        /// <see cref="IGuildModelBaseExtensions.AddMemberAndUpdateRoles(IGuildModelBase, EntityId, int, GuildMemberPlayerDataBase)"/> instead.
        /// </summary>
        void                                            AddMember                           (EntityId memberPlayerId, int memberInstanceId, GuildMemberRole role, GuildMemberPlayerDataBase playerData);

        /// <summary>
        /// Removes existing members. The <paramref name="memberPlayerId"/> is guaranteed to be a member.
        /// This method is not expected to change roles. Do not call directly, use
        /// <see cref="IGuildModelBaseExtensions.RemoveMemberAndUpdateRoles(IGuildModelBase, EntityId)"/> instead.
        /// </summary>
        void                                            RemoveMember                        (EntityId memberPlayerId);

        /// <summary>
        /// Returns true if <paramref name="memberPlayerId"/> has the permission to invite players to guild with the <paramref name="inviteType"/> type.
        /// </summary>
        bool                                            HasPermissionToInvite               (EntityId memberPlayerId, GuildInviteType inviteType);

        /// <summary>
        /// Returns true if <paramref name="kickerPlayerId"/> has the permission to kick <paramref name="kickedPlayerId"/>.
        /// </summary>
        bool                                            HasPermissionToKickMember           (EntityId kickerPlayerId, EntityId kickedPlayerId);

        /// <summary>
        /// Returns true if <paramref name="requesterPlayerId"/> has the permission to change the role of <paramref name="targetingPlayerId"/>
        /// into <paramref name="targetRole"/> (from it's current value).
        /// </summary>
        bool                                            HasPermissionToChangeRoleTo         (EntityId requesterPlayerId, EntityId targetingPlayerId, GuildMemberRole targetRole);

        /// <summary>
        /// Returns the role changes for member for various membership events. If a certain member role does not change,
        /// it may be omitted from the returned list.
        ///
        /// <para>
        /// There are 3 possible events that need to be handled.
        /// </para>
        ///
        /// <para>
        /// If <paramref name="roleEvent"/> is <see cref="GuildMemberRoleEvent.MemberAdd"/>, a new player is about to be added to the guild. The <paramref name="subjectMemberId"/>
        /// contains the playerId of the new player and <paramref name="subjectRole"/> is unset. The resulting dictionary must contain a role for <paramref name="subjectMemberId"/>.
        /// </para>
        ///
        /// <para>
        /// If <paramref name="roleEvent"/> is <see cref="GuildMemberRoleEvent.MemberRemove"/>, a player has left the guild. The <paramref name="subjectMemberId"/> contains the playerId of the
        /// player left and <paramref name="subjectRole"/> role the player had.
        /// </para>
        ///
        /// <para>
        /// If <paramref name="roleEvent"/> is <see cref="GuildMemberRoleEvent.MemberEdit"/>, a player's role is being edited. The <paramref name="subjectMemberId"/> contains the playerId of the
        /// player being edited and <paramref name="subjectRole"/> the expected role the player is expected to get. For example, if player is being demoted, the <paramref name="subjectRole"/>
        /// would be one rank less than the current Role in Members.
        /// </para>
        /// </summary>
        OrderedDictionary<EntityId, GuildMemberRole>    ComputeRoleChangesForRoleEvent    (GuildMemberRoleEvent roleEvent, EntityId subjectMemberId, GuildMemberRole subjectRole);

        /// <summary>
        /// Updates in-place all EntityIds in the Model using the <paramref name="remapper"/>.
        /// </summary>
        Task RemapEntityIdsAsync(IModelEntityIdRemapper remapper);
    }

    public interface IGuildModel<TGuildModel> : IGuildModelBase where TGuildModel : IGuildModel<TGuildModel>
    {
    }

    public static class IGuildModelBaseExtensions
    {
        /// <summary>
        /// Helper for <see cref="IGuildModelBase.AddMember(EntityId, int, GuildMemberRole, GuildMemberPlayerDataBase)"/> that also updates roles.
        /// </summary>
        public static void AddMemberAndUpdateRoles(this IGuildModelBase guild, EntityId playerId, int memberInstanceId, GuildMemberPlayerDataBase playerData)
        {
            OrderedDictionary<EntityId, GuildMemberRole> newRoles = guild.ComputeRoleChangesForRoleEvent(GuildMemberRoleEvent.MemberAdd, playerId, default);
            guild.AddMember(playerId, memberInstanceId, newRoles[playerId], playerData);

            // Apply role changes to others
            guild.ApplyMemberRoleChanges(newRoles);
        }

        /// <summary>
        /// Helper for <see cref="IGuildModelBase.RemoveMember(EntityId)"/> that also updates roles.
        /// </summary>
        public static void RemoveMemberAndUpdateRoles(this IGuildModelBase guild, EntityId playerId)
        {
            if (!guild.TryGetMember(playerId, out GuildMemberBase removedMember))
                return;

            guild.RemoveMember(playerId);

            // Apply role changes to others
            OrderedDictionary<EntityId, GuildMemberRole> newRoles = guild.ComputeRoleChangesForRoleEvent(GuildMemberRoleEvent.MemberRemove, playerId, removedMember.Role);
            guild.ApplyMemberRoleChanges(newRoles);
        }

        /// <summary>
        /// Apply the given role changes and call <see cref="IGuildModelServerListenerCore.MemberRoleChanged"/>
        /// and <see cref="IGuildModelClientListenerCore.MemberRoleChanged"/> for each member concerned.
        /// </summary>
        public static void ApplyMemberRoleChanges(this IGuildModelBase guild, OrderedDictionary<EntityId, GuildMemberRole> newRoles)
        {
            foreach ((EntityId memberPlayerId, GuildMemberRole newRole) in newRoles)
            {
                if (guild.TryGetMember(memberPlayerId, out GuildMemberBase member))
                    member.Role = newRole;
            }
            foreach ((EntityId memberPlayerId, GuildMemberRole newRole) in newRoles)
            {
                if (guild.TryGetMember(memberPlayerId, out GuildMemberBase member))
                {
                    guild.ServerListenerCore.MemberRoleChanged(memberPlayerId);
                    guild.ClientListenerCore.MemberRoleChanged(memberPlayerId);
                }
            }
        }

        /// <summary>
        /// Gets the timestamp any current member of a guild was latest online. In particular,
        /// if a new player joins, is online and then leaves, this value will return to the original
        /// value. If any player is currently online, returns <paramref name="timestampNow"/>.
        /// <para>
        /// This value is good for assessing the quality of the guild.
        /// </para>
        /// </summary>
        public static MetaTime GetMemberOnlineLatestAt(this IGuildModelBase model, MetaTime timestampNow)
        {
            MetaTime latestMemberOnlineAt = MetaTime.Epoch;
            foreach (EntityId playerId in model.EnumerateMembers())
            {
                if (!model.TryGetMember(playerId, out GuildMemberBase member))
                    continue;
                if (member.IsOnline)
                    return timestampNow;
                latestMemberOnlineAt = MetaTime.Max(latestMemberOnlineAt, member.LastOnlineAt);
            }
            return latestMemberOnlineAt;
        }
    }

    /// <summary>
    /// Common base for GuildModel Members.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(1, 100)]
    public abstract class GuildMemberBase
    {
        /// <summary>
        /// True, if the there is an active session for the member. Session (and
        /// hence this value) might remain alive (true) for a short time after the
        /// user closes the client app.
        /// </summary>
        [MetaMember(1), Transient] public bool      IsOnline;

        /// <summary>
        /// If the player is not online, the point in time when member was last online.
        /// This value is updated when a member logs in or logs out. Use <see cref="IsOnline"/>
        /// to determine if member is currently online.
        /// </summary>
        [MetaMember(2)] public MetaTime             LastOnlineAt;

        /// <summary>
        /// Displayed name of the guild member
        /// </summary>
        [MetaMember(3)] public string               DisplayName;

        /// <summary>
        /// Epoch number of the last executed player enqueued op.
        /// </summary>
        [MetaMember(4), ServerOnly] public int      LastGuildOpEpoch;

        /// <summary>
        /// Highest epoch number in PendingPlayerOps.
        /// </summary>
        [MetaMember(5), ServerOnly] public int      LastPendingPlayerOpEpoch;

        /// <summary>
        /// Set of operations that are not yet confirmed by player to have been executed. Key is epoch number.
        /// May be null if empty.
        /// Marked as ExcludeFromGdprExport as this represents internal communication protocol state, not game state, and
        /// serializing these is just gonna create a lot irrelevant noise.
        /// </summary>
        [MetaMember(6), ServerOnly, ExcludeFromGdprExport]
        public OrderedDictionary<int, GuildMemberPlayerOpLogEntry>  PendingPlayerOps;

        /// <summary>
        /// Uniquely identifies this member instance in this guild. Each member is given a unique id on join. If player
        /// leaves and re-joins a guild, it will be given a new instance id.
        /// </summary>
        [MetaMember(7)] public int                  MemberInstanceId;

        /// <summary>
        /// The role of the player, which usually defines the permissions the member has. See <see cref="GuildMemberRole"/>
        /// for details how to define Member Roles.
        /// </summary>
        [MetaMember(8)] public GuildMemberRole      Role;

        /// <summary>
        /// Set of active invites of the member. The dictionary key is the InviteId.
        /// The contained invitations may have expired already. The expirations are cleaned only periodically.
        /// </summary>
        [MetaMember(9), ServerOnly]
        public OrderedDictionary<int, GuildInviteState>             Invites = new OrderedDictionary<int, GuildInviteState>();

        /// <summary>
        /// The maximum number of active invites a player may have. This value is only checked when new invite is created. When player reaches
        /// this limit, they must either revoke an existing invitation or wait for them to be used or them to expire.
        /// </summary>
        [IgnoreDataMember]
        public virtual int                          MaxNumInvites => 20;

        /// <summary>
        /// The Player Id of the member. This is convenience value as the the PlayerId of GuildModel.Member[X] is always X.
        /// </summary>
        public EntityId                             PlayerId { get; internal set; }

        public GuildMemberBase() { }
        public GuildMemberBase(int memberInstanceId, GuildMemberRole role, EntityId playerId)
        {
            MemberInstanceId = memberInstanceId;
            Role = role;
            PlayerId = playerId; // New member path. Existing players are set via GuildModelBase.SetExistingMemberPlayerIds.
        }
    }

    /// <summary>
    /// Common base for GuildModel classes.
    /// </summary>
    [MetaReservedMembers(1, 100)]
    public abstract class GuildModelBase<
        TGuildModel,
        TGuildMember
        > : IGuildModel<TGuildModel>
        where TGuildModel : GuildModelBase<
            TGuildModel,
            TGuildMember
            >
        where TGuildMember : GuildMemberBase
    {
        [IgnoreDataMember] public int                                                       LogicVersion            { get; set; } = 0;
        [IgnoreDataMember] public ISharedGameConfig                                         GameConfig              { get; set; }
        [IgnoreDataMember] public LogChannel                                                Log                     { get; set; } = LogChannel.Empty;

        [IgnoreDataMember] public IGuildModelServerListenerCore                             ServerListenerCore      { get; set; } = EmptyGuildModelServerListenerCore.Instance;
        [IgnoreDataMember] public IGuildModelClientListenerCore                             ClientListenerCore      { get; set; } = EmptyGuildModelClientListenerCore.Instance;

        [IgnoreDataMember] public AnalyticsEventHandler<IGuildModelBase, GuildEventBase>    AnalyticsEventHandler   { get; set; } = AnalyticsEventHandler<IGuildModelBase, GuildEventBase>.NopHandler;

        [IgnoreDataMember]
        public ContextWrappingAnalyticsEventHandler<IGuildModelBase, GuildEventBase> EventStream
            => new ContextWrappingAnalyticsEventHandler<IGuildModelBase, GuildEventBase>(context: this, handler: AnalyticsEventHandler);

        public IGameConfigDataResolver GetDataResolver() => GameConfig;

        int IMultiplayerModel.TicksPerSecond => GetTicksPerSecond();
        string IMultiplayerModel.GetDisplayNameForDashboard() => DisplayName;
        EntityId IMultiplayerModel.EntityId
        {
            get => GuildId;
            set { GuildId = value; }
        }
        MultiplayerMemberPrivateStateBase IMultiplayerModel.GetMemberPrivateState(EntityId memberPlayerId) => GetMemberPrivateState(memberPlayerId);

        public MetaTime CurrentTime => ModelUtil.TimeAtTick(CurrentTick, TimeAtFirstTick, GetTicksPerSecond());

        /// <summary>
        /// The point in time when CurrentTick = 0
        /// </summary>
        [MetaMember(1), Transient] public MetaTime                                  TimeAtFirstTick         { get; private set; }

        /// <summary>
        /// Current tick, since TimeAtFirstTick.
        /// </summary>
        [MetaMember(2), Transient] public int                                       CurrentTick             { get; private set; }

        [MetaMember(3)] public EntityId                                             GuildId                 { get; set; }
        [MetaMember(4), ServerOnly] public GuildLifecyclePhase                      LifecyclePhase          { get; set; }
        [MetaMember(5)] public string                                               DisplayName             { get; set; }
        [MetaMember(6)] public string                                               Description             { get; set; }
        [MetaMember(7)] public MetaTime                                             CreatedAt               { get; set; }
        [MetaMember(8)] public OrderedDictionary<EntityId, TGuildMember>            Members                 { get; set; } = new OrderedDictionary<EntityId, TGuildMember>();

        [MetaMember(9), ServerOnly]
        public OrderedDictionary<EntityId, GuildPendingMemberKickState>             PendingKicks            { get; set; } = new OrderedDictionary<EntityId, GuildPendingMemberKickState>();
        [MetaMember(10), ServerOnly] public int                                     RunningMemberInstanceId { get; set; }

        [MetaMember(11), ServerOnly] public bool                                    IsNameSearchValid       { get; set; } = false; // Has name search table been populated? Used for migrating old guilds
        [MetaMember(12), ServerOnly] public int                                     RunningInviteId         { get; set; }

        [PrettyPrint(PrettyPrintFlag.HideInDiff)]
        [MetaMember(13), ServerOnly] public GuildEventLog                           EventLog                { get; private set; } = new GuildEventLog();

        // \todo: JoinRequirements
        //     public class PendingInvitation
        //     {
        //         EntityId    PlayerId;   // None if
        //         string      InviteCode;
        //         string      url?;
        //     }
        // [MetaMember(6)] List<>                                                      InvitedPlayers { get; }
        // [MetaMember(6)] List<>                                                      PendingJoinRequests { get; }
        // Banned-to-join list is kept on PlayerModel. Distributes better
        // JoinMode { OpenIfFullfillsRequirements, MemberMustInvite (direct invite / code invite) , PlayerMustRequest & member accept }

        protected GuildModelBase()
        {
        }

        [MetaOnDeserializedAttribute]
        void SetExistingMemberPlayerIds()
        {
            // existing players
            foreach ((EntityId playerId, TGuildMember member) in Members)
                member.PlayerId = playerId;
        }

        protected T GetGameConfig<T>() where T : ISharedGameConfig { return (T)GameConfig; }

        public void SetGameConfig(ISharedGameConfig config) { GameConfig = config; }

        public virtual async Task RemapEntityIdsAsync(IModelEntityIdRemapper remapper)
        {
            GuildId = await remapper.RemapEntityIdAsync(GuildId);

            OrderedDictionary<EntityId, TGuildMember> newMembers = new OrderedDictionary<EntityId, TGuildMember>();
            foreach ((EntityId playerId, TGuildMember member) in Members)
                newMembers[await remapper.RemapEntityIdAsync(playerId)] = member;
            Members = newMembers;

            OrderedDictionary<EntityId, GuildPendingMemberKickState> newKicks = new OrderedDictionary<EntityId, GuildPendingMemberKickState>();
            foreach ((EntityId playerId, GuildPendingMemberKickState pendingKick) in PendingKicks)
                newKicks[await remapper.RemapEntityIdAsync(playerId)] = pendingKick;
            PendingKicks = newKicks;
        }

        public int MemberCount => Members.Count;

        public bool TryGetMember(EntityId memberPlayerId, out GuildMemberBase member)
        {
            if (Members.TryGetValue(memberPlayerId, out TGuildMember concreteMember))
            {
                member = concreteMember;
                return true;
            }
            member = null;
            return false;
        }

        public IEnumerable<EntityId> EnumerateMembers() => Members.Keys; // \todo: Don't alloc.

        public void Tick(IChecksumContext checksumCtx)
        {
            CurrentTick += 1;
            OnTick();
        }

        public void ResetTime(MetaTime timeAtFirstTick)
        {
            TimeAtFirstTick = timeAtFirstTick;
            CurrentTick = 0;
        }

        #region Abstracts and virtuals

        public virtual IModelRuntimeData<IGuildModelBase> GetRuntimeData() => new GuildModelRuntimeDataBase(this);

        public abstract int MaxNumMembers { get; }

        protected abstract int GetTicksPerSecond();

        /// <inheritdoc cref="MultiplayerModelBase{TModel}.OnTick"/>
        public abstract void OnTick();
        public abstract void OnFastForwardTime(MetaDuration elapsedTime);
        public abstract GuildMemberPrivateStateBase GetMemberPrivateState(EntityId memberPlayerId);
        public abstract void AddMember(EntityId memberPlayerId, int memberInstanceId, GuildMemberRole role, GuildMemberPlayerDataBase playerData);
        public abstract void RemoveMember(EntityId memberPlayerId);
        public abstract bool HasPermissionToInvite(EntityId memberPlayerId, GuildInviteType inviteType);
        public abstract bool HasPermissionToKickMember(EntityId kickerPlayerId, EntityId kickedPlayerId);
        public abstract bool HasPermissionToChangeRoleTo(EntityId requesterPlayerId, EntityId targetingPlayerId, GuildMemberRole targetRole);
        public abstract OrderedDictionary<EntityId, GuildMemberRole> ComputeRoleChangesForRoleEvent(GuildMemberRoleEvent roleEvent, EntityId subjectMemberId, GuildMemberRole subjectRole);

        #endregion
    }

    public class GuildModelRuntimeDataBase : IModelRuntimeData<IGuildModelBase>
    {
        readonly ISharedGameConfig                                          _gameConfig;
        readonly int                                                        _logicVersion;
        readonly LogChannel                                                 _log;
        readonly IGuildModelServerListenerCore                              _serverListenerCore;
        readonly IGuildModelClientListenerCore                              _clientListenerCore;
        readonly AnalyticsEventHandler<IGuildModelBase, GuildEventBase>     _analyticsEventHandler;

        public GuildModelRuntimeDataBase(IGuildModelBase instance)
        {
            _gameConfig             = instance.GameConfig;
            _logicVersion           = instance.LogicVersion;
            _log                    = instance.Log;
            _serverListenerCore     = instance.ServerListenerCore;
            _clientListenerCore     = instance.ClientListenerCore;
            _analyticsEventHandler  = instance.AnalyticsEventHandler;
        }

        public virtual void CopyResolversTo(IGuildModelBase instance)
        {
            instance.GameConfig = _gameConfig;
            instance.LogicVersion = _logicVersion;
        }

        public virtual void CopySideEffectListenersTo(IGuildModelBase instance)
        {
            instance.Log                    = _log;
            instance.ServerListenerCore     = _serverListenerCore;
            instance.ClientListenerCore     = _clientListenerCore;
            instance.AnalyticsEventHandler  = _analyticsEventHandler;
        }
    }

    public class GuildModelRuntimeDataBase<TGuildModel> : GuildModelRuntimeDataBase where TGuildModel : IGuildModel<TGuildModel>
    {
        public GuildModelRuntimeDataBase(TGuildModel model) : base(model)
        {
        }

        public virtual void CopyResolversTo(TGuildModel model)
        {
            base.CopyResolversTo(model);
        }

        public sealed override void CopyResolversTo(IGuildModelBase instance)
        {
            CopyResolversTo((TGuildModel)instance);
        }

        public virtual void CopySideEffectListenersTo(TGuildModel model)
        {
            base.CopySideEffectListenersTo(model);
        }

        public sealed override void CopySideEffectListenersTo(IGuildModelBase instance)
        {
            CopySideEffectListenersTo((TGuildModel)instance);
        }
    }

    /// <summary>
    /// Contains all information that are needed for setting up a guild. This bundle is delivered to GuildActor.SetupGuildWithInitArgs.
    /// This data has been validated by PlayerActor.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(1, 100)]
    public abstract class GuildCreationParamsBase
    {
        [MetaMember(1)] public string                   DisplayName     { get; set; }
        [MetaMember(2)] public string                   Description     { get; set; }
    }

    /// <summary>
    /// Contains all information that are needed for setting up a guild that live in a CreationRequest. Notably this differs from <see cref="GuildCreationParamsBase"/>
    /// for this contains only client-visible, client-set values and these values are not yet been validated.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(1, 100)]
    public abstract class GuildCreationRequestParamsBase
    {
    }

    /// <summary>
    /// Contains data that is copied from Player to Guild on login (or join to guild). This member information
    /// should contain all player-owned data that can be changed without guild actions. For example, this base
    /// class contains and handles copying Player's DisplayName into GuildMember's corresponding field.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(1, 100)]
    public abstract class GuildMemberPlayerDataBase
    {
        [MetaMember(1)] public string DisplayName { get; private set; }

        public GuildMemberPlayerDataBase() { }
        protected GuildMemberPlayerDataBase(string displayName)
        {
            DisplayName = displayName;
        }

        /// <summary>
        /// Determines whether a call to <see cref="ApplyOnMember"/> would cause any changes.
        /// Returns true if GuildModel's member information to match this player data, othewise false.
        /// If information does not match, the data is updated by executing an action which calls
        /// <see cref="ApplyOnMember"/>.
        /// </summary>
        public virtual bool IsUpToDate(GuildMemberBase member)
        {
            if (member.DisplayName != DisplayName)
                return false;
            return true;
        }

        /// <summary>
        /// Updates member information to match this player data.
        ///
        /// Note that Guild should not gain the ownership to this <paramref name="playerData"/> or any of the members, and
        /// the guild may not modify them. Specifically, this method must copy all non-immutable objects and data-structures
        /// to the Guild to prevent modifications.
        /// </summary>
        /// <param name="member"></param>
        public virtual void ApplyOnMember(GuildMemberBase member, IGuildModelBase guild, GuildMemberPlayerDataUpdateKind updateKind)
        {
            member.DisplayName = DisplayName;
        }
    }

    public enum GuildMemberPlayerDataUpdateKind
    {
        NewMember,
        UpdateMember,
    }

    /// <summary>
    /// Contains guild member-specific private data of in guild. Member's private data is data
    /// stored in Guild that is visible only to Server (ServerOnly) and the corresponding member.
    /// This can be useful for example in voting scenarios where whether a player has voted is
    /// shared but each player's vote should only be visible to the server and the player itself.
    /// In that case, the implementing type would contain the given vote of the player, if any.
    ///
    /// This type is used to deliver the private fields to a each player separately. Server calls
    /// <see cref="IGuildModelBase.GetMemberPrivateState(EntityId)"/> to create this state and
    /// client then consumes the data in <see cref="ApplyToModel(EntityId, IGuildModelBase)"/>
    /// </summary>
    [MetaSerializableDerived(100)]
    public class GuildMemberPrivateStateBase : MultiplayerMemberPrivateStateBase
    {
        [MetaMember(1)]
        public OrderedDictionary<int, GuildInviteState> Invites { get; private set; }

        GuildMemberPrivateStateBase() {}
        public GuildMemberPrivateStateBase(EntityId memberPlayerId, IGuildModelBase model) : base(memberPlayerId)
        {
            if (!model.TryGetMember(memberPlayerId, out GuildMemberBase member))
                return;

            Invites = member.Invites;
        }

        public override void ApplyToModel(IModel model)
        {
            IGuildModelBase guildModel = (IGuildModelBase)model;
            if (!guildModel.TryGetMember(MemberId, out GuildMemberBase member))
                return;
            member.Invites = Invites;
        }
    }

    // \todo: Move somewhere
    /// <summary>
    /// Contains the user-visible information of the inviter player in a guild invite.
    /// </summary>
    [MetaSerializable]
    [MetaReservedMembers(1, 100)]
    public abstract class GuildInviterAvatarBase
    {
        [MetaMember(1)] public EntityId     PlayerId    { get; set; }
        [MetaMember(2)] public string       DisplayName { get; set; }
    }
}

#endif
