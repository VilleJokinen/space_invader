// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Message;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Player;
using System;
using System.Threading.Tasks;

namespace Metaplay.Core.MultiplayerEntity
{
    public interface IMultiplayerEntityClient : IEntitySubClient
    {
        new IMultiplayerEntityClientContext Context { get; }
    }

    /// <summary>
    /// Phase of a <see cref="MultiplayerEntityClientBase{TModel}"/> client. See values for details.
    /// </summary>
    public enum MultiplayerEntityClientPhase
    {
        /// <summary>
        /// There is no active Metaplay connection.
        /// </summary>
        NoSession = 0,

        /// <summary>
        /// The client has currently no active entity.
        /// </summary>
        NoEntity,

        /// <summary>
        /// The client is connected to an Entity on the server but the data is not yet
        /// available on the client. The client is currently downloading the required data and
        /// Entity will become available when it completes.
        /// </summary>
        LoadingEntity,

        /// <summary>
        /// The client is connected to an Entity.
        /// </summary>
        EntityActive
    }

    /// <summary>
    /// Generic client for multiplayer entities. See also convenience helper <see cref="MultiplayerEntityClientBase{TModel}"/>
    /// </summary>
    public abstract class MultiplayerEntityClientBase : IMultiplayerEntityClient
    {
        /// <summary>
        /// The <see cref="Metaplay.Core.Client.ClientSlot"/> of this client.
        /// The ClientSlot is used to direct the initial entity state from server
        /// to the correct client, and as an unique identifier for this client.
        /// </summary>
        public abstract ClientSlot ClientSlot { get; }

        /// <summary>
        /// Name used for the model's log channel.
        /// </summary>
        protected abstract string LogChannelName { get; }

        /// <summary>
        /// Currently active entity model, or <c>null</c> if there is no active entity, i.e
        /// <see cref="Phase"/> is not <see cref="MultiplayerEntityClientPhase.EntityActive"/>.
        /// </summary>
        public IMultiplayerModel Model => Context?.CommittedModel;

        /// <summary>
        /// The client context for the current entity, or <c>null</c> if no active entity.
        /// </summary>
        public IMultiplayerEntityClientContext Context { get; private set; }

        /// <summary>
        /// The client environment services of the current client. This is different for Unity and
        /// Bot Clients.
        /// </summary>
        protected IMetaplaySubClientServices Services { get; private set; }

        /// <summary>
        /// Current phase of the connection. See values for details.
        /// </summary>
        public MultiplayerEntityClientPhase Phase { get; private set; }

        /// <summary>
        /// Invoked after <see cref="Phase"/> changes. This is invoked on Unity thread on Unity Client and on Actor thread on bot client.
        /// </summary>
        public event Action PhaseChanged;

        /// <summary>
        /// Invoked after Model (in Context) changes. This is mostly useful for debugging and Editor inspector
        /// </summary>
        public event Action ModelUpdated;

        protected LogChannel _log;
        protected IMessageDispatcher _rootMessageDispatcher;
        protected EntityMessageDispatcher _entityMessageDispatcher;

        protected EntityId _playerId;
        protected bool _enableConsistencyChecks;
        protected int _logicVersion;

        Task<ISharedGameConfig> _waitingConfigs;
        EntityInitialState _waitingState;

        Action<IMultiplayerModel> _applyClientListeners;

        /// <inheritdoc />
        public virtual void Initialize(IMetaplaySubClientServices clientServices)
        {
            Services = clientServices;
            _log = clientServices.CreateLogChannel(LogChannelName);
            _rootMessageDispatcher = Services.MessageDispatcher;

            _rootMessageDispatcher.AddListener<EntitySwitchedMessage>(OnEntitySwitchedMessage);

            _entityMessageDispatcher = new EntityMessageDispatcher(_log, _rootMessageDispatcher);
            _entityMessageDispatcher.ResetPeer();
        }

        public virtual void Dispose()
        {
            _rootMessageDispatcher.RemoveListener<EntitySwitchedMessage>(OnEntitySwitchedMessage);
            _entityMessageDispatcher.Dispose();
        }

        /// <inheritdoc />
        public virtual void OnSessionStart(SessionProtocol.SessionStartSuccess successMessage, ClientSessionStartResources sessionStartResources)
        {
            _playerId                = successMessage.PlayerId;
            _enableConsistencyChecks = Services.EnableConsistencyChecks;
            _logicVersion            = -1;
            _entityMessageDispatcher.ResetPeer();

            int initialEntityIndex = -1;
            for (int ndx = 0; ndx < successMessage.EntityStates.Count; ++ndx)
            {
                if (!ShouldAttachToNewEntityChannel(successMessage.EntityStates[ndx]))
                    continue;
                if (initialEntityIndex != -1)
                    throw new InvalidOperationException($"Entity client {GetType().ToGenericTypeString()} has ambiguous initial entity. Could be either index {initialEntityIndex} or {ndx}.");
                initialEntityIndex = ndx;
            }

            if (initialEntityIndex != -1)
            {
                ActivateWithState(successMessage.EntityStates[initialEntityIndex], sessionStartResources.GameConfigs[ClientSlot]);
            }
            else
            {
                Phase   = MultiplayerEntityClientPhase.NoEntity;
                Context = null;
                InvokePhaseChanged();
            }
        }

        /// <summary>
        /// Should be called when session is stopped.
        /// </summary>
        public virtual void OnSessionStop()
        {
            _entityMessageDispatcher.ResetPeer();

            Phase = MultiplayerEntityClientPhase.NoSession;
            Context = null;
            InvokePhaseChanged();

            _waitingConfigs = null;
            _waitingState = default;
        }

        /// <inheritdoc />
        public virtual void EarlyUpdate()
        {
            // If waited configs complete, try to materialize it.
            if (_waitingConfigs != null && _waitingConfigs.IsCompleted)
            {
                if (_waitingConfigs.IsFaulted)
                {
                    Exception configLoadError = _waitingConfigs.Exception;
                    OnConfigFetchFailed(configLoadError);
                    _entityMessageDispatcher.ResetPeer();
                }
                else if (_waitingConfigs.Status == TaskStatus.RanToCompletion)
                {
                    _rootMessageDispatcher.SendMessage(new EntityActivated(_waitingState.ChannelId));
                    ActivateWithState(_waitingState, _waitingConfigs.GetCompletedResult());
                }
                else
                {
                    // cancelled. Ignored.
                    _entityMessageDispatcher.ResetPeer();
                }

                _waitingConfigs = null;
                _waitingState = default;
            }
        }

        /// <summary>
        /// Should be called just after network update.
        /// </summary>
        public virtual void UpdateLogic(MetaTime timeNow)
        {
            Context?.Update(timeNow);
        }

        public void FlushPendingMessages()
        {
            Context?.FlushActions();
        }

        void InvokePhaseChanged()
        {
            try
            {
                PhaseChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _log.Warning("Failed to executed PhaseChanged: {Error}", ex.ToString());
            }
        }

        void InvokeModelUpdated()
        {
            try
            {
                ModelUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                _log.Warning("Failed to executed ModelUpdated: {Error}", ex.ToString());
            }
        }

        /// <summary>
        /// Creates the Model state from the protocol initialization message.
        /// </summary>
        protected IMultiplayerModel DefaultDeserializeModel(EntitySerializedState state, ISharedGameConfig gameConfig)
        {
            IGameConfigDataResolver     resolver    = gameConfig;
            IMultiplayerModel           model       = state.PublicState.Deserialize(resolver, state.LogicVersion);
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

        /// <summary>
        /// Creates the deafult <see cref="ClientMultiplayerEntityContextInitArgs"/> for a newly created context.
        /// </summary>
        protected ClientMultiplayerEntityContextInitArgs DefaultInitArgs(IMultiplayerModel model, EntityInitialState state)
        {
            return new ClientMultiplayerEntityContextInitArgs(_log, model, state.State.CurrentOperation, _playerId, _entityMessageDispatcher, _enableConsistencyChecks, Services, this);
        }

        void ActivateWithState(EntityInitialState state, ISharedGameConfig gameConfig)
        {
            _logicVersion = state.State.LogicVersion;
            _entityMessageDispatcher.SetPeer(state.ChannelId, state.State.LogicVersion, gameConfig);

            Context = CreateUntypedActiveModelContext(state, gameConfig);
            Context?.SetClientListeners(_applyClientListeners);

            // Playback all updates received
            _entityMessageDispatcher.FlushPeerBuffer();

            OnActivatingModel();
            Phase = MultiplayerEntityClientPhase.EntityActive;
            InvokePhaseChanged();
            OnActivatedModel();
        }

        void OnEntitySwitchedMessage(EntitySwitchedMessage switched)
        {
            bool wasDetached = false;

            // Detaching the current entity?
            if (switched.OldChannelId != -1 && switched.OldChannelId == _entityMessageDispatcher.ChannelId)
            {
                if (Context != null)
                {
                    // Active entity was detached
                    Phase = MultiplayerEntityClientPhase.NoEntity;
                    Context = null;
                    InvokePhaseChanged();
                }
                else if (_waitingConfigs != null)
                {
                    Phase = MultiplayerEntityClientPhase.NoEntity;
                    InvokePhaseChanged();

                    // Pending entity was detached. Just forget.
                    _waitingConfigs = null;
                    _waitingState = default;
                    _entityMessageDispatcher.ResetPeer();
                }

                wasDetached = true;
            }

            // Attach to new
            if (switched.NewState != null && (wasDetached || ShouldAttachToNewEntityChannel(switched.NewState)))
            {
                Phase = MultiplayerEntityClientPhase.LoadingEntity;
                Context = null;
                InvokePhaseChanged();

                // Start downloading the config and buffer all updates while at it.
                _waitingConfigs = GetConfigAsync(
                    configVersion:          switched.NewState.State.SharedGameConfigVersion,
                    patchesVersion:         switched.NewState.State.SharedConfigPatchesVersion,
                    experimentAssignment:   switched.NewState.State.TryGetNonEmptyExperimentAssignment());
                _waitingState = switched.NewState;
                _entityMessageDispatcher.StartPeerBuffering(switched.NewState.ChannelId);
            }
        }

        public void OnDisconnected()
        {
            Context?.OnDisconnected();
        }

        /// <summary>
        /// Returns true if this client should attach to the new channel starting with <paramref name="initialState"/> state.
        /// Conventional implementation will look into <see cref="EntityInitialState.ClientSlot"/> or <see cref="EntityInitialState.State"/>
        /// to determine if the type matches the client type.
        /// </summary>
        protected virtual bool ShouldAttachToNewEntityChannel(EntityInitialState initialState) =>
            initialState.ContextData.ClientSlot == ClientSlot;

        /// <summary>
        /// Called if config loading fails for an entity that was attached during session time. Entity cannot be loaded. If this is not tolerable, implementation should close the connection
        /// with <c>ConnectionStates.TransientError.ConfigFetchFailed</c> using <c>MetaplaySDK.Connection.CloseWithError</c> in Unity, or by just throwing in bot client.
        /// Note that config load fails during session start are handled specially.
        /// Default implementation delegates this to <c>Services.DefaultHandleConfigFetchFailed</c>.
        /// See also: <seealso cref="IMetaplaySubClientServices.DefaultHandleConfigFetchFailed(Exception)"/>
        /// </summary>
        protected virtual void OnConfigFetchFailed(Exception configLoadError)
        {
            Services.DefaultHandleConfigFetchFailed(configLoadError);
        }

        /// <summary>
        /// Called if applying a timeline update fails for an entity. Entity is desynchronized and cannot continue to exist. If this is not tolerable, implementation should close the connection
        /// with <c>ConnectionStates.TerminalError.Unknown</c> using <c>MetaplaySDK.Connection.CloseWithError</c> in Unity, or by just throwing in bot client.
        /// Default implementation delegates this to <c>Services.DefaultHandleEntityTimelineUpdateFailed</c>.
        /// See also: <seealso cref="IMetaplaySubClientServices.DefaultHandleEntityTimelineUpdateFailed"/>
        /// </summary>
        public virtual void OnTimelineUpdateFailed()
        {
            Services.DefaultHandleEntityTimelineUpdateFailed();
        }

        /// <summary>
        /// Called Entity has advanced on Server issued Timeline.
        /// </summary>
        public virtual void OnAdvancedOnTimeline()
        {
            InvokeModelUpdated();
        }

        /// <summary>
        /// Fetches the config from cache or downloads it. Default implementation delegates this to <c>Services.GetConfigAsync</c>.
        /// See also: <seealso cref="IMetaplaySubClientServices.GetConfigAsync(ContentHash, ContentHash, OrderedDictionary{PlayerExperimentId, ExperimentVariantId})"/>
        /// </summary>
        protected virtual Task<ISharedGameConfig> GetConfigAsync(ContentHash configVersion, ContentHash patchesVersion, OrderedDictionary<PlayerExperimentId, ExperimentVariantId> experimentAssignment)
        {
            return Services.GetConfigAsync(configVersion, patchesVersion, experimentAssignment);
        }

        /// <summary>
        /// Creates the client context for the model that is being activated. This is called before <see cref="Phase"/> is changed
        /// and before <see cref="OnActivatingModel"/> is called.
        /// </summary>
        protected abstract IMultiplayerEntityClientContext CreateUntypedActiveModelContext(EntityInitialState state, ISharedGameConfig gameConfig);

        /// <summary>
        /// Called after model is activated, but before <see cref="Phase"/> is changed and <see cref="PhaseChanged"/> is invoked.
        /// This can be used to prepare make internal state consistent before control is given to game even handler callbacks.
        /// </summary>
        protected virtual void OnActivatingModel() { }

        /// <summary>
        /// Called after model is activated, <see cref="Phase" /> is changed and <see cref="PhaseChanged"/> is invoked.
        /// This can be used to invoke custom events add some custom logic that needs to happen after model activation.
        /// </summary>
        protected virtual void OnActivatedModel() { }

        /// <summary>
        /// Sets client listener setter. The method is invoked for the current and any future model
        /// and it should only modify the client listeners.
        /// </summary>
        public void SetClientListeners(Action<IMultiplayerModel> applyFn)
        {
            _applyClientListeners = applyFn;
            Context?.SetClientListeners(applyFn);
        }

        IEntityClientContext IEntitySubClient.Context => Context;
    }

    /// <summary>
    /// Base class for managing the client-side of the Multiplayer Entity protocol. This is identical
    /// to <see cref="MultiplayerEntityClientBase{TModel, TContext}"/> using the default <see cref="MultiplayerEntityClientContext{TModel}"/>
    /// client context.
    /// </summary>
    public abstract class MultiplayerEntityClientBase<TModel> :
        MultiplayerEntityClientBase<TModel, MultiplayerEntityClientContext<TModel>>
        where TModel : class, IMultiplayerModel<TModel>
    {
    }

    /// <summary>
    /// Base class for managing the client-side of the Multiplayer Entity protocol.
    /// </summary>
    public abstract class MultiplayerEntityClientBase<TModel, TContext>
        : MultiplayerEntityClientBase
        where TModel : class, IMultiplayerModel<TModel>
        where TContext : MultiplayerEntityClientContext<TModel>
    {
        /// <summary>
        /// Currently active entity model, or <c>null</c> if there is no active entity, i.e
        /// <see cref="Phase"/> is not <see cref="MultiplayerEntityClientPhase.EntityActive"/>.
        /// </summary>
        public new TModel Model => Context?.CommittedModel;

        /// <summary>
        /// The client context for the current entity, or <c>null</c> if no active entity.
        /// </summary>
        public new TContext Context => (TContext)base.Context;

        /// <inheritdoc cref="MultiplayerEntityClientBase.CreateUntypedActiveModelContext"/>
        protected abstract TContext CreateActiveModelContext(EntityInitialState state, ISharedGameConfig gameConfig);
        protected sealed override IMultiplayerEntityClientContext CreateUntypedActiveModelContext(EntityInitialState state, ISharedGameConfig gameConfig) => CreateActiveModelContext(state, gameConfig);

        /// <inheritdoc cref="MultiplayerEntityClientBase.DefaultDeserializeModel"/>
        protected new TModel DefaultDeserializeModel(EntitySerializedState state, ISharedGameConfig gameConfig) => (TModel)base.DefaultDeserializeModel(state, gameConfig);
    }
}
