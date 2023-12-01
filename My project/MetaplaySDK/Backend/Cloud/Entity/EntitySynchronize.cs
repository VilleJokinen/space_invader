// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Serialization;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Entity.Synchronize
{
    public class EntitySynchronizationError : Exception
    {
        public EntitySynchronizationError(string message) : base(message)
        {
        }
    }

    interface IEntitySynchronizeCallbacks
    {
        void EntitySynchronizeSend(EntitySynchronize state, MetaMessage message);
        void EntitySynchronizeDispose(EntitySynchronize state);
    }

    /// <summary>
    /// A context object for EntitySynchronize operations.
    ///
    /// <para>
    /// EntitySynchronize establishes a synchronized execution context between two entities
    /// based on message passing. This allows to two enties to synchronize their execution
    /// by performing I-Go-You-Go, We-Go, or any other phase-based execution model without
    /// having to process entities' message queues. Avoiding the message queue allows both
    /// peers to have consistent, predictable and synchronized state during the execution.
    /// </para>
    ///
    /// <para>
    /// Within an EntitySynchronize, all messages are delivered in a separate message channel.
    /// Any messages sent outside this channel, for example normal CastMessages, are queued
    /// for later execution and hence cannot interfere or mutate the state of either peer
    /// of the EntitySynchronize.
    /// </para>
    /// </summary>
    public sealed class EntitySynchronize : IDisposable
    {
        IEntitySynchronizeCallbacks                 _owner;
        int                                         _channelId;
        EntityId                                    _selfEntityId;
        EntityId                                    _targetEntityId;
        ChannelReader<MetaSerialized<MetaMessage>>  _mailbox;
        Stopwatch                                   _sw;
        string                                      _label;
        bool                                        _isCaller; // true when this end initiated the Sync. False if received it.

        bool IsDisposed => _owner == null;

        public int      ChannelId   => _channelId;
        public TimeSpan Elapsed     => _sw.Elapsed;
        public string   Label       => _label;
        public bool     IsCaller    => _isCaller;
        public EntityId EntityId    => _targetEntityId;

        internal EntitySynchronize(
            IEntitySynchronizeCallbacks                 owner,
            EntityId                                    selfEntityId,
            EntityId                                    targetEntityId,
            int                                         channelId,
            ChannelReader<MetaSerialized<MetaMessage>>  mailbox,
            Stopwatch                                   sw,
            string                                      label,
            bool                                        isCaller
            )
        {
            _owner = owner;
            _selfEntityId = selfEntityId;
            _targetEntityId = targetEntityId;
            _channelId = channelId;
            _sw = sw;
            _mailbox = mailbox;
            _label = label;
            _isCaller = isCaller;
        }

        /// <inheritdoc cref="ReceiveAsync{T}(TimeSpan)"/>
        public Task<T> ReceiveAsync<T>() where T : MetaMessage => ReceiveAsync<T>(TimeSpan.FromSeconds(5));

        /// <summary>
        /// Receives a single message sent by the other peer with <see cref="Send(MetaMessage)"/>. Messages
        /// are read in order. If the received message is not of type <typeparamref name="T"/> or if the
        /// Synchronization was disposed by the remote peer, a <see cref="EntitySynchronizationError"/> is thrown.
        /// If timeout is reached without receiving message or channel being closed, <see cref="TimeoutException"/>
        /// is thrown.
        /// <para>
        /// Default timeout is 5 seconds.
        /// </para>
        /// </summary>
        public async Task<T> ReceiveAsync<T>(TimeSpan timeout) where T : MetaMessage
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(EntitySynchronize));

            MetaSerialized<MetaMessage> messageBlob;

            if (!_mailbox.TryRead(out messageBlob))
            {
                using CancellationTokenSource cts = new CancellationTokenSource();

                cts.CancelAfter(timeout);

                try
                {
                    bool canRead = await _mailbox.WaitToReadAsync(cts.Token).ConfigureAwait(false);
                    if (!canRead)
                        throw new EntitySynchronizationError("Receive from closed synchronization channel");

                    if (!_mailbox.TryRead(out messageBlob))
                        throw new InvalidOperationException("Channel may not fail to TryRead after successful WaitToReadAsync");
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException($"Timeout during EntitySynchronize.Receive<{typeof(T).Name}> in {_selfEntityId} to {_targetEntityId}");
                }
            }

            MetaMessage message = messageBlob.Deserialize(resolver: null, logicVersion: null);
            if (message is T typedMessage)
                return typedMessage;
            throw new EntitySynchronizationError($"Expected {typeof(T).GetNestedClassName()} but got {message.GetType().GetNestedClassName()} from synchronization channel");
        }

        /// <summary>
        /// Sends a message to the other peer for consumption via <see cref="Receive{T}"/>. The message is delivered
        /// at most once, and in order. If a message was delivered, all preceeding messages in this channel were also
        /// delivered. To make sure a delivery of a certain message succeedeed, make the other peer Ack it and listen
        /// for the Ack.
        /// </summary>
        public void Send(MetaMessage message)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(EntitySynchronize));
            _owner.EntitySynchronizeSend(this, message);
        }

        /// <summary>
        /// Closes the message channel cleanly. After all preceeding message have been processed, any ReceiveAsync by
        /// the remote peer will result in <see cref="EntitySynchronizationError"/>.
        /// </summary>
        public void Dispose()
        {
            _owner?.EntitySynchronizeDispose(this);
            _owner = null;
        }
    }

}
