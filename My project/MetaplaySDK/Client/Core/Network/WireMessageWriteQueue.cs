// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Serialization;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core.Network
{
    class WireMessageWriteQueue
    {
        public class CloseEnqueuedException : Exception
        {
            public CloseEnqueuedException() : base()
            {
            }
        }
        public class WireMessageTooLargeException : Exception
        {
            public WireMessageTooLargeException(string message) : base(message)
            {
            }
        }

        public struct SendBufferRef
        {
            public readonly byte[] Buffer;
            public readonly int Start;
            public readonly int Length;
            public bool IsLastBufferRef;

            public SendBufferRef(byte[] buffer, int start, int length, bool isLastBufferRef)
            {
                Buffer = buffer;
                Start = start;
                Length = length;
                IsLastBufferRef = isLastBufferRef;
            }
        }
        public struct OutgoingMessage
        {
            public enum MessageKind
            {
                MetaMessage,
                Ping,
                Pong,
                Fence,
                Info,
                Close,
            }

            public SendBufferRef                SendBuffer;
            public MessageKind                  Kind;
            public TaskCompletionSource<int>    FenceCS;
            public MessageTransport.Info        Info;
            public object                       ClosePayload;

            public OutgoingMessage(SendBufferRef sendBuffer, MessageKind kind)
            {
                SendBuffer = sendBuffer;
                Kind = kind;
                FenceCS = null;
                Info = null;
                ClosePayload = null;
            }
            public OutgoingMessage(TaskCompletionSource<int> fenceCS)
            {
                SendBuffer = default;
                Kind = MessageKind.Fence;
                FenceCS = fenceCS;
                Info = null;
                ClosePayload = null;
            }
            public OutgoingMessage(MessageTransport.Info info)
            {
                SendBuffer = default;
                Kind = MessageKind.Info;
                FenceCS = null;
                Info = info;
                ClosePayload = null;
            }
            public OutgoingMessage(object closePayload)
            {
                SendBuffer = default;
                Kind = MessageKind.Close;
                FenceCS = null;
                Info = null;
                ClosePayload = closePayload;
            }
        }


        object                      _lock;
        SegmentedIOBuffer           _messageWriteBuffer;
        List<OutgoingMessage>       _outgoingMessages;
        byte[]                      _acquiredBuffer;
        SemaphoreSlim               _waiter;
        Task                        _unservedWaiter;
        bool                        _closeEnqueued;
        bool                        _disposed;
        bool                        _isAcquired;
        bool                        _enableCompression;

        public WireMessageWriteQueue()
        {
            _lock = new object();
            _messageWriteBuffer = new SegmentedIOBuffer(segmentSize: 4096);
            _outgoingMessages = new List<OutgoingMessage>();
            _waiter = new SemaphoreSlim(initialCount: 0, maxCount: 1);
        }

        public void EnableCompression(bool enableCompression)
        {
            _enableCompression = enableCompression;
        }

        /// <summary>
        /// Enqueues the message and returns the number of bytes written to buffer.
        /// </summary>
        /// <exception cref="CloseEnqueuedException">Close has been enqueued already</exception>
        /// <exception cref="ObjectDisposedException">Dispose has been called already</exception>
        /// <exception cref="WireMessageTooLargeException">Message size exceeds limits</exception>
        public int EnqueueMessage(MetaMessage message)
        {
            bool shouldSignalWaiter = false;
            int numBytesNeeded;

            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("WireMessageWriteQueue");
                if (_closeEnqueued)
                    throw new CloseEnqueuedException();

                // Serialize to a temporary buffer to measure size (and possibly compress).
                using (IOWriter writer = new IOWriter(_messageWriteBuffer, IOWriter.Mode.Truncate))
                {
                    MetaSerialization.SerializeTagged<MetaMessage>(writer, message, MetaSerializationFlags.SendOverNetwork, logicVersion: null);
                }
                int uncompressedPayloadSize = _messageWriteBuffer.Count;

                // Check uncompressed size limit
                if (uncompressedPayloadSize > WireProtocol.MaxPacketUncompressedPayloadSize)
                    throw new WireMessageTooLargeException($"Maximum packet uncompressed size exceeded for {message.GetType().Name} (size={uncompressedPayloadSize}, max={WireProtocol.MaxPacketUncompressedPayloadSize})");

                // Compress data if needed.
                WirePacketCompression compressionMode;
                if (_enableCompression && _messageWriteBuffer.Count >= WireProtocol.CompressionThresholdBytes)
                {
                    compressionMode = WirePacketCompression.Deflate;
                    byte[] compressed = CompressUtil.DeflateCompress(_messageWriteBuffer.ToArray());
                    using (IOWriter writer = new IOWriter(_messageWriteBuffer, IOWriter.Mode.Truncate))
                    {
                        writer.WriteBytes(compressed, 0, compressed.Length);
                    }
                }
                else
                {
                    compressionMode = WirePacketCompression.None;
                }

                // Check on-wire size limit
                if (_messageWriteBuffer.Count > WireProtocol.MaxPacketWirePayloadSize)
                {
                    throw new WireMessageTooLargeException(
                        $"Maximum packet on-wire size exceeded for {message.GetType().Name}" +
                        $" (size={_messageWriteBuffer.Count}, uncompressedSize={uncompressedPayloadSize}," +
                        $" max={WireProtocol.MaxPacketWirePayloadSize}, uncompressedMax={WireProtocol.MaxPacketUncompressedPayloadSize})");
                }

                // Allocate buffer on send queue
                numBytesNeeded = WireProtocol.PacketHeaderSize + _messageWriteBuffer.Count;
                (byte[] buffer, int start) = AcquireSendBuffer(numBytesNeeded);
                SendBufferRef sendRef = new SendBufferRef(buffer, start: start, length: numBytesNeeded, isLastBufferRef: true);

                // Write payload (compressed or not) into buffer
                WireProtocol.EncodePacketHeader(new WirePacketHeader(WirePacketType.Message, compressionMode, _messageWriteBuffer.Count), sendRef.Buffer, sendRef.Start);
                _messageWriteBuffer.CopyTo(sendRef.Buffer, dstOffset: sendRef.Start + WireProtocol.PacketHeaderSize);
                _outgoingMessages.Add(new OutgoingMessage(sendRef, OutgoingMessage.MessageKind.MetaMessage));

                // First enqueuer commits to serve the waiter
                if (_unservedWaiter != null && _outgoingMessages.Count == 1)
                    shouldSignalWaiter = true;
            }

            if (shouldSignalWaiter)
                SigalWaiter();

            return numBytesNeeded;
        }

        (byte[] buffer, int offset) AcquireSendBuffer(int numBytesNeeded)
        {
            // Find last message on message buffer
            for (int ndx = _outgoingMessages.Count - 1; ndx >= 0; --ndx)
            {
                SendBufferRef sendBufRef = _outgoingMessages[ndx].SendBuffer;

                // No buffer, ignore.
                if (sendBufRef.Buffer == null)
                    continue;

                // This must be the last buffer.
                int numBytesOnLastBuffer = sendBufRef.Buffer.Length - (sendBufRef.Start + sendBufRef.Length);
                if (numBytesNeeded <= numBytesOnLastBuffer)
                {
                    // Steal ownership
                    SendBufferRef newSendBuffer = new SendBufferRef(sendBufRef.Buffer, sendBufRef.Start, sendBufRef.Length, isLastBufferRef: false);
                    _outgoingMessages[ndx] = new OutgoingMessage(newSendBuffer, _outgoingMessages[ndx].Kind);

                    int end = sendBufRef.Start + sendBufRef.Length;
                    return (sendBufRef.Buffer, end);
                }

                // We only append so don't look into further buffer refs.
                break;
            }

            // New buffer
            byte[] buffer = ArrayPool<byte>.Shared.Rent(minimumLength: System.Math.Max(4096, numBytesNeeded));
            return (buffer, 0);
        }

        /// <summary>
        /// Enqueues the Ping message and returns the number of bytes written to buffer.
        /// </summary>
        /// <exception cref="CloseEnqueuedException">Close has been enqueued already</exception>
        /// <exception cref="ObjectDisposedException">Dispose has been called already</exception>
        public int EnqueuePing32(uint payload)
        {
            bool shouldSignalWaiter = false;
            int numBytesNeeded;

            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("WireMessageWriteQueue");
                if (_closeEnqueued)
                    throw new CloseEnqueuedException();

                numBytesNeeded = WireProtocol.PacketHeaderSize + 4;
                (byte[] buffer, int start) = AcquireSendBuffer(numBytesNeeded);

                WireProtocol.EncodePacketHeader(new WirePacketHeader(WirePacketType.Ping, WirePacketCompression.None, 4), buffer, start);
                buffer[start + WireProtocol.PacketHeaderSize + 0] = (byte)(payload >> 24);
                buffer[start + WireProtocol.PacketHeaderSize + 1] = (byte)(payload >> 16);
                buffer[start + WireProtocol.PacketHeaderSize + 2] = (byte)(payload >> 8);
                buffer[start + WireProtocol.PacketHeaderSize + 3] = (byte)(payload >> 0);

                SendBufferRef sendRef = new SendBufferRef(buffer, start: start, length: numBytesNeeded, isLastBufferRef: true);
                _outgoingMessages.Add(new OutgoingMessage(sendRef, OutgoingMessage.MessageKind.Ping));

                // First enqueuer commits to serve the waiter
                if (_unservedWaiter != null && _outgoingMessages.Count == 1)
                    shouldSignalWaiter = true;
            }

            if (shouldSignalWaiter)
                SigalWaiter();

            return numBytesNeeded;
        }

        /// <summary>
        /// Enqueues the Pong message and returns the number of bytes written to buffer.
        /// </summary>
        /// <exception cref="CloseEnqueuedException">Close has been enqueued already</exception>
        /// <exception cref="ObjectDisposedException">Dispose has been called already</exception>
        public int EnqueuePong(byte[] payload, int payloadStart, int payloadLength)
        {
            bool shouldSignalWaiter = false;
            int numBytesNeeded;

            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("WireMessageWriteQueue");
                if (_closeEnqueued)
                    throw new CloseEnqueuedException();

                numBytesNeeded = WireProtocol.PacketHeaderSize + payloadLength;
                (byte[] buffer, int start) = AcquireSendBuffer(numBytesNeeded);

                WireProtocol.EncodePacketHeader(new WirePacketHeader(WirePacketType.PingResponse, WirePacketCompression.None, 4), buffer, start);
                Buffer.BlockCopy(payload, payloadStart, buffer, start + WireProtocol.PacketHeaderSize, payloadLength);

                SendBufferRef sendRef = new SendBufferRef(buffer, start: start, length: numBytesNeeded, isLastBufferRef: true);
                _outgoingMessages.Add(new OutgoingMessage(sendRef, OutgoingMessage.MessageKind.Pong));

                // First enqueuer commits to serve the waiter
                if (_unservedWaiter != null && _outgoingMessages.Count == 1)
                    shouldSignalWaiter = true;
            }

            if (shouldSignalWaiter)
                SigalWaiter();

            return numBytesNeeded;
        }

        /// <summary>
        /// Enqueues the Info.
        /// </summary>
        /// <exception cref="CloseEnqueuedException">Close has been enqueued already</exception>
        /// <exception cref="ObjectDisposedException">Dispose has been called already</exception>
        public void EnqueueInfo(MessageTransport.Info info)
        {
            bool shouldSignalWaiter = false;

            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("WireMessageWriteQueue");
                if (_closeEnqueued)
                    throw new CloseEnqueuedException();

                _outgoingMessages.Add(new OutgoingMessage(info: info));

                // First enqueuer commits to serve the waiter
                if (_unservedWaiter != null && _outgoingMessages.Count == 1)
                    shouldSignalWaiter = true;
            }

            if (shouldSignalWaiter)
                SigalWaiter();
        }

        /// <summary>
        /// Enqueues the Fence.
        /// </summary>
        /// <exception cref="CloseEnqueuedException">Close has been enqueued already</exception>
        /// <exception cref="ObjectDisposedException">Dispose has been called already</exception>
        public void EnqueueFence(TaskCompletionSource<int> tcs)
        {
            bool shouldSignalWaiter = false;

            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("WireMessageWriteQueue");
                if (_closeEnqueued)
                    throw new CloseEnqueuedException();

                _outgoingMessages.Add(new OutgoingMessage(fenceCS: tcs));

                // First enqueuer commits to serve the waiter
                if (_unservedWaiter != null && _outgoingMessages.Count == 1)
                    shouldSignalWaiter = true;
            }

            if (shouldSignalWaiter)
                SigalWaiter();
        }

        /// <summary>
        /// Enqueues the transport close message.
        /// </summary>
        /// <exception cref="CloseEnqueuedException">Close has been enqueued already</exception>
        /// <exception cref="ObjectDisposedException">Dispose has been called already</exception>
        public void EnqueueClose(object closePayload)
        {
            bool shouldSignalWaiter = false;

            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException("WireMessageWriteQueue");
                if (_closeEnqueued)
                    throw new CloseEnqueuedException();

                _outgoingMessages.Add(new OutgoingMessage(closePayload: closePayload));
                _closeEnqueued = true;

                // First enqueuer commits to serve the waiter
                if (_unservedWaiter != null && _outgoingMessages.Count == 1)
                    shouldSignalWaiter = true;
            }

            if (shouldSignalWaiter)
                SigalWaiter();
        }

        /// <summary>
        /// Return value becomes completed when there are items to be used.
        /// </summary>
        public Task NextAvailableAsync()
        {
            lock (_lock)
            {
                if (_outgoingMessages.Count > 0)
                    return Task.CompletedTask;

                // Register waiter or return the pre-existing.
                if (_unservedWaiter == null)
                    _unservedWaiter = _waiter.WaitAsync();
                return _unservedWaiter;
            }
        }

        void SigalWaiter()
        {
            try
            {
                _waiter.Release(1);
            }
            catch (SemaphoreFullException)
            {
            }
        }

        /// <summary>
        /// Acquires the next message in the write queue. The acquired message must be released with <see cref="ReleaseAcquired"/> or <see cref="ReturnAcquired"/>.
        /// </summary>
        public bool TryAcquireNext(out OutgoingMessage message)
        {
            lock (_lock)
            {
                if (_isAcquired)
                    throw new InvalidOperationException("already acquired");

                if (_disposed || _outgoingMessages.Count == 0)
                {
                    _acquiredBuffer = null;
                    message = default;
                    return false;
                }

                message = _outgoingMessages[0];
                _acquiredBuffer = _outgoingMessages[0].SendBuffer.Buffer;
                _isAcquired = true;

                // When the only reader succeeds in consuming something from the queue, there is no longer
                // unserved waiter (since the only reader is right now being served).
                _unservedWaiter = null;

                return true;
            }
        }

        /// <summary>
        /// Returns the acquired message back into the queue.
        /// </summary>
        public void ReturnAcquired()
        {
            lock (_lock)
            {
                if (!_isAcquired)
                    throw new InvalidOperationException("not acquired, cannot return");
                _isAcquired = false;

                if (_disposed && _acquiredBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(_acquiredBuffer);
                }
                _acquiredBuffer = null;
            }
        }

        /// <summary>
        /// Consumes the acquired message from the queue and releases any resources.
        /// </summary>
        public void ReleaseAcquired()
        {
            // Release buffer if any
            lock (_lock)
            {
                if (!_isAcquired)
                    throw new InvalidOperationException("not acquired, cannot release");
                _isAcquired = false;

                // If the queue was Disposed during operation, release the acquired buffer
                if (_disposed && _acquiredBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(_acquiredBuffer);
                }
                _acquiredBuffer = null;
                if (_disposed)
                    return;

                // Release the acquired
                if (_outgoingMessages[0].SendBuffer.Buffer != null && _outgoingMessages[0].SendBuffer.IsLastBufferRef)
                {
                    ArrayPool<byte>.Shared.Return(_outgoingMessages[0].SendBuffer.Buffer);
                }
                _outgoingMessages.RemoveAt(0);
            }
        }

        /// <summary>
        /// Releases allocated memory buffers.
        /// </summary>
        public void Dispose()
        {
            List<OutgoingMessage> messages;
            byte[] acquiredBuffer;
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;

                messages = _outgoingMessages;
                acquiredBuffer = _acquiredBuffer;
                _outgoingMessages = null;
            }

            // Release allocs
            foreach (OutgoingMessage message in messages)
            {
                // If a buffer is acquired, it's kept aside for the caller to return on release/return
                if (message.SendBuffer.IsLastBufferRef && message.SendBuffer.Buffer != acquiredBuffer)
                    ArrayPool<byte>.Shared.Return(message.SendBuffer.Buffer);
            }
            _messageWriteBuffer.Dispose();
        }

        /// <summary>
        /// Encodes MetaMessage into wire format. Utility for one-off encoding. Message is not compressed.
        /// </summary>
        public static byte[] EncodeMessage(MetaMessage message)
        {
            // Serialize message
            byte[] payload = MetaSerialization.SerializeTagged<MetaMessage>(message, MetaSerializationFlags.SendOverNetwork, logicVersion: null);
            int uncompressedPayloadSize = payload.Length;

            // Check uncompressed size limit
            if (payload.Length > WireProtocol.MaxPacketUncompressedPayloadSize)
                throw new WireMessageTooLargeException($"Maximum packet uncompressed size exceeded for {message.GetType().Name} (size={payload.Length}, max={WireProtocol.MaxPacketUncompressedPayloadSize})");

            // Check on-wire size limit
            if (payload.Length > WireProtocol.MaxPacketWirePayloadSize)
            {
                throw new WireMessageTooLargeException(
                    $"Maximum packet on-wire size exceeded for {message.GetType().Name}" +
                    $" (size={payload.Length}, uncompressedSize={uncompressedPayloadSize}," +
                    $" max={WireProtocol.MaxPacketWirePayloadSize}, uncompressedMax={WireProtocol.MaxPacketUncompressedPayloadSize})");
            }

            // Allocate buffer & encode header
            byte[] buffer = new byte[WireProtocol.PacketHeaderSize + payload.Length];
            WireProtocol.EncodePacketHeader(new WirePacketHeader(WirePacketType.Message, WirePacketCompression.None, payload.Length), buffer);

            // Append payload (compressed or not) into buffer
            Buffer.BlockCopy(payload, 0, buffer, WireProtocol.PacketHeaderSize, payload.Length);

            return buffer;
        }
    }
}
