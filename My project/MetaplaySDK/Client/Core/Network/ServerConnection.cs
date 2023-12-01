// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Client.Messages;
using Metaplay.Core.Message;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.Session;
using Metaplay.Core.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Core.Network
{
    /// <summary>
    /// Configure whether the client and the server commit ids need to match for the connection to proceed.
    /// </summary>
    public enum ClientServerCommitIdCheckRule
    {
        Disabled,       // CommitIds are not checked (\note Unity will default to this value for existing projects)
        OnlyIfDefined,  // CommitIds must match, if defined (useful to allow logging in without checks when running in Unity, where CommitId isn't defined)
        Strict,         // CommitIds must always match (if either side doesn't define the commit id, the connection attempt is terminated with an error)
    }

    public enum ConnectionInternalWatchdogType
    {
        Transport,
        Resetup,
    }

    enum ServerHandshakePhase
    {
        NotConnected = 0,
        WaitingForHelloAcceptedFollowedByCreateGuestAccountResponse,
        WaitingForHelloAcceptedFollowedByLoginCompletion,
        WaitingForHelloAcceptedFollowedBySessionResumption,
        WaitingForCreateGuestAccountResponse,
        WaitingForCreateGuestAccountCreationHandledByUser,
        WaitingForLoginCompletion,
        WaitingForSessionResumption,
        WaitingForSessionStartCompletion,
        WaitingForResourceCorrectionHandledByUser,
        WaitingForResumeSessionAfterConnectionDropHandledByUser,
        InSession,
        Error,
    }
    static class ServerHandshakePhaseExtensions
    {
        public static bool CanHandleLogicVersionMismatch(this ServerHandshakePhase phase)
        {
            switch (phase)
            {
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedByCreateGuestAccountResponse:
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedByLoginCompletion:
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedBySessionResumption:
                case ServerHandshakePhase.WaitingForCreateGuestAccountResponse:
                case ServerHandshakePhase.WaitingForLoginCompletion:
                case ServerHandshakePhase.WaitingForSessionResumption:
                    return true;
                default:
                    return false;
            }
        }
        public static bool CanHandleOngoingMaintenance(this ServerHandshakePhase phase)
        {
            switch (phase)
            {
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedByCreateGuestAccountResponse:
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedByLoginCompletion:
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedBySessionResumption:
                case ServerHandshakePhase.WaitingForCreateGuestAccountResponse:
                case ServerHandshakePhase.WaitingForLoginCompletion:
                case ServerHandshakePhase.WaitingForSessionResumption:
                    return true;
                default:
                    return false;
            }
        }
        public static bool CanHandleOperationStillOngoing(this ServerHandshakePhase phase)
        {
            switch (phase)
            {
                case ServerHandshakePhase.WaitingForCreateGuestAccountResponse:
                case ServerHandshakePhase.WaitingForLoginCompletion:
                case ServerHandshakePhase.WaitingForSessionStartCompletion:
                    return true;
                default:
                    return false;
            }
        }
        public static bool CanHandleLoginProtocolVersionMismatch(this ServerHandshakePhase phase)
        {
            switch (phase)
            {
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedByCreateGuestAccountResponse:
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedByLoginCompletion:
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedBySessionResumption:
                    return true;
                default:
                    return false;
            }
        }
        public static bool CanHandleRedirectToServer(this ServerHandshakePhase phase)
        {
            switch (phase)
            {
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedByCreateGuestAccountResponse:
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedByLoginCompletion:
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedBySessionResumption:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Implements Server-Client protocol on top of a IMessageTransport.
    /// Also session management is done.
    /// </summary>
    /// \todo [nuutti] Should session management be instead be implemented
    ///                in some separate class that would be implemented
    ///                on top of ServerConnection?
    public class ServerConnection
    {
        public class UnexpectedLoginMessageError : MessageTransport.Error
        {
            public string MessageType { get; }
            public UnexpectedLoginMessageError (string type)
            {
                MessageType = type;
            }
        }
        public class LogicVersionMismatchError : MessageTransport.Error
        {
            public MetaVersionRange     ClientSupportedVersions { get; }
            public MetaVersionRange     ServerAcceptedVersions  { get; }
            public LogicVersionMismatchError(MetaVersionRange clientSupportedVersions, MetaVersionRange serverAcceptedVersions)
            {
                ClientSupportedVersions = clientSupportedVersions;
                ServerAcceptedVersions = serverAcceptedVersions;
            }
        }
        public class LoginProtocolVersionMismatchError : MessageTransport.Error
        {
            public int ClientVersion { get; }
            public int ServerVersion { get; }
            public LoginProtocolVersionMismatchError(int clientVersion, int serverVersion)
            {
                ClientVersion = clientVersion;
                ServerVersion = serverVersion;
            }
        }
        public class RedirectToServerError : MessageTransport.Error
        {
            public ServerEndpoint RedirectToServer { get; }
            public RedirectToServerError(ServerEndpoint redirectToServer)
            {
                RedirectToServer = redirectToServer;
            }
        }
        public class FullProtocolHashMismatchInfo : MessageTransport.Info
        {
            public uint ClientProtocolHash { get; }
            public uint ServerProtocolHash { get; }
            public FullProtocolHashMismatchInfo(uint clientProtocolHash, uint serverProtocolHash)
            {
                ClientProtocolHash = clientProtocolHash;
                ServerProtocolHash = serverProtocolHash;
            }
        }
        public class CommitIdMismatchMismatchError : MessageTransport.Error
        {
            public string ClientCommitId { get; }
            public string ServerCommitId { get; }
            public CommitIdMismatchMismatchError(string clientCommitId, string serverCommitId)
            {
                ClientCommitId = clientCommitId;
                ServerCommitId = serverCommitId;
            }
        }
        public class SessionResumeFailed : MessageTransport.Error
        {
            public SessionResumeFailed() { }
        }
        public class SessionStartFailed : MessageTransport.Error
        {
            public string Message { get; }
            public SessionStartFailed(string message)
            {
                Message = message ?? throw new ArgumentNullException(nameof(message));
            }
        }
        public class SessionForceTerminatedError : MessageTransport.Error
        {
            public SessionForceTerminateReason Reason { get; }

            public SessionForceTerminatedError(SessionForceTerminateReason reason)
            {
                Reason = reason;
            }
        }
        public class SessionError : MessageTransport.Error
        {
            public string Reason { get; }

            public SessionError(string reason)
            {
                Reason = reason;
            }
        }
        public class WatchdogDeadlineExceededError : MessageTransport.Error
        {
            public ConnectionInternalWatchdogType WatchdogType { get; }

            public WatchdogDeadlineExceededError(ConnectionInternalWatchdogType watchdogType)
            {
                WatchdogType = watchdogType;
            }
        }
        public class PlayerIsBannedError : MessageTransport.Error
        {
            public PlayerIsBannedError() { }
        }

        public class PlayerDeserializationFailureError : MessageTransport.Error
        {
            public string Error { get; }

            public PlayerDeserializationFailureError(string error)
            {
                Error = error;
            }
        }

        public class MaintenanceModeOngoingError : MessageTransport.Error
        {
            public MaintenanceModeOngoingError() { }
        }

        public class TransportLifecycleInfo : MessageTransport.Info
        {
            public bool IsTransportAttached { get; }

            public TransportLifecycleInfo(bool isTransportAttached)
            {
                IsTransportAttached = isTransportAttached;
            }
        }

        public class GotServerHello : MessageTransport.Info
        {
            public Handshake.ServerHello ServerHello { get; }

            public GotServerHello(Handshake.ServerHello serverHello)
            {
                ServerHello = serverHello;
            }
        }

        /// <summary>Guest account has been created</summary>
        public class GuestAccountCreatedInfo : MessageTransport.Info
        {
            public readonly ISessionCredentialService.GuestCredentials GuestCredentials;

            public GuestAccountCreatedInfo(string deviceId, string authToken, EntityId playerId)
            {
                GuestCredentials = new ISessionCredentialService.GuestCredentials(
                    deviceId:       deviceId,
                    authToken:      authToken,
                    playerIdHint:   playerId);
            }
        }

        /// <summary>
        /// Marker for when Client sends the start request. This is useful for timeout management as this allows
        /// determining if the pending operation is for waiting for client to complete preparation steps or for server
        /// to reply to the request.
        /// </summary>
        public class SessionStartRequested : MessageTransport.Info
        {
        }

        /// <summary>
        /// Session start was refused due to server refusing resource proposal. Client should apply
        /// the supplied resource correction.
        /// </summary>
        public class ResourceCorrectionInfo : MessageTransport.Info
        {
            public readonly SessionProtocol.SessionResourceCorrection ResourceCorrection;

            public ResourceCorrectionInfo(SessionProtocol.SessionResourceCorrection resourceCorrection)
            {
                ResourceCorrection = resourceCorrection;
            }
        }

        /// <summary>
        /// A service (injected dependency) has failed. This cannot be handled by the protocol manager and connection has been terminated.
        /// </summary>
        public class ServiceFailureError : MessageTransport.Error
        {
            public readonly Exception Error;
            public ServiceFailureError(Exception error)
            {
                Error = error;
            }
        }

        public class SessionConnectionErrorLostInfo : MessageTransport.Info
        {
            public SessionResumptionAttempt Attempt;
            public SessionConnectionErrorLostInfo(SessionResumptionAttempt attempt)
            {
                Attempt = attempt;
            }
        }

        public class Config
        {
            public MetaDuration                                     ConnectTimeout;
            public ClientServerCommitIdCheckRule                    CommitIdCheckRule                           = ClientServerCommitIdCheckRule.Disabled;
            public string                                           DeviceModel;
            public Handshake.ILoginRequestGamePayload               LoginGamePayload                            = null;
            public ISessionStartRequestGamePayload                  SessionStartGamePayload                     = null;
        };

        public struct Stats
        {
            /// <summary>
            /// Duration from start of the connection to completion of protocol handshake. Not valid until ConnectedToServer is received.
            /// </summary>
            public TimeSpan DurationToConnected;

            /// <summary>
            /// Duration from start of the connection to Login success. Not valid until MessageLoginSuccess is received.
            /// </summary>
            public TimeSpan DurationToLoginSuccess;
        }

        public class SessionResumptionAttempt
        {
            /// <summary>
            /// The latest error that caused running session's connection loss, or subsequent reconnect attempt to fail.
            /// </summary>
            public readonly MessageTransport.Error   LatestTransportError;

            /// <summary>
            /// The time when running session's connection was initially lost.
            /// </summary>
            public readonly DateTime                 StartTime;

            /// <summary>
            /// The time when the latest error happened. The first error time, or the time of the latest reconnection failure.
            /// </summary>
            public readonly DateTime                 LatestErrorTime;

            /// <summary>
            /// The reconnection number of this resumption. First reconnection attempt is 1.
            /// </summary>
            public readonly int                      NumConnectionAttempts;

            SessionResumptionAttempt(MessageTransport.Error latestTransportError, DateTime startTime, DateTime latestErrorTime, int numConnectionAttempts)
            {
                LatestTransportError = latestTransportError;
                StartTime = startTime;
                LatestErrorTime = latestErrorTime;
                NumConnectionAttempts = numConnectionAttempts;
            }

            public static SessionResumptionAttempt ForFirstError(MessageTransport.Error error)
            {
                DateTime now = DateTime.UtcNow;
                return new SessionResumptionAttempt(error, now, now, numConnectionAttempts: 1);
            }

            public static SessionResumptionAttempt ForSubsequentError(SessionResumptionAttempt previous, MessageTransport.Error latestTransportError)
            {
                DateTime now = DateTime.UtcNow;
                return new SessionResumptionAttempt(latestTransportError, previous.StartTime, now, numConnectionAttempts: previous.NumConnectionAttempts + 1);
            }
        }

        public class DebugInfo
        {
            public SessionResumptionAttempt SessionResumptionAttempt    { get; }
            public DateTime?                ConnectionStartTime         { get; }

            public DebugInfo(SessionResumptionAttempt sessionResumptionAttempt, DateTime? connectionStartTime)
            {
                SessionResumptionAttempt    = sessionResumptionAttempt;
                ConnectionStartTime         = connectionStartTime;
            }
        }

        class SessionState
        {
            public SessionParticipantState  SessionParticipant          { get; }
            public EntityId                 PlayerId                    { get; }
            public byte[]                   ResumptionToken             { get; }
            public SessionResumptionAttempt CurrentResumptionAttempt    { get; set; } = null; // \note null when resumption attempt is not ongoing

            public SessionState(SessionParticipantState sessionParticipant, EntityId playerId, byte[] resumptionToken)
            {
                SessionParticipant = sessionParticipant;
                PlayerId = playerId;
                ResumptionToken = resumptionToken;
            }
        }

        public delegate IMessageTransport CreateTransportFn(ServerGateway serverGateway);
        public delegate LoginDebugDiagnostics GetDebugDiagnosticsFn(bool isSessionResumption);

        /// <summary>
        /// Filter for messages. This method is called for each message after builtin protocol processing but immediately before
        /// delivering it to receive queue for SDK and game specific message handling. Returning <c>true</c> filters out the message,
        /// i.e. it wont be placed into the receive queue. Returning <c>false</c> causes the message to be processed normally.
        /// <para>
        /// The filter is executed synhronously on the network thread and it must not block. Blocking network thread
        /// will cause internal timeouts and eventually connection loss.
        /// </para>
        /// </summary>
        public delegate bool EarlyMessageFilterSyncFn(MetaMessage msg);

        IMetaLogger                                     Log             { get; }

        readonly Config                                 _config;
        readonly ServerEndpoint                         _endpoint;
        readonly SessionNonceService                    _nonceService;
        readonly ISessionDeviceGuidService              _guidService;
        readonly ISessionCredentialService.LoginMethod  _loginMethod;
        IMessageTransport                               _transport                      = null;
        object                                          _transportLock                  = new object();
        object                                          _incomingLock                   = new object();
        List<MetaMessage>                               _incomingMessages               = new List<MetaMessage>();
        MessageTransport.Error                          _incomingError;
        ServerHandshakePhase                            _handshakePhase                 = ServerHandshakePhase.NotConnected;
        object                                          _enqueueCloseLock               = new object();
        bool                                            _closeEnqueued                  = false;
        CreateTransportFn                               _createTransport;
        readonly BuildVersion                           _buildVersion;
        SessionProtocol.SessionResourceProposal         _resourceProposal;
        ClientAppPauseStatus                            _clientAppPauseStatus;
        int                                             _currentSessionStartQueryId     = 0;
        GetDebugDiagnosticsFn                           _getDebugDiagnostics;
        EarlyMessageFilterSyncFn                        _earlyMessageFilterSyncFn;
        SessionState                                    _currentSession                 = null;
        object                                          _currentSessionSendQueueLock    = new object(); // Lock for _currentSession.SessionParticipant's send queue and sending messages with _transport.EnqueueSendMessage.
        TaskCompletionSource<MessageTransport.Error>    _terminatingErrorTask;
        DateTime                                        _connectionStartTime;
        Stats                                           _statistics;
        DateTime                                        _watchdogDeadlineAt;
        TimeSpan                                        _watchdogDeadlineLastDuration;
        object                                          _watchdogLock                   = new object();
        bool                                            _enableWatchdog;
        Action                                          _reportWatchdogViolation;
        DateTime                                        _previousUpdateAt;
        ServerGateway                                   _currentGateway;
        int                                             _numSuccessfulSessionResumes = 0;
        CancellationTokenSource                         _disposeCts                     = new CancellationTokenSource();

        volatile LoginServerConnectionDebugDiagnostics  _connDiagnostics                = new LoginServerConnectionDebugDiagnostics();
        volatile LoginTransportDebugDiagnostics         _transportDiagnostics           = new LoginTransportDebugDiagnostics();

        public ServerGateway    CurrentGateway      => _currentGateway;
        public Stats            Statistics          => _statistics;
        public DebugInfo        DebugInformation    => new DebugInfo(_currentSession?.CurrentResumptionAttempt, _transport != null ? (DateTime?)_connectionStartTime : null);

        public LoginSessionDebugDiagnostics TryGetLoginSessionDebugDiagnostics()
        {
            lock (_currentSessionSendQueueLock)
            {
                if (_currentSession == null)
                    return null;
                else
                {
                    IEnumerable<PlayerFlushActions> pendingPlayerFlushActions = _currentSession.SessionParticipant.RememberedSent.OfType<PlayerFlushActions>();

                    int[] firstFewRememberedSentTypeCodes =
                        _currentSession.SessionParticipant.RememberedSent
                        .Take(3)
                        .Select(msg => MetaMessageRepository.Instance.TryGetFromType(msg.GetType(), out MetaMessageSpec spec)
                                       ? spec.TypeCode
                                       : -1)
                        .ToArray();

                    return new LoginSessionDebugDiagnostics
                    {
                        NumSent                                 = _currentSession.SessionParticipant.NumSent,
                        NumRememberedSent                       = _currentSession.SessionParticipant.RememberedSent.Count,
                        FirstNRememberedSentMessageTypeCodes    = firstFewRememberedSentTypeCodes,
                        TotalPendingFlushActionsOperationsBytes = pendingPlayerFlushActions.Sum(flush => flush.Operations.Bytes.Length),
                        TotalPendingFlushActionsChecksums       = pendingPlayerFlushActions.Sum(flush => flush.Checksums.Length),
                        PreviousTransportErrorName              = _currentSession.CurrentResumptionAttempt?.LatestTransportError?.GetType().Name,
                    };
                }
            }
        }

        public LoginServerConnectionDebugDiagnostics GetLoginServerConnectionDebugDiagnostics() => _connDiagnostics.Clone();
        public LoginTransportDebugDiagnostics GetLoginTransportDebugDebugDiagnostics() => _transportDiagnostics.Clone();

        public ServerConnection(
            IMetaLogger                                 log,
            Config                                      config,
            ServerEndpoint                              endpoint,
            SessionNonceService                         nonceService,
            ISessionDeviceGuidService                   guidService,
            ISessionCredentialService.LoginMethod       loginMethod,
            CreateTransportFn                           createTransport,
            BuildVersion                                buildVersion,
            SessionProtocol.SessionResourceProposal     initialResourceProposal,
            ClientAppPauseStatus                        initialClientAppPauseStatus,
            GetDebugDiagnosticsFn                       getDebugDiagnostics,
            EarlyMessageFilterSyncFn                    earlyMessageFilterSync,
            bool                                        enableWatchdog,
            Action                                      reportWatchdogViolation,
            int                                         numFailedConnectionAttempts)
        {
            Log = log;
            _config = config;
            _endpoint = endpoint;
            _nonceService = nonceService;
            _guidService = guidService;
            _loginMethod = loginMethod;
            _createTransport = createTransport;
            _buildVersion = buildVersion;
            _resourceProposal = initialResourceProposal;
            _clientAppPauseStatus = initialClientAppPauseStatus;
            _getDebugDiagnostics = getDebugDiagnostics;
            _earlyMessageFilterSyncFn = earlyMessageFilterSync;
            _enableWatchdog = enableWatchdog;
            _reportWatchdogViolation = reportWatchdogViolation;
            _terminatingErrorTask = new TaskCompletionSource<MessageTransport.Error>();
            _previousUpdateAt = DateTime.UtcNow;

            _currentGateway = ServerGatewayScheduler.SelectGatewayForInitialConnection(_endpoint, numFailedConnectionAttempts);

            SetupTransport();
        }

        void SetWatchdog(TimeSpan watchdogValidFor)
        {
            DateTime requestedDeadline = DateTime.UtcNow + watchdogValidFor;
            lock(_watchdogLock)
            {
                _watchdogDeadlineAt = requestedDeadline;
                _watchdogDeadlineLastDuration = watchdogValidFor;
            }
        }

        void IncreaseWatchdog(TimeSpan watchdogValidFor)
        {
            DateTime requestedDeadline = DateTime.UtcNow + watchdogValidFor;
            lock(_watchdogLock)
            {
                if (requestedDeadline > _watchdogDeadlineAt)
                {
                    _watchdogDeadlineAt = requestedDeadline;
                    _watchdogDeadlineLastDuration = watchdogValidFor;
                }
            }
        }

        public void Dispose()
        {
            DisposeWithCause(null);
        }

        void DisposeWithCause(MessageTransport.Error error)
        {
            _disposeCts.Cancel();

            DisposeTransport();
            _terminatingErrorTask.TrySetResult(error);
        }

        void DisposeTransport()
        {
            lock (_transportLock)
            {
                IMessageTransport transport = _transport;
                if (transport == null)
                    return;

                _transport = null;
                transport.Dispose();

                transport.SetDebugDiagnosticsRef(null);
                transport.OnConnect     -= HandleOnConnect;
                transport.OnError       -= HandleOnError;
                transport.OnReceive     -= HandleOnReceive;
                transport.OnInfo        -= HandleOnInfo;
            }
        }

        void SetupTransport()
        {
            lock (_transportLock)
            {
                MetaDebug.Assert(_transport == null, "Tried to setup transport but old transport still exists");
                MetaDebug.Assert(!_disposeCts.IsCancellationRequested, "Tried to setup transport after Dispose was called");

                _connectionStartTime = DateTime.UtcNow;

                SetWatchdog(TimeSpan.FromMilliseconds(_config.ConnectTimeout.Milliseconds) + TimeSpan.FromSeconds(5));

                _transport = _createTransport(_currentGateway);
                _transport.SetDebugDiagnosticsRef(_transportDiagnostics);
                _transport.OnConnect    += HandleOnConnect;
                _transport.OnError      += HandleOnError;
                _transport.OnReceive    += HandleOnReceive;
                _transport.OnInfo       += HandleOnInfo;
                EnqueueInfo(new TransportLifecycleInfo(isTransportAttached: true));

                _transport.Open();
            }

            _nonceService.NewConnection();

            Interlocked.Increment(ref _connDiagnostics.TransportsCreated);
        }

        /// <summary>
        /// Appends received messages into <paramref name="outputMessages" />, optionally
        /// received infos into <paramref name="outputInfos"/>, and returns the connection
        /// error state that was observed after receiving those messages. If no error has
        /// been observed, returns null.
        /// </summary>
        public MessageTransport.Error ReceiveMessages(List<MetaMessage> outputMessages)
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan timeSinceLastUpdate = now - _previousUpdateAt;
            _previousUpdateAt = now;

            // Kill connection if deadline exceeded. (And connection is running and not terminated).
            // Unless Now jumps between frames (device skips time or CPU starvation), in which case we restart the timers since the watchdogs are not reliable.

            if (timeSinceLastUpdate > TimeSpan.FromSeconds(30))
            {
                TimeSpan watchdogDuration;
                lock(_watchdogLock)
                {
                    watchdogDuration = _watchdogDeadlineLastDuration;
                }
                SetWatchdog(watchdogDuration);
            }

            bool watchdogDeadlineExceeded;
            lock(_watchdogLock)
            {
                watchdogDeadlineExceeded = _enableWatchdog && (now > _watchdogDeadlineAt);
            }
            if (watchdogDeadlineExceeded)
            {
                MessageTransport.Error incomingError;
                lock (_incomingLock)
                {
                    incomingError = _incomingError;
                }

                // \todo: which locks?
                ServerHandshakePhase phase = _handshakePhase;

                if (incomingError == null && phase != ServerHandshakePhase.Error && phase != ServerHandshakePhase.WaitingForResumeSessionAfterConnectionDropHandledByUser)
                {
                    Log.Warning("Transport watchdog deadline exceeded. Killing transport.");
                    _reportWatchdogViolation();
                    DropTransportWithError(new WatchdogDeadlineExceededError(ConnectionInternalWatchdogType.Transport));
                }
            }

            lock (_incomingLock)
            {
                // Copy all messages and infos to output
                outputMessages.AddRange(_incomingMessages);
                _incomingMessages.Clear();
                return _incomingError;
            }
        }

        /// <summary>
        /// Enqueues session payload message for delivery.
        /// </summary>
        /// <returns>true, if the message was enqueued for delivery in the session, false otherwise</returns>
        public bool EnqueueSendMessage(MetaMessage payloadMessage)
        {
            Interlocked.Increment(ref _connDiagnostics.SessionMessageEnqueuesAttempted);
            bool wasBuffered;

            lock (_currentSessionSendQueueLock)
            {
                // not connected yet, drop anything else
                // \note Handshake phase may be other than InSession due to a retained session.
                //       As long as we have a session, messages can be enqueued (though will
                //       not be sent to transport right away if we don't have transport).
                if (_currentSession == null)
                    return false;

                SessionUtil.HandleSendPayloadMessage(_currentSession.SessionParticipant, payloadMessage);

                IMessageTransport transport = _transport;
                if (_handshakePhase == ServerHandshakePhase.InSession && transport != null)
                {
                    Interlocked.Increment(ref _connDiagnostics.SessionMessageImmediateSendEnqueues);
                    Interlocked.CompareExchange(ref _connDiagnostics.FirstSessionMessageSentAtMS, MetaTime.Now.MillisecondsSinceEpoch, 0);
                    wasBuffered = false;
                    transport.EnqueueSendMessage(payloadMessage);
                }
                else
                {
                    Interlocked.Increment(ref _connDiagnostics.SessionMessagesDelayedSendEnqueues);
                    wasBuffered = true;
                }
            }

            Interlocked.Increment(ref _connDiagnostics.SessionMessagesEnqueues);

            if (Log.IsVerboseEnabled)
            {
                if (!wasBuffered)
                    Log.Verbose("Outgoing session payload message (type {MessageType}, index {MessageIndex})", payloadMessage.GetType().Name, _currentSession.SessionParticipant.NumSent);
                else
                    Log.Verbose("Buffered outgoing session payload message (type {MessageType}, index {MessageIndex})", payloadMessage.GetType().Name, _currentSession.SessionParticipant.NumSent);
            }

            return true;
        }

        /// <summary>
        /// Enqueues connection to be closed after other enqueued messages are first processed.
        /// Note that the time required this to complete is not bounded. If you depend on this to
        /// complete "reasonably" fast, you should consider a custom "reasonable" timeout.
        /// </summary>
        /// <param name="payload">Marker payload set for the matching <see cref="MessageTransport.EnqueuedCloseError"/>. This can be used to differentiate which close closed the connection.</param>
        public Task EnqueueCloseAsync(object payload)
        {
            Task completionTask;
            bool hadTransportToClose;

            lock (_enqueueCloseLock)
            {
                _closeEnqueued = true;

                IMessageTransport transport =  _transport;
                if (transport != null)
                {
                    transport.EnqueueClose(payload);
                    hadTransportToClose = true;
                }
                else
                {
                    // \note Calling TerminateWithError from here should be safe,
                    //       even though it is normally called from the transport thread,
                    //       because no transport exists that would call it.
                    // \todo [nuutti] This specific error type in this scenario is a bit dirty - there's no transport involved
                    TerminateWithError(new MessageTransport.EnqueuedCloseError(payload));
                    hadTransportToClose = false;
                }

                completionTask = _terminatingErrorTask.Task;
            }

            if (!hadTransportToClose)
            {
                Log.Info("Got EnqueueCloseAsync while there's no transport; terminated immediately");
            }

            return completionTask;
        }

        /// <summary>
        /// Tries to enqueue a write fence. If successful, the returned fence completes when
        /// all messages sent before the fence have been submitted to the socket. This does
        /// not guarantee delivery to the server. The fence may never complete. Returns null
        /// if fence could not be enqueued.
        /// </summary>
        public MessageTransportWriteFence TryEnqueueTransportWriteFence()
        {
            IMessageTransport transport;
            lock (_currentSessionSendQueueLock)
            {
                // If there is no session, no point in flushing. Non-session communications
                // are expected to be lossy, and there is no point in waiting them.
                if (_handshakePhase != ServerHandshakePhase.InSession)
                    return null;

                transport = _transport;
                if (transport == null)
                    return null;
            }
            return transport.EnqueueWriteFence();
        }

        public void OnApplicationResume()
        {
            // resumed from background. Give some time for threads to resolve states.
            IncreaseWatchdog(TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// Retries session start if it was rejected by <see cref="ResourceCorrectionInfo"/> message.
        /// </summary>
        public void RetrySessionStart(SessionProtocol.SessionResourceProposal resourceProposal, ClientAppPauseStatus clientAppPauseStatus)
        {
            _resourceProposal = resourceProposal;
            _clientAppPauseStatus = clientAppPauseStatus;

            if (_handshakePhase != ServerHandshakePhase.WaitingForResourceCorrectionHandledByUser)
                return;

            _handshakePhase = ServerHandshakePhase.WaitingForSessionStartCompletion;
            EnqueueInfo(new SessionStartRequested());
            _transport.EnqueueSendMessage(new SessionProtocol.SessionStartRequest(
                queryId:                        ++_currentSessionStartQueryId,
                deviceGuid:                     _guidService.TryGetDeviceGuid(),
                deviceModel:                    _config.DeviceModel,
                timeZoneInfo:                   PlayerTimeZoneInfo.CreateForCurrentDevice(),
                resourceProposal:               _resourceProposal,
                isDryRun:                       false,
                gamePayload:                    _config.SessionStartGamePayload,
                supportedArchiveCompressions:   CompressUtil.GetSupportedDecompressionAlgorithms(),
                clientAppPauseStatus:           _clientAppPauseStatus));
        }

        /// <summary>
        /// Aborts session start and optionally sends the incident payload to the server. If message could not be sent, returns false.
        /// </summary>
        public bool AbortSessionStart(SessionProtocol.SessionStartAbortReasonTrailer optionalReason)
        {
            IMessageTransport transport = _transport;
            if (transport == null)
                return false;

            transport.EnqueueSendMessage(new SessionProtocol.SessionStartAbort(hasReasonTrailer: optionalReason != null));
            if (optionalReason != null)
                transport.EnqueueSendMessage(optionalReason);
            return true;
        }

        /// <summary>
        /// Signals connection to continue login after the created account information is processed by the supervisor.
        /// </summary>
        public void ContinueGuestLoginAfterAccountCreation(ISessionCredentialService.GuestCredentials guestCredentials)
        {
            if (_handshakePhase != ServerHandshakePhase.WaitingForCreateGuestAccountCreationHandledByUser)
                return;

            // After login credentials have been created, log in with them.
            _handshakePhase = ServerHandshakePhase.WaitingForLoginCompletion;
            RequestLoginAndSessionStartWithCredentials(guestCredentials);
        }

        void EnqueueInfo(MessageTransport.Info info)
        {
            lock (_incomingLock)
                _incomingMessages.Add(new MessageTransportInfoWrapperMessage(info));
        }

        void TerminateWithError(MessageTransport.Error error)
        {
            _handshakePhase = ServerHandshakePhase.Error;
            lock (_currentSessionSendQueueLock)
                _currentSession = null;

            lock (_incomingLock)
            {
                _incomingMessages.Add(new DisconnectedFromServer());
                _incomingError = error;
            }
            DisposeWithCause(error);
        }

        void HandleOnConnect(Handshake.ServerHello serverHello, MessageTransport.TransportHandshakeReport transportHandshake)
        {
            Log.Debug("Connected to server.");

            // \note: Transport has sent and receive hello
            Interlocked.Increment(ref _connDiagnostics.HellosSent);
            Interlocked.Increment(ref _connDiagnostics.HellosReceived);

            _statistics.DurationToConnected = DateTime.UtcNow - _connectionStartTime;

            bool isConnectionIPv4 = transportHandshake.ChosenProtocol == System.Net.Sockets.AddressFamily.InterNetwork;

            // Inform higher-ups
            lock (_incomingLock)
                _incomingMessages.Add(new ConnectedToServer(transportHandshake.ChosenHostname, isConnectionIPv4, transportHandshake.TlsPeerDescription));

            SetWatchdog(TimeSpan.FromSeconds(10));

            HandleOnReceiveServerHello(serverHello);
        }

        void HandleOnError(MessageTransport.Error error)
        {
            Log.Debug("Transport lost {Error}", error);

            switch (error)
            {
                case WireMessageTransport.ProtocolStatusError protocolStatusError:
                {
                    lock (_incomingLock)
                        _incomingMessages.Add(new ConnectionHandshakeFailure(protocolStatusError.status));
                    break;
                }
            }

            switch (error)
            {
                case StreamMessageTransport.StreamClosedError _:    Interlocked.Increment(ref _connDiagnostics.StreamClosedErrors); break;
                case StreamMessageTransport.StreamIOFailedError _:  Interlocked.Increment(ref _connDiagnostics.StreamIOFailedErrors); break;
                case StreamMessageTransport.StreamExecutorError _:  Interlocked.Increment(ref _connDiagnostics.StreamExecutorErrors); break;
                case StreamMessageTransport.ConnectTimeoutError _:  Interlocked.Increment(ref _connDiagnostics.ConnectTimeoutErrors); break;
                case StreamMessageTransport.HeaderTimeoutError _:   Interlocked.Increment(ref _connDiagnostics.HeaderTimeoutErrors); break;
                case StreamMessageTransport.ReadTimeoutError _:     Interlocked.Increment(ref _connDiagnostics.ReadTimeoutErrors); break;
                case StreamMessageTransport.WriteTimeoutError _:    Interlocked.Increment(ref _connDiagnostics.WriteTimeoutErrors); break;
                default:                                            Interlocked.Increment(ref _connDiagnostics.OtherErrors); break;
            }

            DropTransportWithError(error);
        }

        void DispatchToInternalHandlers(MetaMessage message)
        {
            // Handshakes, common early responses
            if (message is Handshake.LogicVersionMismatch mismatch && _handshakePhase.CanHandleLogicVersionMismatch())
            {
                HandleLogicVersionMismatch(mismatch);
                return;
            }
            else if (message is Handshake.OngoingMaintenance maintenance && _handshakePhase.CanHandleOngoingMaintenance())
            {
                HandleOngoingMaintenance(maintenance);
                return;
            }
            else if (message is Handshake.OperationStillOngoing && _handshakePhase.CanHandleOperationStillOngoing())
            {
                // pass
                return;
            }
            else if (message is Handshake.LoginProtocolVersionMismatch loginVersionMismatch && _handshakePhase.CanHandleLoginProtocolVersionMismatch())
            {
                HandleLoginProtocolVersionMismatch(loginVersionMismatch);
                return;
            }
            else if (message is Handshake.RedirectToServer redirect && _handshakePhase.CanHandleRedirectToServer())
            {
                HandleRedirectToServer(redirect);
                return;
            }

            // Handshakes, phase-specific handlers.
            switch (_handshakePhase)
            {
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedByCreateGuestAccountResponse:
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedByLoginCompletion:
                case ServerHandshakePhase.WaitingForHelloAcceptedFollowedBySessionResumption:
                {
                    if (message is Handshake.ClientHelloAccepted helloAccepted)
                        HandleClientHelloAccepted(helloAccepted);
                    else
                    {
                        Log.Debug("Expected hello response, got {Message}", PrettyPrint.Compact(message));
                        TerminateWithError(new UnexpectedLoginMessageError(message.GetType().Name));
                    }
                    return;
                }

                case ServerHandshakePhase.WaitingForCreateGuestAccountResponse:
                {
                    if (message is Handshake.CreateGuestAccountResponse createAccountResponse)
                        HandleCreateGuestAccountResponse(createAccountResponse);
                    else
                    {
                        Log.Debug("Expected account creation response, got {Message}", PrettyPrint.Compact(message));
                        TerminateWithError(new UnexpectedLoginMessageError(message.GetType().Name));
                    }
                    return;
                }

                case ServerHandshakePhase.WaitingForLoginCompletion:
                {
                    if (message is Handshake.LoginSuccessResponse loginSuccess)
                        HandleLoginSuccessResponse(loginSuccess);
                    else
                    {
                        Log.Debug("Expected login response, got {Message}", PrettyPrint.Compact(message));
                        TerminateWithError(new UnexpectedLoginMessageError(message.GetType().Name));
                    }
                    return;
                }

                case ServerHandshakePhase.WaitingForSessionStartCompletion:
                {
                    if (message is SessionProtocol.SessionStartSuccess startSucceess)
                        HandleSessionStartSuccess(startSucceess);
                    else if (message is SessionProtocol.SessionStartFailure startFailure)
                        HandleSessionStartFailure(startFailure);
                    else if (message is SessionProtocol.SessionStartResourceCorrection startResourceCorrection)
                        HandleSessionStartResourceCorrection(startResourceCorrection);
                    else
                    {
                        Log.Debug("Expected session response, got {Message}", PrettyPrint.Compact(message));
                        TerminateWithError(new UnexpectedLoginMessageError(message.GetType().Name));
                    }
                    return;
                }

                case ServerHandshakePhase.WaitingForSessionResumption:
                {
                    if (message is SessionProtocol.SessionResumeSuccess resumeSuccess)
                        HandleSessionResumeSuccess(resumeSuccess);
                    else if (message is SessionProtocol.SessionResumeFailure resumeFailure)
                        HandleSessionResumeFailure(resumeFailure);
                    else
                    {
                        Log.Debug("Expected session response, got {Message}", PrettyPrint.Compact(message));
                        TerminateWithError(new UnexpectedLoginMessageError(message.GetType().Name));
                    }
                    return;
                }

                case ServerHandshakePhase.InSession:
                {
                    if (message is SessionForceTerminateMessage forceTerminate)
                    {
                        Log.Info("Got {Message}", PrettyPrint.Compact(forceTerminate));
                        TerminateWithError(new SessionForceTerminatedError(forceTerminate.Reason));
                    }
                    else
                    {
                        Interlocked.Increment(ref _connDiagnostics.SessionMessagesReceived);

                        // \note Need to lock because incoming acknowledgements modify our send queue.
                        SessionUtil.ReceiveResult result;
                        lock (_currentSessionSendQueueLock)
                            result = SessionUtil.HandleReceive(_currentSession.SessionParticipant, message);
                        switch (result.Type)
                        {
                            case SessionUtil.ReceiveResult.ResultType.ReceivedValidAck:
                                if (Log.IsVerboseEnabled)
                                    Log.Verbose("Handled session acknowledgement from server: {Success}", PrettyPrint.Compact(result.ValidAck));
                                break;

                            case SessionUtil.ReceiveResult.ResultType.ReceivedFaultyAck:
                                // \note Acknowledgement should never fail, assuming a legitimate server.
                                TerminateWithError(new SessionError($"Session acknowledgement failure: {PrettyPrint.Compact(result.FaultyAck)}"));
                                break;

                            case SessionUtil.ReceiveResult.ResultType.ReceivedPayloadMessage:
                                Interlocked.Increment(ref _connDiagnostics.SessionPayloadMessagesReceived);
                                OnSessionPayloadMessageReceivedFromServer(result.PayloadMessage, message);
                                break;

                            default:
                                throw new MetaAssertException($"Unknown {nameof(SessionUtil.ReceiveResult)}: {result}");
                        }
                    }
                    return;
                }
            }
        }

        void HandleOnReceive(MetaMessage message)
        {
            DispatchToInternalHandlers(message);

            // Add to incoming queue, even if we processed the message
            // Except if we just errored
            if (_handshakePhase != ServerHandshakePhase.Error)
            {
                bool dropMessage = false;

                // Filter out
                if (_earlyMessageFilterSyncFn != null)
                {
                    try
                    {
                        if (_earlyMessageFilterSyncFn.Invoke(message))
                            dropMessage = true;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Early message filter failed, ignored: {Ex}", ex);
                    }
                }

                if (!dropMessage)
                {
                    lock (_incomingLock)
                        _incomingMessages.Add(message);
                }
            }
        }

        void HandleOnReceiveServerHello(Handshake.ServerHello serverHello)
        {
            Log.Debug("Received {Message}", PrettyPrint.Compact(serverHello));

            EnqueueInfo(new GotServerHello(serverHello));

            // Warn about mismatching protocol hash (but allow game to continue)
            // \todo [petri] keep track of mismatch / expose the mismatch to outside, so can include info when reporting Desyncs?
            if (serverHello.FullProtocolHash != MetaSerializerTypeRegistry.FullProtocolHash)
            {
                Log.Warning("Full protocol hash mismatch: client=0x{ClientProtocol:X8} vs server=0x{ServerProtocol:X8}", MetaSerializerTypeRegistry.FullProtocolHash, serverHello.FullProtocolHash);
                EnqueueInfo(new FullProtocolHashMismatchInfo(
                    clientProtocolHash: MetaSerializerTypeRegistry.FullProtocolHash,
                    serverProtocolHash: serverHello.FullProtocolHash));
            }

            // Verify client/server commit ids, if either:
            // - Using ClientServerCommitIdCheckRule.Always
            // - Using ClientServerCommitIdCheckRule.OnlyIfDefined AND both client & server CommitIds are defined.
            bool isClientCommitIdDefined = !string.IsNullOrEmpty(_buildVersion.CommitId) && (_buildVersion.CommitId != "undefined");
            bool isServerCommitIdDefined = !string.IsNullOrEmpty(serverHello.CommitId) && (serverHello.CommitId != "undefined");
            bool shouldCompareCommitIds =
                (_config.CommitIdCheckRule == ClientServerCommitIdCheckRule.Strict) ||
                (_config.CommitIdCheckRule == ClientServerCommitIdCheckRule.OnlyIfDefined && isClientCommitIdDefined && isServerCommitIdDefined);
            bool areCommitIdsEqual = _buildVersion.CommitId == serverHello.CommitId && isClientCommitIdDefined && isServerCommitIdDefined;
            if (shouldCompareCommitIds && !areCommitIdsEqual)
            {
                Log.Warning("Commit Id mismatch: client={ClientCommitId}, server={ServerCommitId}, checkRule={CheckRule} (you may configure this from the DeploymentConfig / CommitIdCheckRule)", _buildVersion.CommitId, serverHello.CommitId, _config.CommitIdCheckRule);
                TerminateWithError(new CommitIdMismatchMismatchError(_buildVersion.CommitId, serverHello.CommitId));
                return;
            }

            // Try to resume existing session
            if (_currentSession != null)
            {
                _handshakePhase = ServerHandshakePhase.WaitingForHelloAcceptedFollowedBySessionResumption;

                LoginDebugDiagnostics debugDiagnostics = _getDebugDiagnostics(isSessionResumption: true);
                _transport.EnqueueSendMessage(new Handshake.LoginAndResumeSessionRequest(
                    claimedPlayerId:    _currentSession.PlayerId,
                    sessionToResume:    SessionResumptionInfo.FromParticipantState(_currentSession.SessionParticipant),
                    resumptionToken:    _currentSession.ResumptionToken,
                    debugDiagnostics:   debugDiagnostics,
                    gamePayload:        _config.LoginGamePayload));
                Interlocked.Increment(ref _connDiagnostics.ResumptionLoginsSent);
                return;
            }

            // Login and start a new session. If login method requires a a new guest account, request it.
            bool needToCreateGuestAccount = false;
            if (_loginMethod is ISessionCredentialService.NewGuestAccountLoginMethod)
                needToCreateGuestAccount = true;
            if (_loginMethod is ISessionCredentialService.DualSocialAuthLoginMethod dualLogin && dualLogin.CreateGuestAccount)
                needToCreateGuestAccount = true;
            if (needToCreateGuestAccount)
            {
                _handshakePhase = ServerHandshakePhase.WaitingForHelloAcceptedFollowedByCreateGuestAccountResponse;
                _transport.EnqueueSendMessage(new Handshake.CreateGuestAccountRequest());
                return;
            }

            _handshakePhase = ServerHandshakePhase.WaitingForHelloAcceptedFollowedByLoginCompletion;
            RequestLoginAndSessionStartWithCredentials(default);
        }

        void RequestLoginAndSessionStartWithCredentials(ISessionCredentialService.GuestCredentials createdAccountCredentials)
        {
            // Send login request
            LoginDebugDiagnostics debugDiagnostics = _getDebugDiagnostics(isSessionResumption: false);
            MetaMessage loginMessage;

            switch (_loginMethod)
            {
                case ISessionCredentialService.NewGuestAccountLoginMethod _:
                {
                    loginMessage = new Handshake.DeviceLoginRequest(
                        deviceId:           createdAccountCredentials.DeviceId,
                        authToken:          createdAccountCredentials.AuthToken,
                        playerIdHint:       createdAccountCredentials.PlayerIdHint,
                        isBot:              false,
                        debugDiagnostics:   debugDiagnostics,
                        gamePayload:        _config.LoginGamePayload);
                    break;
                }

                case ISessionCredentialService.GuestAccountLoginMethod guestAccount:
                {
                    loginMessage = new Handshake.DeviceLoginRequest(
                        deviceId:           guestAccount.GuestCredentials.DeviceId,
                        authToken:          guestAccount.GuestCredentials.AuthToken,
                        playerIdHint:       guestAccount.GuestCredentials.PlayerIdHint,
                        isBot:              false,
                        debugDiagnostics:   debugDiagnostics,
                        gamePayload:        _config.LoginGamePayload);
                    break;
                }

                case ISessionCredentialService.BotLoginMethod botAccount:
                {
                    loginMessage = new Handshake.DeviceLoginRequest(
                        deviceId:           botAccount.BotCredentials.DeviceId,
                        authToken:          botAccount.BotCredentials.AuthToken,
                        playerIdHint:       botAccount.BotCredentials.PlayerIdHint,
                        isBot:              true,
                        debugDiagnostics:   debugDiagnostics,
                        gamePayload:        _config.LoginGamePayload);
                    break;
                }

                case ISessionCredentialService.SocialAuthLoginMethod socialAuth:
                {
                    loginMessage = new Handshake.SocialAuthenticationLoginRequest(
                        claim:              socialAuth.Claim,
                        playerIdHint:       socialAuth.PlayerIdHint,
                        isBot:              socialAuth.IsBot,
                        debugDiagnostics:   debugDiagnostics,
                        gamePayload:        _config.LoginGamePayload);
                    break;
                }

                case ISessionCredentialService.DualSocialAuthLoginMethod dualSocialAuth:
                {
                    loginMessage = new Handshake.DualSocialAuthenticationLoginRequest(
                        playerIdHint:       dualSocialAuth.CreateGuestAccount ? createdAccountCredentials.PlayerIdHint : dualSocialAuth.PlayerIdHint,
                        isBot:              dualSocialAuth.IsBot,
                        debugDiagnostics:   debugDiagnostics,
                        gamePayload:        _config.LoginGamePayload,
                        claim:              dualSocialAuth.Claim,
                        deviceId:           dualSocialAuth.CreateGuestAccount ? createdAccountCredentials.DeviceId : dualSocialAuth.DeviceId,
                        authToken:          dualSocialAuth.CreateGuestAccount ? createdAccountCredentials.AuthToken : dualSocialAuth.AuthToken);
                    break;
                }

                default:
                {
                    TerminateWithError(new ServiceFailureError(new InvalidOperationException("Invalid LoginMethod from SessionCredentialService")));
                    return;
                }
            }
            _transport.EnqueueSendMessage(loginMessage);

            // Send session start request
            EnqueueInfo(new SessionStartRequested());
            _transport.EnqueueSendMessage(new SessionProtocol.SessionStartRequest(
                queryId:                        ++_currentSessionStartQueryId,
                deviceGuid:                     _guidService.TryGetDeviceGuid(),
                deviceModel:                    _config.DeviceModel,
                timeZoneInfo:                   PlayerTimeZoneInfo.CreateForCurrentDevice(),
                resourceProposal:               _resourceProposal,
                isDryRun:                       false,
                gamePayload:                    _config.SessionStartGamePayload,
                supportedArchiveCompressions:   CompressUtil.GetSupportedDecompressionAlgorithms(),
                clientAppPauseStatus:           _clientAppPauseStatus));

            Interlocked.Increment(ref _connDiagnostics.InitialLoginsSent);
        }

        void HandleClientHelloAccepted(Handshake.ClientHelloAccepted accepted)
        {
            if (_handshakePhase == ServerHandshakePhase.WaitingForHelloAcceptedFollowedByCreateGuestAccountResponse)
            {
                // Guest Account creation already pipelined
                _handshakePhase = ServerHandshakePhase.WaitingForCreateGuestAccountResponse;
            }
            else if (_handshakePhase == ServerHandshakePhase.WaitingForHelloAcceptedFollowedByLoginCompletion)
            {
                // Login request already pipelined
                _handshakePhase = ServerHandshakePhase.WaitingForLoginCompletion;
            }
            else if (_handshakePhase == ServerHandshakePhase.WaitingForHelloAcceptedFollowedBySessionResumption)
            {
                // Session resumption already pipelined
                _handshakePhase = ServerHandshakePhase.WaitingForSessionResumption;
            }
            else
                throw new InvalidOperationException("unreachable");
        }

        void HandleCreateGuestAccountResponse(Handshake.CreateGuestAccountResponse createAccountResponse)
        {
            // Server created guest account for us. Use them.
            _handshakePhase = ServerHandshakePhase.WaitingForCreateGuestAccountCreationHandledByUser;
            EnqueueInfo(new GuestAccountCreatedInfo(
                deviceId:   createAccountResponse.DeviceId,
                authToken:  createAccountResponse.AuthToken,
                playerId:   createAccountResponse.PlayerId));
        }

        void HandleLoginSuccessResponse(Handshake.LoginSuccessResponse loginResponse)
        {
            Interlocked.Increment(ref _connDiagnostics.LoginSuccessesReceived);
            Volatile.Write(ref _connDiagnostics.LastLoginSuccessReceivedAtMS, MetaTime.Now.MillisecondsSinceEpoch);

            Log.Info("Successfully logged in");

            _statistics.DurationToLoginSuccess = DateTime.UtcNow - _connectionStartTime;
            _handshakePhase = ServerHandshakePhase.WaitingForSessionStartCompletion;
        }

        void HandleLogicVersionMismatch(Handshake.LogicVersionMismatch versionMismatch)
        {
            MetaVersionRange acceptedVersions = versionMismatch.ServerAcceptedLogicVersions;
            Log.Warning("Client LogicVersion is too old for server: clientSupportedLogicVersions={ClientSupportedLogicVersions}, serverAcceptedLogicVersions={ServerLogicVersionMin}..{ServerLogicVersionMax}", MetaplayCore.Options.SupportedLogicVersions, acceptedVersions.MinVersion, acceptedVersions.MaxVersion);
            TerminateWithError(new LogicVersionMismatchError(MetaplayCore.Options.SupportedLogicVersions, acceptedVersions));
        }

        void HandleLoginProtocolVersionMismatch(Handshake.LoginProtocolVersionMismatch versionMismatch)
        {
            Log.Warning("Client Protocol version is not compatible with server: client={ClientProtocolVersion}, server={ServerProtocolVersion}", MetaplayCore.LoginProtocolVersion, versionMismatch.ServerAcceptedProtocolVersion);
            TerminateWithError(new LoginProtocolVersionMismatchError(MetaplayCore.LoginProtocolVersion, versionMismatch.ServerAcceptedProtocolVersion));
        }

        void HandleOngoingMaintenance(Handshake.OngoingMaintenance maintenance)
        {
            Log.Warning("Server is currently ongoing maintenance!");
            TerminateWithError(new MaintenanceModeOngoingError());
        }

        void HandleRedirectToServer(Handshake.RedirectToServer redirect)
        {
            ServerEndpoint endpoint = redirect.RedirectToEndpoint;
            if (endpoint != null)
                Log.Info("Redirecting to server {ServerHost}:{ServerPort} tls={EnableTls} (CdnBaseUrl={CdnBaseUrl})", endpoint.ServerHost, endpoint.ServerPort, endpoint.EnableTls, endpoint.CdnBaseUrl);
            else
                Log.Error("Redirecting to null server!");
            TerminateWithError(new RedirectToServerError(endpoint));
        }

        void HandleSessionStartSuccess(SessionProtocol.SessionStartSuccess sessionSuccess)
        {
            if (_currentSession != null)
            {
                TerminateWithError(new SessionError("Tried to start new session but we already have a session"));
                return;
            }
            if (sessionSuccess.QueryId != _currentSessionStartQueryId)
            {
                TerminateWithError(new SessionError(Invariant($"Got session start success response for stale request. Got id {sessionSuccess.QueryId}, expected {_currentSessionStartQueryId}")));
                return;
            }

            Log.Info("Session created, token {SessionToken}", sessionSuccess.SessionToken);

            if (!string.IsNullOrEmpty(sessionSuccess.CorrectedDeviceGuid))
            {
                try
                {
                    _guidService.StoreDeviceGuid(sessionSuccess.CorrectedDeviceGuid);
                }
                catch (Exception ex)
                {
                    TerminateWithError(new ServiceFailureError(ex));
                    return;
                }
            }

            lock (_currentSessionSendQueueLock)
            {
                _currentSession = new SessionState(new SessionParticipantState(sessionSuccess.SessionToken), sessionSuccess.PlayerId, sessionSuccess.ResumptionToken);
                _handshakePhase = ServerHandshakePhase.InSession;
            }
        }

        void HandleSessionResumeSuccess(SessionProtocol.SessionResumeSuccess sessionResume)
        {
            if (_currentSession == null)
            {
                TerminateWithError(new SessionError("Got resume session but don't have a session"));
                return;
            }

            SessionUtil.ResumeResult    resumeResult;
            int                         numMessagesResent;
            lock (_currentSessionSendQueueLock)
            {
                SessionResumptionInfo serverSessionToResume = new SessionResumptionInfo(sessionResume.SessionToken, sessionResume.ServerSessionAcknowledgement);

                resumeResult = SessionUtil.HandleResume(_currentSession?.SessionParticipant, serverSessionToResume);

                if (resumeResult is SessionUtil.ResumeResult.Success resumeSuccess)
                {
                    _currentSession.CurrentResumptionAttempt = null;
                    numMessagesResent = _currentSession.SessionParticipant.RememberedSent.Count;

                    foreach (MetaMessage payloadMessage in _currentSession.SessionParticipant.RememberedSent)
                        _transport.EnqueueSendMessage(payloadMessage);
                    Interlocked.Add(ref _connDiagnostics.SessionMessagesDelayedSent, numMessagesResent);
                }
                else
                {
                    goto fail;
                }

                _handshakePhase = ServerHandshakePhase.InSession;
            }

            Log.Info("Session resume successful, token {SessionToken}, server session acknowledgement {ServerSessionAcknowledgement}, result {ResumeResult}, re-sending {NumMessages} unacknowledged session payload messages",
                sessionResume.SessionToken,
                PrettyPrint.Compact(sessionResume.ServerSessionAcknowledgement),
                PrettyPrint.Compact((SessionUtil.ResumeResult.Success)resumeResult),
                numMessagesResent);
            _numSuccessfulSessionResumes++;
            return;

            fail:
            switch (resumeResult)
            {
                // \note After server has responded with success, session resumption on client should not fail (assuming a legitimate server).
                case SessionUtil.ResumeResult.Failure resumeFailure:
                    TerminateWithError(new SessionError($"session resume payload could not be applied: {PrettyPrint.Compact(resumeFailure)}"));
                    return;

                default:
                    throw new MetaAssertException($"Unknown {nameof(SessionUtil.ResumeResult)}: {resumeResult}");
            }
        }

        void HandleSessionStartFailure(SessionProtocol.SessionStartFailure sessionFailure)
        {
            switch (sessionFailure.Reason)
            {
                case SessionProtocol.SessionStartFailure.ReasonCode.InternalError:
                {
                    // \note: QueryId does not matter. Even "stale" failure means the server failed
                    Log.Warning("Failed to start session. Provided message: {Message}", sessionFailure.DebugOnlyErrorMessage ?? "<no message>");
                    TerminateWithError(new SessionStartFailed(sessionFailure.DebugOnlyErrorMessage ?? "internal error"));
                    return;
                }

                case SessionProtocol.SessionStartFailure.ReasonCode.DryRun:
                {
                    // Dry run succeeded, no need to do anything.
                    return;
                }

                case SessionProtocol.SessionStartFailure.ReasonCode.Banned:
                {
                    TerminateWithError(new PlayerIsBannedError());
                    return;
                }

                case SessionProtocol.SessionStartFailure.ReasonCode.PlayerDeserializationFailure:
                {
                    TerminateWithError(new PlayerDeserializationFailureError(sessionFailure.DebugOnlyErrorMessage ?? "deserialization failure"));
                    return;
                }
            }
        }

        void HandleSessionStartResourceCorrection(SessionProtocol.SessionStartResourceCorrection startResourceCorrection)
        {
            // Server requested correction. Only makes sense for the current query.
            if (startResourceCorrection.QueryId != _currentSessionStartQueryId)
            {
                Log.Debug("Session resource correction request was stale, ignored. Got id {QueryId}, expected {ExpectedId}", startResourceCorrection.QueryId, _currentSessionStartQueryId);
                return;
            }

            _handshakePhase = ServerHandshakePhase.WaitingForResourceCorrectionHandledByUser;
            EnqueueInfo(new ResourceCorrectionInfo(startResourceCorrection.ResourceCorrection));
        }

        void HandleSessionResumeFailure(SessionProtocol.SessionResumeFailure failed)
        {
            Log.Warning("Session resumption failure");
            TerminateWithError(new SessionResumeFailed());
        }

        void OnSessionPayloadMessageReceivedFromServer(SessionUtil.ReceivePayloadMessageResult result, MetaMessage payloadMessage)
        {
            if (Log.IsVerboseEnabled)
                Log.Verbose("Incoming session payload message (type {MessageType}, index {MessageIndex})", payloadMessage.GetType().Name, result.PayloadMessageIndex);

            if (result.ShouldSendAcknowledgement)
            {
                Log.Verbose("Sending acknowledgement for {ClientNumReceived} received payload messages", _currentSession.SessionParticipant.NumReceived);
                _transport.EnqueueSendMessage(new SessionAcknowledgementMessage(SessionAcknowledgement.FromParticipantState(_currentSession.SessionParticipant)));
            }
        }

        void HandleOnInfo(MessageTransport.Info info)
        {
            if (info is StreamMessageTransport.ThreadCycleUpdateInfo)
            {
                SetWatchdog(TimeSpan.FromSeconds(10));
                return;
            }

            if (_handshakePhase != ServerHandshakePhase.Error)
                EnqueueInfo(info);
        }

        /// <summary>
        /// Signals that reconnect should be attempted to resume session. This is the handling for SessionConnectionErrorLostInfo.
        /// </summary>
        public void ResumeSessionAfterConnectionDrop()
        {
            if (_handshakePhase != ServerHandshakePhase.WaitingForResumeSessionAfterConnectionDropHandledByUser)
                throw new InvalidOperationException("invalid phase for ResumeSessionAfterConnectionDrop");

            ServerGateway resumeGateway = ServerGatewayScheduler.SelectGatewayForConnectionResume(_endpoint, _currentGateway, numFailedResumeAttempts: _currentSession.CurrentResumptionAttempt.NumConnectionAttempts - 1, _numSuccessfulSessionResumes);
            _currentGateway = resumeGateway;
            SetupTransport();
        }

        /// <summary>
        /// Signals that the session resume has encountered an unrecoverable error, and resume should not be continued. For example timeout
        /// after a number of of attempts.
        /// </summary>
        public void AbortSessionAfterConnectionDrop()
        {
            // Close already enqueued.
            lock (_enqueueCloseLock)
            {
                if (_closeEnqueued)
                    return;
            }

            MessageTransport.Error error;
            lock (_currentSessionSendQueueLock)
            {
                error = _currentSession?.CurrentResumptionAttempt?.LatestTransportError;
                if (error == null)
                    return;
            }
            TerminateWithError(error);
        }

        void DropTransportWithError(MessageTransport.Error error)
        {
            if (TryDeferSessionTransportLossErrorToSupervisor(error))
                return;

            // Either no session or session cannot be attempted to be resumed
            TerminateWithError(error);
        }

        bool TryDeferSessionTransportLossErrorToSupervisor(MessageTransport.Error error)
        {
            // If we have a session, let the supervisor choose how to proceed. Options are to retry (to keep the session alive), or to
            // stop the session. Supervisor chooses resumption by calling ResumeSessionAfterConnectionDrop or AbortSessionAfterConnectionDrop.
            //
            // If there was no session, i.e. we were never succesfully connected, always stop. There is nothing useful in reusing this
            // ServerConnection if there was no session.

            if (IsMessageTransportErrorFatalForSession(error))
                return false;

            lock (_enqueueCloseLock)
            {
                if (_closeEnqueued)
                    return false;
            }

            SessionResumptionAttempt attempt;
            lock (_currentSessionSendQueueLock)
            {
                if (_currentSession == null)
                    return false;

                if (_currentSession.CurrentResumptionAttempt == null)
                    attempt = SessionResumptionAttempt.ForFirstError(error);
                else
                    attempt = SessionResumptionAttempt.ForSubsequentError(_currentSession.CurrentResumptionAttempt, error);
                _currentSession.CurrentResumptionAttempt = attempt;
            }

            DisposeTransport();

            _handshakePhase = ServerHandshakePhase.WaitingForResumeSessionAfterConnectionDropHandledByUser;
            EnqueueInfo(new TransportLifecycleInfo(isTransportAttached: false));
            EnqueueInfo(new SessionConnectionErrorLostInfo(attempt));

            return true;
        }

        /// <summary>
        /// Determines if error from MessageTransport is fatal for a session. This includes
        /// intentional Closes, and unrecoverable connection errors.
        /// </summary>
        static bool IsMessageTransportErrorFatalForSession(MessageTransport.Error error)
        {
            return error is MessageTransport.EnqueuedCloseError
                || error is WireMessageTransport.ProtocolStatusError
                || error is WireMessageTransport.WireFormatError
                || error is WireMessageTransport.InvalidGameMagic
                || error is WireMessageTransport.WireProtocolVersionMismatch
                || error is WireMessageTransport.MissingHelloError;
        }
    }
}
