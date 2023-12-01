// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if !METAPLAY_DISABLE_GUILDS

using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Guild.Messages.Core;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core.Guild
{
    public class ViewGuildRefusedException : Exception
    {
    }

    public class GuildCreateInvitationResult
    {
        /// <summary>
        /// The type of the invite requested.
        /// </summary>
        public readonly GuildInviteType                             Type;

        /// <summary>
        /// <c>Success</c> if query completed succesfully. The error code of the failure otherwise.
        /// </summary>
        public readonly GuildCreateInvitationResponse.StatusCode    Status;

        /// <summary>
        /// The InviteId of the created invitation, if <c>Status</c> is <c>Success</c>.
        /// </summary>
        public readonly int                                         InviteId;

        /// <summary>
        /// The the created invitation, if <c>Status</c> is <c>Success</c>.
        /// </summary>
        public readonly GuildInviteState                            InviteState;

        public GuildCreateInvitationResult(GuildInviteType type, GuildCreateInvitationResponse.StatusCode status, int inviteId, GuildInviteState inviteState)
        {
            Type = type;
            Status = status;
            InviteId = inviteId;
            InviteState = inviteState;
        }
    }

    public class GuildInviteInfo
    {
        public readonly EntityId                GuildId;
        public readonly int                     InviteId;
        public readonly GuildDiscoveryInfoBase  GuildInfo;
        public readonly GuildInviterAvatarBase  InviterAvatar;
        public readonly GuildInviteCode         InviteCode;

        public GuildInviteInfo(EntityId guildId, int inviteId, GuildDiscoveryInfoBase guildInfo, GuildInviterAvatarBase inviterAvatar, GuildInviteCode inviteCode)
        {
            GuildId = guildId;
            InviteId = inviteId;
            GuildInfo = guildInfo;
            InviterAvatar = inviterAvatar;
            InviteCode = inviteCode;
        }
    }

    /// <summary>
    /// Phase of a <see cref="GuildClient"/> client. See values for details.
    /// </summary>
    public enum GuildClientPhase
    {
        /// <summary>
        /// There is no active Metaplay connection.
        /// </summary>
        NoSession,

        /// <summary>
        /// Player is not in a Guild
        /// </summary>
        NoGuild,

        /// <summary>
        /// Player is a member of a Guild
        /// </summary>
        GuildActive,

        /// <summary>
        /// Player is currently creating a Guild
        /// </summary>
        CreatingGuild,

        /// <summary>
        /// Player is currently attempting to join a Guild
        /// </summary>
        JoiningGuild,
    }

    /// <summary>
    /// Generic client for guild entities. See also convenience helper <see cref="GuildClient{TGuildModel}"/>
    /// </summary>
    public class GuildClient : IEntitySubClient
    {
        /// <summary>
        /// Current phase of the guild connection. See values for details.
        /// </summary>
        public GuildClientPhase Phase { get; private set; }

        /// <summary>
        /// Current guild context, if <see cref="Phase"/> is <see cref="GuildClientPhase.GuildActive"/>. Otherwise null.
        /// </summary>
        public IGuildClientContext GuildContext { get; private set; }

        IEntityClientContext IEntitySubClient.Context => GuildContext;

        /// <summary>
        /// Current player's id. None if not in a session.
        /// </summary>
        public EntityId PlayerId => _playerId;

        /// <summary>
        /// Invoked after <see cref="Phase"/> changes. This is invoked even if the phase changes to the same value.
        /// (session ending before connection).
        /// </summary>
        public event Action PhaseChanged;

        /// <summary>
        /// Invoked after GuildModel (in GuildContext) changes.
        /// </summary>
        public event Action ActiveGuildUpdated;

        class GuildViewRequestState
        {
            public EntityId                                     GuildId;
            public int                                          GuildChannelId;
            public EntitySerializedState                        GuildState;
            public Task<ISharedGameConfig>                      ConfigDownload;
            public CompletionDispatcher<ForeignGuildContext>    Completion;

            public GuildViewRequestState(EntityId guildId)
            {
                GuildId = guildId;
                Completion = new CompletionDispatcher<ForeignGuildContext>();
            }
        }

        class InviteCreationState
        {
            public GuildInviteType                                      Type;
            public CompletionDispatcher<GuildCreateInvitationResult>    Completion;
            public MetaDuration?                                        ExpirationDuration;
            public int                                                  UsageLimit;

            public InviteCreationState(GuildInviteType type, MetaDuration? expirationDuration, int usageLimit)
            {
                Type = type;
                Completion = new CompletionDispatcher<GuildCreateInvitationResult>();
                ExpirationDuration = expirationDuration;
                UsageLimit = usageLimit;
            }
        }

        class InspectGuildInviteCodeState
        {
            public GuildInviteCode                          InviteCode;
            public CompletionDispatcher<GuildInviteInfo>    Completion;

            public InspectGuildInviteCodeState(GuildInviteCode inviteCode)
            {
                InviteCode = inviteCode;
                Completion = new CompletionDispatcher<GuildInviteInfo>();
            }
        }

        readonly struct DelayedAction
        {
            public readonly MetaTime RunAt;
            public readonly Action Action;

            public DelayedAction(MetaTime runAt, Action action)
            {
                RunAt = runAt;
                Action = action;
            }
        }

        EntityId _playerId;
        bool _playerConsistencyChecks;
        IMetaplaySubClientServices _services;
        LogChannel _log;
        int _logicVersion;
        IMessageDispatcher _messageDispatcher;
        MetaplayClientStore _clientStore;
        Action<IGuildModelBase> _applyClientListeners;

        CompletionDispatcher<bool> _pendingGuildCreationCompletion;

        EntityId _pendingGuildJoin;
        CompletionDispatcher<bool> _pendingGuildJoinCompletion;

        CompletionDispatcher<GuildDiscoveryResponse> _pendingDiscoveryCompletion;
        bool _discoveryOngoing;

        CompletionDispatcher<GuildSearchResponse> _pendingSearchCompletion;
        bool _searchOngoing;

        EntitySerializedState _waitingState;
        List<GuildTimelineUpdateMessage> _waitedOps;
        int _waitedChannelId;
        Task<ISharedGameConfig> _waitingConfigs;
        CompletionDispatcher<bool> _waitingGuildCreationCompletion;
        CompletionDispatcher<bool> _waitingGuildJoinCompletion;

        int _guildChannelId = -1; // ChannelId of the current (our) guild
        Dictionary<int, ForeignGuildContext> _guildViews; // ChannelId -> Context
        Dictionary<int, GuildViewRequestState> _pendingGuildViews; // QueryId -> PendingState
        int _viewQueryRunningId = 1;
        int _invitationQueryRunningId = 1;
        Dictionary<int, InviteCreationState> _pendingCreateInvitations;
        int _inspectGuildInviteCodeQueryRunningId = 1;
        Dictionary<int, InspectGuildInviteCodeState> _pendingInspectGuildInviteCode;

        List<DelayedAction> _delayedActions = new List<DelayedAction>();

        public Dictionary<int, ForeignGuildContext>.ValueCollection GuildViews => _guildViews.Values;
        public bool HasOngoingGuildDiscovery => _discoveryOngoing;
        public bool HasOngoingGuildSearch => _searchOngoing;

        public ClientSlot ClientSlot => ClientSlotCore.Guild;

#if UNITY_EDITOR
        public static GuildClient EditorHookCurrent = null;
#endif

        public GuildClient()
        {
#if UNITY_EDITOR
            EditorHookCurrent = this;
#endif
        }

        /// <inheritdoc />
        public void Initialize(IMetaplaySubClientServices services)
        {
            Phase                          = GuildClientPhase.NoSession;
            GuildContext                   = null;
            _clientStore                   = services.ClientStore;
            _messageDispatcher             = services.MessageDispatcher;
            _playerConsistencyChecks       = services.EnableConsistencyChecks;
            _services                      = services;
            _log                           = services.CreateLogChannel("guild");

            _guildViews                    = new Dictionary<int, ForeignGuildContext>();
            _pendingGuildViews             = new Dictionary<int, GuildViewRequestState>();
            _pendingCreateInvitations      = new Dictionary<int, InviteCreationState>();
            _pendingInspectGuildInviteCode = new Dictionary<int, InspectGuildInviteCodeState>();

            _messageDispatcher.AddListener<GuildTimelineUpdateMessage>(OnGuildTimelineUpdateMessage);
            _messageDispatcher.AddListener<GuildCreateResponse>(OnGuildCreateResponse);
            _messageDispatcher.AddListener<GuildJoinResponse>(OnGuildJoinResponse);
            _messageDispatcher.AddListener<GuildDiscoveryResponse>(OnGuildDiscoveryResponse);
            _messageDispatcher.AddListener<GuildTransactionResponse>(OnGuildTransactionResponse);
            _messageDispatcher.AddListener<GuildSearchResponse>(OnGuildSearchResponse);
            _messageDispatcher.AddListener<GuildViewResponse>(OnGuildViewResponse);
            _messageDispatcher.AddListener<GuildViewEnded>(OnGuildViewEnded);
            _messageDispatcher.AddListener<GuildSwitchedMessage>(OnGuildSwitchedMessage);
            _messageDispatcher.AddListener<GuildCreateInvitationResponse>(OnGuildCreateInvitationResponse);
            _messageDispatcher.AddListener<GuildInspectInvitationResponse>(OnGuildInspectInvitationResponse);
        }

        public void Dispose()
        {
            _messageDispatcher.RemoveListener<GuildTimelineUpdateMessage>(OnGuildTimelineUpdateMessage);
            _messageDispatcher.RemoveListener<GuildCreateResponse>(OnGuildCreateResponse);
            _messageDispatcher.RemoveListener<GuildJoinResponse>(OnGuildJoinResponse);
            _messageDispatcher.RemoveListener<GuildDiscoveryResponse>(OnGuildDiscoveryResponse);
            _messageDispatcher.RemoveListener<GuildTransactionResponse>(OnGuildTransactionResponse);
            _messageDispatcher.RemoveListener<GuildSearchResponse>(OnGuildSearchResponse);
            _messageDispatcher.RemoveListener<GuildViewResponse>(OnGuildViewResponse);
            _messageDispatcher.RemoveListener<GuildViewEnded>(OnGuildViewEnded);
            _messageDispatcher.RemoveListener<GuildSwitchedMessage>(OnGuildSwitchedMessage);
            _messageDispatcher.RemoveListener<GuildCreateInvitationResponse>(OnGuildCreateInvitationResponse);
            _messageDispatcher.RemoveListener<GuildInspectInvitationResponse>(OnGuildInspectInvitationResponse);
        }

        public void OnSessionStart(SessionProtocol.SessionStartSuccess successMessage, ClientSessionStartResources sessionStartResources)
        {
            _playerId                = successMessage.PlayerId;
            _logicVersion            = successMessage.LogicVersion;

            int initialEntityIndex = -1;
            for (int ndx = 0; ndx < successMessage.EntityStates.Count; ++ndx)
            {
                bool shouldAttachToNewEntityChannel = successMessage.EntityStates[ndx].ContextData.ClientSlot == ClientSlot;
                if (!shouldAttachToNewEntityChannel)
                    continue;
                if (initialEntityIndex != -1)
                    throw new InvalidOperationException($"Guild client has ambiguous initial entity. Could be either index {initialEntityIndex} or {ndx}.");
                initialEntityIndex = ndx;
            }

            if (initialEntityIndex != -1)
            {
                EntityInitialState initialState = successMessage.EntityStates[initialEntityIndex];
                ActivateGuildWithState(initialState.State, initialState.ChannelId, guildUpdatesMaybe: null, sessionStartResources.GameConfigs[ClientSlot]);
            }
            else
            {
                Phase = GuildClientPhase.NoGuild;
                GuildContext = null;
                PhaseChanged?.Invoke();
            }
        }

        public void OnSessionStop()
        {
            Phase = GuildClientPhase.NoSession;
            GuildContext = null;
            PhaseChanged?.Invoke();

            _playerId = EntityId.None;

            _pendingDiscoveryCompletion?.Cancel();
            _pendingDiscoveryCompletion = null;
            _discoveryOngoing = false;

            _pendingDiscoveryCompletion?.Cancel();
            _pendingDiscoveryCompletion = null;
            _searchOngoing = false;

            _waitingState = default;
            _waitedOps = null;
            _waitingConfigs = null;

            foreach (GuildViewRequestState pendingState in _pendingGuildViews.Values)
                pendingState.Completion.Cancel();

            foreach (ForeignGuildContext foreignContext in _guildViews.Values)
                foreignContext.Dispose();

            _pendingGuildViews.Clear();
            _guildViews.Clear();

            _pendingGuildCreationCompletion?.Cancel();
            _pendingGuildCreationCompletion = null;

            _pendingGuildJoinCompletion?.Cancel();
            _pendingGuildJoinCompletion = null;

            _waitingGuildCreationCompletion?.Cancel();
            _waitingGuildCreationCompletion = null;

            _waitingGuildJoinCompletion?.Cancel();
            _waitingGuildJoinCompletion = null;

            CancelAllOngoingCreateInvites();

            foreach (InspectGuildInviteCodeState pendingState in _pendingInspectGuildInviteCode.Values)
                pendingState.Completion.Cancel();
            _pendingInspectGuildInviteCode.Clear();

            _delayedActions.Clear();
        }

        public void OnDisconnected()
        {
        }

        void CancelAllOngoingCreateInvites()
        {
            foreach (InviteCreationState pendingState in _pendingCreateInvitations.Values)
                pendingState.Completion.Cancel();
            _pendingCreateInvitations.Clear();
        }

        public void EarlyUpdate()
        {
            // If waited configs complete, try to materialize it.
            if (_waitingConfigs != null && _waitingConfigs.IsCompleted)
            {
                if (_waitingConfigs.IsFaulted)
                {
                    Exception configLoadError = _waitingConfigs.Exception;

                    _waitingState = default;
                    _waitedOps = null;

                    _services.DefaultHandleConfigFetchFailed(configLoadError);
                }
                else if (_waitingConfigs.Status == TaskStatus.RanToCompletion)
                {
                    ISharedGameConfig gameConfig = _waitingConfigs.GetCompletedResult();
                    ActivateGuildWithState(_waitingState, _waitedChannelId, _waitedOps, gameConfig);

                    _waitingState = default;
                    _waitedOps = null;
                    _waitedChannelId = -1;

                    _waitingGuildCreationCompletion?.Dispatch(true);
                    _waitingGuildCreationCompletion = null;

                    _waitingGuildJoinCompletion?.Dispatch(true);
                    _waitingGuildJoinCompletion = null;
                }
                else
                {
                    // cancelled. Ignored.
                }

                _waitingConfigs = null;
                _waitingState = default;
            }

            bool pendingViewsUpdated;
            do
            {
                pendingViewsUpdated = false;
                foreach ((int queryId, GuildViewRequestState state) in _pendingGuildViews)
                {
                    if (state.ConfigDownload != null && state.ConfigDownload.IsCompleted)
                    {
                        if (state.ConfigDownload.IsFaulted)
                        {
                            Exception configLoadError = state.ConfigDownload.Exception;
                            _services.DefaultHandleConfigFetchFailed(configLoadError);
                        }
                        else if (state.ConfigDownload.Status == TaskStatus.RanToCompletion)
                        {
                            ISharedGameConfig gameConfig = state.ConfigDownload.GetCompletedResult();
                            IGuildModelBase model = DefaultDeserializeModel(state.GuildState, gameConfig);
                            ForeignGuildContext context = CreateForeignGuildContext(model, state.GuildChannelId);
                            _guildViews[state.GuildChannelId] = context;
                            state.Completion.Dispatch(context);
                        }
                        else
                        {
                            // cancelled. Ignored.
                        }

                        _pendingGuildViews.Remove(queryId);
                        pendingViewsUpdated = true;
                        break;
                    }
                }
            } while(pendingViewsUpdated);

            // Delayed updates
            MetaTime now = MetaTime.Now;
            for (int ndx = 0; ndx < _delayedActions.Count; )
            {
                if (_delayedActions[ndx].RunAt > now)
                {
                    ++ndx;
                    continue;
                }

                // swap last and pop
                DelayedAction curAction = _delayedActions[ndx];
                _delayedActions.RemoveAt(ndx);

                curAction.Action();
            }
        }

        public void UpdateLogic(MetaTime timeNow)
        {
            GuildContext?.Update(timeNow);
        }

        public void FlushPendingMessages()
        {
            GuildContext?.FlushActions();
        }

        /// <summary>
        /// Starts creation of a new guild (with the supplied params), and moves into <see cref="GuildClientPhase.CreatingGuild"/> phase.
        /// <para>
        /// If create eventually succeeds, Phase will be set to <see cref="GuildClientPhase.GuildActive"/> and <paramref name="onCompletion"/>
        /// is called with <c>true</c> argument. Otherwise if creating does not succeed, Phase will reset to <see cref="GuildClientPhase.NoGuild"/> and
        /// <paramref name="onCompletion"/> is called with <c>false</c> argument. The Callback is called on the Unity thread.
        /// </para>
        /// <para>
        /// If creation cannot be started (no session, already in a guild or creating or joining a guild), throws <see cref="InvalidOperationException" />.
        /// If session lost during the join, the callback is not called.
        /// </para>
        /// </summary>
        public void BeginCreateGuild(GuildCreationRequestParamsBase creationParams, Action<bool> onCompletion)
        {
            if (Phase != GuildClientPhase.NoGuild)
                throw new InvalidOperationException($"guild phase must be NoGuild, got {Phase}");

            _pendingGuildCreationCompletion = new CompletionDispatcher<bool>();
            _pendingGuildCreationCompletion.RegisterAction(onCompletion);
            Phase = GuildClientPhase.CreatingGuild;
            PhaseChanged?.Invoke();

            _services.MessageDispatcher.SendMessage(new GuildCreateRequest(creationParams));
        }

        /// <summary>
        /// Starts creation of a new guild (with the supplied params), and moves into <see cref="GuildClientPhase.CreatingGuild"/> phase.
        /// <para>
        /// If create eventually succeeds, Phase will be set to <see cref="GuildClientPhase.GuildActive"/> and the returned task completes
        /// successfully with <c>true</c>. Otherwise if create does not succeed, Phase will reset to <see cref="GuildClientPhase.NoGuild"/> and the
        /// returned task completes with <c>false</c> value.
        /// </para>
        /// <para>
        /// If creation cannot be started (no session, already in a guild, or creating a guild), the task completes with
        /// <see cref="InvalidOperationException" /> exception.  If session lost during the join, the returned task is cancelled.
        /// </para>
        /// </summary>
        public Task<bool> BeginCreateGuild(GuildCreationRequestParamsBase creationParams)
        {
            if (Phase != GuildClientPhase.NoGuild)
                return Task.FromException<bool>(new InvalidOperationException($"guild phase must be NoGuild, got {Phase}"));

            _pendingGuildCreationCompletion = new CompletionDispatcher<bool>();
            Phase = GuildClientPhase.CreatingGuild;
            PhaseChanged?.Invoke();

            _services.MessageDispatcher.SendMessage(new GuildCreateRequest(creationParams));

            return _pendingGuildCreationCompletion.GetTask();
        }

        void OnGuildCreateResponse(GuildCreateResponse response)
        {
            // No matter what phase we are in, server can force us to create a guild.
            // But, if the create was refused, we can skip this weird step.

            if (!response.GuildState.HasValue) // refuse
            {
                Phase = GuildClientPhase.NoGuild;
                PhaseChanged?.Invoke();

                _pendingGuildCreationCompletion?.Dispatch(false);
                _pendingGuildCreationCompletion = null;
                return;
            }

            // Guild forces us to create a guild
            if (Phase != GuildClientPhase.CreatingGuild)
            {
                Phase = GuildClientPhase.CreatingGuild;
                PhaseChanged?.Invoke();
            }

            _waitingState = response.GuildState.Value;
            _waitedOps = new List<GuildTimelineUpdateMessage>();
            _waitedChannelId = response.GuildChannelId;
            _waitingConfigs = _services.GetConfigAsync(response.GuildState.Value.SharedGameConfigVersion, response.GuildState.Value.SharedConfigPatchesVersion, response.GuildState.Value.TryGetNonEmptyExperimentAssignment());
            _waitingGuildCreationCompletion = _pendingGuildCreationCompletion;
            _pendingGuildCreationCompletion = null;
        }

        /// <summary>
        /// Begins joining into a guild, and moves into <see cref="GuildClientPhase.JoiningGuild"/> Phase.
        /// <para>
        /// If join eventually succeeds, Phase will be set to <see cref="GuildClientPhase.GuildActive"/> and <paramref name="onCompletion"/> is called with
        /// <c>true</c> argument. Otherwise if joining does not succeed, Phase will reset to <see cref="GuildClientPhase.NoGuild"/> and
        /// <paramref name="onCompletion"/> is called with <c>false</c> argument. The Callback is called on the Unity thread.
        /// </para>
        /// <para>
        /// If joining cannot be started (no session, already in a guild, or creating a guild), throws <see cref="InvalidOperationException" />.
        /// If session lost during the join, the callback is not called.
        /// </para>
        /// </summary>
        public void BeginJoinGuild(EntityId guildId, Action<bool> onCompletion)
        {
            if (Phase != GuildClientPhase.NoGuild)
                throw new InvalidOperationException($"guild phase must be NoGuild, got {Phase}");

            _pendingGuildJoin = guildId;
            _pendingGuildJoinCompletion = new CompletionDispatcher<bool>();
            _pendingGuildJoinCompletion.RegisterAction(onCompletion);
            Phase = GuildClientPhase.JoiningGuild;
            PhaseChanged?.Invoke();

            _services.MessageDispatcher.SendMessage(new GuildJoinRequest(GuildJoinRequest.JoinMode.Normal, guildId, 0, default));
        }

        /// <summary>
        /// Begins joining into a guild, and moves into <see cref="GuildClientPhase.JoiningGuild"/> phase.
        /// <para>
        /// If join eventually succeeds, Phase will be set to <see cref="GuildClientPhase.GuildActive"/> and returned task completes with
        /// <c>true</c> value. Otherwise if joining does not succeed, Phase will reset to <see cref="GuildClientPhase.NoGuild"/> and the
        /// returned task completes with <c>false</c> value.
        /// </para>
        /// <para>
        /// If joining cannot be started (no session, already in a guild, or creating a guild), the task completes with
        /// <see cref="InvalidOperationException" /> exception.  If session lost during the join, the returned task is cancelled.
        /// </para>
        /// </summary>
        public Task<bool> BeginJoinGuild(EntityId guildId)
        {
            if (Phase != GuildClientPhase.NoGuild)
                return Task.FromException<bool>(new InvalidOperationException($"guild phase must be NoGuild, got {Phase}"));

            _pendingGuildJoin = guildId;
            _pendingGuildJoinCompletion = new CompletionDispatcher<bool>();
            Phase = GuildClientPhase.JoiningGuild;
            PhaseChanged?.Invoke();

            _services.MessageDispatcher.SendMessage(new GuildJoinRequest(GuildJoinRequest.JoinMode.Normal, guildId, 0, default));

            return _pendingGuildJoinCompletion.GetTask();
        }

        /// <summary>
        /// <inheritdoc cref="BeginJoinGuild(EntityId, Action{bool})"/>
        /// </summary>
        public void BeginJoinGuildWithInviteCode(EntityId guildId, int inviteId, GuildInviteCode inviteCode, Action<bool> onCompletion)
        {
            if (Phase != GuildClientPhase.NoGuild)
                throw new InvalidOperationException($"guild phase must be NoGuild, got {Phase}");

            _pendingGuildJoin = guildId;
            _pendingGuildJoinCompletion = new CompletionDispatcher<bool>();
            _pendingGuildJoinCompletion.RegisterAction(onCompletion);
            Phase = GuildClientPhase.JoiningGuild;
            PhaseChanged?.Invoke();

            _services.MessageDispatcher.SendMessage(new GuildJoinRequest(GuildJoinRequest.JoinMode.InviteCode, guildId, inviteId, inviteCode));
        }

        /// <summary>
        /// <inheritdoc cref="BeginJoinGuild(EntityId)"/>
        /// </summary>
        public Task<bool> BeginJoinGuildWithInviteCode(EntityId guildId, int inviteId, GuildInviteCode inviteCode)
        {
            if (Phase != GuildClientPhase.NoGuild)
                return Task.FromException<bool>(new InvalidOperationException($"guild phase must be NoGuild, got {Phase}"));

            _pendingGuildJoin = guildId;
            _pendingGuildJoinCompletion = new CompletionDispatcher<bool>();
            Phase = GuildClientPhase.JoiningGuild;
            PhaseChanged?.Invoke();

            _services.MessageDispatcher.SendMessage(new GuildJoinRequest(GuildJoinRequest.JoinMode.InviteCode, guildId, inviteId, inviteCode));

            return _pendingGuildJoinCompletion.GetTask();
        }

        void OnGuildJoinResponse(GuildJoinResponse response)
        {
            if (Phase != GuildClientPhase.JoiningGuild)
                return;

            _pendingGuildJoin = EntityId.None;

            if (!response.GuildState.HasValue) // refuse
            {
                Phase = GuildClientPhase.NoGuild;
                PhaseChanged?.Invoke();

                _pendingGuildJoinCompletion?.Dispatch(false);
                _pendingGuildJoinCompletion = null;
                return;
            }

            _waitingState = response.GuildState.Value;
            _waitedOps = new List<GuildTimelineUpdateMessage>();
            _waitedChannelId = response.GuildChannelId;
            _waitingConfigs = _services.GetConfigAsync(response.GuildState.Value.SharedGameConfigVersion, response.GuildState.Value.SharedConfigPatchesVersion, response.GuildState.Value.TryGetNonEmptyExperimentAssignment());
            _waitingGuildJoinCompletion = _pendingGuildJoinCompletion;
            _pendingGuildJoinCompletion = null;
        }

        /// <summary>
        /// Leaves guild, sets Phase to <see cref="GuildClientPhase.NoGuild"/> and returns true. If cannot leave the guild
        /// (no session, not in a guild, is currently creating or joining a guild) returns false.
        /// </summary>
        public bool LeaveGuild()
        {
            if (Phase != GuildClientPhase.GuildActive)
                return false;

            GuildContext?.FlushActions();

            Phase = GuildClientPhase.NoGuild;
            GuildContext = null;
            PhaseChanged?.Invoke();

            _services.MessageDispatcher.SendMessage(new GuildLeaveRequest(_guildChannelId));

            CancelAllOngoingCreateInvites();
            return true;
        }

        /// <summary>
        /// Request server for guild recommendations. On completion, <paramref name="onResponse"/>
        /// will be called on Unity thread. If session is closed before response, uncalled callbacks
        /// are forgotten and will not be called.
        /// </summary>
        public void DiscoverGuilds(Action<GuildDiscoveryResponse> onResponse)
        {
            if (Phase == GuildClientPhase.NoSession)
                return; // forgotten immediately
            if (_pendingDiscoveryCompletion == null)
                _pendingDiscoveryCompletion = new CompletionDispatcher<GuildDiscoveryResponse>();

            _pendingDiscoveryCompletion.RegisterAction(onResponse);

            if (!_discoveryOngoing)
            {
                _discoveryOngoing = true;
                _services.MessageDispatcher.SendMessage(new GuildDiscoveryRequest());
            }
        }

        /// <summary>
        /// Request server for guild recommendations. On completion, returned task completes
        /// with the response. If session is closed before response, the returned task is
        /// Cancelled.
        /// </summary>
        public Task<GuildDiscoveryResponse> DiscoverGuilds()
        {
            if (Phase == GuildClientPhase.NoSession)
                return Task.FromCanceled<GuildDiscoveryResponse>(CancellationToken.None);
            if (_pendingDiscoveryCompletion == null)
                _pendingDiscoveryCompletion = new CompletionDispatcher<GuildDiscoveryResponse>();

            if (!_discoveryOngoing)
            {
                _discoveryOngoing = true;
                _services.MessageDispatcher.SendMessage(new GuildDiscoveryRequest());
            }
            return _pendingDiscoveryCompletion.GetTask();
        }

        void OnGuildDiscoveryResponse(GuildDiscoveryResponse response)
        {
            _discoveryOngoing = false;

            CompletionDispatcher<GuildDiscoveryResponse> completion = _pendingDiscoveryCompletion;
            _pendingDiscoveryCompletion = null;
            completion?.Dispatch(response);
        }

        /// <summary>
        /// Request server to search for guilds matching <paramref name="searchParams"/>. On completion,
        /// <paramref name="onResponse"/> will be called on Unity thread. If session is closed before response,
        /// uncalled callbacks are forgotten and will not be called.
        /// </summary>
        public void SearchGuilds(GuildSearchParamsBase searchParams, Action<GuildSearchResponse> onResponse)
        {
            if (Phase == GuildClientPhase.NoSession)
                return; // forgotten immediately
            if (_searchOngoing)
                throw new InvalidOperationException("search already ongoing");

            _pendingSearchCompletion = new CompletionDispatcher<GuildSearchResponse>();
            _pendingSearchCompletion.RegisterAction(onResponse);

            _searchOngoing = true;
            _services.MessageDispatcher.SendMessage(new GuildSearchRequest(searchParams));
        }

        /// <summary>
        /// Request server to search for guilds matching <paramref name="searchParams"/>. On completion,
        /// returned task completes with the response. If session is closed before response, the returned
        /// task is Cancelled.
        /// </summary>
        public Task<GuildSearchResponse> SearchGuilds(GuildSearchParamsBase searchParams)
        {
            if (Phase == GuildClientPhase.NoSession)
                return Task.FromCanceled<GuildSearchResponse>(CancellationToken.None);
            if (_searchOngoing)
                throw new InvalidOperationException("search already ongoing");

            _pendingSearchCompletion = new CompletionDispatcher<GuildSearchResponse>();

            _searchOngoing = true;
            _services.MessageDispatcher.SendMessage(new GuildSearchRequest(searchParams));
            return _pendingSearchCompletion.GetTask();
        }

        void OnGuildSearchResponse(GuildSearchResponse response)
        {
            _searchOngoing = false;

            CompletionDispatcher<GuildSearchResponse> completion = _pendingSearchCompletion;
            _pendingSearchCompletion = null;
            completion.Dispatch(response);
        }

        /// <summary>
        /// Requests server to view some guild. On success, a <see cref="ForeignGuildContext"/> is created
        /// and passed to <paramref name="onResponse"/>. On failure, <c>null</c> is passed instead. The created
        /// ForeignGuildContext will remain alive and active until it is disposed, closed with <see cref="EndViewGuild"/>,
        /// session is lost, or until server forcibly closes the view.
        /// <para>
        /// <paramref name="onResponse"/> will be called on Unity thread. If session is closed before response,
        /// uncalled callbacks are forgotten and will not be called.
        /// </para>
        /// </summary>
        public void BeginViewGuild(EntityId guildId, Action<ForeignGuildContext> onResponse)
        {
            CheckNoPreexistingViewToGuild(guildId);

            int queryId = _viewQueryRunningId++;

            GuildViewRequestState pendingState = new GuildViewRequestState(guildId);
            pendingState.Completion.RegisterAction(onResponse);
            _pendingGuildViews[queryId] = pendingState;

            _services.MessageDispatcher.SendMessage(new GuildBeginViewRequest(guildId, queryId));
        }

        /// <summary>
        /// Requests server to view some guild. On success, the function completes with a created <see cref="ForeignGuildContext"/>.
        /// On failure, task completes with a <see cref="ViewGuildRefusedException"/>. If session is closed before response, the returned
        /// task is Cancelled. The created  ForeignGuildContext will remain alive and active until it is disposed, closed with <see cref="EndViewGuild"/>,
        /// session is lost, or until server forcibly closes the view.
        /// </summary>
        public Task<ForeignGuildContext> BeginViewGuild(EntityId guildId)
        {
            CheckNoPreexistingViewToGuild(guildId);

            int queryId = _viewQueryRunningId++;

            GuildViewRequestState pendingState = new GuildViewRequestState(guildId);
            _pendingGuildViews[queryId] = pendingState;

            _services.MessageDispatcher.SendMessage(new GuildBeginViewRequest(guildId, queryId));
            return pendingState.Completion.GetTask();
        }

        void CheckNoPreexistingViewToGuild(EntityId guildId)
        {
            foreach (ForeignGuildContext context in _guildViews.Values)
            {
                if (context.Model.GuildId == guildId)
                    throw new InvalidOperationException($"View to a guild {guildId} already opened");
            }
            foreach (GuildViewRequestState requestState in _pendingGuildViews.Values)
            {
                if (requestState.GuildId == guildId)
                    throw new InvalidOperationException($"Request to view a guild {guildId} already pending");
            }
        }

        void OnGuildViewResponse(GuildViewResponse response)
        {
            if (!_pendingGuildViews.TryGetValue(response.QueryId, out GuildViewRequestState state))
            {
                _log.Warning("Got GuildViewResponse but there is no matching request");
                return;
            }
            else if (state.ConfigDownload != null)
            {
                _log.Warning("Got GuildViewResponse but but already processed it");
                return;
            }

            if (response.Status != GuildViewResponse.StatusCode.Success)
            {
                _pendingGuildViews.Remove(response.QueryId);
                state.Completion.DispatchAndThrow(null, new ViewGuildRefusedException());
                return;
            }

            // Successfully started a view on server. In order to view it on client, we need to its configs

            state.GuildChannelId = response.GuildChannelId;
            state.GuildState = response.GuildState;
            state.ConfigDownload = _services.GetConfigAsync(response.GuildState.SharedGameConfigVersion, response.GuildState.SharedConfigPatchesVersion, response.GuildState.TryGetNonEmptyExperimentAssignment());
        }

        void OnGuildViewEnded(GuildViewEnded notify)
        {
            // From pending set.
            foreach ((int queryId, GuildViewRequestState state) in _pendingGuildViews)
            {
                if (state.GuildChannelId != notify.GuildChannelId)
                    continue;

                _pendingGuildViews.Remove(queryId);
                state.Completion.DispatchAndThrow(null, new ViewGuildRefusedException());
                return;
            }

            // From active set
            if (_guildViews.Remove(notify.GuildChannelId, out ForeignGuildContext context))
            {
                context.OnViewClosedByServer();
                return;
            }
        }

        /// <summary>
        /// Closes the active guild view. This is functionally identical to Disposing the context.
        /// See <see cref="ForeignGuildContext.Dispose"/>.
        /// </summary>
        public void EndViewGuild(ForeignGuildContext context)
        {
            DisposeViewByChannelId(context.ChannelId);
        }

        void OnGuildSwitchedMessage(GuildSwitchedMessage changed)
        {
            // Kill any ongoing operations (without Phase change notifications yet).
            switch (Phase)
            {
                case GuildClientPhase.JoiningGuild:
                {
                    _pendingGuildJoinCompletion?.Dispatch(false);
                    _pendingGuildJoinCompletion = null;

                    _waitingGuildJoinCompletion?.Dispatch(false);
                    _waitingGuildJoinCompletion = null;
                    break;
                }

                case GuildClientPhase.CreatingGuild:
                {
                    _pendingGuildCreationCompletion?.Dispatch(false);
                    _pendingGuildCreationCompletion = null;

                    _waitingGuildCreationCompletion?.Dispatch(false);
                    _waitingGuildCreationCompletion = null;
                    break;
                }
            }

            Phase = GuildClientPhase.NoGuild;
            GuildContext = null;

            _pendingGuildJoin = EntityId.None;

            _waitingConfigs = null;
            _waitingState = default;
            _waitedOps = null;
            _waitedChannelId = -1;

            // observe as NoGuild in both paths (kick/remove and put into guild) to avoid mixing with potential (failed) join operation.
            PhaseChanged?.Invoke();

            CancelAllOngoingCreateInvites();

            if (changed.GuildState == null)
            {
                // was kicked/removed from guild. We are done.
            }
            else
            {
                // was placed in a guild. Like a join.

                // observe as joining
                Phase = GuildClientPhase.JoiningGuild;
                PhaseChanged?.Invoke();

                _waitingState = changed.GuildState.Value;
                _waitedOps = new List<GuildTimelineUpdateMessage>();
                _waitedChannelId = changed.GuildChannelId;
                _waitingConfigs = _services.GetConfigAsync(changed.GuildState.Value.SharedGameConfigVersion, changed.GuildState.Value.SharedConfigPatchesVersion, changed.GuildState.Value.TryGetNonEmptyExperimentAssignment());
                _waitingGuildJoinCompletion = _pendingGuildJoinCompletion;
                _pendingGuildJoinCompletion = null;
            }
        }

        void DisposeViewByChannelId(int channelId)
        {
            // already closed?
            if (!_guildViews.Remove(channelId, out ForeignGuildContext context))
                return;

            _services.MessageDispatcher.SendMessage(new GuildEndViewRequest(context.ChannelId));
        }

        /// <summary>
        /// Creates the Model state from the protocol initialization message.
        /// </summary>
        IGuildModelBase DefaultDeserializeModel(EntitySerializedState state, ISharedGameConfig gameConfig)
        {
            IGameConfigDataResolver             resolver    = gameConfig;
            IGuildModelBase                     model       = (IGuildModelBase)state.PublicState.Deserialize(resolver, state.LogicVersion);
            MultiplayerMemberPrivateStateBase   memberState = null;
            if (!state.MemberPrivateState.IsEmpty)
                memberState = state.MemberPrivateState.Deserialize(resolver, _logicVersion);
            if (memberState != null)
                memberState.ApplyToModel(model);

            model.LogicVersion  = state.LogicVersion;
            model.GameConfig    = gameConfig;
            model.Log           = _log;

            return model;
        }

        protected virtual GuildClientContext CreateClientContext(LogChannel log, EntityId playerId, IGuildModelBase model, int channelId, int currentOperation, ITimelineHistory timelineHistory, Func<MetaMessage, bool> sendMessageToServer, bool enableConsistencyChecks)
        {
            return new GuildClientContext(
                log,
                playerId,
                model,
                channelId,
                currentOperation,
                timelineHistory,
                sendMessageToServer,
                enableConsistencyChecks);
        }

        protected virtual ForeignGuildContext CreateForeignGuildContext(IGuildModelBase model, int channelId)
        {
            return new ForeignGuildContext(this, model, channelId);
        }

        void ActivateGuildWithState(EntitySerializedState state, int guildChannelId, List<GuildTimelineUpdateMessage> guildUpdatesMaybe, ISharedGameConfig gameConfig)
        {
            IGuildModelBase model = DefaultDeserializeModel(state, gameConfig);

            GuildContext = CreateClientContext(
                _log,
                _playerId,
                model,
                guildChannelId,
                state.CurrentOperation,
                _services.TimelineHistory,
                _services.MessageDispatcher.SendMessage,
                _playerConsistencyChecks
                );
            GuildContext.SetClientListeners(_applyClientListeners);
            _guildChannelId = guildChannelId;

            // \todo: should run these without side-effects (log, listeners?)
            if (guildUpdatesMaybe != null)
            {
                foreach (GuildTimelineUpdateMessage update in guildUpdatesMaybe)
                {
                    if (update.GuildChannelId != _guildChannelId)
                        continue;

                    // \todo: better error state
                    bool success = GuildContext.HandleGuildTimelineUpdateMessage(update);
                    if (!success)
                        _services.DefaultHandleEntityTimelineUpdateFailed();
                }
            }

            Phase = GuildClientPhase.GuildActive;
            PhaseChanged?.Invoke();
        }

        void OnGuildTimelineUpdateMessage(GuildTimelineUpdateMessage msg)
        {
            // spool messages while waiting for configs.
            if (_waitingConfigs != null)
            {
                _waitedOps.Add(msg);
                return;
            }

            if (GuildContext == null)
                return;

            if (msg.GuildChannelId != _guildChannelId)
                return;

            // \todo: better error state
            bool success = GuildContext.HandleGuildTimelineUpdateMessage(msg);
            if (!success)
                _services.DefaultHandleEntityTimelineUpdateFailed();

            ActiveGuildUpdated?.Invoke();
        }

        /// <summary>
        /// Starts execution of a transaction by executing initiating action and enqueuing execution of the rest of the transaction.
        /// </summary>
        /// <param name="logOnAbort">if set, warning will be printed for planning or initiating action execution failure</param>
        public void ExecuteGuildTransaction(IGuildTransaction transaction, bool logOnAbort = true)
        {
            IPlayerClientContext playerContext = _clientStore.GetPlayerClientContext();
            playerContext.FlushActions();
            GuildContext.FlushActions();

            transaction.InvokingPlayerId = _playerId;

            ITransactionPlan playerPlan;
            try
            {
                playerPlan = transaction.PlanForPlayer(playerContext.Journal.CheckpointModel);
            }
            catch (TransactionPlanningFailure)
            {
                // aborted, nothing to do
                if (logOnAbort)
                    _log.Warning("ExecuteGuildTransaction aborted, got TranssactionPlanningFailure while PlanForPlayer");
                return;
            }

            var initiatingAction = transaction.CreateInitiatingPlayerAction(playerPlan);
            if (initiatingAction != null)
            {
                var initiatingResult = playerContext.DryExecuteAction(initiatingAction);
                if (initiatingResult != MetaActionResult.Success)
                {
                    // aborted, nothing to do
                    if (logOnAbort)
                        _log.Warning("ExecuteGuildTransaction aborted, initating action did not run successfully. Got: {Result}", initiatingResult);
                    return;
                }

                // mark the initiating action as sent
                playerContext.ExecuteAction(initiatingAction);
                playerContext.MarkPendingActionsAsFlushed();
            }

            _services.MessageDispatcher.SendMessage(new GuildTransactionRequest(new MetaSerialized<IGuildTransaction>(transaction, MetaSerializationFlags.SendOverNetwork, logicVersion: _logicVersion), _guildChannelId));
        }

        void OnGuildTransactionResponse(GuildTransactionResponse txnResponse)
        {
            GuildActionBase guildAction = null;
            if (!txnResponse.GuildAction.IsEmpty)
                guildAction = txnResponse.GuildAction.Deserialize(resolver: null, logicVersion: _logicVersion);

            PlayerTransactionFinalizingActionBase playerAction = null;
            if (!txnResponse.PlayerAction.IsEmpty)
                playerAction = txnResponse.PlayerAction.Deserialize(resolver: null, logicVersion: _logicVersion);

            if (GuildContext != null)
            {
                // Losing context is completely ok during a Transaction. Player could leave the guild for example.
                // In that case, the guild is modified as normal but we are not just interested in it.
                if (guildAction != null)
                {
                    guildAction.InvokingPlayerId = _playerId;
                    GuildContext.HandleGuildTransactionResponse(guildAction);
                }
            }

            IPlayerClientContext playerContext = _clientStore.GetPlayerClientContext();
            if (playerContext != null)
            {
                // Losing player context is weird but ok.
                if (playerAction != null)
                    playerContext.HandleGuildTransactionResponse(playerAction, txnResponse.PlayerActionTrackingId);
            }

            ActiveGuildUpdated?.Invoke();
        }

        /// <summary>
        /// Starts a revokation of an existing guild invite. Upon receipt of the revokation request, server
        /// revokes the invite, and removes the invite from the guild member's Invites. Invalid and stale requests
        /// are ignored.
        /// <para>
        /// Phase must be <see cref="GuildClientPhase.GuildActive"/>, otherwise throws <see cref="InvalidOperationException" />.
        /// </para>
        /// </summary>
        public void RevokeGuildInvite(int inviteId)
        {
            if (Phase != GuildClientPhase.GuildActive)
                throw new InvalidOperationException($"guild phase must be GuildActive, got {Phase}");

            _services.MessageDispatcher.SendMessage(new GuildRevokeInvitationRequest(inviteId));
        }

        /// <summary>
        /// Requests server to create an invitation code to the current guild. On success, a new invitation code is created
        /// and added to <see cref="GuildMemberBase.Invites"/> and then <paramref name="onResponse"/> is called.
        /// <para>
        /// Phase must be <see cref="GuildClientPhase.GuildActive"/>, otherwise throws <see cref="InvalidOperationException" />.
        /// </para>
        /// <para>
        /// <paramref name="onResponse"/> will be called on Unity thread. If session is closed before response,
        /// uncalled callbacks are forgotten and will not be called. If guild is lost during the operation,
        /// <paramref name="onResponse"/> is called with a <see cref="GuildCreateInvitationResponse.StatusCode.NotAMember"/>
        /// failure code.
        /// </para>
        /// </summary>
        public void BeginCreateGuildInviteCode(MetaDuration? expirationDuration, int? usageLimit, Action<GuildCreateInvitationResult> onResponse)
        {
            if (Phase != GuildClientPhase.GuildActive)
                throw new InvalidOperationException($"guild phase must be GuildActive, got {Phase}");

            int queryId = _invitationQueryRunningId++;
            InviteCreationState pendingState = new InviteCreationState(GuildInviteType.InviteCode, expirationDuration, usageLimit ?? 0);
            pendingState.Completion.RegisterAction(onResponse);
            _pendingCreateInvitations[queryId] = pendingState;

            _services.MessageDispatcher.SendMessage(new GuildCreateInvitationRequest(queryId, pendingState.Type, pendingState.ExpirationDuration, pendingState.UsageLimit));
        }

        /// <summary>
        /// Requests server to create an invitation code to the current guild. On success, a new invitation code is created
        /// and added to <see cref="GuildMemberBase.Invites"/> and the function completes with a <see cref="GuildCreateInvitationResult"/>.
        /// <para>
        /// Phase must be <see cref="GuildClientPhase.GuildActive"/>, otherwise throws <see cref="InvalidOperationException" />.
        /// </para>
        /// <para>
        /// If session is closed before response, the returned task is Cancelled. If guild is lost during the operation,
        /// <paramref name="onResponse"/> the returned task completes with a <see cref="GuildCreateInvitationResponse.StatusCode.NotAMember"/>
        /// failure code.
        /// </para>
        /// </summary>
        public Task<GuildCreateInvitationResult> BeginCreateGuildInviteCode(MetaDuration? expirationDuration, int? usageLimit)
        {
            if (Phase != GuildClientPhase.GuildActive)
                throw new InvalidOperationException($"guild phase must be GuildActive, got {Phase}");

            int queryId = _invitationQueryRunningId++;
            InviteCreationState pendingState = new InviteCreationState(GuildInviteType.InviteCode, expirationDuration, usageLimit ?? 0);
            _pendingCreateInvitations[queryId] = pendingState;

            _services.MessageDispatcher.SendMessage(new GuildCreateInvitationRequest(queryId, pendingState.Type, pendingState.ExpirationDuration, pendingState.UsageLimit));
            return pendingState.Completion.GetTask();
        }

        void OnGuildCreateInvitationResponse(GuildCreateInvitationResponse response)
        {
            if (!_pendingCreateInvitations.Remove(response.QueryId, out InviteCreationState pendingCreation))
                return;
            // in illegal situations, don't complete the request. It is the safest thing to do.
            if (Phase != GuildClientPhase.GuildActive)
                return;
            if (!GuildContext.CommittedModel.TryGetMember(PlayerId, out GuildMemberBase selfMember))
                return;

            if (response.Status == GuildCreateInvitationResponse.StatusCode.RateLimited)
            {
                int                     queryId             = response.QueryId;
                EntityId                originalGuildId     = GuildContext.CommittedModel.GuildId;
                int                     originalInstanceId  = selfMember.MemberInstanceId;

                // Add back to the "pending" and try again with the same queryId after 1 second
                // Unless session has ended or the current guild has changed.

                _pendingCreateInvitations.Add(queryId, pendingCreation);

                RunAfter(delay: MetaDuration.FromSeconds(1), () =>
                {
                    if (Phase != GuildClientPhase.GuildActive)
                        return;
                    if (GuildContext.CommittedModel.GuildId != originalGuildId)
                        return;
                    if (!GuildContext.CommittedModel.TryGetMember(PlayerId, out GuildMemberBase newSelfMember))
                        return;
                    if (newSelfMember.MemberInstanceId != originalInstanceId)
                        return;

                    _services.MessageDispatcher.SendMessage(new GuildCreateInvitationRequest(queryId, pendingCreation.Type, pendingCreation.ExpirationDuration, pendingCreation.UsageLimit));
                });
                return;
            }

            GuildCreateInvitationResult result;
            if (response.Status == GuildCreateInvitationResponse.StatusCode.Success)
            {
                if (!selfMember.Invites.TryGetValue(response.InviteId, out GuildInviteState inviteState))
                    return;

                result = new GuildCreateInvitationResult(
                    type:           pendingCreation.Type,
                    status:         GuildCreateInvitationResponse.StatusCode.Success,
                    inviteId:       response.InviteId,
                    inviteState:    inviteState);
            }
            else
            {
                result = new GuildCreateInvitationResult(
                    type:           pendingCreation.Type,
                    status:         response.Status,
                    inviteId:       0,
                    inviteState:    null);
            }
            pendingCreation.Completion.Dispatch(result);
        }

        /// <summary>
        /// Requests server to inspect the contents of an invitation code. On success, <paramref name="onResponse"/> is
        /// called with the retrieved information. On failure, <c>null</c> is passed <paramref name="onResponse"/> instead.
        /// <para>
        /// On completion, <paramref name="onResponse"/> will be called on Unity thread. If session is closed before response,
        /// uncalled callbacks are forgotten and will not be called.
        /// </para>
        /// </summary>
        public void BeginInspectGuildInviteCode(GuildInviteCode inviteCode, Action<GuildInviteInfo> onResponse)
        {
            int queryId = _inspectGuildInviteCodeQueryRunningId++;
            InspectGuildInviteCodeState pendingState = new InspectGuildInviteCodeState(inviteCode);
            pendingState.Completion.RegisterAction(onResponse);
            _pendingInspectGuildInviteCode[queryId] = pendingState;

            _services.MessageDispatcher.SendMessage(new GuildInspectInvitationRequest(queryId, inviteCode));
        }

        /// <summary>
        /// Requests server to inspect the contents of an invitation code. On success, returned task completes
        /// with the retrieved information. On failure, the returned task completed with <c>null</c> instead.
        /// <para>
        /// If session is closed before response, the returned task is Cancelled.
        /// </para>
        /// </summary>
        public Task<GuildInviteInfo> BeginInspectGuildInviteCode(GuildInviteCode inviteCode)
        {
            int queryId = _inspectGuildInviteCodeQueryRunningId++;
            InspectGuildInviteCodeState pendingState = new InspectGuildInviteCodeState(inviteCode);
            _pendingInspectGuildInviteCode[queryId] = pendingState;

            _services.MessageDispatcher.SendMessage(new GuildInspectInvitationRequest(queryId, inviteCode));
            return pendingState.Completion.GetTask();
        }

        void OnGuildInspectInvitationResponse(GuildInspectInvitationResponse response)
        {
            if (!_pendingInspectGuildInviteCode.Remove(response.QueryId, out InspectGuildInviteCodeState pendingInspect))
                return;

            if (response.Status == GuildInspectInvitationResponse.StatusCode.RateLimited)
            {
                int queryId = response.QueryId;

                // Add back to the "pending" and try again with the same queryId after 1 second
                // Unless session has ended.

                _pendingInspectGuildInviteCode.Add(queryId, pendingInspect);

                RunAfter(delay: MetaDuration.FromSeconds(1), () =>
                {
                    _services.MessageDispatcher.SendMessage(new GuildInspectInvitationRequest(queryId, pendingInspect.InviteCode));
                });
                return;
            }

            if (response.Status == GuildInspectInvitationResponse.StatusCode.Success)
            {
                GuildDiscoveryInfoBase discoveryInfo = response.GuildDiscoveryInfo.Deserialize(null, _logicVersion);
                GuildInviterAvatarBase inviterAvatar = response.InviterAvatar.Deserialize(null, _logicVersion);

                GuildInviteInfo inviteInfo = new GuildInviteInfo(
                    guildId:        discoveryInfo.GuildId,
                    inviteId:       response.InviteId,
                    guildInfo:      discoveryInfo,
                    inviterAvatar:  inviterAvatar,
                    inviteCode:     pendingInspect.InviteCode);
                pendingInspect.Completion.Dispatch(inviteInfo);
            }
            else
            {
                pendingInspect.Completion.Dispatch(null);
            }
        }

        void RunAfter(MetaDuration delay, Action action)
        {
            _delayedActions.Add(new DelayedAction(runAt: MetaTime.Now + delay, action));
        }

        /// <summary>
        /// Returns true if there is a guild view pending to the <paramref name="guildId"/>. Pending view means
        /// the state is not yet available, i.e. either server has not responded yet or the client is still loading
        /// game configs for the guild.
        /// </summary>
        public bool HasPendingGuildView(EntityId guildId)
        {
            foreach (GuildViewRequestState pendingView in _pendingGuildViews.Values)
            {
                if (pendingView.GuildId == guildId)
                    return true;
            }
            return false;
        }

        internal void OnForeignGuildContextDisposed(ForeignGuildContext foreignGuild)
        {
            DisposeViewByChannelId(foreignGuild.ChannelId);
        }

        public void SetClientListeners(Action<IGuildModelBase> applyFn)
        {
            _applyClientListeners = applyFn;
            GuildContext?.SetClientListeners(applyFn);
        }
    }

    /// <summary>
    /// Client for a guild entity. Convenience wrapper of <see cref="GuildClient"/>.
    /// </summary>
    public class GuildClient<TGuildModel> : GuildClient
        where TGuildModel : IGuildModelBase
    {
        public new IGuildClientContext<TGuildModel> GuildContext => (IGuildClientContext<TGuildModel>)base.GuildContext;

        protected override GuildClientContext CreateClientContext(LogChannel log, EntityId playerId, IGuildModelBase model, int channelId, int currentOperation, ITimelineHistory timelineHistory, Func<MetaMessage, bool> sendMessageToServer, bool enableConsistencyChecks)
        {
            return new GuildClientContext<TGuildModel>(
                log,
                playerId,
                (TGuildModel)model,
                channelId,
                currentOperation,
                timelineHistory,
                sendMessageToServer,
                enableConsistencyChecks);
        }

        protected override ForeignGuildContext CreateForeignGuildContext(IGuildModelBase model, int channelId)
        {
            return new ForeignGuildContext<TGuildModel>(this, (TGuildModel)model, channelId);
        }

        /// <inheritdoc cref="GuildClient.BeginViewGuild(EntityId)"/>
        public new async Task<ForeignGuildContext<TGuildModel>> BeginViewGuild(EntityId guildId)
        {
            ForeignGuildContext context = await base.BeginViewGuild(guildId);
            return (ForeignGuildContext<TGuildModel>)context;
        }

        /// <inheritdoc cref="GuildClient.BeginViewGuild(EntityId, Action{ForeignGuildContext})"/>
        public void BeginViewGuild(EntityId guildId, Action<ForeignGuildContext<TGuildModel>> onResponse)
        {
            base.BeginViewGuild(guildId, onResponse: (context) =>
            {
                onResponse?.Invoke((ForeignGuildContext<TGuildModel>)context);
            });
        }
    }
}

#endif
