// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Message;
using Metaplay.Core.Tasks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.Core.Network
{
    /// <summary>
    /// An IMessageTransport wrapper that is able to inject connection errors on demand.
    /// </summary>
    public sealed class FaultInjectingMessageTransport : MessageTransport
    {
        IMessageTransport _innerTransport;
        readonly object _lock;
        readonly TaskQueueExecutor _executor;
        bool _errored;
        List<object> _haltedEvents;
        List<object> _haltedCommands;
        MetaTimer _streamReadTimeoutTimer;
        MetaTimer _streamReadWarningTimer;
        bool _readWarningActive;
        bool _onConnectEmitted;
        bool _openRequested;
        Error _pendingOnOpenError;

        struct HaltedOnConnect
        {
            public Handshake.ServerHello    ServerHello;
            public TransportHandshakeReport HandshakeReport;

            public HaltedOnConnect(Handshake.ServerHello serverHello, TransportHandshakeReport handshakeReport)
            {
                ServerHello = serverHello;
                HandshakeReport = handshakeReport;
            }
        }
        class EnqueueCloseCmd
        {
            public readonly object Payload;
            public EnqueueCloseCmd(object payload)
            {
                Payload = payload;
            }
        }

        public FaultInjectingMessageTransport(IMessageTransport innerTransport)
        {
            _innerTransport = innerTransport;
            _innerTransport.OnConnect += HandleOnConnect;
            _innerTransport.OnReceive += HandleOnReceive;
            _innerTransport.OnInfo += HandleOnInfo;
            _innerTransport.OnError += HandleOnError;
            _lock = new object();

            // This transport is only used on Editor and in botclients. On both, the scheduler is available.
            _executor = new TaskQueueExecutor(TaskScheduler.Default);

            _errored = false;
            _haltedEvents = null;
            _haltedCommands = null;
        }

        /// <summary>
        /// Disposes the underlying transport immediately (i.e. does not flush). OnError event is emitted asynchronously.
        /// </summary>
        public void InjectError(MessageTransport.Error error)
        {
            lock(_lock)
            {
                if (_errored)
                    return;
                if (_openRequested)
                {
                    _errored = true;
                    PostEvent(() => InvokeOnError(error));
                }
                else if (_pendingOnOpenError == null)
                {
                    _pendingOnOpenError = error;
                }
            }
        }

        /// <summary>
        /// Suspends dispatch of new events and commands.
        /// </summary>
        public void Halt()
        {
            lock(_lock)
            {
                if (_haltedEvents == null)
                    _haltedEvents = new List<object>();
                if (_haltedCommands == null)
                    _haltedCommands = new List<object>();

                // emulate read warnings and timeouts
                if (_innerTransport is StreamMessageTransport smt)
                {
                    if (!_onConnectEmitted)
                    {
                        if (_streamReadTimeoutTimer == null)
                        {
                            _streamReadTimeoutTimer = new MetaTimer(
                                callback:   StreamConnectTimeout,
                                state:      null,
                                dueTime:    smt.Config().ConnectTimeout,
                                period:     TimeSpan.FromMilliseconds(-1));
                        }
                    }
                    else
                    {
                        if (_streamReadWarningTimer == null)
                        {
                            _streamReadWarningTimer = new MetaTimer(
                                callback:   StreamReadTimeoutWarning,
                                state:      null,
                                dueTime:    smt.Config().WarnAfterReadDuration,
                                period:     TimeSpan.FromMilliseconds(-1));
                        }
                        if (_streamReadTimeoutTimer == null)
                        {
                            _streamReadTimeoutTimer = new MetaTimer(
                                callback:   StreamReadTimeout,
                                state:      null,
                                dueTime:    smt.Config().ReadTimeout,
                                period:     TimeSpan.FromMilliseconds(-1));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Restores execution of events and commands. Buffered events are played back asynchronously.
        /// </summary>
        public void Resume()
        {
            lock(_lock)
            {
                // Note: lists may change in response to commands and events. Must not use foreach

                if (_readWarningActive)
                {
                    PostEvent(() => InvokeOnInfo(StreamMessageTransport.ReadDurationWarningInfo.ForEnd()));
                    _readWarningActive = false;
                }

                if (_haltedEvents != null)
                {
                    for (int i = 0; i < _haltedEvents.Count; ++i)
                    {
                        if (_errored)
                            break;
                        switch(_haltedEvents[i])
                        {
                            case HaltedOnConnect onConnect:
                                _onConnectEmitted = true;
                                PostEvent(() => InvokeOnConnect(onConnect.ServerHello, onConnect.HandshakeReport));
                                break;
                            case MetaMessage message:
                                PostEvent(() => InvokeOnReceive(message));
                                break;
                            case Info info:
                                PostEvent(() => InvokeOnInfo(info));
                                break;
                            case Error error:
                                _errored = true;
                                PostEvent(() => InvokeOnError(error));
                                break;
                        }
                    }
                    _haltedEvents = null;
                }

                if (_haltedCommands != null)
                {
                    for (int i = 0; i < _haltedCommands.Count; ++i)
                    {
                        if (_errored)
                            break;
                        switch(_haltedCommands[i])
                        {
                            case int cmdcode when cmdcode == 0:
                                _innerTransport?.Open();
                                break;
                            case EnqueueCloseCmd enqueueCloseCmd:
                                _innerTransport?.EnqueueClose(enqueueCloseCmd.Payload);
                                break;
                            case MetaMessage message:
                                _innerTransport?.EnqueueSendMessage(message);
                                break;
                            case TaskCompletionSource<int> tcs:
                                MessageTransportWriteFence innerFence = _innerTransport?.EnqueueWriteFence();
                                if (innerFence == null)
                                {
                                    // forget TCS. It never completes
                                }
                                else
                                {
                                    _ = innerFence.WhenComplete.ContinueWithCtx((task) => tcs.TrySetResult(1));
                                }
                                break;

                        }
                    }
                    _haltedCommands = null;
                }

                _streamReadTimeoutTimer?.Dispose();
                _streamReadTimeoutTimer = null;

                _streamReadWarningTimer?.Dispose();
                _streamReadWarningTimer = null;
            }
        }

        /// <summary>
        /// The number of SendMessages halted.
        /// </summary>
        public int NumHaltedSendMessages
        {
            get
            {
                int count = 0;
                lock(_lock)
                {
                    if (_haltedCommands != null)
                    {
                        for (int i = 0; i < _haltedCommands.Count; ++i)
                        {
                            if (_haltedCommands[i] is MetaMessage)
                                count++;
                        }
                    }
                }
                return count;
            }
        }

        // Timers

        private void StreamReadTimeout(object _)
        {
            lock(_lock)
            {
                if (_errored)
                    return;
                _errored = true;
                PostEvent(() => InvokeOnError(new StreamMessageTransport.ReadTimeoutError()));
            }
            Dispose();
        }
        private void StreamConnectTimeout(object _)
        {
            lock(_lock)
            {
                if (_errored)
                    return;
                _errored = true;
                PostEvent(() => InvokeOnError(new StreamMessageTransport.ConnectTimeoutError()));
            }
            Dispose();
        }
        private void StreamReadTimeoutWarning(object _)
        {
            lock(_lock)
            {
                if (_errored)
                    return;
                if (!_readWarningActive)
                    PostEvent(() => InvokeOnInfo(StreamMessageTransport.ReadDurationWarningInfo.ForBegin()));
                _readWarningActive = true;
            }
        }

        // IMessageTransport events

        private void HandleOnConnect(Handshake.ServerHello serverHello, TransportHandshakeReport handshakeReport)
        {
            lock(_lock)
            {
                if (_errored)
                    return;
                if (_haltedEvents != null)
                {
                    _haltedEvents.Add(new HaltedOnConnect(serverHello, handshakeReport));
                    return;
                }
                _onConnectEmitted = true;

                PostEvent(() => InvokeOnConnect(serverHello, handshakeReport));
            }
        }
        private void HandleOnReceive(MetaMessage message)
        {
            lock(_lock)
            {
                if (_errored)
                    return;
                if (_haltedEvents != null)
                {
                    _haltedEvents.Add(message);
                    return;
                }

                PostEvent(message, (FaultInjectingMessageTransport self, object msgObject) => self.InvokeOnReceive((MetaMessage)msgObject));
            }
        }
        private void HandleOnInfo(Info info)
        {
            lock(_lock)
            {
                if (_errored)
                    return;
                if (_haltedEvents != null && !(info is StreamMessageTransport.ThreadCycleUpdateInfo))
                {
                    _haltedEvents.Add(info);
                    return;
                }

                PostEvent(info, (FaultInjectingMessageTransport self, object infoObj) => self.InvokeOnInfo((Info)infoObj));
            }
        }
        private void HandleOnError(Error error)
        {
            lock(_lock)
            {
                if (_errored)
                    return;

                if (_haltedEvents != null)
                {
                    _haltedEvents.Add(error);
                    return;
                }

                // error is only stored if it was not caught by halt
                _errored = true;
                PostEvent(() => InvokeOnError(error));
            }
        }

        // IMessageTransport

        public override void Dispose()
        {
            _innerTransport?.Dispose();
            _innerTransport = null;

            lock (_lock)
            {
                _errored = true;
            }
        }

        public override void EnqueueClose(object closedErrorPayload)
        {
            lock(_lock)
            {
                if (_haltedCommands != null)
                {
                    _haltedCommands.Add(new EnqueueCloseCmd(closedErrorPayload));
                    return;
                }
            }

            _innerTransport?.EnqueueClose(closedErrorPayload);
        }

        public override void EnqueueSendMessage(MetaMessage message)
        {
            lock(_lock)
            {
                if (_haltedCommands != null)
                {
                    _haltedCommands.Add(message);
                    return;
                }
            }

            _innerTransport?.EnqueueSendMessage(message);
        }

        public override MessageTransportWriteFence EnqueueWriteFence()
        {
            lock(_lock)
            {
                if (_haltedCommands != null)
                {
                    TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();
                    _haltedCommands.Add(tcs);
                    return new MessageTransportWriteFence(tcs.Task);
                }
            }

            return _innerTransport?.EnqueueWriteFence();
        }

        public override void EnqueueInfo(Info info)
        {
            lock(_lock)
            {
                if (_onConnectEmitted && !_errored)
                    PostEvent(() => InvokeOnInfo(info));
            }
        }

        public override void Open()
        {
            lock(_lock)
            {
                if (_openRequested)
                    throw new InvalidOperationException("Open can only be called once");
                _openRequested = true;

                // Already injected error triggers immediately on open
                if (_pendingOnOpenError != null && !_errored)
                {
                    _errored = true;
                    PostEvent(() => InvokeOnError(_pendingOnOpenError));
                    return;
                }

                if (_haltedCommands != null)
                {
                    int cmdcode = 0;
                    _haltedCommands.Add(cmdcode);
                    return;
                }
            }

            _innerTransport?.Open();
        }

        public override void SetDebugDiagnosticsRef(LoginTransportDebugDiagnostics debugDiagnostics)
        {
            _innerTransport?.SetDebugDiagnosticsRef(debugDiagnostics);
        }

        // Internal

        void PostEvent(Action action)
        {
            PostEvent(action, (FaultInjectingMessageTransport self, object action_) =>
            {
                Action action = (Action)action_;
                action();
            });
        }

        void PostEvent(object payload, Action<FaultInjectingMessageTransport, object> action)
        {
            _executor.EnqueueAsync(this, action, payload, (object self_, object action_, object payload_) =>
            {
                FaultInjectingMessageTransport self = (FaultInjectingMessageTransport)self_;
                Action<FaultInjectingMessageTransport, object> action = (Action<FaultInjectingMessageTransport, object>)action_;

                // Don't push events after dispose
                bool isDisposed = (self._innerTransport == null);
                if (isDisposed)
                    return;

                action(self, payload_);
            });
        }
    }
}
