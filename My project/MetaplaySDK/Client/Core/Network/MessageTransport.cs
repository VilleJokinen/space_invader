// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Message;
using Metaplay.Core.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Core.Network
{
    /// <summary>
    /// Represents a single, bidirectional, non-recoverable message transport channel.
    ///
    /// <para>
    /// Any series of OnConnect(C), OnReceive(R), OnInfo(I), OnError(E) of a single instance will be recognized
    /// the regex expression "(C(R|I)*)?E?". Notably OnConnect is called before any OnReceive or
    /// OnInfo; and OnError, if emitted, will be the final event.
    /// </para>
    ///
    /// <para>
    /// The events may be delivered on any thread, but the calls are never interleaved.
    /// </para>
    /// </summary>
    public interface IMessageTransport
    {
        void SetDebugDiagnosticsRef(LoginTransportDebugDiagnostics debugDiagnostics);

        event MessageTransport.ConnectEventHandler    OnConnect;
        event MessageTransport.ReceiveEventHandler    OnReceive;
        event MessageTransport.InfoEventHandler       OnInfo;
        event MessageTransport.ErrorEventHandler      OnError;

        /// <summary>
        /// Begins opening the transport.
        /// </summary>
        void Open();

        /// <summary>
        /// Adds message to send queue.
        ///
        /// <para>
        /// May be called from any thread, or from any event handler. However, if transport is not
        /// in Connected state (has not emitted OnConnect, or has emitted OnError), messages may be
        /// silently dropped.
        /// </para>
        /// </summary>
        void EnqueueSendMessage(MetaMessage message);

        /// <summary>
        /// Sets the transport to close after currenty enqueued messages have been sent. Closing will
        /// emit OnError(EnqueuedCloseError). This may complete synchronously or asynchronously.
        /// </summary>
        /// <param name="closedErrorPayload">Payload given to the <see cref="MessageTransport.EnqueuedCloseError"/>.</param>
        void EnqueueClose(object closedErrorPayload);

        /// <summary>
        /// Returns a fence that completes after all preceeding messages have been written to the transport.
        /// If fence cannot be enqueued, returns null.
        /// </summary>
        MessageTransportWriteFence EnqueueWriteFence();

        /// <summary>
        /// Enqueues a custom Info to be emitted with the same observability guarantees as other Info messages. Infos enqueued
        /// before OnConnect may be discarded. Infos enqueued after OnError will be discarded. Note that all Happens-Before and Happens-After
        /// relations are evalutated by the MessageTransport and may differ from call site observation. In particular, even if
        /// OnError has not been observed by the call site, it might have been observed already in MessageTransport.
        /// <para>
        /// The info is delivered to the OnInfo handler asynchronously. But even if the Info callback is invoked
        /// asynchronously, the callback execution may have started and even completed before control returns to the call-site
        /// as the transport may execute callbacks on another thread.
        /// </para>
        /// </summary>
        void EnqueueInfo(MessageTransport.Info info);

        /// <summary>
        /// Begins teardown of the transport.
        ///
        /// <para>
        /// Disposing a transport does not emit OnError event. However, since events are delivered
        /// asyncronously, an earlier OnError event might be observed after this call.
        /// </para>
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// Represents a write fence of a write operation in a MessageTransport. Fence will become
    /// signaled when preceeding write operations have been completed. Completion means the data
    /// has been written to the socket, but this DOES NOT mean the data has been received by the
    /// server or that the data has been physically sent.
    /// <para>
    /// The fence may never complete and all usages should define a timeout if fence is waited.
    /// </para>
    /// </summary>
    public class MessageTransportWriteFence
    {
        public readonly Task WhenComplete;
        public MessageTransportWriteFence(Task whenComplete)
        {
            if (whenComplete == null)
                throw new ArgumentNullException(nameof(whenComplete));
            WhenComplete = whenComplete;
        }
    }

    public abstract class MessageTransport : IMessageTransport
    {
        public abstract class Error
        {
        };
        public abstract class Info
        {
        };
        public struct TransportHandshakeReport
        {
            public string           ChosenHostname;
            public AddressFamily    ChosenProtocol;
            public string           TlsPeerDescription;

            public TransportHandshakeReport(string chosenHostname, AddressFamily chosenProtocol, string tlsPeerDescription)
            {
                ChosenHostname = chosenHostname;
                ChosenProtocol = chosenProtocol;
                TlsPeerDescription = tlsPeerDescription;
            }
        }

        /// <summary>
        /// Error signifying EnqueueClose has completed successfully.
        /// </summary>
        public sealed class EnqueuedCloseError : Error
        {
            /// <summary>
            /// The custom payload given in EnqueueClose. This can be used to differentiate which callsite
            /// caused the EnqueuedCloseError error.
            /// </summary>
            public readonly object Payload;

            public EnqueuedCloseError(object payload)
            {
                Payload = payload;
            }
        }

        public delegate void ConnectEventHandler(Handshake.ServerHello serverHello, TransportHandshakeReport transportHandshake);
        public delegate void ReceiveEventHandler(MetaMessage message);
        public delegate void InfoEventHandler(Info info);
        public delegate void ErrorEventHandler(Error error);

        public event ConnectEventHandler    OnConnect;
        public event ReceiveEventHandler    OnReceive;
        public event InfoEventHandler       OnInfo;
        public event ErrorEventHandler      OnError;

        protected void InvokeOnConnect(Handshake.ServerHello serverHello, TransportHandshakeReport transportHandshake)
        {
            OnConnect?.Invoke(serverHello, transportHandshake);
        }
        protected void InvokeOnReceive(MetaMessage message)
        {
            OnReceive?.Invoke(message);
        }
        protected void InvokeOnInfo(Info info)
        {
            OnInfo?.Invoke(info);
        }
        protected void InvokeOnError(Error error)
        {
            OnError?.Invoke(error);
        }

        public abstract void SetDebugDiagnosticsRef(LoginTransportDebugDiagnostics debugDiagnostics);
        public abstract void Open();
        public abstract void EnqueueSendMessage(MetaMessage message);
        public abstract void EnqueueClose(object closedErrorPayload);
        public abstract MessageTransportWriteFence EnqueueWriteFence();
        public abstract void EnqueueInfo(MessageTransport.Info info);
        public abstract void Dispose();
    }

    public abstract class WireMessageTransport : MessageTransport
    {
        /// <summary>
        /// Represents an unacceptable server-supplied ProtocolStatus. Only acceptable
        /// status is ClusterRunning.
        /// </summary>
        public class ProtocolStatusError : Error
        {
            public ProtocolStatus status;
            public ProtocolStatusError(ProtocolStatus s)
            {
                status = s;
            }
        };

        /// <summary>
        /// Wraps errors encountered when deserializing a message.
        /// </summary>
        public class WireFormatError : Error
        {
            public Exception DecodeException;
            public WireFormatError (Exception ex)
            {
                DecodeException = ex;
            }
        }

        /// <summary>
        /// Server did not respond with a correct magic identifier.
        /// </summary>
        public class InvalidGameMagic : Error
        {
            public UInt32 Magic;
            public InvalidGameMagic (UInt32 magic)
            {
                Magic = magic;
            }
        }

        /// <summary>
        /// WireProtocolVersion mismatch between client and server.
        /// </summary>
        public class WireProtocolVersionMismatch : Error
        {
            public int ServerProtocolVersion;
            public WireProtocolVersionMismatch(int serverProtocolVersion)
            {
                ServerProtocolVersion = serverProtocolVersion;
            }
        }

        /// <summary>
        /// Server did not respond with hello message.
        /// </summary>
        public class MissingHelloError : Error
        {
            public MissingHelloError()
            {
            }
        }
        /// <summary>Timeout while accessing transport</summary>
        public class TimeoutError : Error
        {
        }
        /// <summary>Timeout while opening transport</summary>
        public class ConnectTimeoutError : TimeoutError
        {
        }
        /// <summary>Timeout while waiting for protocol header</summary>
        public class HeaderTimeoutError : TimeoutError
        {
        }
        /// <summary>Timeout reading from the transport</summary>
        public class ReadTimeoutError : TimeoutError
        {
        }
        /// <summary>Timeout writing to the transport</summary>
        public class WriteTimeoutError : TimeoutError
        {
        }

        /// <summary>Thread has completed one update cycle</summary>
        public class ThreadCycleUpdateInfo : Info
        {
            public static readonly ThreadCycleUpdateInfo Instance = new ThreadCycleUpdateInfo();
        }
    }

    public abstract class StreamMessageTransport : WireMessageTransport
    {
        /// <summary>Stream was closed</summary>
        public class StreamClosedError : Error
        {
        }
        /// <summary>Stream IO operation failed</summary>
        public class StreamIOFailedError : Error
        {
            public enum OpType
            {
                Read,
                Write,
            }
            public readonly OpType Op;
            public readonly Exception Exception;
            public StreamIOFailedError(OpType op, Exception ex)
            {
                Op = op;
                Exception = ex;
            }
        }
        /// <summary>Stream executor loop failed</summary>
        public class StreamExecutorError : Error
        {
            public readonly Exception Exception;
            public StreamExecutorError(Exception ex)
            {
                Exception = ex;
            }
        }
        /// <summary>Write operation has exeeded warn-after duration, or after such event the warned operation completes</summary>
        public class WriteDurationWarningInfo : Info
        {
            WriteDurationWarningInfo(bool isEnd) => IsEnd = isEnd;
            public bool IsEnd { get; }
            public bool IsBegin => !IsEnd;
            public static WriteDurationWarningInfo ForBegin() => new WriteDurationWarningInfo(false);
            public static WriteDurationWarningInfo ForEnd() => new WriteDurationWarningInfo(true);
        }
        /// <summary>Read operation has exeeded warn-after duration, or after such event the warned operation completes</summary>
        public class ReadDurationWarningInfo : Info
        {
            ReadDurationWarningInfo(bool isEnd) => IsEnd = isEnd;
            public bool IsEnd { get; }
            public bool IsBegin => !IsEnd;
            public static ReadDurationWarningInfo ForBegin() => new ReadDurationWarningInfo(false);
            public static ReadDurationWarningInfo ForEnd() => new ReadDurationWarningInfo(true);
        }


        protected IMetaLogger           Log { get; }

        // Accessed by both threads
        object                          _lock                                       = new object();
        CancellationTokenSource         _cts;
        TaskCompletionSource<object>    _abortOpenTcs                               = new TaskCompletionSource<object>();
        WireMessageWriteQueue           _writeQueue; // Set to non-null when connected. Becomes Disposed after close.

        // Accessed by the worker
        byte[]                          _writeBuffer                                = new byte[2048];
        int                             _writeBufferContentLength;
        int                             _writeBufferNumMetaMessages;
        int                             _writeBufferNumPackets;

        public class ConfigBase
        {
        }
        public class ConfigArgs : ConfigBase
        {
            public string               GameMagic;
            public string               Version;
            public string               BuildNumber;
            public MetaVersionRange     SupportedLogicVersions;
            public uint                 FullProtocolHash;
            public string               CommitId;
            public uint                 ClientSessionConnectionNdx;
            public uint                 ClientSessionNonce;
            public uint                 AppLaunchId;
            public ClientPlatform       Platform;
            public int                  LoginProtocolVersion;

            // \note Timeouts vary slightly to distinguish them on the server in certain connection issue scenarios.
            public TimeSpan ConnectTimeout          = TimeSpan.FromSeconds(32);
            public TimeSpan HeaderReadTimeout       = TimeSpan.FromSeconds(34);

            /// <summary>
            /// Time limit how long receiving any data from server is allowed to take.
            /// Exceeding this limit will cause transition into Error state.
            /// </summary>
            public TimeSpan ReadTimeout = TimeSpan.FromSeconds(28);

            /// <summary>
            /// Time limit how long a write to underlying socket is allowed to take.
            /// Exceeding this limit will cause transition into Error state.
            /// </summary>
            public TimeSpan WriteTimeout = TimeSpan.FromSeconds(26);

            /// <summary>
            /// Maximimum idle time without writing anything on connection before a Keepalive-message is sent by client.
            /// </summary>
            public TimeSpan WriteKeepaliveInterval = TimeSpan.FromSeconds(10);

            /// <summary>
            /// Maximimum idle time without reading anything on connection before a Keepalive-message is sent by client.
            /// </summary>
            public TimeSpan ReadKeepaliveInterval = TimeSpan.FromSeconds(10);

            /// <summary>
            /// Time limit how long a write to underlying socket should to take.
            /// Exceeding this limit will cause <c>WriteDurationWarningInfo</c> which on
            /// client causes <c>Connected.IsHealthy</c> to become <c>false</c>.
            /// </summary>
            public TimeSpan WarnAfterWriteDuration = TimeSpan.FromSeconds(15);

            /// <summary>
            /// Time limit how long receiving any data from server should take.
            /// Exceeding this limit will cause <c>ReadDurationWarningInfo</c> which on
            /// client causes <c>Connected.IsHealthy</c> to become <c>false</c>.
            /// </summary>
            public TimeSpan WarnAfterReadDuration = TimeSpan.FromSeconds(15);
        }
        protected readonly ConfigBase _configBase;
        public ConfigArgs Config() => (ConfigArgs)_configBase;

        public StreamMessageTransport(IMetaLogger log, ConfigArgs config)
        {
            Log = log;
            _configBase = config;
        }

        volatile LoginTransportDebugDiagnostics _debugDiagnostics = new LoginTransportDebugDiagnostics(); // \note Kept always non-null to simplify usage

        public override void SetDebugDiagnosticsRef(LoginTransportDebugDiagnostics debugDiagnostics)
        {
            _debugDiagnostics = debugDiagnostics ?? new LoginTransportDebugDiagnostics(); // \note Kept always non-null to simplify usage
        }

        public override void Open()
        {
            lock (_lock)
            {
                // already started
                if (_cts != null)
                    throw new InvalidOperationException();

                // Spawn background Task to read/write socket
                _cts = new CancellationTokenSource();
                _ = MetaTask.Run(ConnectionTask);
            }
        }

        public override void EnqueueSendMessage(MetaMessage message)
        {
            Interlocked.Increment(ref _debugDiagnostics.MetaMessageEnqueuesAttempted);
            Interlocked.Increment(ref _debugDiagnostics.PacketEnqueuesAttempted);

            if (_writeQueue == null)
            {
                Interlocked.Increment(ref _debugDiagnostics.MetaMessageUnconnectedEnqueuesAttempted);
                Log.Warning("Attempted to enqueue message to a transport that has not yet Connected()");
                return;
            }

            int numBytesEnqueued;
            try
            {
                numBytesEnqueued = _writeQueue.EnqueueMessage(message);
            }
            catch (WireMessageWriteQueue.WireMessageTooLargeException)
            {
                // \todo: handle proper exception
                Interlocked.Increment(ref _debugDiagnostics.MetaMessagePacketSizesExceeded);
                throw;
            }
            catch (WireMessageWriteQueue.CloseEnqueuedException)
            {
                Interlocked.Increment(ref _debugDiagnostics.MetaMessageClosingEnqueuesAttempted);
                Log.Warning("Enqueuing message to a closing transport");
                return;
            }
            catch (ObjectDisposedException)
            {
                Interlocked.Increment(ref _debugDiagnostics.MetaMessageDisposedEnqueuesAttempted);
                Log.Warning("Enqueuing message to a disposed transport");
                return;
            }

            Interlocked.Increment(ref _debugDiagnostics.PacketsEnqueued);
            Interlocked.Add(ref _debugDiagnostics.BytesEnqueued, numBytesEnqueued);
            Interlocked.Increment(ref _debugDiagnostics.MetaMessagesEnqueued);
        }

        private void EnqueueSendPing32(uint payload)
        {
            Interlocked.Increment(ref _debugDiagnostics.PacketEnqueuesAttempted);
            if (_writeQueue == null)
                return;
            try
            {
                int numBytesEnqueued = _writeQueue.EnqueuePing32(payload);
                Interlocked.Increment(ref _debugDiagnostics.PacketsEnqueued);
                Interlocked.Add(ref _debugDiagnostics.BytesEnqueued, numBytesEnqueued);
            }
            catch (WireMessageWriteQueue.CloseEnqueuedException)
            {
                // Ignore if closing
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already closed
            }
        }

        private void EnqueueSendPong(byte[] payloadArray, int payloadOffset, int payloadSize)
        {
            Interlocked.Increment(ref _debugDiagnostics.PacketEnqueuesAttempted);
            if (_writeQueue == null)
                return;
            try
            {
                int numBytesEnqueued = _writeQueue.EnqueuePong(payloadArray, payloadOffset, payloadSize);
                Interlocked.Increment(ref _debugDiagnostics.PacketsEnqueued);
                Interlocked.Add(ref _debugDiagnostics.BytesEnqueued, numBytesEnqueued);
            }
            catch (WireMessageWriteQueue.CloseEnqueuedException)
            {
                // Ignore if closing
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already closed
            }
        }

        public override void EnqueueClose(object payload)
        {
            // Store into outgoing messages
            lock (_lock)
            {
                if (_writeQueue != null)
                {
                    // Connection has Connected().
                    // If the connection is not running, then it must have errored out already.
                    try
                    {
                        _writeQueue.EnqueueClose(payload);
                    }
                    catch (WireMessageWriteQueue.CloseEnqueuedException)
                    {
                        // Ignore if closing
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore if already closed
                    }
                }
                else if (_cts != null)
                {
                    // Connection has been open()ed but not yet connected.
                    // Abort the open(). The cancel may have errored out already.
                    _abortOpenTcs.TrySetResult(payload);
                }
                else
                {
                    // Create cts to mark thread as started. Invoke error from
                    // thread pool to avoid making OnError surprisingly
                    // synchronous/re-entrant in certain cases.
                    _cts = new CancellationTokenSource();
                    _ = MetaTask.Run(() => { InvokeOnError(new EnqueuedCloseError(payload)); });
                }
            }
        }

        public override MessageTransportWriteFence EnqueueWriteFence()
        {
            lock (_lock)
            {
                // No worker? Cannot enqueue.
                if (_cts == null)
                    return null;
            }

            TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
            try
            {
                _writeQueue.EnqueueFence(tcs);
            }
            catch (WireMessageWriteQueue.CloseEnqueuedException)
            {
                // Close already enqueued
                return null;
            }
            catch (ObjectDisposedException)
            {
                // Closed already
                return null;
            }
            return new MessageTransportWriteFence(tcs.Task);
        }

        public override void EnqueueInfo(Info info)
        {
            lock (_lock)
            {
                // No worker? Cannot enqueue.
                if (_cts == null)
                    return;
            }
            try
            {
                _writeQueue.EnqueueInfo(info);
            }
            catch (WireMessageWriteQueue.CloseEnqueuedException)
            {
                // Close already enqueued
            }
            catch (ObjectDisposedException)
            {
                // Closed already
            }
        }

        public override void Dispose()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// Denotes a case where connection task was terminated due to an error (or cancel),
        /// but this case has already been take care of (i.e. error event is raised).
        /// </summary>
        [Serializable]
        protected class ConnectionTaskOverException : Exception
        {
        }

        struct ReadWriteBuffer
        {
            public byte[]   Array;
            public int      Limit;
            public int      Position;

            public int Capacity => Array?.Length ?? 0;
            public int Remaining => Limit - Position;

            // Flips buffer from Write mode to read mode
            public void Flip()
            {
                Limit = Position;
                Position = 0;
            }

            // Compacts Read mode buffer, and transitions to write mode
            public void Compact(int capacity)
            {
                if (Array == null)
                {
                    Array = new byte[capacity];
                }
                else if (capacity != Array.Length)
                {
                    byte[] newArray = new byte[capacity];
                    if (Remaining > 0)
                        Buffer.BlockCopy(Array, Position, newArray, 0, Remaining);
                    Array = newArray;
                }
                else
                {
                    if (Position > 0 && Remaining > 0)
                        Buffer.BlockCopy(Array, Position, Array, 0, Remaining);
                }
                Position = Remaining;
                Limit = Array.Length;
            }
        };

        protected void AbandonConnectionStream(Task<(Stream, TransportHandshakeReport)> streamTask, MetaTime connectionStartedAt)
        {
            // unwrap stream from result-report-pair
            Task<Stream> streamFuture = streamTask.ContinueWithCtx((Task<(Stream, TransportHandshakeReport)> task) =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                    return task.GetCompletedResult().Item1;
                else if (task.IsFaulted)
                    throw task.Exception;
                else
                    throw new TaskCanceledException();
            });
            AbandonConnectionStream(streamFuture, connectionStartedAt);
        }
        protected void AbandonConnectionStream(Task<Stream> streamTask, MetaTime connectionStartedAt)
        {
            MetaTime abandonedAt = MetaTime.Now;

            _ = streamTask.ContinueWithCtx(async task =>
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    using (Stream stream = task.GetCompletedResult())
                    {
                        byte[] message = WireMessageWriteQueue.EncodeMessage(new Handshake.ClientAbandon(
                            connectionStartedAt:    connectionStartedAt,
                            connectionAbandonedAt:  abandonedAt,
                            abandonedCompletedAt:   MetaTime.Now,
                            source:                 Handshake.ClientAbandon.AbandonSource.PrimaryConnection
                            ));
                        await stream.WriteAsync(message, offset: 0, count: message.Length);
                    }
                }
                else if (task.IsFaulted)
                {
                    _ = task.Exception;
                }
            });
        }

        #region Handshake

        async Task<Stream> OpenAndPerformHandshake(ConfigArgs config, CancellationToken ct)
        {
            MetaTime connectionStartedAt = MetaTime.Now;

            (Stream stream, TransportHandshakeReport transportHandshake) = await OpenStreamWithAbortAndCancellation(ct, connectionStartedAt);
            Handshake.ServerHello serverHello = await PerformHandshakeAsync(stream, config, ct);

            // Switch into running state. Check Abort state for the last time
            lock (_lock)
            {
                if (_abortOpenTcs.Task.IsCompleted)
                {
                    stream.Dispose();
                    InvokeOnError(new EnqueuedCloseError(_abortOpenTcs.Task.GetCompletedResult()));
                    throw new ConnectionTaskOverException();
                }

                _writeQueue = new WireMessageWriteQueue();
            }

            // Switched into running. Inform listeners
            InvokeOnConnect(serverHello, transportHandshake);
            InvokeOnInfo(ThreadCycleUpdateInfo.Instance);

            return stream;
        }

        async Task<(Stream, TransportHandshakeReport)> OpenStreamWithAbortAndCancellation(CancellationToken ct, MetaTime connectionStartedAt)
        {
            Task<(Stream, TransportHandshakeReport)>    openTask            = OpenStream(ct);
            Task<object>                                abortTask           = _abortOpenTcs.Task;
            Task                                        timeout             = MetaTask.Delay(Config().ConnectTimeout, ct);
            Task                                        completed           = await Task.WhenAny(openTask, abortTask, timeout).ConfigureAwaitFalse();

            if (ct.IsCancellationRequested)
            {
                // Dispose() was called.
                // openTask may still be running and may complete successfully (despite the ct). If that happens, inform server that
                // the connection was disposed, and it was intentionally closed.
                AbandonConnectionStream(openTask, connectionStartedAt);
                throw new ConnectionTaskOverException();
            }

            if (abortTask == completed)
            {
                // EnqueueClose() was called.
                // openTask may still be running and may complete successfully. Like above, if the openTask ever completes successfully,
                // kill it gracefully.
                AbandonConnectionStream(openTask, connectionStartedAt);
                InvokeOnError(new EnqueuedCloseError(abortTask.GetCompletedResult()));
                throw new ConnectionTaskOverException();
            }

            if (openTask == completed)
            {
                // Open task completed, either successfully or with an error. (Cancellation is not possible
                // since ct is already checked, and openTask was completed already before it).
                return await openTask;
            }

            // timed out. Let openTask terminate like in other error paths.
            AbandonConnectionStream(openTask, connectionStartedAt);
            InvokeOnError(new ConnectTimeoutError());
            throw new ConnectionTaskOverException();
        }

        async Task<Handshake.ServerHello> PerformHandshakeAsync(Stream stream, ConfigArgs config, CancellationToken ct)
        {
            Task            timeout     = MetaTask.Delay(config.HeaderReadTimeout);
            Task<object>    abortTask   = _abortOpenTcs.Task;

            await WriteHandshakeHelloAsync(stream, config, abortTask, timeout, ct);
            return await ReadHandhakeHelloAsync(stream, abortTask, timeout, ct);
        }

        async Task WriteHandshakeHelloAsync(Stream stream, ConfigArgs config, Task<object> abortTask, Task timeoutTask, CancellationToken ct)
        {
            byte[] outgoingMessage = WireMessageWriteQueue.EncodeMessage(new Handshake.ClientHello(
                clientVersion:              config.Version,
                buildNumber:                config.BuildNumber,
                supportedLogicVersions:     config.SupportedLogicVersions,
                fullProtocolHash:           config.FullProtocolHash,
                commitId:                   config.CommitId,
                timestamp:                  MetaTime.Now,
                appLaunchId:                config.AppLaunchId,
                clientSessionNonce:         config.ClientSessionNonce,
                clientSessionConnectionNdx: config.ClientSessionConnectionNdx,
                platform:                   config.Platform,
                loginProtocolVersion:       config.LoginProtocolVersion));

            Task    writeTask   = stream.WriteAsync(outgoingMessage, 0, outgoingMessage.Length, ct);
            Task    ctTask      = MetaTask.Delay(-1, ct);
            Task    completed   = await Task.WhenAny(writeTask, abortTask, timeoutTask, ctTask);

            if (completed != writeTask || writeTask.Status == TaskStatus.Faulted || writeTask.Status == TaskStatus.Canceled)
            {
                // Error path. The write may still be running. Dispose the connection after
                _ = writeTask.ContinueWith((Task task) =>
                {
                    // If task faulted, observe the error.
                    if (task.IsFaulted)
                        _ = task.Exception;
                    stream.Dispose();
                }, TaskScheduler.Default);

                if (completed == ctTask)
                {
                    // Dispose() was called.
                    throw new ConnectionTaskOverException();
                }
                else if (completed == abortTask)
                {
                    // EnqueueClose() was called.
                    InvokeOnError(new EnqueuedCloseError(abortTask.GetCompletedResult()));
                    throw new ConnectionTaskOverException();
                }
                else if (completed == timeoutTask)
                {
                    // Timeout was triggered
                    InvokeOnError(new HeaderTimeoutError());
                    throw new ConnectionTaskOverException();
                }
                else
                {
                    // Write did not complete succesfully
                    if (writeTask.Status == TaskStatus.Canceled)
                    {
                        // Dispose() was called.
                        throw new ConnectionTaskOverException();
                    }
                    else
                    {
                        // Write faulted
                        Exception ex = writeTask.Exception.GetBaseException();
                        Log.Debug("message transport closing due to write error: {Exception}", NetworkErrorLoggingUtil.GetMinimalDescription(ex));
                        InvokeOnError(new StreamIOFailedError(op: StreamIOFailedError.OpType.Write, ex: ex));
                        throw new ConnectionTaskOverException();
                    }
                }
            }

            // Write task completed succesfully
        }

        async Task<Handshake.ServerHello> ReadHandhakeHelloAsync(Stream stream, Task<object> abortTask, Task timeoutTask, CancellationToken ct)
        {
            // Wait for protocol header
            byte[] protocolHeader = new byte[WireProtocol.ProtocolHeaderSize];
            await ReadHandhakeBytes(protocolHeader, stream, abortTask, timeoutTask, ct, isProtocolHeader: true);

            ProtocolStatus status = WireProtocol.ParseProtocolHeader(protocolHeader, 0, Config().GameMagic);
            if (status != ProtocolStatus.ClusterRunning)
            {
                stream.Dispose();

                if (status == ProtocolStatus.InvalidGameMagic)
                {
                    uint magic32 = ((uint)protocolHeader[3] << 0)
                                 | ((uint)protocolHeader[2] << 8)
                                 | ((uint)protocolHeader[1] << 16)
                                 | ((uint)protocolHeader[0] << 24);
                    InvokeOnError(new InvalidGameMagic(magic32));
                }
                else if (status == ProtocolStatus.WireProtocolVersionMismatch)
                {
                    int serverProtocolVersion = protocolHeader[4];
                    InvokeOnError(new WireProtocolVersionMismatch(serverProtocolVersion));
                }
                else
                {
                    InvokeOnError(new ProtocolStatusError(status));
                }
                throw new ConnectionTaskOverException();
            }

            // Wait for Server hello.
            byte[] packetHeaderData = new byte[WireProtocol.PacketHeaderSize];
            await ReadHandhakeBytes(packetHeaderData, stream, abortTask, timeoutTask, ct, isProtocolHeader: false);

            WirePacketHeader header;
            try
            {
                header = WireProtocol.DecodePacketHeader(packetHeaderData, 0, enforcePacketPayloadSizeLimit: false);
            }
            catch (Exception ex)
            {
                stream.Dispose();
                // \todo: more fine grained exception handling
                InvokeOnError(new WireFormatError(ex));
                throw new ConnectionTaskOverException();
            }

            if (header.Type != WirePacketType.Message || header.Compression != WirePacketCompression.None)
            {
                stream.Dispose();
                InvokeOnError(new MissingHelloError());
                throw new ConnectionTaskOverException();
            }

            byte[] packetPayloadData = new byte[header.PayloadSize];
            await ReadHandhakeBytes(packetPayloadData, stream, abortTask, timeoutTask, ct, isProtocolHeader: false);

            MetaMessage message;
            try
            {
                message = WireProtocol.DecodeMessage(packetPayloadData, 0, packetPayloadData.Length, resolver: null);
            }
            catch (Exception ex)
            {
                stream.Dispose();
                // \todo: more fine grained exception handling
                InvokeOnError(new WireFormatError(ex));
                throw new ConnectionTaskOverException();
            }

            if (message is Handshake.ServerHello serverHello)
            {
                return serverHello;
            }
            else
            {
                stream.Dispose();
                InvokeOnError(new MissingHelloError());
                throw new ConnectionTaskOverException();
            }
        }

        async Task ReadHandhakeBytes(byte[] bytes, Stream stream, Task<object> abortTask, Task timeoutTask, CancellationToken ct, bool isProtocolHeader)
        {
            Task    readTask        = stream.ReadAllAsync(bytes, 0, bytes.Length, ct);
            Task    ctTask          = MetaTask.Delay(-1, ct);
            Task    completed       = await Task.WhenAny(readTask, abortTask, timeoutTask, ctTask);

            if (completed != readTask || readTask.Status == TaskStatus.Faulted || readTask.Status == TaskStatus.Canceled)
            {
                // Error path. The read may still be running. Dispose the connection after
                _ = readTask.ContinueWith((Task task) =>
                {
                    // If task faulted, observe the error.
                    if (task.IsFaulted)
                        _ = task.Exception;
                    stream.Dispose();
                }, TaskScheduler.Default);

                if (completed == ctTask)
                {
                    // Dispose() was called.
                    throw new ConnectionTaskOverException();
                }
                else if (completed == abortTask)
                {
                    // EnqueueClose() was called.
                    InvokeOnError(new EnqueuedCloseError(abortTask.GetCompletedResult()));
                    throw new ConnectionTaskOverException();
                }
                else if (completed == timeoutTask)
                {
                    // Timeout was triggered
                    if (isProtocolHeader)
                        InvokeOnError(new HeaderTimeoutError());
                    else
                        InvokeOnError(new ReadTimeoutError());
                    throw new ConnectionTaskOverException();
                }
                else
                {
                    // Write did not complete succesfully
                    if (readTask.Status == TaskStatus.Canceled)
                    {
                        // Dispose() was called.
                        throw new ConnectionTaskOverException();
                    }
                    else
                    {
                        // Write faulted
                        Exception ex = readTask.Exception.GetBaseException();
                        Log.Debug("message transport closing due to write error: {Exception}", NetworkErrorLoggingUtil.GetMinimalDescription(ex));
                        InvokeOnError(new StreamIOFailedError(op: StreamIOFailedError.OpType.Read, ex: ex));
                        throw new ConnectionTaskOverException();
                    }
                }
            }

            // read completed succesfully
        }

        #endregion

        void CheckTimeoutIfNotNull<TError>(DateTime? timeout) where TError : MessageTransport.Error, new()
        {
            if (timeout == null)
                return;
            if (DateTime.UtcNow >= timeout.Value)
            {
                InvokeOnError(new TError());
                throw new ConnectionTaskOverException();
            }
        }

        bool TryCompleteWrite(ref Task writeTask, CancellationToken ct)
        {
            if (writeTask == null)
                return false;

            switch (writeTask.Status)
            {
                case TaskStatus.RanToCompletion:
                    writeTask.Dispose();
                    writeTask = null;

                    Interlocked.Increment(ref _debugDiagnostics.WritesCompleted);
                    Interlocked.Add(ref _debugDiagnostics.MetaMessagesWritten, _writeBufferNumMetaMessages);
                    Interlocked.Add(ref _debugDiagnostics.PacketsWritten, _writeBufferNumPackets);
                    Interlocked.Add(ref _debugDiagnostics.BytesWritten, _writeBufferContentLength);

                    return true;

                case TaskStatus.Faulted:
                    if (ct.IsCancellationRequested)
                        throw new ConnectionTaskOverException();

                    Log.Debug("message transport closing due to write error: {Exception}", NetworkErrorLoggingUtil.GetMinimalDescription(writeTask.Exception));
                    InvokeOnError(new StreamIOFailedError(op: StreamIOFailedError.OpType.Write, ex: writeTask.Exception));
                    throw new ConnectionTaskOverException();

                case TaskStatus.Canceled:
                    throw new ConnectionTaskOverException();

                default:
                    return false;
            }
        }

        bool TryCompleteRead(ref ReadWriteBuffer buffer, ref Task<int> readTask, CancellationToken ct)
        {
            if (readTask == null)
                return false;

            switch (readTask.Status)
            {
                case TaskStatus.RanToCompletion:
                    int numRead = readTask.GetCompletedResult();
                    readTask.Dispose();
                    readTask = null;
                    if (numRead == 0)
                    {
                        InvokeOnError(new StreamClosedError());
                        throw new ConnectionTaskOverException();
                    }
                    buffer.Position += numRead;
                    Interlocked.Increment(ref _debugDiagnostics.ReadsCompleted);
                    Interlocked.Add(ref _debugDiagnostics.BytesRead, numRead);
                    return true;

                case TaskStatus.Faulted:
                    if (ct.IsCancellationRequested)
                        throw new ConnectionTaskOverException();

                    Log.Debug("message transport closing due to read error: {Exception}", NetworkErrorLoggingUtil.GetMinimalDescription(readTask.Exception));
                    InvokeOnError(new StreamIOFailedError(op: StreamIOFailedError.OpType.Read, ex: readTask.Exception));
                    throw new ConnectionTaskOverException();

                case TaskStatus.Canceled:
                    throw new ConnectionTaskOverException();

                default:
                    return false;
            }
        }

        enum PumpWriteQueueResult
        {
            /// <summary>
            /// Write queue has been completed for now.
            /// </summary>
            QueueEmpty,

            /// <summary>
            /// PumpWriteQueue() should be called again.
            /// </summary>
            CallAgain,

            /// <summary>
            /// A write command has been dequeued from the write queue into the send buffer. The send buffer should be sent.
            /// </summary>
            SendBufferPrepared,
        }

        PumpWriteQueueResult PumpWriteQueue()
        {
            // Empty queue?
            if (!_writeQueue.TryAcquireNext(out WireMessageWriteQueue.OutgoingMessage queueFirst))
                return PumpWriteQueueResult.QueueEmpty;

            // Handle first
            try
            {
                // Handle marker
                if (queueFirst.SendBuffer.Buffer == null)
                {
                    switch (queueFirst.Kind)
                    {
                        case WireMessageWriteQueue.OutgoingMessage.MessageKind.Fence:
                        {
                            queueFirst.FenceCS.TrySetResult(0);
                            return PumpWriteQueueResult.CallAgain;
                        }
                        case WireMessageWriteQueue.OutgoingMessage.MessageKind.Info:
                        {
                            EnqueueInfo(queueFirst.Info);
                            return PumpWriteQueueResult.CallAgain;
                        }
                        case WireMessageWriteQueue.OutgoingMessage.MessageKind.Close:
                        {
                            InvokeOnError(new EnqueuedCloseError(queueFirst.ClosePayload));
                            throw new ConnectionTaskOverException();
                        }

                        default:
                            throw new InvalidOperationException("unreachable");
                    }
                }

                _writeBufferNumMetaMessages = (queueFirst.Kind == WireMessageWriteQueue.OutgoingMessage.MessageKind.MetaMessage) ? 1 : 0;
                _writeBufferNumPackets = 1;
                _writeBufferContentLength = 0;

                // Copy to write buffer
                if (_writeBuffer.Length < queueFirst.SendBuffer.Length)
                    _writeBuffer = new byte[queueFirst.SendBuffer.Length];
                Buffer.BlockCopy(queueFirst.SendBuffer.Buffer, queueFirst.SendBuffer.Start, _writeBuffer, 0, queueFirst.SendBuffer.Length);
                _writeBufferContentLength = queueFirst.SendBuffer.Length;
            }
            finally
            {
                _writeQueue.ReleaseAcquired();
            }

            // Merge following messages into the queue
            for (;;)
            {
                if (!_writeQueue.TryAcquireNext(out WireMessageWriteQueue.OutgoingMessage message))
                    break;

                if (message.SendBuffer.Buffer == null)
                {
                    _writeQueue.ReturnAcquired();
                    break;
                }

                // Don't write too large frames
                int numBytesInSendBufferWithThisMessage = _writeBufferContentLength + message.SendBuffer.Length;
                if (numBytesInSendBufferWithThisMessage > 2048)
                {
                    _writeQueue.ReturnAcquired();
                    break;
                }

                // Append to write buffer
                if (_writeBuffer.Length < numBytesInSendBufferWithThisMessage)
                {
                    byte[] newBuffer = new byte[numBytesInSendBufferWithThisMessage];
                    Buffer.BlockCopy(_writeBuffer, 0, newBuffer, 0, _writeBufferContentLength);
                    _writeBuffer = newBuffer;
                }

                Buffer.BlockCopy(message.SendBuffer.Buffer, message.SendBuffer.Start, _writeBuffer, _writeBufferContentLength, message.SendBuffer.Length);

                _writeBufferNumMetaMessages = (message.Kind == WireMessageWriteQueue.OutgoingMessage.MessageKind.MetaMessage) ? 1 : 0;
                _writeBufferNumPackets += 1;
                _writeBufferContentLength = numBytesInSendBufferWithThisMessage;
                _writeQueue.ReleaseAcquired();
            }

            return PumpWriteQueueResult.SendBufferPrepared;
        }

        bool TryStartWriteTask(Stream stream, ref Task writeTask, CancellationToken ct)
        {
            // already ongoing task
            if (writeTask != null)
                return false;

            for (;;)
            {
                PumpWriteQueueResult pumpResult = PumpWriteQueue();

                if (pumpResult == PumpWriteQueueResult.QueueEmpty)
                    return false;
                else if (pumpResult == PumpWriteQueueResult.CallAgain)
                    continue;
                else if (pumpResult == PumpWriteQueueResult.SendBufferPrepared)
                {
                    // Send buffer is ready
                    Interlocked.Increment(ref _debugDiagnostics.WritesStarted);
                    Task ongoingWrite = stream.WriteAsync(_writeBuffer, 0, _writeBufferContentLength, ct);

                    writeTask = ongoingWrite;
                    return true;
                }
            }
        }

        bool TryStartReadTask(Stream stream, ref ReadWriteBuffer buffer, ref Task<int> readTask, CancellationToken ct)
        {
            // already ongoing task
            if (readTask != null)
                return false;

            try
            {
                Interlocked.Increment(ref _debugDiagnostics.ReadsStarted);
                readTask = stream.ReadAsync(buffer.Array, buffer.Position, buffer.Remaining, ct);
                return true;
            }
            catch (IOException ex)
            {
                if (ct.IsCancellationRequested)
                    throw new ConnectionTaskOverException();

                Log.Debug("message transport closing due to write error: {Exception}", NetworkErrorLoggingUtil.GetMinimalDescription(ex));
                InvokeOnError(new StreamIOFailedError(op: StreamIOFailedError.OpType.Read, ex: ex));
                throw new ConnectionTaskOverException();
            }
            catch (OperationCanceledException)
            {
                // Cancelled
                throw new ConnectionTaskOverException();
            }
        }

        bool TryProcessMessage(ref ReadWriteBuffer buffer, out int framedSize, CancellationToken ct)
        {
            framedSize = WireProtocol.PacketHeaderSize;
            if (buffer.Remaining < framedSize)
                return false;

            WirePacketHeader header;
            try
            {
                header = WireProtocol.DecodePacketHeader(buffer.Array, buffer.Position, enforcePacketPayloadSizeLimit: false);
            }
            catch (Exception ex)
            {
                // \todo: more fine grained exception handling
                InvokeOnError(new WireFormatError(ex));
                throw new ConnectionTaskOverException();
            }

            framedSize = WireProtocol.PacketHeaderSize + header.PayloadSize;
            if (buffer.Remaining < framedSize)
                return false;

            // Decompress message
            byte[] bytes;
            int payloadOffset;
            int payloadSize;
            switch (header.Compression)
            {
                case WirePacketCompression.None:
                    bytes = buffer.Array;
                    payloadOffset = buffer.Position + WireProtocol.PacketHeaderSize;
                    payloadSize = header.PayloadSize;
                    break;

                case WirePacketCompression.Deflate:
                    bytes = CompressUtil.DeflateDecompress(buffer.Array, buffer.Position + WireProtocol.PacketHeaderSize, header.PayloadSize);
                    payloadOffset = 0;
                    payloadSize = bytes.Length;
                    break;

                default:
                    throw new InvalidOperationException($"Invalid CompressionMode in packet: {header.Compression}");
            }

            Interlocked.Increment(ref _debugDiagnostics.PacketsRead);

            // Decode message and handle it
            switch (header.Type)
            {
                case WirePacketType.Message:
                {
                    MetaMessage message;
                    try
                    {
                        message = WireProtocol.DecodeMessage(bytes, payloadOffset, payloadSize, resolver: null);
                    }
                    catch (Exception ex)
                    {
                        // \todo: more fine grained exception handling
                        InvokeOnError(new WireFormatError(ex));
                        throw new ConnectionTaskOverException();
                    }

                    buffer.Position += framedSize;
                    Interlocked.Increment(ref _debugDiagnostics.MetaMessagesRead);

                    if (message is Handshake.ClientHelloAccepted helloAccepted)
                        _writeQueue.EnableCompression(helloAccepted.ServerOptions.EnableWireCompression);

                    // Normal payload message
                    InvokeOnReceive(message);

                    Interlocked.Increment(ref _debugDiagnostics.MetaMessagesReceived);

                    // Event handler might cancel
                    if (ct.IsCancellationRequested)
                        throw new ConnectionTaskOverException();
                    break;
                }

                case WirePacketType.Ping:
                    // Got ping, respond with pong
                    EnqueueSendPong(bytes, payloadOffset, payloadSize);
                    buffer.Position += framedSize;
                    break;

                case WirePacketType.PingResponse:
                    // Got pong.
                    buffer.Position += framedSize;
                    break;

                default:
                    InvokeOnError(new WireFormatError(new Exception($"unrecognized WirePacketType: {header.Type}")));
                    throw new ConnectionTaskOverException();
            }

            return true;
        }

        bool TryConsumeTimeoutIfNotNull(ref DateTime? timeout)
        {
            if (timeout == null)
                return false;

            if (DateTime.UtcNow >= timeout)
            {
                timeout = null;
                return true;
            }
            return false;
        }

        async Task ConnectionTask()
        {
            CancellationToken   ct                  = _cts.Token;
            ReadWriteBuffer     incomingBuffer      = new ReadWriteBuffer();
            AsyncPollSourceSet  pollSet             = new AsyncPollSourceSet();
            Task                writeTask           = null;
            Task<int>           readTask            = null;
            Stream              stream              = null;

            incomingBuffer.Compact(capacity: 8192);

            try
            {
                ConfigArgs  config                  = Config();
                DateTime?   writeTimeout            = null;
                DateTime?   readTimeout             = null;
                DateTime?   writeKeepaliveTimeout   = DateTime.UtcNow + config.WriteKeepaliveInterval;
                DateTime?   readKeepaliveTimeout    = DateTime.UtcNow + config.ReadKeepaliveInterval;
                DateTime?   writeWarnTimeout        = null;
                DateTime?   readWarnTimeout         = null;
                bool        pendingReadWarnTimeout  = false;
                bool        pendingWriteWarnTimeout = false;

                stream = await OpenAndPerformHandshake(config, ct);

                while (true)
                {
                    InvokeOnInfo(ThreadCycleUpdateInfo.Instance);

                    // Cancels and timeouts
                    if (ct.IsCancellationRequested)
                        throw new ConnectionTaskOverException();
                    CheckTimeoutIfNotNull<ReadTimeoutError>(readTimeout);
                    CheckTimeoutIfNotNull<WriteTimeoutError>(writeTimeout);

                    // End write
                    if (TryCompleteWrite(ref writeTask, ct))
                    {
                        writeTimeout = null;
                        if (writeWarnTimeout == null && pendingWriteWarnTimeout == false)
                        {
                            // write warning timer consumed, i.e. warning info has been sent. End warning.
                            InvokeOnInfo(WriteDurationWarningInfo.ForEnd());
                        }
                        else if (writeWarnTimeout == null && pendingWriteWarnTimeout == true)
                        {
                            // warning was set pending, but not sent yet
                            pendingWriteWarnTimeout = false;
                        }
                        else
                        {
                            // cancel write warning timer
                            writeWarnTimeout = null;
                        }

                        // successful starts resets keepalive
                        writeKeepaliveTimeout = DateTime.UtcNow + config.WriteKeepaliveInterval;
                    }

                    // End read
                    if (TryCompleteRead(ref incomingBuffer, ref readTask, ct))
                    {
                        readTimeout = null;
                        if (readWarnTimeout == null && pendingReadWarnTimeout == false)
                        {
                            // read warning timer consumed, i.e. warning info has been sent. End warning.
                            InvokeOnInfo(ReadDurationWarningInfo.ForEnd());
                        }
                        else if (readWarnTimeout == null && pendingReadWarnTimeout == true)
                        {
                            // warning was set pending, but not sent yet
                            pendingReadWarnTimeout = false;
                        }
                        else
                        {
                            // cancel read warning timer
                            readWarnTimeout = null;
                        }
                        // successful read of anything resets read keepalive
                        readKeepaliveTimeout = null;

                        incomingBuffer.Flip();

                        int maxFramedSize = 0;
                        while (true)
                        {
                            bool shouldContinue = TryProcessMessage(ref incomingBuffer, out int framedSize, ct);
                            maxFramedSize = System.Math.Max(maxFramedSize, framedSize);
                            if (!shouldContinue)
                                break;
                        }

                        // Make sure any observed packet can fit in the receive buffer
                        int nextCapacity;
                        if (maxFramedSize > incomingBuffer.Capacity)
                            nextCapacity = System.Math.Max(incomingBuffer.Capacity * 2, maxFramedSize);
                        else
                            nextCapacity = incomingBuffer.Capacity;

                        incomingBuffer.Compact(nextCapacity);
                    }

                    // Begin write
                    if (TryStartWriteTask(stream, ref writeTask, ct))
                    {
                        writeTimeout            = DateTime.UtcNow + config.WriteTimeout;
                        writeWarnTimeout        = DateTime.UtcNow + config.WarnAfterWriteDuration;
                        pendingWriteWarnTimeout = false;

                        // no write keepalives during ongoing write operation
                        writeKeepaliveTimeout   = null;
                    }

                    // Begin read
                    if (TryStartReadTask(stream, ref incomingBuffer, ref readTask, ct))
                    {
                        readTimeout             = DateTime.UtcNow + config.ReadTimeout;
                        readWarnTimeout         = DateTime.UtcNow + config.WarnAfterReadDuration;
                        pendingReadWarnTimeout  = false;

                        // start a keepalive timer to make sure we will read something eventually (if connection is alive).
                        readKeepaliveTimeout    = DateTime.UtcNow + config.ReadKeepaliveInterval;
                    }

                    // Keepalive
                    if (TryConsumeTimeoutIfNotNull(ref writeKeepaliveTimeout))
                    {
                        Log.Verbose("Sending keepalive");
                        EnqueueSendPing32(payload: 1);

                        writeKeepaliveTimeout = DateTime.UtcNow + config.WriteKeepaliveInterval;

                        // Completing keepalive will make sure there will be something to read in the future. Hence
                        // read keepalive (if set) is unnecessary.
                        readKeepaliveTimeout = null;

                    }
                    if (TryConsumeTimeoutIfNotNull(ref readKeepaliveTimeout))
                    {
                        Log.Verbose("Sending keepalive");
                        EnqueueSendPing32(payload: 1);

                        readKeepaliveTimeout = DateTime.UtcNow + config.ReadKeepaliveInterval;

                        // Completing keepalive writes to socket. So reset the write timer, except if
                        // there is an ongoing Write (timeout == null)
                        if (writeKeepaliveTimeout != null)
                            writeKeepaliveTimeout = DateTime.UtcNow + config.WriteKeepaliveInterval;
                    }

                    // Warnings
                    if (TryConsumeTimeoutIfNotNull(ref writeWarnTimeout))
                        pendingWriteWarnTimeout = true;
                    if (TryConsumeTimeoutIfNotNull(ref readWarnTimeout))
                        pendingReadWarnTimeout = true;

                    if (pendingReadWarnTimeout)
                    {
                        InvokeOnInfo(ReadDurationWarningInfo.ForBegin());
                        pendingReadWarnTimeout = false;
                    }
                    if (pendingWriteWarnTimeout)
                    {
                        InvokeOnInfo(WriteDurationWarningInfo.ForBegin());
                        pendingWriteWarnTimeout = false;
                    }

                    // Poll
                    pollSet.Begin();
                    if (readTask != null)
                        pollSet.AddTask(readTask);
                    if (writeTask != null)
                        pollSet.AddTask(writeTask);

                    if (writeTask == null)
                    {
                        // wait for the item in the write buffer only if we could process it. Not
                        // waiting for the signal during a write is not a problem, we will get
                        // awaken by the Write completion.
                        pollSet.AddTask(_writeQueue.NextAvailableAsync());
                    }

                    if (readTimeout != null)
                        pollSet.AddTimeout(readTimeout.Value);
                    if (writeTimeout != null)
                        pollSet.AddTimeout(writeTimeout.Value);
                    if (writeKeepaliveTimeout != null)
                        pollSet.AddTimeout(writeKeepaliveTimeout.Value);
                    if (readKeepaliveTimeout != null)
                        pollSet.AddTimeout(readKeepaliveTimeout.Value);
                    if (readWarnTimeout != null)
                        pollSet.AddTimeout(readWarnTimeout.Value);
                    if (writeWarnTimeout != null)
                        pollSet.AddTimeout(writeWarnTimeout.Value);

                    pollSet.AddTimeout(DateTime.UtcNow + TimeSpan.FromMilliseconds(5000));
                    await pollSet.WaitAsync();
                }
            }
            catch (ConnectionTaskOverException)
            {
                // Ended
            }
            catch (Exception ex)
            {
                // all other exceptions are unexpected
                Log.Warning("unexpected error in message transport: {Exception}", ex);
                InvokeOnError(new StreamExecutorError(ex));
            }
            finally
            {
                // In case we are tearing down the connection without having triggerd CT, trigger it now.
                _cts.Cancel();

                _writeQueue?.Dispose();

                // Let writes and reads, if any, complete (with cancellation) and then kill the stream.
                // For the case of write or read hangs, have a sanity timeout of one second.

                Task deadline = MetaTask.Delay(TimeSpan.FromMilliseconds(1000));

                if (readTask != null)
                {
                    _ = await Task.WhenAny(readTask, deadline);
                    _ = readTask.ContinueWithCtx((Task t) => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                }

                if (writeTask != null)
                {
                    _ = await Task.WhenAny(writeTask, deadline);
                    _ = writeTask.ContinueWithCtx((Task t) => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                }

                stream?.Dispose();
            }
        }

        protected abstract Task<(Stream, TransportHandshakeReport)> OpenStream(CancellationToken ct);
    }

    public class TcpMessageTransport : StreamMessageTransport
    {
        public class CouldNotConnectError : Error
        {
        }
        public class ConnectionRefused : TimeoutError
        {
        }

        class TcpRejectedException : Exception, IHasMinimalErrorDescription
        {
            string IHasMinimalErrorDescription.GetMinimalDescription() => "tcp connection rejected";
        }
        class CannotConnectException : Exception, IHasMinimalErrorDescription
        {
            string IHasMinimalErrorDescription.GetMinimalDescription() => "tcp connection failed";
        }
        class NoAddressException : Exception, IHasMinimalErrorDescription
        {
            string IHasMinimalErrorDescription.GetMinimalDescription() => "no address for target host";
        }

        public new class ConfigArgs : StreamMessageTransport.ConfigArgs
        {
            public string   ServerHostIPv4;
            public string   ServerHostIPv6;
            public int      ServerPort;
            public int      IPv4HeadStartMilliseconds = 1000;
            public TimeSpan DnsCacheMaxTTL = TimeSpan.FromSeconds(5);
        }

        public TcpMessageTransport(IMetaLogger log, ConfigArgs config = null)
            : base(log, config ?? new ConfigArgs())
        {
        }

        public new ConfigArgs Config() => (ConfigArgs)_configBase;

        private async Task<Stream> TryOpenConnectionAsync(string hostname, int port, AddressFamily af, CancellationToken ct)
        {
            IPAddress[] addrs       = await DnsCache.GetHostAddressesAsync(hostname, af, maxTimeToLive: Config().DnsCacheMaxTTL, Log).ConfigureAwaitFalse();
            Socket      socket      = new Socket(af, SocketType.Stream, ProtocolType.Tcp);
            bool        gotAddress  = false;
            bool        gotRejected = false;

            foreach (IPAddress addr in addrs)
            {
                ct.ThrowIfCancellationRequested();

                gotAddress = true;

                Task connectTask;
                try
                {
                    connectTask = socket.ConnectAsync(new IPEndPoint(addr, port));
                }
                catch(Exception ex)
                {
                    Log.Debug("tcp open error: {af}, {Exception}", af, NetworkErrorLoggingUtil.GetMinimalDescription(ex));
                    connectTask = Task.FromException(ex);
                }

                // if fails synchronously, try next
                if (connectTask.IsFaulted)
                {
                    _ = connectTask.Exception; // observe
                    continue;
                }

                try
                {
                    await connectTask.ConfigureAwaitFalse();
                    if (!socket.Connected)
                    {
                        Log.Debug("tcp open completed with rejection: {af}", af);
                        gotRejected = true;
                        continue;
                    }
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch(SocketException ex)
                {
                    Log.Debug("tcp open error: {af}, {Exception}", af, NetworkErrorLoggingUtil.GetMinimalDescription(ex));

                    if (ex.SocketErrorCode == SocketError.ConnectionRefused)
                        gotRejected = true;
                }
                catch(Exception ex)
                {
                    Log.Debug("tcp open error: {af}, {Exception}", af, NetworkErrorLoggingUtil.GetMinimalDescription(ex));
                }
            }

            if (gotRejected)
                throw new TcpRejectedException();
            if (gotAddress)
                throw new CannotConnectException();
            throw new NoAddressException();
        }

        protected override async Task<(Stream, TransportHandshakeReport)> OpenStream(CancellationToken ct)
        {
            Task<Stream>    connectTask4;
            Task<Stream>    connectTask6;
            bool            shouldLogError4;
            bool            shouldLogError6;

            // Run v4 with a head-start
            if (Config().ServerHostIPv4 != null)
            {
                connectTask4 = TryOpenConnectionAsync(Config().ServerHostIPv4, Config().ServerPort, AddressFamily.InterNetwork, ct);
                shouldLogError4 = true;
            }
            else
            {
                connectTask4 = Task.FromException<Stream>(new NoAddressException());
                shouldLogError4 = false;
            }

            Task headStartDelay = MetaTask.Delay(Config().IPv4HeadStartMilliseconds, ct);
            await Task.WhenAny(connectTask4, headStartDelay).ConfigureAwaitFalse();

            if (ct.IsCancellationRequested)
            {
                connectTask4.ContinueWithDispose();
                throw new OperationCanceledException();
            }

            // IPv4 won during headstart?
            if (connectTask4.Status == TaskStatus.RanToCompletion)
            {
                TransportHandshakeReport report = new TransportHandshakeReport(
                    chosenHostname: Config().ServerHostIPv4,
                    chosenProtocol: AddressFamily.InterNetwork,
                    tlsPeerDescription: "tcp");

                return (connectTask4.GetCompletedResult(), report);
            }

            Log.Debug("IPv4 connect did not complete during the head-start, adding IPv6 to the race");

            // V6 joins
            if (Config().ServerHostIPv6 != null)
            {
                connectTask6 = TryOpenConnectionAsync(Config().ServerHostIPv6, Config().ServerPort, AddressFamily.InterNetworkV6, ct);
                shouldLogError6 = true;
            }
            else
            {
                connectTask6 = Task.FromException<Stream>(new NoAddressException());
                shouldLogError6 = false;
            }

            // Race the sockets until one succeeds, or we get cancelled
            Task ctTriggerTask = MetaTask.Delay(-1, ct);
            while(true)
            {
                List<Task> waitedTasks = new List<Task>();

                if (connectTask4.Status != TaskStatus.Faulted)
                    waitedTasks.Add(connectTask4);
                else if (shouldLogError4)
                {
                    shouldLogError4 = false;
                    Log.Debug("v4 connection failed with error {ex}", NetworkErrorLoggingUtil.GetMinimalDescription(connectTask4.Exception));
                }
                if (connectTask6.Status != TaskStatus.Faulted)
                    waitedTasks.Add(connectTask6);
                else if (shouldLogError6)
                {
                    shouldLogError6 = false;
                    Log.Debug("v6 connection failed with error {ex}", NetworkErrorLoggingUtil.GetMinimalDescription(connectTask6.Exception));
                }
                waitedTasks.Add(ctTriggerTask);

                // All race participants have failed? Fail with better exception
                // \note: we read list size to avoid reading Status twice. Might race otherwise.
                if (waitedTasks.Count == 1)
                {
                    connectTask4.ContinueWithDispose();
                    connectTask6.ContinueWithDispose();

                    // which error should we return. Prefer the Refusal as that is more specific
                    Error processedError;
                    var errorFor4 = connectTask4.Exception.GetBaseException();
                    var errorFor6 = connectTask6.Exception.GetBaseException();
                    if (errorFor4 is TcpRejectedException || errorFor6 is TcpRejectedException)
                        processedError = new ConnectionRefused();
                    else
                        processedError = new CouldNotConnectError();
                    InvokeOnError(processedError);
                    throw new ConnectionTaskOverException();
                }

                await Task.WhenAny(waitedTasks).ConfigureAwaitFalse();

                if (ct.IsCancellationRequested)
                {
                    connectTask4.ContinueWithDispose();
                    connectTask6.ContinueWithDispose();
                    throw new OperationCanceledException();
                }

                if (connectTask4.Status == TaskStatus.RanToCompletion)
                {
                    connectTask6.ContinueWithDispose();

                    TransportHandshakeReport report = new TransportHandshakeReport(
                        chosenHostname: Config().ServerHostIPv4,
                        chosenProtocol: AddressFamily.InterNetwork,
                        tlsPeerDescription: "tcp");

                    return (connectTask4.GetCompletedResult(), report);
                }
                else if (connectTask6.Status == TaskStatus.RanToCompletion)
                {
                    connectTask4.ContinueWithDispose();

                    TransportHandshakeReport report = new TransportHandshakeReport(
                        chosenHostname: Config().ServerHostIPv6,
                        chosenProtocol: AddressFamily.InterNetworkV6,
                        tlsPeerDescription: "tcp");

                    return (connectTask6.GetCompletedResult(), report);
                }
            }
        }
    }

    public class TlsMessageTransport : TcpMessageTransport
    {
        public class TlsError : Error
        {
            public enum ErrorCode
            {
                NotAuthenticated,
                FailureWhileAuthenticating,
                NotEncrypted,
            }
            public ErrorCode Error;

            public TlsError(ErrorCode error)
            {
                Error = error;
            }
        }

        public new class ConfigArgs : TcpMessageTransport.ConfigArgs
        {
        }

        public TlsMessageTransport(IMetaLogger log, ConfigArgs config = null)
            : base(log, config ?? new ConfigArgs())
        {
        }

        public new ConfigArgs Config() => (ConfigArgs)_configBase;

        protected override async Task<(Stream, TransportHandshakeReport)> OpenStream(CancellationToken ct)
        {
            MetaTime connectionStartedAt = MetaTime.Now;
            (Stream tcpStream, TransportHandshakeReport tcpReport) = await base.OpenStream(ct).ConfigureAwaitFalse();

            // Establish secure TLS stream
            Log.Debug("Authenticating TLS...");
            SslStream sslStream;

            // TLS authentication
            try
            {
                sslStream = new SslStream(tcpStream, false, CertificateValidationCallback, null);

                Task authenticateTask = sslStream.AuthenticateAsClientAsync(tcpReport.ChosenHostname);
                await Task.WhenAny(authenticateTask, MetaTask.Delay(-1, ct)).ConfigureAwaitFalse();

                if (ct.IsCancellationRequested)
                {
                    Task<Stream> authenticatedTlsStreamTask = authenticateTask.ContinueWithCtx<Stream>(task =>
                    {
                        if (task.Status == TaskStatus.RanToCompletion)
                        {
                            // authentication succeeded, stream is ready
                            return sslStream;
                        }

                        // authentication failed, stream is unusable
                        if (task.IsFaulted)
                            _ = task.Exception;
                        sslStream.Dispose();
                        throw new OperationCanceledException(ct);
                    });

                    AbandonConnectionStream(authenticatedTlsStreamTask, connectionStartedAt);
                    throw new ConnectionTaskOverException();
                }

                // pump exceptions out
                await authenticateTask.ConfigureAwaitFalse();
            }
            catch (ConnectionTaskOverException)
            {
                throw;
            }
            catch (AuthenticationException ex)
            {
                Log.Debug("tls authentication error: {Exception}", ex);

                InvokeOnError(new TlsError(TlsError.ErrorCode.NotAuthenticated));
                throw new ConnectionTaskOverException();
            }
            catch (Exception ex)
            {
                Log.Debug("tls processing error: {Exception}", ex);

                InvokeOnError(new TlsError(TlsError.ErrorCode.FailureWhileAuthenticating));
                throw new ConnectionTaskOverException();
            }

            if (!sslStream.IsEncrypted)
            {
                Log.Debug("tls authentication failed. Expected IsEncrypted.");

                InvokeOnError(new TlsError(TlsError.ErrorCode.NotEncrypted));
                throw new ConnectionTaskOverException();
            }

            string tlsPeerDescription;
            try
            {
                string thumbprint = ((X509Certificate2)sslStream.RemoteCertificate).Thumbprint;
                string issuer = sslStream.RemoteCertificate.Issuer;
                tlsPeerDescription = $"{issuer}, 1.3.6.1.4.1.388.6.3.34.5.1.1.12={thumbprint}"; // Some OID for fingerprint to keep syntax consistent
            }
            catch
            {
                tlsPeerDescription = "error";
            }

            TransportHandshakeReport tlsReport = new TransportHandshakeReport(
                chosenHostname: tcpReport.ChosenHostname,
                chosenProtocol: tcpReport.ChosenProtocol,
                tlsPeerDescription: tlsPeerDescription);
            return (sslStream, tlsReport);
        }

        bool CertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            // Always accept certificate, we just want to encrypt the traffic
            // \todo [petri] would be nice to get full certificate check in place as well
            return true;
        }
    }

    #if UNITY_WEBGL
    // \todo [nomi] Reduce code duplication with StreamMessageTransport
    // \todo [nomi] Add the rest of debugDiagnostics counters
    public class WebSocketTransport : WireMessageTransport
    {
        public class ConfigArgs : StreamMessageTransport.ConfigArgs
        {
            public string WebsocketUrl;
        }

        /// <summary>
        /// Denotes a case where connection task was terminated due to an error (or cancel),
        /// but this case has already been take care of (i.e. error event is raised).
        /// </summary>
        [Serializable]
        protected class ConnectionTaskOverException : Exception
        {
        }

        // Invoked if the WebSocket encounters any error.
        public class WebSocketError : Error
        {
            public string Message { get; private set; }

            public WebSocketError(string message)
            {
                Message = message;
            }
        }

        // Invoked when the websocket connection has been closed by the server.
        public class WebSocketClosedError : Error
        {
            public int    Code     { get; private set; }
            public string Reason   { get; private set; }
            public bool   WasClean { get; private set; }

            public WebSocketClosedError(int code, string reason, bool wasClean)
            {
                Code     = code;
                Reason   = reason;
                WasClean = wasClean;
            }
        }

        public class CouldNotConnectError : Error
        {
        }

        CancellationTokenSource         _cts;
        ConfigArgs                      _args;
        IMetaLogger                     _log;
        LoginTransportDebugDiagnostics  _debugDiagnostics;
        TransportHandshakeReport        _transportHandshake;
        bool                            _onConnectedInvoked;
        bool                            _receivedServerHello;
        bool                            _receivedProtocolHeader;
        MetaTime                        _lastMessageReceivedAt;
        TaskCompletionSource<int>       _callbackErrorSource;
        bool                            _errorEmitted;
        WireMessageWriteQueue           _writeQueue; // Set to non-null when connected. Becomes Disposed after close.

        int _socketId = -1;

        DateTime? _keepAliveTimeout;
        DateTime? _headerTimeout;

        int _maxFramedSize;

        public ConfigArgs Config() => _args;
        protected IMetaLogger Log => _log;

        public WebSocketTransport(IMetaLogger log, ConfigArgs config = null)
        {
            _log  = log;
            _args = config ?? new ConfigArgs();
        }

        /// <inheritdoc />
        public override void SetDebugDiagnosticsRef(LoginTransportDebugDiagnostics debugDiagnostics)
        {
            _debugDiagnostics = debugDiagnostics;
        }

        /// <inheritdoc />
        public override void Open()
        {
            _cts = new CancellationTokenSource();
            _callbackErrorSource = new TaskCompletionSource<int>();
            _ = MetaTask.Run(ConnectionTask);
        }

        async Task ConnectionTask()
        {
            try
            {
                CancellationToken ct = _cts.Token;

                try
                {
                    _socketId = await WebSocketConnector.Connect(Config().WebsocketUrl, ConnectorOnError, ConnectorOnConnectionClosed, ConnectorOnReceive);
                    _transportHandshake = new TransportHandshakeReport(Config().WebsocketUrl, AddressFamily.InterNetwork, "");
                }
                catch (Exception)
                {
                    InvokeOnError(new CouldNotConnectError());
                    throw new ConnectionTaskOverException();
                }

                ConfigArgs config = Config();

                _headerTimeout = DateTime.UtcNow + config.HeaderReadTimeout;
                Task callbackErrorTask = _callbackErrorSource.Task;

                byte[] clientHello = WireMessageWriteQueue.EncodeMessage(
                    new Handshake.ClientHello(
                        clientVersion: config.Version,
                        buildNumber: config.BuildNumber,
                        supportedLogicVersions: config.SupportedLogicVersions,
                        fullProtocolHash: config.FullProtocolHash,
                        commitId: config.CommitId,
                        timestamp: MetaTime.Now,
                        appLaunchId: config.AppLaunchId,
                        clientSessionNonce: config.ClientSessionNonce,
                        clientSessionConnectionNdx: config.ClientSessionConnectionNdx,
                        platform: config.Platform,
                        loginProtocolVersion: config.LoginProtocolVersion));
                SendBytes(clientHello);

                clientHello = null;

                _keepAliveTimeout = DateTime.UtcNow + config.WriteKeepaliveInterval;

                AsyncPollSourceSet pollSet = new AsyncPollSourceSet();
                while (true)
                {
                    // Ask watchdog not to kill us.
                    if (_onConnectedInvoked)
                        InvokeOnInfo(ThreadCycleUpdateInfo.Instance);

                    pollSet.Begin();
                    if (_headerTimeout != null)
                        pollSet.AddTimeout(_headerTimeout.Value);
                    if (_keepAliveTimeout != null)
                        pollSet.AddTimeout(_keepAliveTimeout.Value);
                    pollSet.AddTask(callbackErrorTask);
                    pollSet.AddCancellation(ct);
                    pollSet.AddTimeout(DateTime.UtcNow + TimeSpan.FromSeconds(5));

                    await pollSet.WaitAsync();

                    // Error handled in callback
                    // \note: we check this BEFORE CTS. Callbacks have already happened and they
                    //        have closed the transport already.
                    if (callbackErrorTask.IsCompleted)
                    {
                        throw new ConnectionTaskOverException();
                    }

                    if (_cts.IsCancellationRequested)
                        throw new OperationCanceledException();

                    // Keepalive
                    // \todo [nomi] Different read and write keepalives?
                    if (TryCompleteKeepalive(ref _keepAliveTimeout))
                    {
                        _keepAliveTimeout = DateTime.UtcNow + config.WriteKeepaliveInterval;
                    }

                    if (TryConsumeTimeoutIfNotNull(ref _headerTimeout))
                    {
                        Log.Warning("Did not receive a header within the allotted time.");
                        InvokeOnError(new HeaderTimeoutError());
                        throw new ConnectionTaskOverException();
                    }

                    if ((MetaTime.Now - _lastMessageReceivedAt).ToTimeSpan() > Config().ReadTimeout)
                    {
                        InvokeOnError(new ReadTimeoutError());
                        throw new ConnectionTaskOverException();
                    }
                }
            }
            catch (ConnectionTaskOverException)
            {
                // Handled shutdown. Error has been emitted. No need to do anything.
            }
            catch (OperationCanceledException)
            {
                // Dispose() called. No need to do anything.
            }
            catch (Exception ex)
            {
                if (ex is AggregateException aex && aex.InnerException is OperationCanceledException)
                {
                    // Dispose() called. No need to do anything.
                }
                else
                {
                    // all other exceptions are unexpected
                    Log.Warning("unexpected error in message transport: {Exception}", ex);
                    InvokeOnError(new StreamMessageTransport.StreamExecutorError(ex));
                }
            }

            if (_socketId != -1)
            {
                Log.Info("Closing WebSocket transport...");
                WebSocketConnector.Close(_socketId);
                _socketId = -1;
            }

            _writeQueue?.Dispose();
        }

        void ConnectorOnReceive(byte[] data)
        {
            _lastMessageReceivedAt = MetaTime.Now;

            // Ask watchdog not to kill us.
            if (_onConnectedInvoked)
                InvokeOnInfo(ThreadCycleUpdateInfo.Instance);
            try
            {
                if (!_receivedProtocolHeader)
                {
                    if (TryProcessProtocolHeader(data, out int framedSize))
                    {
                        _receivedProtocolHeader = true;
                        _headerTimeout = null;
                    }
                    _maxFramedSize = System.Math.Max(_maxFramedSize, framedSize);
                }
                else
                {
                    ArraySegment<byte> remainingData = new ArraySegment<byte>(data);
                    while (remainingData.Count > 0)
                    {
                        bool shouldContinue = TryProcessMessage(remainingData, out int framedSize);

                        _maxFramedSize = System.Math.Max(_maxFramedSize, framedSize);

                        if (remainingData.Count <= framedSize)
                            break;

                        // remainingData.Slice(framedSize)
                        remainingData = new ArraySegment<byte>(data, remainingData.Offset + framedSize, remainingData.Count - framedSize);

                        if (!shouldContinue)
                            break;
                    }
                }
            }
            catch (ConnectionTaskOverException)
            {
                // connection error was detected and handled.
                _callbackErrorSource.TrySetResult(0);
            }
        }

        void ConnectorOnError(string err)
        {
            Log.Error(err);
            InvokeOnError(new WebSocketError(err));
            _callbackErrorSource.TrySetResult(0);
        }

        void ConnectorOnConnectionClosed(int code, string reason, bool wasClean)
        {
            Log.Info("WebSocket has closed with code {Code} and reason {Reason}", code, reason);
            InvokeOnError(new WebSocketClosedError(code, reason, wasClean));
            _callbackErrorSource.TrySetResult(0);
        }

        /// <inheritdoc />
        public override void EnqueueClose(object closedErrorPayload)
        {
            Log.Verbose("Enqueuing close.");

            // Close is reported synchronously since all writes are synchronous and hence complete.
            InvokeOnError(new EnqueuedCloseError(closedErrorPayload));
            _callbackErrorSource.TrySetResult(0);
        }

        /// <inheritdoc />
        public override MessageTransportWriteFence EnqueueWriteFence()
        {
            // \todo [nomi] The browser handles writing to the socket so we have no visibility when
            // a message is actually sent.
            return new MessageTransportWriteFence(Task.CompletedTask);
        }

        public override void EnqueueInfo(Info info)
        {
            // WebSocket is only used in WebGL builds. We have only one thread, but we still have to defer the execution.
            _ = MetaTask.Run(() =>
            {
                if (_onConnectedInvoked && !_errorEmitted)
                    InvokeOnInfo(info);
            }, MetaTask.BackgroundScheduler);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            // Mark error as emitted to prevent any furher emissions from a disposed transport
            _errorEmitted = true;
            _cts?.Cancel();
        }

        public override void EnqueueSendMessage(MetaMessage message)
        {
            Interlocked.Increment(ref _debugDiagnostics.MetaMessageEnqueuesAttempted);
            Interlocked.Increment(ref _debugDiagnostics.PacketEnqueuesAttempted);

            if (_writeQueue == null)
            {
                Interlocked.Increment(ref _debugDiagnostics.MetaMessageUnconnectedEnqueuesAttempted);
                Log.Warning("Attempted to enqueue message to a transport that has not yet Connected()");
                return;
            }

            int numBytesEnqueued;
            try
            {
                numBytesEnqueued = _writeQueue.EnqueueMessage(message);
            }
            catch (WireMessageWriteQueue.WireMessageTooLargeException)
            {
                // \todo: handle proper exception
                Interlocked.Increment(ref _debugDiagnostics.MetaMessagePacketSizesExceeded);
                throw;
            }
            catch (WireMessageWriteQueue.CloseEnqueuedException)
            {
                Interlocked.Increment(ref _debugDiagnostics.MetaMessageClosingEnqueuesAttempted);
                Log.Warning("Enqueuing message to a closing transport");
                return;
            }
            catch (ObjectDisposedException)
            {
                Interlocked.Increment(ref _debugDiagnostics.MetaMessageDisposedEnqueuesAttempted);
                Log.Warning("Enqueuing message to a disposed transport");
                return;
            }

            Interlocked.Increment(ref _debugDiagnostics.PacketsEnqueued);
            Interlocked.Add(ref _debugDiagnostics.BytesEnqueued, numBytesEnqueued);
            Interlocked.Increment(ref _debugDiagnostics.MetaMessagesEnqueued);

            FlushWriteQueue();
        }

        bool TryConsumeTimeoutIfNotNull(ref DateTime? timeout)
        {
            if (timeout == null)
                return false;

            if (DateTime.UtcNow >= timeout)
            {
                timeout = null;
                return true;
            }
            return false;
        }

        bool TryCompleteKeepalive(ref DateTime? timeout)
        {
            if (TryConsumeTimeoutIfNotNull(ref timeout))
            {
                Log.Verbose("Sending keepalive");
                SendPing32(payload: 1);
                return true;
            }
            return false;
        }

        private void SendPing32(uint payload)
        {
            Interlocked.Increment(ref _debugDiagnostics.PacketEnqueuesAttempted);
            if (_writeQueue == null)
                return;
            try
            {
                int numBytesEnqueued = _writeQueue.EnqueuePing32(payload);
                Interlocked.Increment(ref _debugDiagnostics.PacketsEnqueued);
                Interlocked.Add(ref _debugDiagnostics.BytesEnqueued, numBytesEnqueued);
            }
            catch (WireMessageWriteQueue.CloseEnqueuedException)
            {
                // Ignore if closing
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already closed
            }

            FlushWriteQueue();
        }

        private void SendPong(byte[] payloadArray, int payloadOffset, int payloadSize)
        {
            Interlocked.Increment(ref _debugDiagnostics.PacketEnqueuesAttempted);
            if (_writeQueue == null)
                return;
            try
            {
                int numBytesEnqueued = _writeQueue.EnqueuePong(payloadArray, payloadOffset, payloadSize);
                Interlocked.Increment(ref _debugDiagnostics.PacketsEnqueued);
                Interlocked.Add(ref _debugDiagnostics.BytesEnqueued, numBytesEnqueued);
            }
            catch (WireMessageWriteQueue.CloseEnqueuedException)
            {
                // Ignore if closing
            }
            catch (ObjectDisposedException)
            {
                // Ignore if already closed
            }

            FlushWriteQueue();
        }

        void FlushWriteQueue()
        {
            for (;;)
            {
                if (!_writeQueue.TryAcquireNext(out WireMessageWriteQueue.OutgoingMessage message))
                    return;
                try
                {
                    MetaDebug.Assert(message.SendBuffer.Buffer != null, "non-data message enqueued to data queue");
                    WebSocketConnector.Send(_socketId, message.SendBuffer.Buffer, message.SendBuffer.Start, message.SendBuffer.Length);
                }
                finally
                {
                    _writeQueue.ReleaseAcquired();
                }
            }
        }

        void SendBytes(byte[] buffer)
        {
            Interlocked.Increment(ref _debugDiagnostics.PacketEnqueuesAttempted);

            // Send to websocket connector
            Log.Verbose("Sending {Bytes} bytes", buffer.Length);
            WebSocketConnector.Send(_socketId, buffer);
            Interlocked.Increment(ref _debugDiagnostics.PacketsEnqueued);
            Interlocked.Add(ref _debugDiagnostics.BytesEnqueued, buffer.Length);
        }

        bool TryProcessProtocolHeader(byte[] buffer, out int framedSize)
        {
            framedSize = WireProtocol.ProtocolHeaderSize;
            if (buffer.Length < WireProtocol.ProtocolHeaderSize)
                return false;

            ProtocolStatus status = WireProtocol.ParseProtocolHeader(buffer, 0, Config().GameMagic);

            if (status == ProtocolStatus.InvalidGameMagic)
            {
                UInt32 magic32 = ((uint)buffer[3 + WireProtocol.PacketHeaderSize] << 0)
                               | ((uint)buffer[2 + WireProtocol.PacketHeaderSize] << 8)
                               | ((uint)buffer[1 + WireProtocol.PacketHeaderSize] << 16)
                               | ((uint)buffer[0 + WireProtocol.PacketHeaderSize] << 24);
                InvokeOnError(new InvalidGameMagic(magic32));
                throw new ConnectionTaskOverException();
            }
            else if (status == ProtocolStatus.WireProtocolVersionMismatch)
            {
                int serverProtocolVersion = buffer[4 + WireProtocol.PacketHeaderSize];
                InvokeOnError(new WireProtocolVersionMismatch(serverProtocolVersion));
                throw new ConnectionTaskOverException();
            }
            else if (status != ProtocolStatus.ClusterRunning)
            {
                InvokeOnError(new ProtocolStatusError(status));
                throw new ConnectionTaskOverException();
            }
            return true;
        }

        bool TryProcessMessage(ArraySegment<byte> inBuffer, out int framedSize)
        {
            framedSize = WireProtocol.PacketHeaderSize;
            if (inBuffer.Count < framedSize)
                return false;

            WirePacketHeader header;
            try
            {
                header = WireProtocol.DecodePacketHeader(inBuffer.Array, inBuffer.Offset, enforcePacketPayloadSizeLimit: false);
            }
            catch (Exception ex)
            {
                // \todo: more fine grained exception handling
                InvokeOnError(new WireFormatError(ex));
                throw new ConnectionTaskOverException();
            }

            framedSize = WireProtocol.PacketHeaderSize + header.PayloadSize;
            if (inBuffer.Count < framedSize)
                return false;

            // Decompress message
            byte[] bytes;
            int payloadOffset;
            int payloadSize;
            switch (header.Compression)
            {
                case WirePacketCompression.None:
                    bytes = inBuffer.Array;
                    payloadOffset = WireProtocol.PacketHeaderSize + inBuffer.Offset;
                    payloadSize = header.PayloadSize;
                    break;

                case WirePacketCompression.Deflate:
                    bytes         = CompressUtil.DeflateDecompress(inBuffer.Array, inBuffer.Offset + WireProtocol.PacketHeaderSize, header.PayloadSize);
                    payloadOffset = 0;
                    payloadSize   = bytes.Length;
                    break;

                default:
                    throw new InvalidOperationException($"Invalid CompressionMode in packet: {header.Compression}");
            }

            Interlocked.Increment(ref _debugDiagnostics.PacketsRead);

            // Decode message and handle it
            switch (header.Type)
            {
                case WirePacketType.Message:
                {
                    MetaMessage message;
                    try
                    {
                        message = WireProtocol.DecodeMessage(bytes, payloadOffset, payloadSize, resolver: null);
                    }
                    catch (Exception ex)
                    {
                        // \todo: more fine grained exception handling
                        InvokeOnError(new WireFormatError(ex));
                        throw new ConnectionTaskOverException();
                    }

                    Interlocked.Increment(ref _debugDiagnostics.MetaMessagesRead);

                    // First message is ServerHello
                    if (!_receivedServerHello)
                    {
                        if (message is Handshake.ServerHello serverHello)
                        {
                            _receivedServerHello = true;

                            // Success, inform upper layers
                            _writeQueue = new WireMessageWriteQueue();
                            _onConnectedInvoked = true;
                            InvokeOnConnect(serverHello, _transportHandshake);
                            InvokeOnInfo(ThreadCycleUpdateInfo.Instance);
                        }
                        else
                        {
                            InvokeOnError(new MissingHelloError());
                            throw new ConnectionTaskOverException();
                        }
                    }
                    else
                    {
                        if (message is Handshake.ClientHelloAccepted helloAccepted)
                            _writeQueue.EnableCompression(helloAccepted.ServerOptions.EnableWireCompression);

                        Log.Verbose("Receive message of type {Type}", message.GetType());
                        // Normal payload message
                        InvokeOnReceive(message);
                    }

                    Interlocked.Increment(ref _debugDiagnostics.MetaMessagesReceived);

                    break;
                }

                case WirePacketType.Ping:
                    // Got ping, respond with pong
                    SendPong(bytes, payloadOffset, payloadSize);
                    break;

                case WirePacketType.PingResponse:
                    // Got pong. Nothing to do.
                    break;

                default:
                    InvokeOnError(new WireFormatError(new Exception($"unrecognized WirePacketType: {header.Type}")));
                    throw new ConnectionTaskOverException();
            }

            return true;
        }

        protected new void InvokeOnError(Error e)
        {
            if (!_errorEmitted)
            {
                _errorEmitted = true;
                base.InvokeOnError(e);
            }
        }
    }
    #endif
}
