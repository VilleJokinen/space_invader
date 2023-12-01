// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core.Message
{
    /// <summary>
    /// Handler for <see cref="IMessageDispatcher"/>.
    /// </summary>
    public delegate void MessageHandler<T>(T message) where T : MetaMessage;

    /// <summary>
    /// Dispatcher class for <see cref="MetaMessage"/>s. Classes can register their own
    /// handler methods by message type. These handlers are then invoked when a message of
    /// the given type is received from the server.
    /// </summary>
    public interface IMessageDispatcher
    {
        /// <summary>
        /// Adds <paramref name="handlerFunc"/> into the set of <typeparamref name="T"/> message handlers.
        /// All message handlers for a message type are invoked. If handler is registered multiple times,
        /// it will be invoked multiple times.
        /// </summary>
        void AddListener<T>(MessageHandler<T> handlerFunc) where T : MetaMessage;

        /// <summary>
        /// Removes the <paramref name="handlerFunc"/> from the registered message handlers. If handler is registered
        /// multiple times, only one occurence is removed.
        /// </summary>
        void RemoveListener<T>(MessageHandler<T> handlerFunc) where T : MetaMessage;

        /// <summary>
        /// Sends message to the server.
        /// </summary>
        bool SendMessage(MetaMessage message);

        /// <summary>
        /// Send a request to server expecting a response.
        /// </summary>
        Task<T> SendRequestAsync<T>(MetaRequest request, CancellationToken ct) where T : MetaResponse;
        /// <inheritdoc cref="SendRequestAsync(MetaRequest, CancellationToken)"/>
        Task<T> SendRequestAsync<T>(MetaRequest request) where T : MetaResponse;
    }
}
