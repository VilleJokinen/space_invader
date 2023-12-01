// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Config;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.Core.Client
{
    public class MetaplayClientStore : IDisposable
    {
        readonly Func<IPlayerClientContext>                         _playerContextGetter;
        readonly OrderedDictionary<ClientSlot, IMetaplaySubClient>  _clients = new OrderedDictionary<ClientSlot, IMetaplaySubClient>();

        /// <summary>
        /// All registered sub clients.
        /// </summary>
        public IEnumerable<IMetaplaySubClient> AllClients => _clients.Values;

        public MetaplayClientStore(Func<IPlayerClientContext> playerContextGetter)
        {
            _playerContextGetter = playerContextGetter;
        }

        /// <summary>
        /// Get the registered <see cref="IPlayerClientContext"/>.
        /// </summary>
        public IPlayerClientContext GetPlayerClientContext()
        {
            return _playerContextGetter();
        }

        /// <summary>
        /// Get a <see cref="IMetaplaySubClient"/> for the provided <see cref="ClientSlot"/>.
        /// </summary>
        public TEntityClient TryGetClient<TEntityClient>(ClientSlot slot)
            where TEntityClient : class, IMetaplaySubClient
        {
            return (TEntityClient)_clients.GetValueOrDefault(slot);
        }

        /// <summary>
        /// Get a <see cref="IMetaplaySubClient"/> for the provided <see cref="ClientSlot"/>.
        /// </summary>
        public IMetaplaySubClient TryGetClient(ClientSlot slot)
        {
            return _clients.GetValueOrDefault(slot);
        }

        /// <summary>
        /// Get a <see cref="IEntityClientContext"/> of the provided <see cref="ClientSlot"/>. Will return <value>null</value>
        /// if nothing is registered for that slot.
        /// </summary>
        public TEntityContext TryGetEntityClientContext<TEntityContext>(ClientSlot slot)
            where TEntityContext : class, IEntityClientContext
        {
            if (slot == ClientSlotCore.Player)
                return (TEntityContext)_playerContextGetter();

            return (TEntityContext)(TryGetClient(slot) as IEntitySubClient)?.Context;
        }

        /// <summary>
        /// Get a <see cref="IEntityClientContext"/> of the provided <see cref="ClientSlot"/>. Will return <value>null</value>
        /// if nothing is registered for that slot.
        /// </summary>
        public IEntityClientContext TryGetEntityClientContext(ClientSlot slot)
        {
            if (slot == ClientSlotCore.Player)
                return _playerContextGetter();

            return (TryGetClient(slot) as IEntitySubClient)?.Context;
        }

        /// <summary>
        /// Add a new client to the store. Will mostly get called by the SDK.
        ///
        /// The game code path to registering clients is to add them to the
        /// MetaplayClientOptions during MetaplayClient.Initialize.
        /// </summary>
        /// <param name="client"></param>
        public void AddClient(IMetaplaySubClient client)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client), "Cannot add a null client!");

            _clients.Add(client.ClientSlot, client);
        }

        /// <summary>
        /// Call <see cref="IMetaplaySubClient.Initialize(IMetaplaySubClientServices)"/> on all registered clients.
        /// </summary>
        public void Initialize(IMetaplaySubClientServices clientServices)
        {
            foreach (IMetaplaySubClient client in _clients.Values)
                client.Initialize(clientServices);
        }

        /// <summary>
        /// Calls <see cref="IDisposable.Dispose"/> on all clients and clears own state.
        /// </summary>
        public void Dispose()
        {
            foreach (IMetaplaySubClient client in _clients.Values)
                client.Dispose();

            _clients.Clear();
        }

        /// <summary>
        /// Call <see cref="IMetaplaySubClient.EarlyUpdate"/> on all registered clients.
        /// </summary>
        public void EarlyUpdate()
        {
            foreach (IMetaplaySubClient client in _clients.Values)
                client.EarlyUpdate();
        }

        /// <summary>
        /// Call <see cref="IMetaplaySubClient.UpdateLogic"/> on all registered clients.
        /// </summary>
        public void UpdateLogic(MetaTime time)
        {
            foreach (IMetaplaySubClient client in _clients.Values)
                client.UpdateLogic(time);
        }

        /// <summary>
        /// Call <see cref="IMetaplaySubClient.OnSessionStart(SessionProtocol.SessionStartSuccess, ClientSessionStartResources)"/> on all registered clients.
        /// </summary>
        public void OnSessionStart(SessionProtocol.SessionStartSuccess success, ClientSessionStartResources resources)
        {
            foreach (IMetaplaySubClient client in _clients.Values)
                client.OnSessionStart(success, resources);
        }

        /// <summary>
        /// Call <see cref="IMetaplaySubClient.OnSessionStop"/> on all registered clients.
        /// </summary>
        public void OnSessionStop()
        {
            foreach (IMetaplaySubClient client in _clients.Values)
                client.OnSessionStop();
        }

        /// <summary>
        /// Call <see cref="IMetaplaySubClient.OnDisconnected"/> on all registered clients.
        /// </summary>
        public void OnDisconnected()
        {
            foreach (IMetaplaySubClient client in _clients.Values)
                client.OnDisconnected();
        }

        /// <summary>
        /// Call <see cref="IMetaplaySubClient.FlushPendingMessages"/> on all registered clients.
        /// </summary>
        public void FlushPendingMessages()
        {
            foreach (IMetaplaySubClient client in _clients.Values)
                client.FlushPendingMessages();
        }
    }

    /// <summary>
    /// <para>
    /// The <see cref="IMetaplaySubClient"/> interface is used as a base type for sub-clients
    /// that want to hook up into the update flow and message dispatching.
    /// The sub-client is responsible for registering its own message handlers in the <see cref="Initialize"/> callback,
    /// and removing them in <see cref="IDisposable.Dispose"/>.
    /// </para>
    /// <para>
    /// If a sub-client deals with an entity, it should inherit the <see cref="IEntitySubClient"/> interface instead.
    /// </para>
    /// </summary>
    public interface IMetaplaySubClient : IDisposable
    {
        ClientSlot ClientSlot { get; }

        /// <summary>
        /// Initialize is called after all the clients have been added to the store.
        /// Contexts may not be available yet, since they are only initialized after
        /// session start.
        /// </summary>
        void Initialize(IMetaplaySubClientServices clientServices);

        /// <summary>
        /// Called after receiving a <see cref="SessionProtocol.SessionStartSuccess"/>.
        /// </summary>
        void OnSessionStart(SessionProtocol.SessionStartSuccess successMessage, ClientSessionStartResources sessionStartResources);

        /// <summary>
        /// Called when the session is stopped.
        /// </summary>
        void OnSessionStop();

        /// <summary>
        /// Called when the connection is irrecoverably lost. This will lead to the loss of session and to <see cref="OnSessionStop"/>
        /// but only after game logic has processed the error. For example, UI may display error dialog to the user and only restart the
        /// connection after user has acknowledged the error. Between this call and <see cref="OnSessionStop"/>, the current session is stale
        /// and any changes made will be lost.
        /// </summary>
        void OnDisconnected();

        /// <summary>
        /// This update gets called right after <see cref="MetaplaySDK.Update"/>
        /// even before all the async resources have been loaded.
        /// </summary>
        void EarlyUpdate();

        /// <summary>
        /// This update gets called after <see cref="EarlyUpdate"/>,
        /// but does not guarantee that the session is established yet.
        /// The implementation should also call its own <see cref="IEntityClientContext.Update"/> if available.
        /// </summary>
        void UpdateLogic(MetaTime time);

        /// <summary>
        /// Flush all pending messages. If this sub client is an entity client, the implementation should also call
        /// its own <see cref="IEntityClientContext.FlushActions"/>.
        /// </summary>
        void FlushPendingMessages();
    }

    /// <summary>
    /// A <see cref="IMetaplaySubClient"/> that deals with an entity and has an <see cref="IEntityClientContext"/>.
    /// Any <see cref="IEntitySubClient"/> registered in the <see cref="MetaplayClientStore"/> can
    /// have its context accessed with <see cref="MetaplayClientStore.GetEntityClientContext"/>.
    /// </summary>
    public interface IEntitySubClient : IMetaplaySubClient
    {
        IEntityClientContext Context { get; }
    }

    /// <summary>
    /// The base interface for Entity client Context. Entity client context maintains the Entity's Model state and the context
    /// state required for the game client. Context manages updating Model state based on server and client input.
    /// </summary>
    public interface IEntityClientContext
    {
        /// <summary>
        /// Entity Client specific log channel. Messages logged into this channel will be prefixed with a client-specific name.
        /// </summary>
        LogChannel Log { get; }

        /// <summary>
        /// The model of this entity client.
        /// </summary>
        IModel Model { get; }

        /// <summary>
        /// Flushes to server any buffered client actions.
        /// </summary>
        void FlushActions();

        /// <param name="time">the current time</param>
        void Update(MetaTime time);
    }

    /// <summary>
    /// Interface defining the client environment services needed by <see cref="IMetaplaySubClient"/>. This is implemented
    /// by the Unity client and by the Bot Client.
    /// </summary>
    public interface IMetaplaySubClientServices
    {
        IMessageDispatcher MessageDispatcher { get; }
        MetaplayClientStore ClientStore { get; }
        ITimelineHistory TimelineHistory { get; }

        /// <summary>
        /// True if player model consistency checks are enabled.
        /// </summary>
        bool EnableConsistencyChecks { get; }

        /// <summary>
        /// Fetches optionally specialized GameConfig. If <paramref name="patchesVersion"/> is <c>None</c>, <paramref name="experimentAssignment"/> may be <c>null</c>.
        /// </summary>
        Task<ISharedGameConfig> GetConfigAsync(ContentHash configVersion, ContentHash patchesVersion, OrderedDictionary<PlayerExperimentId, ExperimentVariantId> experimentAssignment);

        /// <summary>
        /// Error handler for GameConfig loading failure.
        /// </summary>
        void DefaultHandleConfigFetchFailed(Exception configLoadError);

        /// <summary>
        /// Error handler for Entity Timeline update failures.
        /// </summary>
        void DefaultHandleEntityTimelineUpdateFailed();

        /// <summary>
        /// Creates the client-specific log channel.
        /// </summary>
        LogChannel CreateLogChannel(string name);
    }
}
