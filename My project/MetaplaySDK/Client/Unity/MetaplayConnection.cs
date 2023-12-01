// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#define UNITY_WEBGL_BUILD
#endif

using Metaplay.Client.Messages;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Debugging;
using Metaplay.Core.IO;
using Metaplay.Core.Localization;
using Metaplay.Core.Message;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Network;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.Session;
using Metaplay.Core.Tasks;
using Metaplay.Network;
using Metaplay.Unity.ConnectionStates;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Metaplay.Unity
{
    /// <summary>
    /// High-level state of the connection.
    /// </summary>
    public enum ConnectionStatus
    {
        /// <summary>
        /// Connection is either not opened, or is cleanly closed with Close().
        /// </summary>
        NotConnected,

        /// <summary>
        /// Connection is being established.
        /// </summary>
        Connecting,

        /// <summary>
        /// Connection is alive.
        /// </summary>
        Connected,

        /// <summary>
        /// Connection is terminated due any other reason than call to Close().
        /// </summary>
        Error,
    };

    /// <summary>
    /// Detailed description of the connection state.
    ///
    /// <para>
    /// For high-level status, see <c>ConnectionState.Status</c>.
    /// </para>
    /// </summary>
    public abstract class ConnectionState
    {
        public abstract ConnectionStatus Status { get; }
    }

    namespace ConnectionStates
    {
        /// <summary>
        /// Error state that has an associated Network Diagnostic report.
        /// </summary>
        public interface IHasNetworkDiagnosticReport
        {
            /// <summary>
            /// Returns NetworkDiagnosticReport attached to the connection error state. Only set
            /// if the error was encountered during the connecting phase. If an error is encountered
            /// during an active session, the error is emitted immediately without waiting for the
            /// report to complete. In this case, the value is null.
            /// </summary>
            // \todo: remove setter
            NetworkDiagnosticReport NetworkDiagnosticReport { get; set; }
        }

        /// <summary>
        /// Connection is either not opened, or is cleanly closed with Close().
        /// </summary>
        public class NotConnected : ConnectionState
        {
            public override sealed ConnectionStatus Status => ConnectionStatus.NotConnected;
        }

        /// <summary>
        /// Connection is being established.
        /// </summary>
        public class Connecting : ConnectionState
        {
            /// <summary> Ranges from 0 to <see cref="ConnectionConfig.ConnectAttemptsMaxCount"/>. Determines the attempt number, i.e. the number of preceeding connection failures. </summary>
            public readonly int ConnectionAttempt;

            public override sealed ConnectionStatus Status => ConnectionStatus.Connecting;

            public Connecting(int connectionAttempt)
            {
                ConnectionAttempt = connectionAttempt;
            }
        }

        /// <summary>
        /// Connection has been established.
        /// </summary>
        public class Connected : ConnectionState
        {
            /// <summary>
            /// Quality of the connection.
            ///
            /// If true, connection quality is good.
            ///
            /// If false, the connection quality is degraded due to packet loss or high latencies,
            /// or other suspicious behavior. Consider showing a connection quality warning to the
            /// user.
            /// </summary>
            public readonly bool IsHealthy;

            /// <summary>
            /// Timestamp when the latest dispatched message was received. Is updated before
            /// message dispatching in Update(), so that message listeners will observe the latest
            /// value.
            ///
            /// If no message have been received, this is the timestamp when connection was openend.
            /// </summary>
            public readonly MetaTime LatestReceivedMessageTimestamp;

            public override sealed ConnectionStatus Status => ConnectionStatus.Connected;

            public Connected(bool isHealthy, MetaTime latestReceivedMessageTimestamp)
            {
                IsHealthy = isHealthy;
                LatestReceivedMessageTimestamp = latestReceivedMessageTimestamp;
            }
        }

        public abstract class ErrorState : ConnectionState
        {
            public virtual string TryGetReasonOverrideForIncidentReport() => null;
        }

        /// <summary>
        /// Connection is terminated due to an error that is transient, i.e. it is more than
        /// possible that just retrying will succeed. For example, if establishing connection
        /// failed.
        /// </summary>
        public abstract class TransientError : ErrorState
        {
            public override sealed ConnectionStatus Status => ConnectionStatus.Error;

            /// <summary> Socket was closed unexpectedly. </summary>
            public sealed class Closed : TransientError, IHasNetworkDiagnosticReport
            {
                [IgnoreDataMember]
                public NetworkDiagnosticReport NetworkDiagnosticReport { get; set; }
            };

            /// <summary> Connection timed out. </summary>
            public sealed class Timeout : TransientError, IHasNetworkDiagnosticReport
            {
                public enum TimeoutSource
                {
                    /// <summary> Timeout during config or localization fetch </summary>
                    ResourceFetch,
                    /// <summary> Timeout while connecting </summary>
                    Connect,
                    /// <summary> Timeout after connection was established </summary>
                    Stream
                };
                public readonly TimeoutSource Source;

                [IgnoreDataMember]
                public NetworkDiagnosticReport NetworkDiagnosticReport { get; set; }

                public Timeout(TimeoutSource source)
                {
                    Source = source;
                }
            };

            /// <summary> Game server does not accept game connections. </summary>
            public sealed class ClusterNotReady : TransientError
            {
                public enum ClusterStatus { ClusterStarting, ClusterShuttingDown };
                public readonly ClusterStatus Reason;
                public ClusterNotReady(ClusterStatus reason)
                {
                    Reason = reason;
                }
            };

            /// <summary> Fetching config failed with exception. </summary>
            public sealed class ConfigFetchFailed : TransientError
            {
                public enum FailureSource
                {
                    /// <summary> Error during config or localization fetch </summary>
                    ResourceFetch,
                    /// <summary> Error while activating (invoking activation the callbacks) </summary>
                    Activation,
                };
                public readonly Exception Exception;
                public readonly FailureSource Source;
                public ConfigFetchFailed(Exception exeption, FailureSource source)
                {
                    Exception = exeption;
                    Source = source;
                }
            };

            /// <summary> Tried to resume session after a disconnect, but server responded with failure. </summary>
            public sealed class FailedToResumeSession : TransientError
            {
            }

            /// <summary> Session was terminated by server. </summary>
            public sealed class SessionForceTerminated : TransientError
            {
                public SessionForceTerminateReason Reason { get; }

                public SessionForceTerminated (SessionForceTerminateReason reason)
                {
                    Reason = reason;
                }
            }

            /// <summary> Unexpected error related to the messaging protocol. </summary>
            public sealed class ProtocolError : TransientError
            {
                public enum ErrorKind
                {
                    UnexpectedLoginMessage,
                    MissingServerHello,
                    SessionStartFailed,
                    SessionProtocolError,
                }

                public ErrorKind Kind { get; }
                public string Message { get; }

                public ProtocolError(ErrorKind kind, string message)
                {
                    Kind = kind;
                    Message = message ?? throw new ArgumentNullException(nameof(message));
                }
            }

            /// <summary> Session was lost resulting from application being in background. </summary>
            public class SessionLostInBackground : TransientError
            {
                public SessionLostInBackground() { }
            }

            /// <summary> Internal worker thread did not respond and it was killed by a watchdog. </summary>
            public sealed class InternalWatchdogDeadlineExceeded : TransientError
            {
                public ConnectionInternalWatchdogType WatchdogType { get; }

                public InternalWatchdogDeadlineExceeded(ConnectionInternalWatchdogType watchdogType)
                {
                    WatchdogType = watchdogType;
                }
            }

            /// <summary> Session was lost resulting from application being in background. </summary>
            // \todo[jarkko]: clarify how behavior differs from SessionLostInBackground, or merge these. Latter preferred.
            // \note[jarkko]: this inherits SessionLostInBackground to keep compatibility until the merge
            public class AppTooLongSuspended : SessionLostInBackground
            {
                public AppTooLongSuspended() { }
            }

            /// <summary> TLS failure in connection . </summary>
            public sealed class TlsError : TransientError
            {
                public enum ErrorCode
                {
                    Unknown = 0,
                    NotAuthenticated,
                    FailureWhileAuthenticating,
                    NotEncrypted,
                };
                public readonly ErrorCode Error;
                public TlsError(ErrorCode error)
                {
                    Error = error;
                }
            };
        }

        /// <summary>
        /// Connection is terminated due to an error that is terminal, i.e. it is not very
        /// likely that just retrying will help. For example, if connection was terminated
        /// due to version/protocol error, or if server is in maintenance mode.
        /// </summary>
        public abstract class TerminalError : ErrorState
        {
            public override sealed ConnectionStatus Status => ConnectionStatus.Error;

            /// <summary> Server uses unsupported wire protocol version. Cannot continue. </summary>
            public class WireProtocolVersionMismatch : TerminalError
            {
                public int ClientProtocolVersion { get; }
                public int ServerProtocolVersion { get; }

                public WireProtocolVersionMismatch(int clientProtocolVersion, int serverProtocolVersion)
                {
                    ClientProtocolVersion = clientProtocolVersion;
                    ServerProtocolVersion = serverProtocolVersion;
                }
            }

            /// <summary> Server identified with a wrong magic. The server is not a server for this game. </summary>
            public class InvalidGameMagic : TerminalError
            {
                public UInt32 Magic { get; }
                public InvalidGameMagic(UInt32 magic)
                {
                    Magic = magic;
                }
            };

            /// <summary> Server is in maintenance. <see cref="MetaplaySDK.MaintenanceMode"/> contains information of the maintenance break.</summary>
            public class InMaintenance : TerminalError { };

            /// <summary> Server and Client versions are incompatible. </summary>
            public class LogicVersionMismatch : TerminalError
            {
                public MetaVersionRange     ClientSupportedLogicVersions    { get; }
                public MetaVersionRange     ServerAcceptedVersions          { get; }
                public LogicVersionMismatch(MetaVersionRange clientSupportedLogicVersions, MetaVersionRange serverAcceptedVersions)
                {
                    ClientSupportedLogicVersions = clientSupportedLogicVersions;
                    ServerAcceptedVersions = serverAcceptedVersions;
                }
            };

            /// <summary> Server and Client versions are incompatible. </summary>
            public class LoginProtocolVersionMismatch : TerminalError
            {
                public int ClientVersion { get; }
                public int ServerVersion { get; }
                public LoginProtocolVersionMismatch(int clientVersion, int serverVersion)
                {
                    ClientVersion = clientVersion;
                    ServerVersion = serverVersion;
                }
            }

            /// <summary> Server and Client are not built from the same commit. </summary>
            public class CommitIdMismatch : TerminalError
            {
                public string ClientCommitId { get; }
                public string ServerCommitId { get; }
                public CommitIdMismatch(string clientCommitId, string serverCommitId)
                {
                    ClientCommitId = clientCommitId;
                    ServerCommitId = serverCommitId;
                }
            }

            /// <summary> Error in serialization or packet framing. </summary>
            public class WireFormatError : TerminalError
            {
                public readonly Exception Exception;
                public WireFormatError(Exception exeption)
                {
                    Exception = exeption;
                }
            };

            /// <summary> Device has no network connectivity. </summary>
            public class NoNetworkConnectivity : TerminalError, IHasNetworkDiagnosticReport
            {
                [IgnoreDataMember]
                public NetworkDiagnosticReport NetworkDiagnosticReport { get; set; }
            };

            /// <summary> Player is banned and may not connect to the server. </summary>
            public class PlayerIsBanned : TerminalError { };

            /// <summary> On the server side, player state failed to be deserialized. </summary>
            public class ServerPlayerDeserializationFailure : TerminalError
            {
                public readonly string Message;
                public ServerPlayerDeserializationFailure(string message)
                {
                    Message = message;
                }
            }

            /// <summary> Client-side connection management error. Unhandled exception from a connection callback or a hook. </summary>
            public class ClientSideConnectionError : TerminalError
            {
                public readonly Exception Exception;
                public ClientSideConnectionError(Exception exeption)
                {
                    Exception = exeption;
                }
            }

            /// <summary> Unknown terminal error. </summary>
            public class Unknown : TerminalError
            {
                public readonly string DebugInfo;
                public Unknown() { }
                public Unknown(string debugInfo)
                {
                    DebugInfo = debugInfo;
                }
            }
        }
    };

    /// <summary>
    /// A <see cref="TerminalError.ClientSideConnectionError"/> payload if writing on persisted storage fails.
    /// </summary>
    public class CannotWriteCredentialsOnDiskError : Exception
    {
    }

    public class ConnectionConfig
    {
        /// <summary>
        /// Number of times the initial connection is attempted.
        ///
        /// Note that this applies only to the initial connection, i.e. the after the call to Connect()
        /// while Connection is in <c>Connecting</c> state.
        ///
        /// If a <c>TerminalError</c> is encountered, no further connections will be attempted regardless
        /// of this value.
        ///
        /// The value of -1 denotes unlimited amount of attempts.
        /// </summary>
        public int          ConnectAttemptsMaxCount = 5;
        /// <summary>
        /// Cooldown interval between connection attempts in <c>Connecting</c> state.
        /// </summary>
        public MetaDuration ConnectAttemptInterval  = MetaDuration.FromMilliseconds(500);

        /// <summary> Time after which to not attempt session resumption anymore. </summary>
        public MetaDuration SessionResumptionAttemptMaxDuration         = MetaDuration.FromSeconds(20);

        /// <summary>
        /// Maximum duration the application is allowed to be paused without the session being terminated.
        /// </summary>
        public MetaDuration MaxSessionRetainingPauseDuration    = MetaDuration.FromSeconds(90);
        /// <summary>
        /// Maximum duration the time between frames is allowed to take without the session being terminated. This
        /// value is mostly a sanity check for cases where a frame is paused for a very long time, and may be left
        /// to a relatively high value.
        /// </summary>
        public MetaDuration MaxSessionRetainingFrameDuration    = MetaDuration.FromSeconds(90);
        /// <summary>
        /// Maximum duration the application is allowed to be paused without certain error types
        /// being masked as <see cref="ConnectionStates.TransientError.SessionLostInBackground"/>
        /// if the error happened during the pause.
        /// </summary>
        public MetaDuration MaxNonErrorMaskingPauseDuration     = MetaDuration.FromSeconds(20);

        /// <summary> Time limit for opening a socket. </summary>
        public MetaDuration ConnectTimeout              = MetaDuration.FromSeconds(32);
        /// <summary> Time limit for server to identify itself after connection is opened. </summary>
        public MetaDuration ServerIdentifyTimeout       = MetaDuration.FromSeconds(10);
        /// <summary> Time limit for server to reply to session start request. </summary>
        public MetaDuration ServerSessionInitTimeout    = MetaDuration.FromSeconds(10);

        /// <summary>
        /// Limit how many times config fetch is attempted if the fetches are unsuccesful.
        /// Exceeding this limit will cause transition into Error state.
        /// </summary>
        public int          ConfigFetchAttemptsMaxCount = 4;

        /// <summary>
        /// Time limit how long a single config fetch is allowed to take.
        /// Exceeding this limit will cause the attempt to be unsuccesful and retried if
        /// <c>ConfigFetchAttemptsMaxCount</c> allows for.
        /// </summary>
        public MetaDuration ConfigFetchTimeout          = MetaDuration.FromSeconds(10);

        /// <summary>
        /// Time limit how long <c>Close(flushEnqueuedMessages: true)</c> allowed to take.
        /// Exceeding this limit will cause the connection be forcibly closed with no
        /// guarantees of flushing.
        /// </summary>
        public MetaDuration CloseFlushTimeout           = MetaDuration.FromMilliseconds(100);

        /// <summary>
        /// The time after first connection attempt until the client will start to fetch server status
        /// hint from the auxiliary systems IF the connection attempt hasn't completed successfully or
        /// unsuccessfully before that. If the connection attempt fails, the fetch is started immediately.
        ///
        /// Auxiliary system is a (partially) independent system from the primary backend. An auxiliary
        /// system can inform the client of situations where the primary backend cannot respond, such
        /// as ongoing infrastructure maintenance.
        /// </summary>
        public MetaDuration ServerStatusHintCheckDelay      = MetaDuration.FromMilliseconds(1000);

        /// <summary>
        /// Time limit within which a connection to an auxiliary system must complete. If the limit is
        /// exceeded, the particular auxiliary system is ignored.
        /// </summary>
        public MetaDuration ServerStatusHintConnectTimeout  = MetaDuration.FromMilliseconds(2000);

        /// <summary>
        /// Time limit within which the connected auxiliary system must reply. If the limit is
        /// exceeded, the particular auxiliary system is ignored.
        /// </summary>
        public MetaDuration ServerStatusHintReadTimeout     = MetaDuration.FromMilliseconds(2000);

        /// <summary>
        /// After a session resumption we send a <see cref="SessionPing"/>, and if we go this
        /// long (plus estimated roundtrip latency) without getting a <see cref="SessionPong"/>
        /// back, we report an incident.
        /// </summary>
        public MetaDuration SessionPingPongDurationIncidentThreshold        = MetaDuration.FromSeconds(5);
        /// <summary>
        /// Max number of session ping-pong duration incidents (see
        /// <see cref="SessionPingPongDurationIncidentThreshold"/>) that can be reported per session.
        /// </summary>
        public int          MaxSessionPingPongDurationIncidentsPerSession   = 3;
    }

    public struct ConnectionStatistics
    {
        public struct CurrentConnectionStats
        {
            /// <summary>
            /// Whether a protocol handshake was completed in any of the attempts. Protocol handshake is
            /// the first phase of a connection.
            /// </summary>
            public bool HasCompletedHandshake;

            /// <summary>
            /// Whether a session was formed at least once. Session can be formed after handshake has been
            /// made, version has been negotiated and client has fetched the config archives.
            /// </summary>
            public bool HasCompletedSessionInit;

            /// <summary>
            /// Status of a network probe (an HTTPS ping to the CDN). True, if probe did successfully communicate with an
            /// internet service. False, if the communication attempt to the internet service failed. This indicates a network
            /// problem on the client device. Null, if probe state is unknown, i.e. probe is still pending.
            /// </summary>
            public bool? NetworkProbeStatus;

            public static CurrentConnectionStats ForNewConnection()
            {
                CurrentConnectionStats stats = new CurrentConnectionStats();
                return stats;
            }
        }

        /// <summary>
        /// Statistics concerning the current connection. In Error state, the values are of the connection that
        /// were just lost. This value is not cleared until the connection is closed normally or a Reconnect is
        /// called.
        /// </summary>
        public CurrentConnectionStats CurrentConnection;

        public static ConnectionStatistics CreateNew()
        {
            return new ConnectionStatistics();
        }
    }

    internal interface IMetaplayConnectionSDKHook
    {
        void OnCurrentCdnAddressUpdated(MetaplayCdnAddress currentAddress);
        void OnScheduledMaintenanceModeUpdated(MaintenanceModeState maintenanceMode);
        void OnSessionStarted(SessionProtocol.SessionStartSuccess sessionStart);
        string GetDeviceGuid();
        void SetDeviceGuid(string deviceGuid);
    }

    /// <summary>
    /// <see cref="IMetaplayConnectionDelegate"/> provides the game-specific behavior of the MetaplayConnection. A single
    /// <see cref="IMetaplayConnectionDelegate"/> instance is always responsible for only a single <see cref="MetaplayConnection"/>
    /// instance, so the class implementation may hold state.
    /// </summary>
    public interface IMetaplayConnectionDelegate
    {
        Handshake.ILoginRequestGamePayload GetLoginPayload();
        ISessionStartRequestGamePayload GetSessionStartRequestPayload();
        void OnSessionStarted(ClientSessionStartResources startResources);

        void Init();
        void Update();
        void OnHandshakeComplete();
        LoginDebugDiagnostics GetLoginDebugDiagnostics(bool isSessionResumption);

        void OnFullProtocolHashMismatch(uint clientProtocolHash, uint serverProtocolHash);

        /// <summary>
        /// Called when <see cref="MetaplayConnection.Close(bool)"/> is invoked with <c>flushPendingMessages=true</c>. This callback
        /// allows game to flush any final messages to <see cref="MetaplaySDK.MessageDispatcher"/> just before connection is flushed
        /// and closed.
        /// </summary>
        void FlushPendingMessages();
    }

    /// <summary>
    /// Info about the game config used during a connection.
    /// </summary>
    public class ConnectionGameConfigInfo
    {
        public readonly ContentHash                  BaselineVersion;
        public readonly ContentHash                  PatchesVersion;
        public readonly List<ExperimentVariantPair>  ExperimentMemberships;

        public ConnectionGameConfigInfo(ContentHash baselineVersion, ContentHash patchesVersion, List<ExperimentVariantPair> experimentMemberships)
        {
            BaselineVersion = baselineVersion;
            PatchesVersion = patchesVersion;
            ExperimentMemberships = experimentMemberships;
        }

        public IncidentGameConfigInfo ToIncidentInfo()
        {
            return new IncidentGameConfigInfo(
                sharedConfigBaselineVersion:    BaselineVersion,
                sharedConfigPatchesVersion:     PatchesVersion,
                experimentMemberships:          ExperimentMemberships);
        }
    }

    /// <summary>
    /// Represents a connection to the metaplay servers.
    ///
    /// <para>
    /// Connection is always in one of the <see cref="ConnectionStatus" /> accessible through <c>State.Status</c>,
    /// and initially with NotConnected status. The <c>State</c> is only updated during <see cref="Update"/>, making
    /// it safe to access and consistent over a frame.
    /// </para>
    /// <para>
    /// In addition to the state transitions, a <see cref="DisconnectedFromServer"/> message is emitted then transitioning
    /// into an Error state from the Connected state.
    /// </para>
    /// <para>
    /// The <c>MetaplayConnection</c> does nothing when reaching a transient, recoverable error state. It is up to the caller
    /// to handle the error, such as by after encountering a transient error, the connection is attempted to be restarted
    /// after a configurable delay.
    /// </para>
    /// </summary>
    public sealed class MetaplayConnection
    {
        private LogChannel                          Log         { get; }
        public IMetaplayConnectionDelegate          Delegate    { get; }
        public ServerEndpoint                       Endpoint    { get; private set; }   // Server to connect to (can be changed if a redirect is ordered by the server)
        public ConnectionState                      State       { get; private set; }
        public ConnectionConfig                     Config;
        public ref readonly ConnectionStatistics    Statistics                          => ref _statistics;
        public ServerConnection.DebugInfo           ServerConnectionDebugInformation    => _serverConnection?.DebugInformation;
        public string                               LatestTlsPeerDescription { get; private set; } // TLS peer description of the latest completely handshaked connection
        public ConnectionGameConfigInfo             LatestGameConfigInfo { get; private set; } = null;
        public string                               LatestServerVersion { get; private set; } = null; // ServerVersion in the latest ServerHello received.

        /// <summary>
        /// The latest server options, or default if options are not yet available. The options are set just before a session
        /// is established, i.e. <c>State.Status == Connected</c>.
        /// </summary>
        public Handshake.ServerOptions              ServerOptions { get; private set; }

        public LoginSessionDebugDiagnostics             TryGetLoginSessionDebugDiagnostics()            => _serverConnection?.TryGetLoginSessionDebugDiagnostics();
        public LoginServerConnectionDebugDiagnostics    TryGetLoginServerConnectionDebugDiagnostics()   => _serverConnection?.GetLoginServerConnectionDebugDiagnostics();
        public LoginTransportDebugDiagnostics           TryGetLoginTransportDebugDiagnostics()          => _serverConnection?.GetLoginTransportDebugDebugDiagnostics();

        /// <summary>
        /// The set of configs negotiated during the session startup.
        /// </summary>
        public ClientSessionStartResources SessionStartResources;

        /// <summary>
        /// Current Offline server instance, or <c>null</c> if not in offline mode.
        /// </summary>
        public IOfflineServer OfflineServer { get; private set; }

        enum Marker
        {
            UpdateComplete = 0,
            HandleMessagesAndCallAgain = 1,
        }

        ServerConnection            _serverConnection;
        IEnumerator<Marker>         _supervisionLoop;
        CancellationTokenSource     _cancellation;
        bool                        _flushEnqueuedMessagesBeforeClose;
        bool                        _supervisionLoopRunning;
        List<MetaMessage>           _messagesToDispatch;
        ConnectionStatistics        _statistics;
        IMetaplayConnectionSDKHook  _sdkHook;
        bool                        _messageDispatchSuspended;
        List<MetaMessage>           _suspendedDispatchMessages = new List<MetaMessage>();
        MetaForwardingLogger        _logForwardingBuffer;
        MetaTimer                   _pauseTerminationTimer;
        MetaplayOfflineOptions      _offlineOptions;
        SessionNonceService         _sessionNonceService;
        UnitySessionGuidService     _sessionGuidService;
        ISessionCredentialService   _sessionCredentialService;
        Task<EntityId>              _sessionCredentialServiceInitTask;
        readonly object             _closedForApplicationBackgroundMarker = new object();

        // For compatibility
        [Obsolete("Accessing credentials directly will not work with non-guest accounts, and may not resolve to the currently active account")]
        public GuestCredentials TryGetGuestCredentials()
        {
            UnityCredentialService service = _sessionCredentialService as UnityCredentialService;
            if (service == null)
                return null;

            ISessionCredentialService.GuestCredentials? credentials = service.TryGetGuestCredentials();
            if (credentials == null)
                return null;

            return new GuestCredentials()
            {
                DeviceId = credentials.Value.DeviceId,
                AuthToken = credentials.Value.AuthToken,
                PlayerId = credentials.Value.PlayerIdHint
            };
        }

        private MetaDuration? JustEndedPauseDuration        => MetaplaySDK.ApplicationPauseStatus == ApplicationPauseStatus.ResumedFromPauseThisFrame ? (MetaDuration?)MetaplaySDK.ApplicationLastPauseDuration : null;
        private bool ShouldEndSessionDueToRecentPause       => JustEndedPauseDuration.HasValue && JustEndedPauseDuration.Value > MetaplaySDK.ApplicationPauseMaxDuration;
        private bool ShouldMaskSimpleErrorDueToRecentPause  => JustEndedPauseDuration.HasValue && JustEndedPauseDuration.Value > Config.MaxNonErrorMaskingPauseDuration;

#if UNITY_EDITOR
        public Func<ServerConnection.CreateTransportFn, ServerConnection.CreateTransportFn> EditorHookCreateTransport = null;
#endif

        /// <summary>
        /// Creates a new ConnectionManager instance in NotConnected State.
        /// </summary>
        internal MetaplayConnection(ServerEndpoint initialEndpoint, ConnectionConfig config, IMetaplayConnectionDelegate connDelegate, IMetaplayConnectionSDKHook sdkHook, MetaplayOfflineOptions offlineOptions)
        {
            Log = MetaplaySDK.Logs.Network;
            Delegate = connDelegate;
            Endpoint = initialEndpoint;
            _sdkHook = sdkHook;
            _offlineOptions = offlineOptions;

            State = new ConnectionStates.NotConnected();
            Config = config ?? new ConnectionConfig();
            _serverConnection = null;
            _supervisionLoop = null;
            _cancellation = null;
            _supervisionLoopRunning = false;
            _messagesToDispatch = new List<MetaMessage>();
            _statistics = ConnectionStatistics.CreateNew();

            _logForwardingBuffer = new MetaForwardingLogger(Log, maxBufferSize: 100, autoflushAfter: TimeSpan.FromSeconds(5.0));
#if UNITY_EDITOR
            // In Editor-mode, force flush when Play Mode is ended. Otherwise we could get warnings after play mode is over.
            MetaplaySDK.EditorHookOnExitingPlayMode += () => _logForwardingBuffer.FlushBufferedToSink();
#endif

            Delegate.Init();

            _sessionNonceService = new SessionNonceService(MetaplaySDK.AppLaunchId);
            _sessionGuidService = new UnitySessionGuidService(_sdkHook);

            if (Endpoint.IsOfflineMode)
                _sessionCredentialService = new OfflineCredentialService();
            else
                _sessionCredentialService = new UnityCredentialService();

            try
            {
                _sessionCredentialServiceInitTask = _sessionCredentialService.InitializeAsync();
            }
            catch (Exception ex)
            {
                _sessionCredentialServiceInitTask = Task.FromException<EntityId>(ex);
            }
        }

        internal void ChangeServerEndpoint(ServerEndpoint endpoint)
        {
            Endpoint = endpoint;
        }

        /// <summary>
        /// Update logic for the Metaplay backend connection. Should be called on every frame from your game's main loop.
        /// By default, this is managed in <see cref="MetaplaySDK" />.
        /// </summary>
        internal void InternalUpdate()
        {
            Delegate.Update();
            OfflineServer?.Update(); // If in offline mode, update OfflineServer
            _logForwardingBuffer.FlushBufferedToSink();

            UpdateSupervisor();
            _logForwardingBuffer.FlushBufferedToSink();
        }

        /// <summary>
        /// If Suspended, messages are not dispatched but instead buffered. If resumed, the buffered messages
        /// are dispatched on next update. State is reset when connection is Closed or Opened and is by default
        /// not suspended.
        /// </summary>
        public void SuspendMessageProcessing(bool isSuspended)
        {
            _messageDispatchSuspended = isSuspended;
        }

        private void UpdateSupervisor()
        {
            Marker updateResult;

            if (!_messageDispatchSuspended)
                DispatchSuspendedMessages();

            if (!StepSupervisor(out updateResult))
                return;

            while (true)
            {
                if (!_messageDispatchSuspended)
                    DispatchSuspendedMessages();

                try
                {
                    foreach (MetaMessage message in _messagesToDispatch)
                    {
                        if (_messageDispatchSuspended)
                            _suspendedDispatchMessages.Add(message);
                        else
                            MetaplaySDK.MessageDispatcher.OnReceiveMessage(message);
                        if (_supervisionLoop == null)
                            return;
                    }
                }
                finally
                {
                    _messagesToDispatch.Clear();
                }

                switch (updateResult)
                {
                    default:
                    case Marker.UpdateComplete:
                        return;

                    case Marker.HandleMessagesAndCallAgain:
                        if (!StepSupervisor(out updateResult))
                            return;
                        break;
                }
            }
        }

        private void DispatchSuspendedMessages()
        {
            // \note: _suspendedDispatchMessages may be modified (only) in the case the message handler calls Close(),
            //         in which case the synthetic DisconnectedFromServer would get inserted into the pending list.
            for (int ndx = 0; ndx < _suspendedDispatchMessages.Count; ++ndx)
            {
                MetaMessage message = _suspendedDispatchMessages[ndx];
                MetaplaySDK.MessageDispatcher.OnReceiveMessage(message);
            }
            _suspendedDispatchMessages.Clear();
        }

        private bool StepSupervisor(out Marker marker)
        {
            if (_supervisionLoop == null)
            {
                marker = Marker.UpdateComplete;
                return false;
            }

            try
            {
                _supervisionLoopRunning = true;
                _messagesToDispatch.Clear();
                if (!_supervisionLoop.MoveNext())
                {
                    marker = Marker.UpdateComplete;
                    return false;
                }
            }
            finally
            {
                _supervisionLoopRunning = false;
            }

            marker = _supervisionLoop.Current;
            return true;
        }

        /// <summary>
        /// Closes the ongoing connection, if any, and transitions into NotConnected state.
        ///
        /// <para>
        /// If <paramref name="flushEnqueuedMessages"/> is set, the call will block until the
        /// enqueued messages are flushed into transport, or until <c>Config.CloseFlushTimeout</c>
        /// is reached. If the timeout is reached, the connection is closed without flushing any
        /// potentially remaining messages, and the call returns.
        /// </para>
        /// </summary>
        /// <param name="flushEnqueuedMessages">if true, flushes enqueued messages before closing the connection</param>
        public void Close(bool flushEnqueuedMessages) => CloseWithState(flushEnqueuedMessages, new ConnectionStates.NotConnected(), deferClosedMessageToNextUpdate: true);

        /// <summary>
        /// Closes the ongoing connection, if any, and transitions into error state.
        ///
        /// <para>
        /// If <paramref name="flushEnqueuedMessages"/> is set, the call will block until the
        /// enqueued messages are flushed into transport, or until <c>Config.CloseFlushTimeout</c>
        /// is reached. If the timeout is reached, the connection is closed without flushing any
        /// potentially remaining messages, and the call returns.
        /// </para>
        /// </summary>
        /// <param name="flushEnqueuedMessages">if true, flushes enqueued messages before closing the connection</param>
        public void CloseWithError(bool flushEnqueuedMessages, ConnectionStates.TerminalError error) => CloseWithState(flushEnqueuedMessages, error, deferClosedMessageToNextUpdate: true);

        /// <summary>
        /// Closes the ongoing connection, if any, and transitions into error state.
        ///
        /// <para>
        /// If <paramref name="flushEnqueuedMessages"/> is set, the call will block until the
        /// enqueued messages are flushed into transport, or until <c>Config.CloseFlushTimeout</c>
        /// is reached. If the timeout is reached, the connection is closed without flushing any
        /// potentially remaining messages, and the call returns.
        /// </para>
        /// </summary>
        /// <param name="flushEnqueuedMessages">if true, flushes enqueued messages before closing the connection</param>
        public void CloseWithError(bool flushEnqueuedMessages, ConnectionStates.TransientError error) => CloseWithState(flushEnqueuedMessages, error, deferClosedMessageToNextUpdate: true);

        /// <summary>
        /// Closes the ongoing connection, if any, and transitions into <paramref name="newState"/>.
        ///
        /// <para>
        /// If <paramref name="flushEnqueuedMessages"/> is set, the call will block until the
        /// enqueued messages are flushed into transport, or until <c>Config.CloseFlushTimeout</c>
        /// is reached. If the timeout is reached, the connection is closed without flushing any
        /// potentially remaining messages, and the call returns.
        /// </para>
        /// </summary>
        private void CloseWithState(bool flushEnqueuedMessages, ConnectionState newState, bool deferClosedMessageToNextUpdate)
        {
            // Trying to edit state from a callback. This causes hard-to-reason-about internal
            // state, so just disallow for now.
            // \note[jarkko]: this case would be easy, it's the Reconnect() that is the hard one.
            if (_supervisionLoopRunning)
                throw new InvalidOperationException("cannot Close() during Update(). (e.g. from Connection Update() callback).");

            if (_cancellation != null)
            {
                if (flushEnqueuedMessages)
                {
                    try
                    {
                        Delegate.FlushPendingMessages();
                    }
                    catch(Exception ex)
                    {
                        Log.Warning("IMetaplayConnectionDelegate FlushPendingMessages failed: {ex}", ex);
                    }
                }

                _flushEnqueuedMessagesBeforeClose = flushEnqueuedMessages;
                _cancellation.Cancel();
                while (_supervisionLoop.MoveNext());
                _supervisionLoop = null;
                _cancellation = null;
            }

            // Loop closed. Set the new state and enqueue Disconnected if we Closed a
            // Connected connection.
            bool wasRunningBeforeClose = (State.Status == ConnectionStatus.Connected);
            State = newState;
            if (wasRunningBeforeClose)
            {
                if (deferClosedMessageToNextUpdate)
                {
                    // To defer the message, we push the injected message to suspended queue; it will get flushed before the next supervisor step.
                    _suspendedDispatchMessages.Add(new DisconnectedFromServer());
                }
                else
                {
                    // Handle synchronously
                    MetaplaySDK.MessageDispatcher.OnReceiveMessage(new DisconnectedFromServer());
                }
            }
            _messageDispatchSuspended = false;

            // CurrentConnection statistics are only cleared if we go back to the initial state, and not in error cases. This allows
            // inspecting the statistics in error handling code also in cases where the error was injected from outside.
            if (newState.Status == ConnectionStatus.NotConnected)
                _statistics.CurrentConnection = ConnectionStatistics.CurrentConnectionStats.ForNewConnection();
        }

        /// <summary>
        /// Starts a new connection. State must be NotConnected. After a successful call, the state is Connecting.
        /// </summary>
        public void Connect()
        {
            if (_cancellation != null)
                throw new InvalidOperationException($"must be NotConnected to call Connect(), but is {State.Status}. See Reconnect()");

            _cancellation = new CancellationTokenSource();
            _messageDispatchSuspended = false;

            // This moves the game into Connecting state
            _supervisionLoop = SupervisionLoop(_cancellation.Token);
            _supervisionLoop.MoveNext();
        }

        /// <summary>
        /// Closes the ongoing connection, if any, and starts a new connection. After this call,
        /// the state is Connecting. If there is an ongoing connection, this call emits <see cref="DisconnectedFromServer"/>
        /// and processes its handlers synchronously before continuing to open the new connection.
        /// </summary>
        /// <param name="flushEnqueuedMessages">if true, flushes enqueued messages before closing the connection</param>
        public void Reconnect(bool flushEnqueuedMessages)
        {
            CloseWithState(flushEnqueuedMessages, newState: new ConnectionStates.NotConnected(), deferClosedMessageToNextUpdate: false);
            Connect();
        }

        internal void InternalOnApplicationResume()
        {
            _serverConnection?.OnApplicationResume();
            DisposePauseTerminationTimer();
        }

        internal void InternalOnApplicationPause(MetaDuration pauseTerminationDelay)
        {
            DisposePauseTerminationTimer();

            // If application is on the background for too long, drop the transport. Reduces battery usage
            // as less network traffic for the keepalives.
            ServerConnection sc = _serverConnection;
            if (sc == null)
                return;

            System.Threading.TimerCallback callback = (_) =>
            {
                // \note This invocation is not synchronized with anything,
                //       so be careful in here!

                Log.Info("Pause termination timer has elapsed. Dropping transport.");

                _ = sc.EnqueueCloseAsync(_closedForApplicationBackgroundMarker);
            };
            _pauseTerminationTimer = new MetaTimer(
                callback:   callback,
                state:      null,
                dueTime:    TimeSpan.FromMilliseconds(pauseTerminationDelay.Milliseconds),
                period:     TimeSpan.FromMilliseconds(-1));
        }

        void DisposePauseTerminationTimer()
        {
            _pauseTerminationTimer?.Dispose();
            _pauseTerminationTimer = null;
        }

        internal bool SendToServer(MetaMessage message)
        {
            ServerConnection sc = _serverConnection;
            if (sc == null)
                return false;
            return sc.EnqueueSendMessage(message);
        }

        /// <summary>
        /// <inheritdoc cref="ServerConnection.TryEnqueueTransportWriteFence"/>
        /// </summary>
        internal MessageTransportWriteFence TryEnqueueTransportWriteFence()
        {
            return _serverConnection?.TryEnqueueTransportWriteFence();
        }

        /// <summary>
        /// Create a transport with default settings and apply the Config().
        /// </summary>
        private IMessageTransport CreateOnlineTransport(ServerGateway gateway)
        {
            TimeSpan infiniteDuration = TimeSpan.FromMilliseconds(-1);

            TimeSpan ToTimeSpan(MetaDuration time)
            {
                // magic to magic (infinite)
                if (time == new MetaDuration())
                    return infiniteDuration;
                // normal
                return TimeSpan.FromMilliseconds(time.Milliseconds);
            }

#if UNITY_WEBGL_BUILD
            WebSocketTransport transport = new WebSocketTransport(_logForwardingBuffer);

            WebSocketTransport.ConfigArgs transportConfig = transport.Config();

#else
            TcpMessageTransport transport;
            if (gateway.EnableTls)
                transport = new TlsMessageTransport(_logForwardingBuffer);
            else
                transport = new TcpMessageTransport(_logForwardingBuffer);

            TcpMessageTransport.ConfigArgs transportConfig = transport.Config();
#endif

            transportConfig.GameMagic                  = MetaplayCore.Options.GameMagic;
            transportConfig.Version                    = MetaplaySDK.BuildVersion.Version;
            transportConfig.BuildNumber                = MetaplaySDK.BuildVersion.BuildNumber;
            transportConfig.SupportedLogicVersions     = MetaplayCore.Options.SupportedLogicVersions;
            transportConfig.FullProtocolHash           = MetaSerializerTypeRegistry.FullProtocolHash;
            transportConfig.CommitId                   = MetaplaySDK.BuildVersion.CommitId;
            transportConfig.ClientSessionConnectionNdx = _sessionNonceService.GetSessionConnectionIndex();
            transportConfig.ClientSessionNonce         = _sessionNonceService.GetSessionNonce();
            transportConfig.AppLaunchId                = _sessionNonceService.GetTransportAppLaunchId();
            transportConfig.Platform                   = ClientPlatformUnityUtil.GetRuntimePlatform();
            transportConfig.LoginProtocolVersion       = MetaplayCore.LoginProtocolVersion;
            transportConfig.ConnectTimeout             = ToTimeSpan(Config.ConnectTimeout);
            transportConfig.HeaderReadTimeout          = ToTimeSpan(Config.ServerIdentifyTimeout);

#if UNITY_WEBGL_BUILD
            string wsProtocol = gateway.EnableTls ? "wss": "ws";
            transportConfig.WebsocketUrl               = $"{wsProtocol}://{gateway.ServerHost}:{gateway.ServerPort}/ws";
#else
            transportConfig.DnsCacheMaxTTL             = TimeSpan.FromMinutes(5);
            transportConfig.ServerHostIPv4             = MetaplayHostnameUtil.GetV4V6SpecificHost(gateway.ServerHost, isIPv4: true);
            transportConfig.ServerHostIPv6             = MetaplayHostnameUtil.GetV4V6SpecificHost(gateway.ServerHost, isIPv4: false);
            transportConfig.ServerPort                 = gateway.ServerPort;
#endif

            _logForwardingBuffer.Information("Opening connection to {ServerHost}:{ServerPort} (tls={EnableTls})", gateway.ServerHost, gateway.ServerPort, gateway.EnableTls);

            return transport;
        }

        /// <summary>
        /// Create a connection to the server.
        /// </summary>
        private async Task<ServerConnection> CreateConnection(SessionProtocol.SessionResourceProposal initialResourceProposal, ClientAppPauseStatus initialPauseStatus, ISessionCredentialService.LoginMethod loginMethod)
        {
            int numFailedConnectionAttempts;
            if (State is ConnectionStates.Connecting connectingState)
                numFailedConnectionAttempts = connectingState.ConnectionAttempt;
            else
                numFailedConnectionAttempts = 0; // should never happen, but tolerate.

            ServerConnection.Config serverConnectionConfig = new ServerConnection.Config();
            serverConnectionConfig.ConnectTimeout                               = Config.ConnectTimeout;
            serverConnectionConfig.CommitIdCheckRule                            = IEnvironmentConfigProvider.Get().ConnectionEndpointConfig.CommitIdCheckRule;
            serverConnectionConfig.DeviceModel                                  = SystemInfo.deviceModel;
            serverConnectionConfig.LoginGamePayload                             = Delegate.GetLoginPayload();
            serverConnectionConfig.SessionStartGamePayload                      = Delegate.GetSessionStartRequestPayload();

            ServerConnection.CreateTransportFn createTransportFn;
            if (Endpoint.IsOfflineMode)
            {
                // Initialize offline server
                IOfflineServer offlineServer = IntegrationRegistry.Create<IOfflineServer>();
                await offlineServer.InitializeAsync(_offlineOptions);
                OfflineServer = offlineServer;

                createTransportFn = (ServerGateway serverGateway) => new OfflineServerTransport(offlineServer);
            }
            else
            {
                createTransportFn = CreateOnlineTransport;
            }

#if UNITY_EDITOR
            if (EditorHookCreateTransport != null)
                createTransportFn = EditorHookCreateTransport(createTransportFn);
#endif

            return new ServerConnection(
                log:                            _logForwardingBuffer,
                config:                         serverConnectionConfig,
                endpoint:                       Endpoint,
                nonceService:                   _sessionNonceService,
                guidService:                    _sessionGuidService,
                loginMethod:                    loginMethod,
                createTransport:                createTransportFn,
                buildVersion:                   MetaplaySDK.BuildVersion,
                initialResourceProposal:        initialResourceProposal,
                initialClientAppPauseStatus:    initialPauseStatus,
                getDebugDiagnostics:            Delegate.GetLoginDebugDiagnostics,
                earlyMessageFilterSync:         null,
                enableWatchdog:                 !Endpoint.IsOfflineMode,
                reportWatchdogViolation:        () => { MetaplaySDK.IncidentTracker.ReportWatchdogDeadlineExceededError(); },
                numFailedConnectionAttempts:    numFailedConnectionAttempts);
        }

        static ClientAppPauseStatus ToClientAppPauseStatus(ApplicationPauseStatus status)
        {
            switch (status)
            {
                case ApplicationPauseStatus.Running: return ClientAppPauseStatus.Running;
                case ApplicationPauseStatus.Pausing: return ClientAppPauseStatus.Paused;
                case ApplicationPauseStatus.ResumedFromPauseThisFrame: return ClientAppPauseStatus.Unpausing;
            }
            throw new ArgumentException(status.ToString());
        }

        private async Task<NetworkDiagnosticReport> GetNetworkDiagnosticReportAsync()
        {
            if (Endpoint.IsOfflineMode)
                return new NetworkDiagnosticReport();

            List<int> gameServerPorts = new List<int> { Endpoint.PrimaryGateway.ServerPort }.Concat(Endpoint.BackupGateways.Select(gw => gw.ServerPort)).ToList();
            (NetworkDiagnosticReport report, Task task) = NetworkDiagnostics.GenerateReport(
                gameServerHost4: MetaplayHostnameUtil.GetV4V6SpecificHost(Endpoint.PrimaryGateway.ServerHost, isIPv4: true),
                gameServerHost6: MetaplayHostnameUtil.GetV4V6SpecificHost(Endpoint.PrimaryGateway.ServerHost, isIPv4: false),
                gameServerPorts: gameServerPorts,
                gameServerUseTls: Endpoint.PrimaryGateway.EnableTls,
                cdnHostname4: new Uri(MetaplaySDK.CdnAddress.IPv4BaseUrl).Host,
                cdnHostname6: new Uri(MetaplaySDK.CdnAddress.IPv6BaseUrl).Host,
                timeout: TimeSpan.FromSeconds(5));

            await task.ConfigureAwaitFalse();
            return report;
        }

        #region ServerStatusHint

        [Serializable]
        private class ServerStatusHintJsonRecord
        {
            #pragma warning disable CS0649
            [Serializable]
            public class MaintenanceModeRecord
            {
                [SerializeField] public string StartAt; // null or DateTime
                [SerializeField] public string EstimatedEndTime; // null or DateTime
            }
            [SerializeField] public MaintenanceModeRecord MaintenanceMode; // null or MaintenanceModeRecord
            #pragma warning restore CS0649
        }
        private class ServerStatusHint
        {
            public class MaintenanceModeHint
            {
                public readonly DateTime StartTime; // DateTime in UTC
                public readonly DateTime? EstimatedEndTime; // null or DateTime in UTC

                public MaintenanceModeHint(DateTime startTime, DateTime? estimatedEndTime)
                {
                    StartTime = startTime;
                    EstimatedEndTime = estimatedEndTime;
                }
                public MetaTime GetStartAtMetaTime() => MetaTime.FromDateTime(StartTime);
                public MetaTime? GetEstimatedMaintenanceOverAtMetaTimeMaybe()
                {
                    if (EstimatedEndTime.HasValue)
                        return MetaTime.FromDateTime(EstimatedEndTime.Value);
                    else
                        return null;
                }
            }
            public readonly MaintenanceModeHint MaintenanceMode;

            ServerStatusHint(MaintenanceModeHint maintenanceMode)
            {
                MaintenanceMode = maintenanceMode;
            }

            public static ServerStatusHint TryParseFromRecord(ServerStatusHintJsonRecord record)
            {
                MaintenanceModeHint maintenanceMode;

                if (record.MaintenanceMode == null)
                {
                    // MaintenanceMode not defained
                    maintenanceMode = null;
                }
                else if (record.MaintenanceMode.StartAt == null && record.MaintenanceMode.EstimatedEndTime == null)
                {
                    // default ctor for MaintenanceMode. Workaround for Unity's JsonUtility parsing missing fields as default constructed
                    maintenanceMode = null;
                }
                else
                {
                    DateTime startTime;
                    DateTime? estimatedEndTime;

                    if (!DateTime.TryParse(record.MaintenanceMode.StartAt, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out startTime))
                        return null;

                    if (record.MaintenanceMode.EstimatedEndTime == null)
                        estimatedEndTime = null;
                    else
                    {
                        DateTime parsed;
                        if (!DateTime.TryParse(record.MaintenanceMode.EstimatedEndTime, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out parsed))
                            return null;
                        estimatedEndTime = parsed;
                    }

                    maintenanceMode = new MaintenanceModeHint(startTime, estimatedEndTime);
                }


                return new ServerStatusHint(maintenanceMode);
            }
            public static ServerStatusHint ForFetchFailure()
            {
                return new ServerStatusHint(maintenanceMode: null);
            }
        }

        static ServerStatusHint TryParseServerStatusHint(byte[] buffer, int length)
        {
            string jsonblob = Encoding.UTF8.GetString(buffer, 0, length);
            try
            {
                ServerStatusHintJsonRecord statusJsonRecord = JsonUtility.FromJson<ServerStatusHintJsonRecord>(jsonblob);
                return ServerStatusHint.TryParseFromRecord(statusJsonRecord);
            }
            catch
            {
                MetaplaySDK.IncidentTracker.ReportServerStatusHintCorrupt(buffer, length);
                return null;
            }
        }

        private static async Task<ServerStatusHint> TryGetServerStatusHintFromSourceAsync(IMetaLogger log, string sourceUrl, ConnectionConfig config)
        {
            HttpWebRequest      request         = HttpWebRequest.CreateHttp(sourceUrl);
            Task<WebResponse>   responseTask    = request.GetResponseAsync();
            Task                responseTimeout = MetaTask.Delay((int)config.ServerStatusHintConnectTimeout.Milliseconds);

            await Task.WhenAny(responseTimeout, responseTask).ConfigureAwaitFalse();

            // handle failures

            switch(responseTask.Status)
            {
                case TaskStatus.RanToCompletion:
                {
                    // pass
                    break;
                }

                case TaskStatus.Faulted:
                {
                    // failure
                    log.Debug("Server status check source failed. Endpoint={Endpoint}, Error={Error}", sourceUrl, responseTask.Exception.GetBaseException().Message);
                    return null;
                }

                default:
                {
                    // timeout
                    log.Debug("Server status check source timed out. Endpoint={Endpoint}", sourceUrl);
                    request.Abort();
                    responseTask.ContinueWithDispose();
                    return null;
                }
            }

            // success path

            using (WebResponse response = responseTask.GetCompletedResult())
            {
                using (MemoryStream memoryStream = new MemoryStream())
                {
                    Task readTimeout  = MetaTask.Delay((int)config.ServerStatusHintReadTimeout.Milliseconds);
                    Task readComplete = response.GetResponseStream().CopyToAsync(memoryStream);

                    await Task.WhenAny(readTimeout, readComplete).ConfigureAwaitFalse();

                    if (readComplete.Status != TaskStatus.RanToCompletion)
                    {
                        // failure
                        log.Debug("Server status check source read timed out. Endpoint={Endpoint}", sourceUrl);
                        return null;
                    }

                    ServerStatusHint status = TryParseServerStatusHint(memoryStream.GetBuffer(), (int)memoryStream.Length);
                    if (status == null)
                    {
                        log.Debug("Server status check source supplied invalid data, ignored. Endpoint={Endpoint}", sourceUrl);
                        return null;
                    }

                    log.Debug("Server status check complete. Endpoint={Endpoint}", sourceUrl);
                    return status;
                }
            }
        }

        private static async Task<ServerStatusHint> GetServerStatusHintAsync(IMetaLogger log, ServerEndpoint endpoint, ConnectionConfig config)
        {
            log.Debug("Server status check started");

            List<string> statusUrls = new List<string>();
            if (!endpoint.IsOfflineMode)
            {
                MetaplayCdnAddress cdnAddress = MetaplaySDK.CdnAddress.GetSubdirectoryAddress("Volatile");
                statusUrls.Add(cdnAddress.PrimaryBaseUrl + "serverStatusHint.json");
                statusUrls.Add(cdnAddress.SecondaryBaseUrl + "serverStatusHint.json");
            }

            foreach (string statusUrl in statusUrls)
            {
                try
                {
                    ServerStatusHint status = await TryGetServerStatusHintFromSourceAsync(log, statusUrl, config);
                    if (status != null)
                        return status;
                }
                finally
                {
                }
            }

            // no data. Return default status
            log.Debug("Server status check failed. All sources failed.");
            return ServerStatusHint.ForFetchFailure();
        }

        MaintenanceModeState ScheduledMaintenanceModeToMaintenanceModeState(ScheduledMaintenanceModeForClient scheduledMaintenanceMaybe)
        {
            if (scheduledMaintenanceMaybe == null)
                return MaintenanceModeState.CreateNotScheduled();

            MetaTime? estimatedMaintenanceOverAt;
            if (scheduledMaintenanceMaybe.EstimationIsValid)
                estimatedMaintenanceOverAt = scheduledMaintenanceMaybe.StartAt + MetaDuration.FromMinutes(scheduledMaintenanceMaybe.EstimatedDurationInMinutes);
            else
                estimatedMaintenanceOverAt = null;

            return MaintenanceModeState.CreateUpcoming(scheduledMaintenanceMaybe.StartAt, estimatedMaintenanceOverAt);
        }

        #endregion

        async Task<ConfigArchive> DownloadConfigArchiveAsync(string name, ContentHash version, string nullableUriSuffix, CancellationToken ct)
        {
            if (Endpoint.IsOfflineMode)
            {
                ConfigArchive configArchive = OfflineServer.GameConfigArchive;
                if (version != configArchive.Version)
                    throw new InvalidOperationException($"Attempted to use offline server with invalid config version ({version}): can only use current {configArchive.Version}");

                return configArchive;
            }
            else
            {
                // First look into cache if we have a valid copy.
                string                  cacheDirectory  = Path.Combine(MetaplaySDK.PersistentDataPath, "GameConfigCache");
                DiskBlobStorage         cacheStorage    = new DiskBlobStorage(cacheDirectory);
                StorageBlobProvider     cacheProvider   = new StorageBlobProvider(cacheStorage);

                byte[] cachedArchiveBytes = null;
                try
                {
                    cachedArchiveBytes = await cacheProvider.GetAsync(name, version);
                }
                catch(Exception ex)
                {
                    MetaplaySDK.Logs.Config.Warning("ConfigArchive disk lookup failed: {Error}", ex);
                }

                // If we have a cached archive, let's try to parse it. If we cannot parse it, delete it and
                // continue with normal path. Files on disk can become unreadable if config format changes.
                if (cachedArchiveBytes != null)
                {
                    try
                    {
                        return ConfigArchive.FromBytes(cachedArchiveBytes);
                    }
                    catch (Exception parseEx)
                    {
                        MetaplaySDK.Logs.Config.Warning("Failed to read ConfigArchive from cache. Purging: {Error}", parseEx);
                        try
                        {
                            await cacheStorage.DeleteAsync(cacheProvider.GetStorageFileName(name, version));
                        }
                        catch (Exception deleteEx)
                        {
                            MetaplaySDK.Logs.Config.Warning("Failed to delete ConfigArchive from cache: {Error}", deleteEx);
                        }
                    }
                }

                // Setup http-based GameConfigProvider with on-disk caching
                HttpBlobProvider            httpProvider    = new HttpBlobProvider(MetaHttpClient.DefaultInstance, MetaplaySDK.CdnAddress.GetSubdirectoryAddress("GameConfig"), uriSuffix: nullableUriSuffix);
                CachingBlobProvider         cachingProvider = new CachingBlobProvider(httpProvider, cacheProvider);
                BlobConfigArchiveProvider   archiveProvider = new BlobConfigArchiveProvider(cachingProvider, name);
                return await DownloadConfigWithRetriesAsync(taskFactory: () => archiveProvider.GetAsync(version), ct);
            }
        }

        async Task<GameConfigSpecializationPatches> DownloadConfigPatchesAsync(ContentHash version, CancellationToken ct)
        {
            // Hash 0 result in empty patches
            if (version == ContentHash.None)
                return null;

            if (Endpoint.IsOfflineMode)
                throw new InvalidOperationException("offline mode does not support patching");

            // Setup http-based GameConfigProvider with on-disk caching
            // \todo[jarkko]: remove this copy-pasta when ConfigManager lands.
            string                  cacheDirectory  = Path.Combine(MetaplaySDK.PersistentDataPath, "GameConfigCache");
            DiskBlobStorage         cacheStorage    = new DiskBlobStorage(cacheDirectory);
            HttpBlobProvider        httpProvider    = new HttpBlobProvider(MetaHttpClient.DefaultInstance, MetaplaySDK.CdnAddress.GetSubdirectoryAddress("GameConfig"));
            StorageBlobProvider     cacheProvider   = new StorageBlobProvider(cacheStorage);
            CachingBlobProvider     cachingProvider = new CachingBlobProvider(httpProvider, cacheProvider);

            return await DownloadConfigWithRetriesAsync(taskFactory: async () =>
            {
                string name = "SharedGameConfigPatches";
                byte[] bytes = await cachingProvider.GetAsync(name, version);
                if (bytes == null)
                    throw new InvalidOperationException($"Cache provide returned null when fetching game config patches version {version}");
                try
                {
                    return GameConfigSpecializationPatches.FromBytes(bytes);
                }
                catch (Exception parseEx)
                {
                    MetaplaySDK.Logs.Config.Warning("Failed to read game config patches from cache. Purging: {Error}", parseEx);
                    try
                    {
                        await cacheStorage.DeleteAsync(cacheProvider.GetStorageFileName(name, version));
                    }
                    catch (Exception deleteEx)
                    {
                        MetaplaySDK.Logs.Config.Warning("Failed to delete game config patches from cache: {Error}", deleteEx);
                    }
                    throw;
                }
            }, ct);
        }

        async Task<TResult> DownloadConfigWithRetriesAsync<TResult>(Func<Task<TResult>> taskFactory, CancellationToken ct)
        {
            int retryNdx = 0;
            for (;;)
            {
                Task                        cancellableTimeout  = MetaTask.Delay(Config.ConfigFetchTimeout.ToTimeSpan(), ct);
                Task<TResult>               fetchTask           = taskFactory();
                Exception                   fetchException;

                await Task.WhenAny(fetchTask, cancellableTimeout).ConfigureAwaitFalse();

                ct.ThrowIfCancellationRequested();

                switch (fetchTask.Status)
                {
                    case TaskStatus.RanToCompletion:
                        return fetchTask.GetCompletedResult();

                    case TaskStatus.Faulted:
                        fetchException = fetchTask.Exception.InnerException;
                        break;

                    default:
                    {
                        // timeout
                        fetchException = new TimeoutException();
                        break;
                    }
                }

                if (retryNdx < Config.ConfigFetchAttemptsMaxCount || Config.ConfigFetchAttemptsMaxCount == -1)
                {
                    ++retryNdx;
                    MetaplaySDK.Logs.Config.Debug("Configuration fetching failed, will retry (retryno={RetryNo}): {Exception}", retryNdx, fetchException);
                    continue;
                }

                MetaplaySDK.Logs.Config.Warning("Configuration fetching failed, will not retry anymore: {Exception}", fetchException);
                throw fetchException;
            }
        }

        public Task<ISharedGameConfig> GetSpecializedGameConfigAsync(ContentHash configVersion, ContentHash patchesVersion, GameConfigSpecializationKey specializationKey)
        {
            return GetSpecializedGameConfigAsync(configVersion, patchesVersion, specializationKey, CancellationToken.None);
        }

        public Task<ISharedGameConfig> GetSpecializedGameConfigAsync(ContentHash configVersion, ContentHash patchesVersion, OrderedDictionary<PlayerExperimentId, ExperimentVariantId> experimentAssignment)
        {
            return GetSpecializedGameConfigAsync(configVersion, patchesVersion, experimentAssignment, CancellationToken.None);
        }

        public async Task<ISharedGameConfig> GetSpecializedGameConfigAsync(ContentHash configVersion, ContentHash patchesVersion, OrderedDictionary<PlayerExperimentId, ExperimentVariantId> experimentAssignment, CancellationToken ct)
        {
            ConfigArchive configArchive = await DownloadConfigArchiveAsync("SharedGameConfig", configVersion, nullableUriSuffix: null, ct);
            GameConfigSpecializationPatches patches       = await DownloadConfigPatchesAsync(patchesVersion, ct);

            GameConfigSpecializationKey specializationKey;

            if (patches != null)
                specializationKey = patches.CreateKeyFromAssignment(experimentAssignment);
            else
                specializationKey = default;

            return GetSpecializedGameConfig(configArchive, patches, specializationKey);
        }

        public async Task<ISharedGameConfig> GetSpecializedGameConfigAsync(ContentHash configVersion, ContentHash patchesVersion, GameConfigSpecializationKey specializationKey, CancellationToken ct)
        {
            ConfigArchive configArchive = await DownloadConfigArchiveAsync("SharedGameConfig", configVersion, nullableUriSuffix: null, ct);
            GameConfigSpecializationPatches patches = await DownloadConfigPatchesAsync(patchesVersion, ct);

            return GetSpecializedGameConfig(configArchive, patches, specializationKey);
        }

        static ISharedGameConfig GetSpecializedGameConfig(ConfigArchive configArchive, GameConfigSpecializationPatches patches, GameConfigSpecializationKey specializationKey)
        {
            OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope> patchesToApply;

            if (patches != null)
                patchesToApply = patches.GetPatchesForSpecialization(specializationKey);
            else
                patchesToApply = new OrderedDictionary<ExperimentVariantPair, GameConfigPatchEnvelope>();

            GameConfigImportParams importParams = GameConfigImportParams.CreateSoloSpecialization(
                GameConfigRepository.Instance.SharedGameConfigType,
                configArchive,
                patchesToApply);

            return (ISharedGameConfig)GameConfigFactory.Instance.ImportGameConfig(importParams);
        }

        abstract class SessionResourceLoader
        {
            protected LogChannel _log;
            protected CancellationToken _ct;

            public bool IsComplete { get; protected set; }

            protected SessionResourceLoader(LogChannel log, CancellationToken ct)
            {
                _log = log;
                _ct = ct;
                IsComplete = true;
            }

            public TransientError TryActivate()
            {
                try
                {
                    Activate();
                    IsComplete = true;
                    return null;
                }
                catch (Exception ex)
                {
                    _log.Debug("Failure while activating resource: {Error}", ex);
                    return new TransientError.ConfigFetchFailed(ex, TransientError.ConfigFetchFailed.FailureSource.Activation);
                }
            }
            public TransientError.ConfigFetchFailed TrySpecialize(SessionProtocol.SessionStartSuccess sessionStartSuccess)
            {
                try
                {
                    Specialize(sessionStartSuccess);
                    return null;
                }
                catch (Exception ex)
                {
                    _log.Debug("Failure while activating specializing: {Error}", ex);
                    return new TransientError.ConfigFetchFailed(ex, TransientError.ConfigFetchFailed.FailureSource.Activation);
                }
            }

            public virtual void Reset()
            {
                IsComplete = true;
            }

            public abstract void SetupFromResourceCorrection(SessionProtocol.SessionResourceCorrection resourceCorrection);
            public abstract bool PollDownload(out TransientError error);
            protected abstract void Activate();
            protected virtual void Specialize(SessionProtocol.SessionStartSuccess sessionStartSuccess) { }
        }
        abstract class DownloadResourceLoader : SessionResourceLoader
        {
            protected IDownload Download { get; private set; }

            protected DownloadResourceLoader(LogChannel log, CancellationToken ct) : base(log, ct)
            {
            }

            public override sealed bool PollDownload(out TransientError error)
            {
                if (Download == null)
                {
                    error = null;
                    return false;
                }

                DownloadStatus loadStatus = Download.Status;
                switch(loadStatus.Code)
                {
                    case DownloadStatus.StatusCode.Completed:
                    {
                        error = null;
                        return true;
                    }

                    case DownloadStatus.StatusCode.Error:
                    {
                        _log.Debug("Failure while fetching resource: {State}", loadStatus.Error);
                        error = new ConnectionStates.TransientError.ConfigFetchFailed(loadStatus.Error, TransientError.ConfigFetchFailed.FailureSource.ResourceFetch);
                        return true;
                    }

                    case DownloadStatus.StatusCode.Timeout:
                    {
                        _log.Debug("Timeout while fetching resource");
                        error = new ConnectionStates.TransientError.Timeout(TransientError.Timeout.TimeoutSource.ResourceFetch);
                        return true;
                    }

                    default:
                    {
                        // not ready yet
                        error = null;
                        return false;
                    }
                }
            }

            public override void Reset()
            {
                Download?.Dispose();
                Download = null;
                base.Reset();
            }

            protected void SetupWithDownload(IDownload download)
            {
                Reset();
                Download = download;
                IsComplete = false;
            }
        }
        class ConfigResourceLoader : SessionResourceLoader
        {
            OrderedDictionary<ClientSlot, DownloadTaskWrapper<ConfigArchive>> _configArchiveDownloads;
            OrderedDictionary<ClientSlot, DownloadTaskWrapper<GameConfigSpecializationPatches>>     _patchArchiveDownloads;
            DownloadTaskWrapper<LocalizationLanguage>                                               _localizationDownload;
            ClientSessionNegotiationResources                                                       _negotiationResources;

            public ClientSessionNegotiationResources NegotiationResources => _negotiationResources;

            public ConfigResourceLoader(LogChannel log, CancellationToken ct) : base(log, ct)
            {
                _configArchiveDownloads = new OrderedDictionary<ClientSlot, DownloadTaskWrapper<ConfigArchive>>();
                _patchArchiveDownloads = new OrderedDictionary<ClientSlot, DownloadTaskWrapper<GameConfigSpecializationPatches>>();
            }

            public override void Reset()
            {
                foreach (DownloadTaskWrapper<ConfigArchive> task in _configArchiveDownloads.Values)
                    task.Dispose();
                foreach (DownloadTaskWrapper<GameConfigSpecializationPatches> task in _patchArchiveDownloads.Values)
                    task.Dispose();
                _configArchiveDownloads.Clear();
                _patchArchiveDownloads.Clear();

                _localizationDownload?.Dispose();
                _localizationDownload = null;

                _negotiationResources = new ClientSessionNegotiationResources();
                _negotiationResources.ActiveLanguage = MetaplaySDK.ActiveLanguage;

                base.Reset();
            }

            public override bool PollDownload(out TransientError error)
            {
                bool allDownloadsComplete = true;
                TransientError firstError = null;

                foreach ((ClientSlot slot, DownloadTaskWrapper<ConfigArchive> task) in _configArchiveDownloads)
                {
                    PollSingleDownload(resourceNameForErrorMessage: $"slot {slot}", task, out bool isComplete, out ConnectionStates.TransientError subError);
                    if (!isComplete)
                        allDownloadsComplete = false;
                    if (subError != null && firstError == null)
                        firstError = subError;

                }
                foreach ((ClientSlot slot, DownloadTaskWrapper<GameConfigSpecializationPatches> task) in _patchArchiveDownloads)
                {
                    PollSingleDownload(resourceNameForErrorMessage: $"slot {slot}", task, out bool isComplete, out ConnectionStates.TransientError subError);
                    if (!isComplete)
                        allDownloadsComplete = false;
                    if (subError != null && firstError == null)
                        firstError = subError;
                }
                if (_localizationDownload != null)
                {
                    PollSingleDownload(resourceNameForErrorMessage: "localizations", _localizationDownload, out bool isComplete, out ConnectionStates.TransientError subError);
                    if (!isComplete)
                        allDownloadsComplete = false;
                    if (subError != null && firstError == null)
                        firstError = subError;
                }

                if (firstError != null)
                {
                    error = firstError;
                    return true;
                }
                if (allDownloadsComplete)
                {
                    error = null;
                    return true;
                }
                error = null;
                return false;
            }

            void PollSingleDownload(string resourceNameForErrorMessage, IDownload task, out bool isComplete, out ConnectionStates.TransientError error)
            {
                DownloadStatus loadStatus = task.Status;
                switch(loadStatus.Code)
                {
                    case DownloadStatus.StatusCode.Completed:
                    {
                        error = null;
                        isComplete = true;
                        break;
                    }

                    case DownloadStatus.StatusCode.Error:
                    {
                        _log.Warning("Failure while fetching resource for {ResourceName}: {State}", resourceNameForErrorMessage, loadStatus.Error);
                        error = new ConnectionStates.TransientError.ConfigFetchFailed(loadStatus.Error, TransientError.ConfigFetchFailed.FailureSource.ResourceFetch);
                        isComplete = true;
                        break;
                    }

                    case DownloadStatus.StatusCode.Timeout:
                    {
                        _log.Warning("Timeout while fetching resource for {ResourceName}", resourceNameForErrorMessage);
                        error = new ConnectionStates.TransientError.Timeout(TransientError.Timeout.TimeoutSource.ResourceFetch);
                        isComplete = true;
                        break;
                    }

                    default:
                    {
                        // not ready yet
                        error = null;
                        isComplete = false;
                        break;
                    }
                }
            }

            public override void SetupFromResourceCorrection(SessionProtocol.SessionResourceCorrection resourceCorrection)
            {
                Reset();

                foreach ((ClientSlot slot, SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo configUpdate) in resourceCorrection.ConfigUpdates)
                {
                    _configArchiveDownloads[slot] = new DownloadTaskWrapper<ConfigArchive>(MetaplaySDK.Connection.DownloadConfigArchiveAsync(
                        "SharedGameConfig",
                        configUpdate.SharedGameConfigVersion,
                        configUpdate.UrlSuffix,
                        _ct));
                }

                foreach ((ClientSlot slot, SessionProtocol.SessionResourceCorrection.ConfigPatchesUpdateInfo configPatches) in resourceCorrection.PatchUpdates)
                {
                    _patchArchiveDownloads[slot] = new DownloadTaskWrapper<GameConfigSpecializationPatches>(MetaplaySDK.Connection.DownloadConfigPatchesAsync(
                        configPatches.PatchesVersion,
                        _ct));
                }

                if (resourceCorrection.LanguageUpdate.HasValue)
                {
                    _localizationDownload = new DownloadTaskWrapper<LocalizationLanguage>(MetaplaySDK.LocalizationManager.FetchLanguageAsync(
                        language:           resourceCorrection.LanguageUpdate.Value.ActiveLanguage,
                        version:            resourceCorrection.LanguageUpdate.Value.LocalizationVersion,
                        cdnAddress:         MetaplaySDK.CdnAddress,
                        numFetchAttempts:   MetaplaySDK.Connection.Config.ConfigFetchAttemptsMaxCount,
                        fetchTimeout:       MetaplaySDK.Connection.Config.ConfigFetchTimeout,
                        ct:                 _ct));
                }

                if (_configArchiveDownloads.Count > 0 || _patchArchiveDownloads.Count > 0 || _localizationDownload != null)
                    IsComplete = false;
            }

            protected override void Activate()
            {
                foreach ((ClientSlot slot, DownloadTaskWrapper<ConfigArchive> task) in _configArchiveDownloads)
                {
                    _negotiationResources.ConfigArchives[slot] = task.GetResult();
                }

                foreach ((ClientSlot slot, DownloadTaskWrapper<GameConfigSpecializationPatches> task) in _patchArchiveDownloads)
                {
                    _negotiationResources.PatchArchives[slot] = task.GetResult();
                }

                if (_localizationDownload != null)
                {
                    _negotiationResources.ActiveLanguage = _localizationDownload.GetResult();
                }

                MetaplaySDK.LocalizationManager.ActivateSessionStartLanguage(_negotiationResources.ActiveLanguage);
            }

            protected override void Specialize(SessionProtocol.SessionStartSuccess sessionStartSuccess)
            {
                Func<ConfigArchive, (GameConfigSpecializationPatches, GameConfigSpecializationKey)?, ISharedGameConfig> gameConfigImporter = (ConfigArchive archive, (GameConfigSpecializationPatches, GameConfigSpecializationKey)? specialization) =>
                {
                    GameConfigSpecializationPatches patches = specialization?.Item1;
                    GameConfigSpecializationKey specializationKey = specialization?.Item2 ?? default;

                    return GetSpecializedGameConfig(archive, patches, specializationKey);
                };

                MetaplaySDK.Connection.SessionStartResources = ClientSessionStartResources.SpecializeResources(sessionStartSuccess, _negotiationResources, gameConfigImporter);
                MetaplaySDK.Connection.LatestGameConfigInfo = new ConnectionGameConfigInfo(
                        baselineVersion:        _negotiationResources.ConfigArchives[ClientSlotCore.Player].Version,
                        patchesVersion:         _negotiationResources.PatchArchives.GetValueOrDefault(ClientSlotCore.Player)?.Version ?? ContentHash.None,
                        experimentMemberships:  _negotiationResources.PatchArchives.ContainsKey(ClientSlotCore.Player) ? sessionStartSuccess.ActiveExperiments.Select(ex => new ExperimentVariantPair(ex.ExperimentId, ex.VariantId)).ToList() : null);
            }
        }

        private IEnumerator<Marker> SupervisionLoop(CancellationToken ct)
        {
            List<MetaMessage>                                   messageBuffer                               = new List<MetaMessage>();
            List<MetaMessage>                                   delayedLoginMessageBuffer                   = new List<MetaMessage>();
            TransportQosMonitor                                 qosMonitor                                  = new TransportQosMonitor();
            int                                                 numConnectAttempts                          = 0;
            MetaTime                                            latestMessagesTimestamp                     = new MetaTime();
            Task<NetworkDiagnosticReport>                       networkDiagnosticReportTask                 = null;
            Task<ServerStatusHint>                              serverStatusHintFetchTask                   = null;
            ConnectionState                                     cannotConnectError;                                 // \note: intentionally undefined to make compiler check for assignment in all control flows
            ConnectionStates.ErrorState                         sessionStartError;                                  // \note: intentionally undefined to make compiler check for assignment in all control flows
            ConfigResourceLoader                                configResourceLoader                        = new ConfigResourceLoader(Log, ct);
            List<SessionResourceLoader>                         sessionResourceLoaders                      = new List<SessionResourceLoader>();
            ScheduledMaintenanceModeForClient                   latestConnectionScheduledMaintenanceMode    = null;
            Handshake.ServerOptions                             latestServerOptions                         = default;
            ClientAppPauseStatus                                lastConnectionPauseStatus;
            ISessionCredentialService.LoginMethod               loginMethod;

            int lastSentSessionResumptionPingId     = 0;
            int lastReceivedSessionResumptionPongId = 0;

            MetaTime lastSessionResumptionPingSentAt = MetaTime.Epoch;
            int lastIncidentReportedSessionResumptionPingId = 0;
            int numSessionResumptionPingIncidentsReported = 0;

            _statistics.CurrentConnection = ConnectionStatistics.CurrentConnectionStats.ForNewConnection();
            State = new ConnectionStates.Connecting(connectionAttempt: 0);

            sessionResourceLoaders.Add(configResourceLoader);

            NetworkProbe networkProbe = NetworkProbe.TestConnectivity(_logForwardingBuffer, Endpoint);

            // Await SessionCredentialStore to initialize
            if (_sessionCredentialServiceInitTask != null)
            {
                MetaTime nextWarnAt = MetaTime.Now + MetaDuration.FromSeconds(5);
                for (;;)
                {
                    if (_sessionCredentialServiceInitTask.IsCompleted)
                        break;
                    if (ct.IsCancellationRequested)
                        goto cancellation_requested;
                    if (MetaTime.Now > nextWarnAt)
                    {
                        Log.Warning("Still waiting for CredentialService initialization.");
                        nextWarnAt = MetaTime.Now + MetaDuration.FromSeconds(5);
                    }
                    yield return Marker.UpdateComplete;
                }
                if (_sessionCredentialServiceInitTask.Status != TaskStatus.RanToCompletion)
                {
                    Exception ex = _sessionCredentialServiceInitTask.Exception.GetBaseException();
                    Log.Debug("Failure initializing credential service: {Error}", ex);
                    cannotConnectError = new ConnectionStates.TerminalError.ClientSideConnectionError(ex);
                    goto wait_cannot_connect_error_diagnostics_then_dispose;
                }

                MetaplaySDK.PlayerId = _sessionCredentialServiceInitTask.GetCompletedResult();
                _sessionCredentialServiceInitTask = null;
            }

            // First connect is not a reconnect.
            _sessionNonceService.NewSession();

            reconnect:
            {
                // Resolve login method
                {
                    Task<ISessionCredentialService.LoginMethod> task;
                    try
                    {
                        task = _sessionCredentialService.GetCurrentLoginMethodAsync();
                    }
                    catch (Exception ex)
                    {
                        task = Task.FromException<ISessionCredentialService.LoginMethod>(ex);
                    }

                    MetaTime nextWarnAt = MetaTime.Now + MetaDuration.FromSeconds(5);
                    for (;;)
                    {
                        if (task.IsCompleted)
                            break;
                        if (ct.IsCancellationRequested)
                            goto cancellation_requested;
                        if (MetaTime.Now > nextWarnAt)
                        {
                            Log.Warning("Still waiting for CredentialService login method.");
                            nextWarnAt = MetaTime.Now + MetaDuration.FromSeconds(5);
                        }
                        yield return Marker.UpdateComplete;
                    }
                    if (task.Status != TaskStatus.RanToCompletion)
                    {
                        Exception ex = task.Exception.GetBaseException();
                        Log.Debug("Failure resolving login method: {Error}", ex);
                        cannotConnectError = new ConnectionStates.TerminalError.ClientSideConnectionError(ex);
                        goto wait_cannot_connect_error_diagnostics_then_dispose;
                    }

                    loginMethod = task.GetCompletedResult();
                }

                foreach (SessionResourceLoader resource in sessionResourceLoaders)
                    resource.Reset();

                lastConnectionPauseStatus = ToClientAppPauseStatus(MetaplaySDK.ApplicationPauseStatus);

                Task<ServerConnection> connectTask = CreateConnection(configResourceLoader.NegotiationResources.ToResourceProposal(), lastConnectionPauseStatus, loginMethod);
                for (; ; )
                {
                    if (connectTask.IsCompleted)
                        break;
                    yield return Marker.UpdateComplete;
                    if (ct.IsCancellationRequested)
                        goto cancellation_requested;
                }
                if (connectTask.Status != TaskStatus.RanToCompletion)
                {
                    Log.Debug("Failure initializing connection: {Error}", connectTask.Exception);
                    State = new ConnectionStates.TerminalError.ClientSideConnectionError(connectTask.Exception.InnerException);
                    goto dispose;
                }
                _serverConnection = connectTask.GetCompletedResult();

                networkDiagnosticReportTask = null; // don't leak reports from previous connections
                // serverStatusHintFetchTask is kept over connection attempts.
                qosMonitor.Reset();
                delayedLoginMessageBuffer.Clear();
                MetaplaySDK.MessageDispatcher.SetConnection(this);
                goto wait_for_connection;
            }

            wait_for_connection:
            {
                bool sessionStarted = false;
                bool isConnected = false;
                MetaTime connectOrReconnectStartedAt = MetaTime.Now;
                MetaTime? sessionInitRequestTimeoutAt = null;

                while (true)
                {
                    // Handle messages internally as they come, but don't let others
                    // until we know the connection was established successfully.
                    MessageTransport.Error connectionError = _serverConnection.ReceiveMessages(messageBuffer);
                    qosMonitor.ProcessMessages(messageBuffer);

                    // Linearize protocol logging with its side effects
                    _logForwardingBuffer.FlushBufferedToSink();

                    foreach (MetaMessage message in messageBuffer)
                    {
                        switch (message)
                        {
                            case ConnectedToServer connected:
                            {
                                // Update the current CDN to the connected server. We might have been redirected and IPv4-IPv6-preference might have changed.
                                if (!Endpoint.IsOfflineMode)
                                    _sdkHook.OnCurrentCdnAddressUpdated(MetaplayCdnAddress.Create(Endpoint.CdnBaseUrl, prioritizeIPv4: connected.IsIPv4));
                                else
                                    _sdkHook.OnCurrentCdnAddressUpdated(MetaplayCdnAddress.Empty);

                                LatestTlsPeerDescription = connected.TlsPeerDescription;

                                // bake domain transforms for exteral API
                                // \todo: better API
                                // \todo: the server host & port is wrong if we ended up using a backup gateway
                                Endpoint = new ServerEndpoint(
                                    serverHost:     Endpoint.ServerHost,
                                    serverPort:     Endpoint.ServerPort,
                                    enableTls:      Endpoint.EnableTls,
                                    cdnBaseUrl:     MetaplaySDK.CdnAddress.PrimaryBaseUrl,
                                    backupGateways: Endpoint.BackupGateways.ToList()
                                    );

                                // \todo: should come from the transport. This is too late.
                                latestMessagesTimestamp = MetaTime.Now;
                                _statistics.CurrentConnection.HasCompletedHandshake = true;
                                isConnected = true;
                                Delegate.OnHandshakeComplete();
                                break;
                            }

                            case Handshake.ClientHelloAccepted helloAccepted:
                            {
                                latestServerOptions = helloAccepted.ServerOptions;
                                break;
                            }

                            case Handshake.LoginSuccessResponse loginResponse:
                            {
                                AuthenticationPlatform updatedPlayerIdPlatform;
                                EntityId updatedPlayerId;
                                EntityId oldPlayerIdHint;

                                switch (loginMethod)
                                {
                                    case ISessionCredentialService.NewGuestAccountLoginMethod _:
                                    {
                                        // Account already created specially, no need to do anything.
                                        break;
                                    }
                                    case ISessionCredentialService.GuestAccountLoginMethod guestAccount:
                                    {
                                        oldPlayerIdHint = guestAccount.GuestCredentials.PlayerIdHint;
                                        updatedPlayerIdPlatform = AuthenticationPlatform.DeviceId;
                                        updatedPlayerId = loginResponse.LoggedInPlayerId;
                                        goto handle_player_id_update;
                                    }
                                    case ISessionCredentialService.SocialAuthLoginMethod socialAuthAccount:
                                    {
                                        oldPlayerIdHint = socialAuthAccount.PlayerIdHint;
                                        updatedPlayerIdPlatform = socialAuthAccount.Claim.Platform;
                                        updatedPlayerId = loginResponse.LoggedInPlayerId;
                                        goto handle_player_id_update;
                                    }
                                    default:
                                    {
                                        Log.Error("Invalid LoginMethod from SessionCredentialService");
                                        sessionStartError = new ConnectionStates.TerminalError.ClientSideConnectionError(new InvalidOperationException("Invalid LoginMethod from SessionCredentialService"));
                                        goto inform_server_of_session_setup_failure_then_dispose;
                                    }
                                }
                                break;

                            handle_player_id_update:
                                // Update Login PlayerID into the used credentials if it has changed.
                                if (oldPlayerIdHint == loginResponse.LoggedInPlayerId)
                                    break;

                                Log.Information("Server updated our {Platform} PlayerId on login: old={OldPlayerId}, new={NewPlayerId}", updatedPlayerIdPlatform, oldPlayerIdHint, loginResponse.LoggedInPlayerId);
                                Task task;
                                try
                                {
                                    task = _sessionCredentialService.OnPlayerIdUpdatedAsync(updatedPlayerIdPlatform, updatedPlayerId);
                                }
                                catch (Exception ex)
                                {
                                    task = Task.FromException(ex);
                                }
                                for (;;)
                                {
                                    if (task.IsCompleted)
                                        break;
                                    if (ct.IsCancellationRequested)
                                        goto cancellation_requested;
                                    yield return Marker.UpdateComplete;
                                }
                                if (task.Status != TaskStatus.RanToCompletion)
                                {
                                    Exception ex = task.Exception.GetBaseException();
                                    Log.Debug("Failure while writing playerId update: {Error}", ex);
                                    sessionStartError = new ConnectionStates.TerminalError.ClientSideConnectionError(ex);
                                    goto inform_server_of_session_setup_failure_then_dispose;
                                }
                                break;
                            }

                            case SessionProtocol.SessionStartSuccess sessionStart:
                            {
                                latestConnectionScheduledMaintenanceMode = sessionStart.ScheduledMaintenanceMode;
                                sessionStarted = true;
                                sessionInitRequestTimeoutAt = null;

                                // specialize game configs for the current player
                                foreach (SessionResourceLoader resource in sessionResourceLoaders)
                                {
                                    TransientError.ConfigFetchFailed error = resource.TrySpecialize(sessionStart);
                                    if (error != null)
                                    {
                                        sessionStartError = error;
                                        goto inform_server_of_session_setup_failure_then_dispose;
                                    }
                                }

                                ServerOptions = latestServerOptions;
                                _sdkHook.OnSessionStarted(sessionStart);

                                // run activation
                                try
                                {
                                    Delegate.OnSessionStarted(SessionStartResources);
                                }
                                catch (Exception ex)
                                {
                                    Log.Debug("Failure while fetching configs: {Exception}", ex);
                                    sessionStartError = new TransientError.ConfigFetchFailed(ex, TransientError.ConfigFetchFailed.FailureSource.Activation);
                                    goto inform_server_of_session_setup_failure_then_dispose;
                                }
                                break;
                            }

                            case UpdateScheduledMaintenanceMode updateScheduledMaintenanceMode:
                            {
                                latestConnectionScheduledMaintenanceMode = updateScheduledMaintenanceMode.ScheduledMaintenanceMode;
                                break;
                            }

                            case MessageTransportInfoWrapperMessage infoWrapper:
                            {
                                switch (infoWrapper.Info)
                                {
                                    case ServerConnection.GotServerHello gotServerHello:
                                    {
                                        LatestServerVersion = gotServerHello.ServerHello.ServerVersion;
                                        break;
                                    }

                                    case ServerConnection.GuestAccountCreatedInfo accountCreated:
                                    {
                                        Log.Information("New guest account created: deviceId={DeviceId}, playerId={PlayerId}", accountCreated.GuestCredentials.DeviceId, accountCreated.GuestCredentials.PlayerIdHint);

                                        Task task;
                                        try
                                        {
                                            task = _sessionCredentialService.OnGuestAccountCreatedAsync(accountCreated.GuestCredentials);
                                        }
                                        catch (Exception ex)
                                        {
                                            task = Task.FromException(ex);
                                        }
                                        for (;;)
                                        {
                                            if (task.IsCompleted)
                                                break;
                                            if (ct.IsCancellationRequested)
                                                goto cancellation_requested;
                                            yield return Marker.UpdateComplete;
                                        }
                                        if (task.Status != TaskStatus.RanToCompletion)
                                        {
                                            Exception ex = task.Exception.GetBaseException();
                                            Log.Debug("Failure while writing credentials: {Error}", ex);
                                            sessionStartError = new ConnectionStates.TerminalError.ClientSideConnectionError(new CannotWriteCredentialsOnDiskError());
                                            goto inform_server_of_session_setup_failure_then_dispose;
                                        }

                                        // After saving credentials, retry login
                                        _serverConnection.ContinueGuestLoginAfterAccountCreation(accountCreated.GuestCredentials);
                                        break;
                                    }

                                    case ServerConnection.ResourceCorrectionInfo resourceCorrection:
                                    {
                                        sessionInitRequestTimeoutAt = null;
                                        foreach (SessionResourceLoader resource in sessionResourceLoaders)
                                            resource.SetupFromResourceCorrection(resourceCorrection.ResourceCorrection);
                                        break;
                                    }

                                    case ServerConnection.FullProtocolHashMismatchInfo protocolHashMismatch:
                                    {
                                        Delegate.OnFullProtocolHashMismatch(clientProtocolHash: protocolHashMismatch.ClientProtocolHash, serverProtocolHash: protocolHashMismatch.ServerProtocolHash);
                                        break;
                                    }

                                    case ServerConnection.SessionStartRequested _:
                                    {
                                        sessionInitRequestTimeoutAt = MetaTime.Now + Config.ServerSessionInitTimeout;
                                        break;
                                    }
                                }
                                break;
                            }

                            case Handshake.OperationStillOngoing _:
                            {
                                // \note: sessionInitRequestTimeoutAt might not have a value if there is something long-running in the protocol that happens before client has started session start.
                                //        For example, ClientToken creation request could take a long time.
                                if (sessionInitRequestTimeoutAt.HasValue)
                                    sessionInitRequestTimeoutAt = MetaTime.Now + Config.ServerSessionInitTimeout;
                                break;
                            }
                        }
                    }

                    if (messageBuffer.Count > 0)
                    {
                        // \todo: should come from the transport. This is too late.
                        latestMessagesTimestamp = MetaTime.Now;
                    }

                    delayedLoginMessageBuffer.AddRange(messageBuffer);
                    messageBuffer.Clear();
                    _statistics.CurrentConnection.NetworkProbeStatus = networkProbe.TryGetConnectivityState();

                    if (connectionError != null)
                    {
                        // Handle redirect to another server (used for handling with clients from the future during app review & production build testing).
                        if (connectionError is ServerConnection.RedirectToServerError redirect)
                        {
                            Log.Information("Redirecting to server: {RedirectToServer}", redirect.RedirectToServer);
                            Endpoint = redirect.RedirectToServer;
                            goto redirect_and_reconnect;
                        }

                        // try retry
                        ConnectionState errorState = TranslateError(connectionError);
                        Log.Debug("Failure while connecting. {Error}, causes {State}", PrettyPrint.Compact(connectionError), PrettyPrint.Compact(errorState));
                        if (errorState is ConnectionStates.TransientError)
                        {
                            // First connection failed, start generating report already
                            if (networkDiagnosticReportTask == null)
                                networkDiagnosticReportTask = GetNetworkDiagnosticReportAsync();

                            // First connection failed (of the session), start fetching server status
                            if (serverStatusHintFetchTask == null)
                                serverStatusHintFetchTask = GetServerStatusHintAsync(_logForwardingBuffer, Endpoint, Config);

                            // Connection attempt failed. This attempt was for the initial connection (i.e. before session), we may
                            // try again. Try to connect as many times as configured.
                            numConnectAttempts++;
                            if (Config.ConnectAttemptsMaxCount == -1 || numConnectAttempts < Config.ConnectAttemptsMaxCount)
                            {
                                State = new ConnectionStates.Connecting(connectionAttempt: numConnectAttempts);
                                goto wait_and_reconnect;
                            }

                            // The connection doesn't fail before ServerStatusHint is received, so let's reconnect until the hint fetch
                            // completes. This has the risk of extending connection failure indefinitely, but for this reason, serverStatusHint
                            // has strict timeouts.
                            // There are two goals: Provide proper error result for the user and future use where ServerStatus
                            // contains the DNS information for the server. We want at least one attempt with the provided info.
                            if (!serverStatusHintFetchTask.IsCompleted)
                            {
                                // \note: we may exceed maximum reconnect count here. It's better to spend that time reconnecting
                                //        than just waiting.
                                State = new ConnectionStates.Connecting(connectionAttempt: numConnectAttempts);
                                goto wait_and_reconnect;
                            }
                        }
                        else if (errorState is ConnectionStates.TerminalError.InMaintenance)
                            goto observed_protocol_in_maintenance_during_connect;

                        cannotConnectError = TranslateConnectionErrorForUser(errorState);
                        goto wait_cannot_connect_error_diagnostics_then_dispose;
                    }
                    if (sessionInitRequestTimeoutAt.HasValue && MetaTime.Now >= sessionInitRequestTimeoutAt.Value)
                    {
                        Log.Warning("Timeout while waiting for session init response from server.");
                        sessionStartError = new ConnectionStates.TransientError.Timeout(TransientError.Timeout.TimeoutSource.Stream);
                        goto inform_server_of_session_setup_failure_then_dispose;
                    }

                    bool someMissingLoginResourceCompleted = false;
                    bool allResourcesCompleted = true;
                    foreach (SessionResourceLoader resource in sessionResourceLoaders)
                    {
                        if (!resource.IsComplete)
                        {
                            // wait for resource to complete fetch
                            if (!resource.PollDownload(out ConnectionStates.TransientError error))
                            {
                                allResourcesCompleted = false;
                                continue;
                            }

                            if (error != null)
                            {
                                sessionStartError = error;
                                goto inform_server_of_session_setup_failure_then_dispose;
                            }

                            // run activation
                            error = resource.TryActivate();
                            if (error != null)
                            {
                                sessionStartError = error;
                                goto inform_server_of_session_setup_failure_then_dispose;
                            }

                            someMissingLoginResourceCompleted = true;
                        }
                    }

                    // last missing resource completed
                    if (someMissingLoginResourceCompleted && allResourcesCompleted)
                    {
                        SessionProtocol.SessionResourceProposal resourceProposal = configResourceLoader.NegotiationResources.ToResourceProposal();
                        lastConnectionPauseStatus = ToClientAppPauseStatus(MetaplaySDK.ApplicationPauseStatus);
                        _serverConnection.RetrySessionStart(resourceProposal, lastConnectionPauseStatus);
                    }

                    if (sessionStarted)
                    {
                        _statistics.CurrentConnection.HasCompletedSessionInit = true;
                        goto connection_active;
                    }

                    // If login takes too long, start fetching server status from Cdn
                    if (!isConnected && serverStatusHintFetchTask == null && MetaTime.Now >= connectOrReconnectStartedAt + Config.ServerStatusHintCheckDelay)
                        serverStatusHintFetchTask = GetServerStatusHintAsync(_logForwardingBuffer, Endpoint, Config);

                    // If we observe maintenance mode in hint, abort connecting immediately.
                    if (serverStatusHintFetchTask != null && serverStatusHintFetchTask.IsCompleted)
                    {
                        ServerStatusHint status = serverStatusHintFetchTask.GetCompletedResult();
                        if (status.MaintenanceMode != null)
                            goto observed_server_status_hint_in_maintenance_during_connect;
                    }

                    yield return Marker.UpdateComplete;
                    if (ct.IsCancellationRequested)
                        goto cancellation_requested;
                }
            }

            connection_active:
            {
                ServerConnection.SessionResumptionAttempt   ongoingSessionResumeAttempt             = null;
                DateTime                                    ongoingSessionResumeCompleteDeadlineAt  = default;
                bool                                        waitingForSessionResumeAttempt          = false;

                State = new ConnectionStates.Connected(isHealthy: qosMonitor.IsHealthy, latestReceivedMessageTimestamp: latestMessagesTimestamp);

                // if pause status has changed during the connection, update it
                if (lastConnectionPauseStatus != ToClientAppPauseStatus(MetaplaySDK.ApplicationPauseStatus))
                {
                    switch (MetaplaySDK.ApplicationPauseStatus)
                    {
                        case ApplicationPauseStatus.Running:
                            _serverConnection.EnqueueSendMessage(new ClientLifecycleHintUnpaused());
                            break;

                        case ApplicationPauseStatus.Pausing:
                            _serverConnection.EnqueueSendMessage(new ClientLifecycleHintPausing(MetaplaySDK.ApplicationPauseDeclaredMaxDuration, MetaplaySDK.ApplicationPauseReason));
                            break;

                        case ApplicationPauseStatus.ResumedFromPauseThisFrame:
                            _serverConnection.EnqueueSendMessage(new ClientLifecycleHintUnpaused());
                            break;
                    }
                }

                // flush messages witheld during connection
                _messagesToDispatch.AddRange(delayedLoginMessageBuffer);
                delayedLoginMessageBuffer.Clear();

                // now that we have the connection, update the maintenance mode that we potentially got during the login sequence
                _sdkHook.OnScheduledMaintenanceModeUpdated(ScheduledMaintenanceModeToMaintenanceModeState(latestConnectionScheduledMaintenanceMode));

                while (true)
                {
                    MessageTransport.Error  connectionError = _serverConnection.ReceiveMessages(messageBuffer);
                    bool                    gotMessages     = messageBuffer.Count > 0;
                    bool                    qosChanged      = qosMonitor.ProcessMessages(messageBuffer);

                    // Linearize protocol logging with its side effects
                    _logForwardingBuffer.FlushBufferedToSink();

                    foreach (MetaMessage receivedMessage in messageBuffer)
                    {
                        switch (receivedMessage)
                        {
                            case SessionProtocol.SessionResumeSuccess loginSuccess:
                            {
                                // transparent session resume
                                latestConnectionScheduledMaintenanceMode = loginSuccess.ScheduledMaintenanceMode;
                                _sdkHook.OnScheduledMaintenanceModeUpdated(ScheduledMaintenanceModeToMaintenanceModeState(latestConnectionScheduledMaintenanceMode));

                                // Send ping. The connection will remain considered "unhealthy" until we get the pong.
                                //
                                // The purpose of this session resumption ping-ponging is to improve UI upon certain
                                // connectivity issues where session messages are not received by the server, yet logins
                                // and session resumptions succeed. In that case, we wish to indicate connection unhealthiness
                                // despite the apparent success in connection (i.e. successful login and resumption).
                                //
                                // Since the ping-pong happens on the session messaging level, receiving the pong
                                // also implies that the server has received all of our session messages preceding the ping.
                                //
                                _serverConnection.EnqueueSendMessage(new SessionPing(++lastSentSessionResumptionPingId));
                                lastSessionResumptionPingSentAt = MetaTime.Now;

                                ongoingSessionResumeAttempt = null;
                                break;
                            }
                            case SessionPong pong:
                            {
                                lastReceivedSessionResumptionPongId = pong.Id;
                                break;
                            }
                            case UpdateScheduledMaintenanceMode updateScheduledMaintenanceMode:
                            {
                                latestConnectionScheduledMaintenanceMode = updateScheduledMaintenanceMode.ScheduledMaintenanceMode;
                                _sdkHook.OnScheduledMaintenanceModeUpdated(ScheduledMaintenanceModeToMaintenanceModeState(latestConnectionScheduledMaintenanceMode));
                                break;
                            }

                            case Handshake.ClientHelloAccepted helloAccepted:
                            {
                                latestServerOptions = helloAccepted.ServerOptions;
                                break;
                            }

                            case MessageTransportInfoWrapperMessage infoWrapper:
                            {
                                switch (infoWrapper.Info)
                                {
                                    case ServerConnection.SessionConnectionErrorLostInfo connectionLost:
                                    {
                                        ongoingSessionResumeAttempt = connectionLost.Attempt;
                                        waitingForSessionResumeAttempt = true;

                                        // Update session resumption deadline. The deadline is always counted from the time when the session
                                        // was initially lost.
                                        // \note: We end up here for each connection loss of a session. This includes failed reconnection attempt of a session
                                        //        resumption. Hence we might end up re-setting ongoingSessionResumeCompleteDeadlineAt to the value it alredy was
                                        //        and that is intentional.
                                        ongoingSessionResumeCompleteDeadlineAt = ongoingSessionResumeAttempt.StartTime + Config.SessionResumptionAttemptMaxDuration.ToTimeSpan();
                                        break;
                                    }
                                }
                                break;
                            }
                        }
                    }
                    _messagesToDispatch.AddRange(messageBuffer);
                    messageBuffer.Clear();

                    _statistics.CurrentConnection.NetworkProbeStatus = networkProbe.TryGetConnectivityState();

                    // \todo: should come from the transport. This is too late.
                    if (gotMessages)
                        latestMessagesTimestamp = MetaTime.Now;

                    // Handle session connection-drop resume attempt timeout
                    if (ongoingSessionResumeAttempt != null && DateTime.UtcNow >= ongoingSessionResumeCompleteDeadlineAt && connectionError == null)
                    {
                        Log.Info("Session resumption did not complete within the deadline, giving up");
                        connectionError = ongoingSessionResumeAttempt.LatestTransportError;

                        // This is a synthetic failure in supervision loop, so we are responsible for emitting the syntetic DisconnectedFromServer()
                        _messagesToDispatch.Add(new DisconnectedFromServer());
                    }

                    // Transition already to `connectionError` state to make it observable
                    // from OnReceiveMessage. This mainly matters for DisconnectedFromServer
                    // pseudo-message.

                    if (connectionError != null)
                    {
                        ConnectionState errorState = TranslateError(connectionError);
                        Log.Debug("Failure during session. {Error}, causes {State}", PrettyPrint.Compact(connectionError), PrettyPrint.Compact(errorState));
                        if (errorState is ConnectionStates.TerminalError.InMaintenance)
                            goto observed_protocol_in_maintenance_during_session;

                        State = TranslateConnectionErrorForUser(errorState);
                        // Connection always emits DisconnectedFromServer when it errors, so we don't need to inject one here.
                    }
                    else if (ShouldEndSessionDueToRecentPause)
                    {
                        Log.Debug("A long duration in background just ended, will end session");
                        State = new ConnectionStates.TransientError.SessionLostInBackground();
                        // Connection is was not aware of this failure, so let's inject a syntetic DisconnectedFromServer()
                        _messagesToDispatch.Add(new DisconnectedFromServer());
                    }
                    else
                    {
                        bool sessionResumptionPongReceived = lastReceivedSessionResumptionPongId == lastSentSessionResumptionPingId;

                        // QoS
                        if (qosChanged || gotMessages)
                        {
                            State = new ConnectionStates.Connected(
                                isHealthy: qosMonitor.IsHealthy && sessionResumptionPongReceived,
                                latestReceivedMessageTimestamp: latestMessagesTimestamp);
                        }

                        // \note lastPauseEndedAt is epoch if no pause has occurred (due to the initial values of ApplicationLastPauseBeganAt and ApplicationLastPauseDuration)
                        MetaTime lastPauseEndedAt = MetaplaySDK.ApplicationLastPauseBeganAt + MetaplaySDK.ApplicationLastPauseDuration;

                        // Report incident when session resumption ping-pong takes a long time.
                        // This is meant to detect a connection issue where session resumptions
                        // succeed but session communication doesn't advance properly.
                        if (!sessionResumptionPongReceived
                            && lastSentSessionResumptionPingId != lastIncidentReportedSessionResumptionPingId // Only report once per transport connection.
                            && numSessionResumptionPingIncidentsReported < Config.MaxSessionPingPongDurationIncidentsPerSession // Cap number of reports per session.
                            && MetaplaySDK.ApplicationPauseStatus == ApplicationPauseStatus.Running // Not paused...
                            && MetaTime.Now > lastPauseEndedAt + MetaDuration.FromSeconds(5)        // ... and hasn't been in pause very recently.
                            )
                        {
                            MetaDuration timeSincePing      = MetaTime.Now - lastSessionResumptionPingSentAt;
                            MetaDuration roundtripEstimate  = MetaDuration.FromTimeSpan(_serverConnection.Statistics.DurationToConnected);

                            if (timeSincePing > roundtripEstimate + Config.SessionPingPongDurationIncidentThreshold)
                            {
                                MetaplaySDK.IncidentTracker.ReportSessionPingPongDurationThresholdExceeded(
                                    // \todo [nuutti] A bit wonky to use "login" debug diagnostics here.
                                    //                It's a bit of a misnomer. Anyway, it's the easiest
                                    //                way to get a bunch of maybe-useful info for now.
                                    debugDiagnostics:   Delegate.GetLoginDebugDiagnostics(isSessionResumption: true),

                                    roundtripEstimate:  roundtripEstimate,
                                    serverGateway:      _serverConnection.CurrentGateway,
                                    pingId:             lastSentSessionResumptionPingId,
                                    timeSincePing:      timeSincePing);

                                lastIncidentReportedSessionResumptionPingId = lastSentSessionResumptionPingId;
                                numSessionResumptionPingIncidentsReported++;
                            }
                        }
                    }

                    // Now handle OnReceiveMessages() while the new state is still observable
                    yield return Marker.HandleMessagesAndCallAgain;

                    // Did message handler Close() the connection? If so, close the connection
                    // unless it was already deemed to be closed.
                    if (ct.IsCancellationRequested)
                    {
                        // No need to inject DisconnectedFromServer(). CloseWithState handles it.
                        if (State.Status == ConnectionStatus.Connected && _flushEnqueuedMessagesBeforeClose)
                            goto wait_for_close;
                        goto cancellation_requested;
                    }

                    if (connectionError != null)
                    {
                        goto dispose;
                    }

                    if (ShouldEndSessionDueToRecentPause)
                    {
                        // DisconnectedFromServer already injected.
                        // Kill immediately, no need to flush and delay resumption any further.
                        goto dispose;
                    }

                    // End of update
                    yield return Marker.UpdateComplete;

                    // Game killed connection between Updates()
                    if (ct.IsCancellationRequested)
                    {
                        // No need to inject DisconnectedFromServer(). Close handles it.
                        if (_flushEnqueuedMessagesBeforeClose)
                            goto wait_for_close;
                        goto cancellation_requested;
                    }

                    // Handle session connection drop resume
                    // We handle this late so the message handler code can handle this (by closing the connection in force-reconnect). Any possible AbortSessionAfterConnectionDrop will be handled in the next iteration.
                    if (waitingForSessionResumeAttempt)
                    {
                        if (!ReconnectScheduler.TryGetDurationToSessionResumeAttempt(ongoingSessionResumeAttempt, ongoingSessionResumeCompleteDeadlineAt, out TimeSpan durationTillReconnect))
                        {
                            Log.Info("Next session resumption reconnect attempt would happen after deadline, giving up");
                            _serverConnection.AbortSessionAfterConnectionDrop();
                            waitingForSessionResumeAttempt = false;
                        }
                        else if (durationTillReconnect <= TimeSpan.Zero)
                        {
                            TimeSpan resumptionAttemptTimeRemaining = ongoingSessionResumeCompleteDeadlineAt - DateTime.UtcNow;
                            Log.Debug("Attempting session resumption reconnect (deadline in {MaxConnectTimeout} sec)...", resumptionAttemptTimeRemaining.TotalSeconds);
                            _serverConnection.ResumeSessionAfterConnectionDrop();
                            waitingForSessionResumeAttempt = false;
                        }
                        else
                        {
                            // still waiting for the durationTillReconnect
                        }
                    }
                }
            }

            redirect_and_reconnect:
            {
                _sessionNonceService.NewSession();
                goto reconnect;
            }

            wait_and_reconnect:
            {
                _serverConnection.Dispose();
                _serverConnection = null;

                // We will be waiting, so start hint fetch in the background.
                if (serverStatusHintFetchTask == null)
                    serverStatusHintFetchTask = GetServerStatusHintAsync(_logForwardingBuffer, Endpoint, Config);

                // Reconnect due to lost connection during
                MetaDuration reconnectInterval = Config.ConnectAttemptInterval;
                MetaTime reconnectAt = MetaTime.Now + reconnectInterval;

                Log.Information("Reconnecting in {ReconnectTimeout} (try {RetryCount} out of {MaxRetryCount})...", reconnectInterval, numConnectAttempts, Config.ConnectAttemptsMaxCount);
                while(true)
                {
                    // While waiting, check if maintenance status becomes available.
                    if (serverStatusHintFetchTask.IsCompleted)
                    {
                        // If auxiliary system also now thinks we are in maintenance, handle using solely the auxiliary
                        // system information.
                        ServerStatusHint status = serverStatusHintFetchTask.GetCompletedResult();
                        if (status.MaintenanceMode != null)
                            goto observed_server_status_hint_in_maintenance_during_connect;
                    }

                    if(MetaTime.Now >= reconnectAt)
                    {
                        goto reconnect;
                    }

                    yield return Marker.UpdateComplete;
                    if(ct.IsCancellationRequested)
                        goto cancellation_requested;
                }
            }

            // like SO_LINGER, but between components.
            wait_for_close:
            {
                #if !UNITY_WEBGL_BUILD
                Log.Information("signaled connection to close, waiting");
                Task closeTask      = _serverConnection.EnqueueCloseAsync(payload: null);
                // \note: no CT here. If we are here, CT is already triggered.
                Task timeoutTask    = MetaTask.Delay((int)Config.CloseFlushTimeout.Milliseconds);
                Task.WaitAny(closeTask, timeoutTask);
                #else
                // \note: On WebGL we cannot wait anything, but there's nothing to wait as
                //        all writes to connection completed already synchronously when issued.
                //        Call the method to keep the consistent flow, but don't wait.
                _ = _serverConnection.EnqueueCloseAsync(payload: null);
                #endif
                goto dispose;
            }

            inform_server_of_session_setup_failure_then_dispose:
            {
                // Something went wrong while client was trying to complete its end of the deal of the session start. Inform
                // server of the issue and continue with normal connection failure path.

                Log.Information("Client failed to start session. Sending failure report to server when network diagnostics complete.");

                // Begin fetching for diagnostics and for Status Hint. We need diagnostics here, but the status hint is for the
                // next state (wait_cannot_connect_error_diagnostics_then_dispose).

                if (serverStatusHintFetchTask == null && sessionStartError is TransientError)
                    serverStatusHintFetchTask = GetServerStatusHintAsync(_logForwardingBuffer, Endpoint, Config);

                if (networkDiagnosticReportTask == null)
                    networkDiagnosticReportTask = GetNetworkDiagnosticReportAsync();

                for(;;)
                {
                    if (networkDiagnosticReportTask.IsCompleted)
                        break;
                    yield return Marker.UpdateComplete;
                    if (ct.IsCancellationRequested)
                        goto cancellation_requested;
                }

                // Network diagnostics are ready. Create the report without informing listeners to avoid needlessly
                // informing the server as the session is about to end. Note that report generation may fail due to local throttling.
                PlayerIncidentReport.SessionStartFailed report = MetaplaySDK.IncidentTracker.ReportSessionStartFailure(sessionStartError, Endpoint, LatestTlsPeerDescription, networkDiagnosticReportTask.GetCompletedResult());
                if (report == null)
                    Log.Debug("Session start incident report throttled. Will not send the report.");

                // Test if we should send this message early and normal protocol limits. If any fails, we just send an empty Abort message
                // and the incident will be sent in the next connection with the normal incident report delivery mechanism.
                if (report != null)
                {
                    int pushIncidentSendRatePermille = 10 * latestServerOptions.PushUploadPercentageSessionStartFailedIncidentReport;
                    if (!KeyedStableWeightedCoinflip.FlipACoin(PlayerIncidentUtil.PushThrottlingKeyflipKey, (uint)PlayerIncidentUtil.GetSuffixFromIncidentId(report.IncidentId), pushIncidentSendRatePermille))
                    {
                        Log.Debug("Session start incident report throttled due to server-side limit. Will not send the report.");
                        report = null;
                    }
                }

                // If there is a report, try to build it into a abort trailer message. If report is too large, this might not
                // succeed.
                SessionProtocol.SessionStartAbortReasonTrailer reasonTrailer = null;
                if (report != null)
                {
                    try
                    {
                        byte[] compressedPayload = PlayerIncidentUtil.CompressIncidentForNetworkDelivery(report);
                        reasonTrailer = new SessionProtocol.SessionStartAbortReasonTrailer(report.IncidentId, compressedPayload);
                    }
                    catch (PlayerIncidentUtil.IncidentReportTooLargeException tooLarge)
                    {
                        Log.Warning("Failed to compress report: {Error}", tooLarge);
                    }
                }

                // Send the message and wait for it to be sent off the socket.
                if (_serverConnection.AbortSessionStart(reasonTrailer))
                {
                    Task closeTask = _serverConnection.EnqueueCloseAsync(payload: null);
                    MetaTime flushTimeoutAt = MetaTime.Now + Config.CloseFlushTimeout;
                    for(;;)
                    {
                        if (closeTask.IsCompleted)
                            break;
                        if (MetaTime.Now > flushTimeoutAt)
                            break;
                        yield return Marker.UpdateComplete;
                        if (ct.IsCancellationRequested)
                            goto cancellation_requested;
                    }

                    if (reasonTrailer != null)
                        Log.Debug("Session start incident report sent.");
                    else
                        Log.Debug("Session start abort request sent.");
                }
                else
                {
                    Log.Debug("Transport was lost before session start incident report could be sent.");
                }

                cannotConnectError = sessionStartError;
                goto wait_cannot_connect_error_diagnostics_then_dispose;
            }

            wait_cannot_connect_error_diagnostics_then_dispose:
            {
                // begin upgrade transient error into InMaintenance
                if (serverStatusHintFetchTask == null && cannotConnectError is TransientError)
                    serverStatusHintFetchTask = GetServerStatusHintAsync(_logForwardingBuffer, Endpoint, Config);

                if (cannotConnectError is IHasNetworkDiagnosticReport report)
                {
                    if (networkDiagnosticReportTask == null)
                        networkDiagnosticReportTask = GetNetworkDiagnosticReportAsync();

                    for(;;)
                    {
                        if (networkDiagnosticReportTask.IsCompleted)
                            break;
                        yield return Marker.UpdateComplete;
                        if (ct.IsCancellationRequested)
                            goto cancellation_requested;
                    }

                    report.NetworkDiagnosticReport = networkDiagnosticReportTask.GetCompletedResult();
                }

                // wait upgrade transient error into InMaintenance
                if (serverStatusHintFetchTask != null && cannotConnectError is TransientError)
                {
                    for(;;)
                    {
                        if (serverStatusHintFetchTask.IsCompleted)
                            break;
                        yield return Marker.UpdateComplete;
                        if (ct.IsCancellationRequested)
                            goto cancellation_requested;
                    }
                    ServerStatusHint status = serverStatusHintFetchTask.GetCompletedResult();
                    if (status.MaintenanceMode != null)
                        goto observed_server_status_hint_in_maintenance_during_connect;
                }

                State = cannotConnectError;
                goto dispose;
            }

            observed_server_status_hint_in_maintenance_during_connect:
            {
                ServerStatusHint.MaintenanceModeHint maintenanceModeHint = serverStatusHintFetchTask.GetCompletedResult().MaintenanceMode;

                // update MetaplaySDK.ScheduledMaintenanceMode, as guaranteed by InMaintenance error spec
                _sdkHook.OnScheduledMaintenanceModeUpdated(MaintenanceModeState.CreateOngoing(
                    maintenanceStartAt:             maintenanceModeHint.GetStartAtMetaTime(),
                    estimatedMaintenanceOverAt:     maintenanceModeHint.GetEstimatedMaintenanceOverAtMetaTimeMaybe()
                    ));

                // \note: OnScheduledMaintenanceModeChanged must be called before, as guaranteed by InMaintenance error spec
                State = new TerminalError.InMaintenance();
                goto dispose;
            }

            observed_protocol_in_maintenance_during_connect:
            {
                // server killed connection with InMaintenance error. The game is still connecting, so let's
                // try to fetch all the maintenance information from the auxiliary system. We are in no rush,
                // the player cannot play the game anyway. So it is worth it to wait here a bit if it gets the
                // correct maintenance data to the user.

                if (serverStatusHintFetchTask == null)
                    serverStatusHintFetchTask = GetServerStatusHintAsync(_logForwardingBuffer, Endpoint, Config);
                for(;;)
                {
                    if (serverStatusHintFetchTask.IsCompleted)
                        break;
                    yield return Marker.UpdateComplete;
                    if (ct.IsCancellationRequested)
                        goto cancellation_requested;
                }

                // If auxiliary system also now thinks we are in maintenance, handle using solely the auxiliary
                // system information.
                ServerStatusHint status = serverStatusHintFetchTask.GetCompletedResult();
                if (status.MaintenanceMode != null)
                    goto observed_server_status_hint_in_maintenance_during_connect;

                // Auxiliary system disagrees or is unavailable. All we know is that we are in maintenance.
                _sdkHook.OnScheduledMaintenanceModeUpdated(MaintenanceModeState.CreateOngoing(maintenanceStartAt: MetaTime.Now, estimatedMaintenanceOverAt: null));

                // \note: OnScheduledMaintenanceModeChanged must be called before, as guaranteed by InMaintenance error spec
                State = new TerminalError.InMaintenance();
                goto dispose;
            }

            observed_protocol_in_maintenance_during_session:
            {
                // Server killed connection with InMaintenance error. The game is running and we cannot
                // wait to fetch for Maintenance data from auxiliary system - We must kill connection now
                // to prevent user's data loss.
                //
                // Set ScheduledMaintenanceMode to contain the fact we are in Maintenance. That is the information
                // we know, UNLESS the latest ScheduledMaintenanceMode update (IF ANY?) contained hint about an
                // upcoming maintenance mode. If we had an advance warning, we assume that maintenance is this
                // maintenance.

                if (latestConnectionScheduledMaintenanceMode != null)
                {
                    // there was a an advance warning, use its data.
                    MetaTime? estimatedMaintenanceOverAt;
                    if (latestConnectionScheduledMaintenanceMode.EstimationIsValid)
                        estimatedMaintenanceOverAt = latestConnectionScheduledMaintenanceMode.StartAt + MetaDuration.FromMinutes(latestConnectionScheduledMaintenanceMode.EstimatedDurationInMinutes);
                    else
                        estimatedMaintenanceOverAt = null;

                    // CreateOngoing will automatically move maintenanceStartAt to not be in the future if that happened to be the case
                    _sdkHook.OnScheduledMaintenanceModeUpdated(MaintenanceModeState.CreateOngoing(
                        maintenanceStartAt:         latestConnectionScheduledMaintenanceMode.StartAt,
                        estimatedMaintenanceOverAt: estimatedMaintenanceOverAt));
                }
                else
                {
                    // there were no advance warning. So all we know is that we are now in a maintenance
                    _sdkHook.OnScheduledMaintenanceModeUpdated(MaintenanceModeState.CreateOngoing(maintenanceStartAt: MetaTime.Now, estimatedMaintenanceOverAt: null));
                }

                // \note: OnScheduledMaintenanceModeChanged must be called before, as guaranteed by InMaintenance error spec
                State = new TerminalError.InMaintenance();
                goto dispose;
            }

            cancellation_requested:
            {
                goto dispose;
            }

            dispose:
            {
                Log.Information("Connection terminated");
                if (_serverConnection != null)
                {
                    _serverConnection.Dispose();
                    _serverConnection = null;
                }
                foreach (SessionResourceLoader resource in sessionResourceLoaders)
                    resource.Reset();
                MetaplaySDK.MessageDispatcher.SetConnection(null);
                networkProbe?.Dispose();
                networkProbe = null;
                DisposePauseTerminationTimer();
                yield break;
            }
        }

        ConnectionState TranslateError(MessageTransport.Error error)
        {
            switch (error)
            {
                case WireMessageTransport.InvalidGameMagic e:
                    return new ConnectionStates.TerminalError.InvalidGameMagic(e.Magic);

                case WireMessageTransport.WireProtocolVersionMismatch e:
                    return new ConnectionStates.TerminalError.WireProtocolVersionMismatch(clientProtocolVersion: WireProtocol.WireProtocolVersion, serverProtocolVersion: e.ServerProtocolVersion);

                case WireMessageTransport.ProtocolStatusError e:
                    switch (e.status)
                    {
                        case ProtocolStatus.ClusterStarting:
                            return new ConnectionStates.TransientError.ClusterNotReady(ConnectionStates.TransientError.ClusterNotReady.ClusterStatus.ClusterStarting);
                        case ProtocolStatus.ClusterShuttingDown:
                            return new ConnectionStates.TransientError.ClusterNotReady(ConnectionStates.TransientError.ClusterNotReady.ClusterStatus.ClusterShuttingDown);
                            #pragma warning disable CS0618
                        case ProtocolStatus.InMaintenance: // \todo Deprecated
                            return new ConnectionStates.TerminalError.InMaintenance();
                            #pragma warning restore CS0618
                    }
                    break;

                case ServerConnection.SessionForceTerminatedError sft when sft.Reason is
                    SessionForceTerminateReason.MaintenanceModeStarted:
                case ServerConnection.MaintenanceModeOngoingError _:    return new ConnectionStates.TerminalError.InMaintenance();
                case WireMessageTransport.WireFormatError e:            return new ConnectionStates.TerminalError.WireFormatError(e.DecodeException);
                case TcpMessageTransport.CouldNotConnectError _:        return new ConnectionStates.TransientError.Closed();
                case TcpMessageTransport.ConnectionRefused _:           return new ConnectionStates.TransientError.Closed();
                #if UNITY_WEBGL
                case WebSocketTransport.CouldNotConnectError _:         return new ConnectionStates.TransientError.Closed();
                case WebSocketTransport.WebSocketError _:               return new ConnectionStates.TransientError.Closed();
                case WebSocketTransport.WebSocketClosedError _:         return new ConnectionStates.TransientError.Closed();
                #endif
                case StreamMessageTransport.StreamClosedError _:        return new ConnectionStates.TransientError.Closed();
                case StreamMessageTransport.StreamIOFailedError _:      return new ConnectionStates.TransientError.Closed();
                case StreamMessageTransport.StreamExecutorError _:      return new ConnectionStates.TransientError.Closed();
                case StreamMessageTransport.ConnectTimeoutError _:      return new ConnectionStates.TransientError.Timeout(ConnectionStates.TransientError.Timeout.TimeoutSource.Connect);
                case StreamMessageTransport.TimeoutError _:             return new ConnectionStates.TransientError.Timeout(ConnectionStates.TransientError.Timeout.TimeoutSource.Stream);
                case ServerConnection.UnexpectedLoginMessageError e:    return new ConnectionStates.TransientError.ProtocolError(TransientError.ProtocolError.ErrorKind.UnexpectedLoginMessage, $"unexpected login message: {e.MessageType}");
                case WireMessageTransport.MissingHelloError _:          return new ConnectionStates.TransientError.ProtocolError(TransientError.ProtocolError.ErrorKind.MissingServerHello, "missing server hello");
                case ServerConnection.LogicVersionMismatchError e:      return new ConnectionStates.TerminalError.LogicVersionMismatch(e.ClientSupportedVersions, e.ServerAcceptedVersions);
                case ServerConnection.LoginProtocolVersionMismatchError e: return new ConnectionStates.TerminalError.LoginProtocolVersionMismatch(e.ClientVersion, e.ServerVersion);
                case ServerConnection.CommitIdMismatchMismatchError e:  return new ConnectionStates.TerminalError.CommitIdMismatch(e.ClientCommitId, e.ServerCommitId);

                case TlsMessageTransport.TlsError e:
                    switch (e.Error)
                    {
                        case TlsMessageTransport.TlsError.ErrorCode.NotAuthenticated:           return new ConnectionStates.TransientError.TlsError(ConnectionStates.TransientError.TlsError.ErrorCode.NotAuthenticated);
                        case TlsMessageTransport.TlsError.ErrorCode.FailureWhileAuthenticating: return new ConnectionStates.TransientError.TlsError(ConnectionStates.TransientError.TlsError.ErrorCode.FailureWhileAuthenticating);
                        case TlsMessageTransport.TlsError.ErrorCode.NotEncrypted:               return new ConnectionStates.TransientError.TlsError(ConnectionStates.TransientError.TlsError.ErrorCode.NotEncrypted);
                        default:                                                                return new ConnectionStates.TransientError.TlsError(ConnectionStates.TransientError.TlsError.ErrorCode.Unknown);
                    }

                case ServerConnection.SessionResumeFailed _:            return new ConnectionStates.TransientError.FailedToResumeSession();
                case ServerConnection.SessionStartFailed e:             return new ConnectionStates.TransientError.ProtocolError(TransientError.ProtocolError.ErrorKind.SessionStartFailed, e.Message);
                case ServerConnection.SessionForceTerminatedError e:    return new ConnectionStates.TransientError.SessionForceTerminated(e.Reason);
                case ServerConnection.SessionError e:                   return new ConnectionStates.TransientError.ProtocolError(TransientError.ProtocolError.ErrorKind.SessionProtocolError, $"session protocol violation: {e.Reason}");
                case ServerConnection.PlayerIsBannedError _:            return new ConnectionStates.TerminalError.PlayerIsBanned();
                case ServerConnection.PlayerDeserializationFailureError e: return new ConnectionStates.TerminalError.ServerPlayerDeserializationFailure(e.Error);
                case ServerConnection.WatchdogDeadlineExceededError e:
                {
                    var translatedError = new ConnectionStates.TransientError.InternalWatchdogDeadlineExceeded(e.WatchdogType);
                    MetaplaySDK.IncidentTracker.ReportWatchdogDeadlineExceededError();
                    return translatedError;
                }
                case ServerConnection.ServiceFailureError e:            return new ConnectionStates.TerminalError.ClientSideConnectionError(e.Error);
                case MessageTransport.EnqueuedCloseError e:
                {
                    // Auto-termination during application pause
                    if (ReferenceEquals(e.Payload, _closedForApplicationBackgroundMarker))
                        return new ConnectionStates.TransientError.SessionLostInBackground();

                    return new ConnectionStates.TransientError.Closed();
                }
            }
            return new ConnectionStates.TerminalError.Unknown(error?.GetType().ToGenericTypeString());
        }

        ConnectionState TranslateConnectionErrorForUser(ConnectionState defaultResult)
        {
            // Inject inferred states
            switch(defaultResult)
            {
                case ConnectionStates.TransientError.Closed _:
                case ConnectionStates.TransientError.TlsError _:
                case ConnectionStates.TransientError.Timeout _:
                case ConnectionStates.TransientError.FailedToResumeSession _:
                {
                    if (ShouldMaskSimpleErrorDueToRecentPause && _statistics.CurrentConnection.HasCompletedSessionInit)
                    {
                        Log.Debug("A long duration in background just ended, will mask error");
                        return new ConnectionStates.TransientError.SessionLostInBackground();
                    }

                    // If we cannot connect anywhere, assume it must be due to not having internet connection
                    if (Statistics.CurrentConnection.NetworkProbeStatus == false && !_statistics.CurrentConnection.HasCompletedHandshake)
                        return new ConnectionStates.TerminalError.NoNetworkConnectivity();

                    return defaultResult;
                }
            }
            return defaultResult;
        }
    }
}
