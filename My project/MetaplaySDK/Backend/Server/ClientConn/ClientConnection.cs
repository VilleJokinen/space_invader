// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Akka.IO;
using Metaplay.Cloud;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Services.Geolocation;
using Metaplay.Cloud.Sharding;
using Metaplay.Cloud.Web3;
using Metaplay.Core;
using Metaplay.Core.Debugging;
using Metaplay.Core.Message;
using Metaplay.Core.Network;
using Metaplay.Core.Serialization;
using Metaplay.Server.Authentication;
using Metaplay.Server.Database;
using Metaplay.Server.Web3;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    // ClientConnection

    [EntityConfig]
    internal sealed class ClientConnectionConfig : EphemeralEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.Connection;
        public override Type                EntityActorType         => typeof(TcpClientConnection);
        public override bool                AllowEntitySpawn        => false;
        public override Type                EntityShardType         => typeof(ConnectionListener);
        public override IShardingStrategy   ShardingStrategy        => new StaticShardingStrategy();
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(60);
    }

    public abstract class ClientConnection : EphemeralEntityActor
    {
        public enum Status
        {
            WaitingForHello,
            WaitingForLoginOrCreateNewGuestAccountRequest,
            WaitingForLogin,
            WaitingForSession,
            WaitingForAbortReason,
            Session,
            StoppingGracefully,
        }

        class LoginAuthParams
        {
            public AuthenticationKey Key { get; }
            public EntityId PlayerId { get; }

            public LoginAuthParams(AuthenticationKey key, EntityId playerId)
            {
                Key = key;
                PlayerId = playerId;
            }
        }

        protected static readonly Prometheus.Counter      c_registerPlayer            = Prometheus.Metrics.CreateCounter("game_player_register_total", "Number of new player registration attempts");
        protected static readonly Prometheus.Counter      c_registerPlayerFail        = Prometheus.Metrics.CreateCounter("game_player_register_fails_total", "Number of new player registration failures");
        protected static readonly Prometheus.Counter      c_clientConnections         = Prometheus.Metrics.CreateCounter("game_client_connections_total", "Number of clients connections");
        protected static readonly Prometheus.Counter      c_clientConnectionRefused   = Prometheus.Metrics.CreateCounter("game_client_connection_refused_total", "Number of client connections that were refused", "reason");
        protected static readonly Prometheus.Counter      c_protocolHashMimatch       = Prometheus.Metrics.CreateCounter("game_client_protocol_hash_mismatch", "Client and server protocol hashes are mismatched (only checked when LogicVersion is exact match)");
        protected static readonly Prometheus.Counter      c_playerLogins              = Prometheus.Metrics.CreateCounter("game_player_logins_total", "Number of player login attempts");
        protected static readonly Prometheus.Counter      c_playerLoginFail           = Prometheus.Metrics.CreateCounter("game_player_login_fails_total", "Number of player login failures");
        protected static readonly Prometheus.Histogram    c_authDuration              = Prometheus.Metrics.CreateHistogram("game_player_auth_duration", "Player authentication check duration", Metaplay.Cloud.Metrics.Defaults.LatencyDurationConfig);
        protected static readonly Prometheus.Counter      c_receivedMessages          = Prometheus.Metrics.CreateCounter("game_conn_received_messages_total", "Number of messages received from clients by type", "message");
        protected static readonly Prometheus.Counter      c_sentMessages              = Prometheus.Metrics.CreateCounter("game_conn_sent_messages_total", "Number of messages sent to clients by type", "message");
        protected static readonly Prometheus.Counter      c_wellKnownMalformeds       = Prometheus.Metrics.CreateCounter("game_conn_well_known_malformeds_total", "Number of connection with malformed data of a well-known kind", "kind");
        protected static readonly Prometheus.Counter      c_sessionStartFailures      = Prometheus.Metrics.CreateCounter("game_session_start_fails_total", "Number of sessions start failures by failure type", "reason"); // \note: the rest of the "game_session_" counters are in SessionActor but this needs to happen earlier
        protected static readonly Prometheus.Counter      c_sessionResumeAttempts     = Prometheus.Metrics.CreateCounter("game_session_resumes_total", "Number of session resume attempts");
        protected static readonly Prometheus.Counter      c_sessionResumeFails        = Prometheus.Metrics.CreateCounter("game_session_resume_fails_total", "Number of player session resume failures by failure type", "reason");

        protected static readonly TimeSpan                ConnectionHardTimeout       = TimeSpan.FromMinutes(1);      // Kick out connections if no messages received for this amount of time
        protected static readonly TimeSpan                ConnectionProbeAfterTimeout = ConnectionHardTimeout / 2;    // Probe connection (ping) if no messages received for this amount of time

        protected override AutoShutdownPolicy   ShutdownPolicy => AutoShutdownPolicy.ShutdownNever(); // Manually shuts down

        protected readonly IPAddress      _srcAddr;
        protected readonly int            _localPort;
        protected Status                  _status                 = Status.WaitingForHello;
        protected ByteString              _incomingBuffer         = ByteString.Empty;
        protected DateTime                _startTime              = DateTime.UtcNow;
        protected DateTime                _lastMessageReceivedAt  = DateTime.UtcNow;
        protected bool                    _connectionProbeSent    = false;
        protected int                     _logicVersion;
        protected bool                    _enableWireCompression;

        protected bool                    _disableRequestHandling = false;
        protected int                     _totalBytesReceived     = 0;
        protected int                     _totalBytesProcessed    = 0;
        protected int                     _totalPacketsReceived   = 0;
        protected bool                    _hasSentResourceCorrection;
        protected int                     _numSessionMessagesReceived;

        // State set in Hello completion
        protected string                  _helloClientVersion;
        protected string                  _helloBuildNumber;
        protected string                  _helloCommitId;
        protected uint                    _helloAppLaunchId;
        protected uint                    _helloClientSessionNonce;
        protected uint                    _helloClientSessionConnectionNdx;
        protected uint                    _helloProtocolHash;
        protected MetaVersionRange        _helloLogicVersionRange;
        protected ClientPlatform          _helloPlatform;

        // State set in Login completion
        protected EntityId                                    _loginPlayerId;
        protected LoginDebugDiagnostics                       _loginDebugDiagnostics;
        protected Handshake.ILoginRequestGamePayload          _loginGamePayload;

        // State set in Login completion, but for new sessions only
        protected AuthenticationKey                           _loginNewSessionAuthKey;

        // State set in Session start completion
        protected EntityId                                    _sessionId;
        protected EntitySubscription                          _sessionSubscription;
        protected bool                                        _sessionWasResumed;

        // Cache version info & protocol header for valid connection
        protected static string           s_serverVersion     = Util.ObjectToStringInvariant(Assembly.GetEntryAssembly().GetName().Version);
        //static string                   s_buildNumber       = Assembly.GetEntryAssembly().GetCustomAttribute<BuildNumberAttribute>()?.BuildNumber ?? "local";
        protected static ByteString       s_protocolHeader    = ByteString.FromBytes(WireProtocol.EncodeProtocolHeader(ProtocolStatus.ClusterRunning, MetaplayCore.Options.GameMagic));

        protected ClientConnection(EntityId entityId, IPAddress srcAddress, int localPort) : base(entityId)
        {
            // Store address and port
            _srcAddr = srcAddress;

            _localPort = localPort;
        }

        protected override void PreStart()
        {
            base.PreStart();

            // Resolve current logic version
            _logicVersion = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;

            // Get client connection options. To avoid changing these settings during a ClientConnection
            // (in case ClientConnectionOptions changes), they get fixed per connection, here at actor start.
            ClientConnectionOptions clientConnectionOptions = RuntimeOptionsRegistry.Instance.GetCurrent<ClientConnectionOptions>();
            _enableWireCompression = clientConnectionOptions.EnableWireCompression;

            // Send initial hello message to client
            WriteBytesToSocket(s_protocolHeader);
            SendToClient(new Handshake.ServerHello(
                s_serverVersion,
                CloudCoreVersion.BuildNumber,
                MetaSerializerTypeRegistry.FullProtocolHash,
                CloudCoreVersion.CommitId));

            // Start timer for checking connection timeouts
            // \todo [paul] this is sort of a lowest-common-denominator time span
            StartRandomizedPeriodicTimer(TimeSpan.FromSeconds(5), ActorTick.Instance);
        }

        void RenewConnectionWatchdog()
        {
            _lastMessageReceivedAt = DateTime.UtcNow;
            _connectionProbeSent = false;
        }

        protected async Task OnNetworkMessage(MetaMessage message)
        {
            // \todo [petri] implement throttling

            c_receivedMessages.WithLabels(message.GetType().Name).Inc();

            // Handle pre-login and post-login messages separately (to avoid accidentally accepting messages before login)
            switch (_status)
            {
                case Status.WaitingForHello:
                    switch (message)
                    {
                        case Handshake.ClientHello hello:
                            await ReceiveClientHello(hello);
                            break;

                        case Handshake.ClientAbandon abandon:
                            await ReceiveClientAbandon(abandon);
                            break;

                        default:
                            _log.Warning("Received invalid message {MessageType} when expecting ClientHello", message.GetType().Name);
                            break;
                    }
                    break;

                case Status.WaitingForLoginOrCreateNewGuestAccountRequest:
                case Status.WaitingForLogin:
                    switch (message)
                    {
                        case Handshake.CreateGuestAccountRequest newGuestAccountRequest when _status == Status.WaitingForLoginOrCreateNewGuestAccountRequest:
                            await ReceiveCreateGuestAccountRequest(newGuestAccountRequest);
                            break;

                        case Handshake.LoginRequest loginReq:
                            await ReceiveLoginRequest(loginReq);
                            break;

                        case Handshake.LoginAndResumeSessionRequest loginAndSessionResumeRequest:
                            await ReceiveLoginAndSessionResumeRequest(loginAndSessionResumeRequest);
                            break;

                        default:
                            _log.Warning("Received invalid message {MessageType} when expecting early login message", message.GetType().Name);
                            await GracefulStopAsync().ConfigureAwait(false);
                            break;
                    }
                    break;

                case Status.WaitingForSession:
                    switch (message)
                    {
                        case SessionProtocol.SessionStartRequest sessionStartRequest:
                            await ReceiveSessionStartRequest(sessionStartRequest);
                            break;

                        case SessionProtocol.SessionStartAbort clientAbort:
                            await ReceiveSessionStartAbort(clientAbort);
                            break;

                        default:
                            _log.Warning("Received invalid message {MessageType} when expecting SessionStartRequest or SessionStartAbort", message.GetType().Name);
                            await GracefulStopAsync().ConfigureAwait(false);
                            break;
                    }
                    break;

                case Status.WaitingForAbortReason:
                    switch (message)
                    {
                        case SessionProtocol.SessionStartAbortReasonTrailer reasonTrailer:
                            await ReceiveSessionStartAbortReasonTrailer(reasonTrailer);
                            break;

                        default:
                            _log.Warning("Received invalid message {MessageType} when expecting SessionStartAbortReasonTrailer", message.GetType().Name);
                            await GracefulStopAsync().ConfigureAwait(false);
                            break;
                    }
                    break;

                case Status.Session:
                {
                    switch (message)
                    {
                        case SessionProtocol.SessionStartAbort clientAbort:
                            await ReceiveSessionStartAbort(clientAbort);
                            break;

                        default:
                        {
                            MetaMessageSpec     messageSpec         = MetaMessageRepository.Instance.GetFromType(message.GetType());
                            MessageDirection    messageDirection    = messageSpec.MessageDirection;

                            // Message direction is checked here. The rest of the routing rules are checked in SessionActor.
                            if (messageDirection != MessageDirection.ClientToServer && messageDirection != MessageDirection.Bidirectional)
                                throw new InvalidOperationException($"Received message from client with invalid message direction: {messageDirection}");

                            SendMessage(_sessionSubscription, new SessionMessageFromClient(message));
                            _numSessionMessagesReceived++;
                            break;
                        }
                    }
                    break;
                }

                case Status.StoppingGracefully:
                {
                    _log.Debug("Already started graceful stop, ignoring {MessageType}", message.GetType().Name);
                    break;
                }

                default:
                    _log.Warning("Unhandled Status: {Status}", _status);
                    break;
            }
        }

        protected async Task OnHealthCheckRequest(HealthCheckTypeBits typeBits)
        {
            _log.Debug("Health check type '{TypeBits}' requested by {SourceAddress}", typeBits, IPAddressRedactionUtil.ToPrivacyProtectingString(_srcAddr));

            HealthCheckTypeBits response = 0;
            if (typeBits.HasFlag(HealthCheckTypeBits.GlobalState))
            {
                try
                {
                    await EntityAskAsync<GlobalStatusResponse>(GlobalStateManager.EntityId, GlobalStatusRequest.Instance);
                    response |= HealthCheckTypeBits.GlobalState;
                }
                catch { }
            }
            if (typeBits.HasFlag(HealthCheckTypeBits.Database))
            {
                try
                {
                    await MetaDatabaseBase.Get().HealthCheck();
                    response |= HealthCheckTypeBits.Database;
                }
                catch { }
            }

            if (typeBits != response)
            {
                _log.Warning("Health check failed for types '{FailedBits}'", ~response & typeBits);
            }

            WriteToSocket(WirePacketType.HealthCheck, WirePacketCompression.None, FormatHealthCheckType(response));

            // The client is supposed to close the connection after a health check request, prevent handling further
            // requests but keep connection open.
            _disableRequestHandling = true;
        }

        async Task ReceiveClientHello(Handshake.ClientHello hello)
        {
            string clientVersion = hello.ClientVersion == null ? null : Util.ShortenString(hello.ClientVersion, 256); // Shorten version string to some conservative size, shouldn't matter with legit clients

            // Log source IP only after a successful Hello. This cuts out the spam caused by test connections.
            _log.Info("Client handshake completed with {SourceAddress}. ClientVersion={ClientVersion}. Local port {LocalPort}.", IPAddressRedactionUtil.ToPrivacyProtectingString(_srcAddr), clientVersion, _localPort);

            _log.Debug("Received {Message}", PrettyPrint.Compact(hello));
            c_clientConnections.Inc();

            // Check whether the client connection gets delayed enroute
            // NOTE: This check depends on the client's clock, so it only makes sense in controlled environments
            MetaDuration helloDelay = MetaTime.Now - hello.Timestamp;
            if (helloDelay.Milliseconds >= 5_000 && helloDelay.Milliseconds <= 2 * 60 * 1000) // cap to 2min to avoid noise from very bad client clocks
                _log.Info("Client connection was delayed by {HelloDelay:0.00}s", helloDelay.Milliseconds / 1000.0);

            // Fetch active LogicVersion config (to see which client versions should be allowed)
            ClientCompatibilitySettings compatSettings = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings;

            // Only accept the current ActiveLogicVersion
            // \todo [petri] this could be loosened in some situations: if players in game cannot interact in ways that would cause versioning problems, we could allow multiple versions -- PlayerActor needs to implement this also
            MetaVersionRange acceptedLogicVersions = new MetaVersionRange(_logicVersion, _logicVersion);

            // In case redirecting is enabled, check if the client is too new: if the client's max supported LogicVersion is higher
            // than server's ActiveLogicVersion, it is considered to be candidate for redirecting
            if (compatSettings.RedirectEnabled && hello.SupportedLogicVersions.MaxVersion > compatSettings.ActiveLogicVersion)
            {
                ServerEndpoint redirectServer = compatSettings.RedirectServerEndpoint;
                if (redirectServer != null)
                {
                    _log.Info("Redirecting future client: clientSupportedLogicVersions={ClientSupportedLogicVersion}, activeLogicVersion={ActiveLogicVersion}", hello.SupportedLogicVersions, _logicVersion);
                    c_clientConnectionRefused.WithLabels("Redirected").Inc();
                    SendToClient(new Handshake.RedirectToServer(redirectServer));
                }
                else
                {
                    _log.Warning("Missing server redirect endpoint for client from future: clientSupportedLogicVersions={ClientSupportedLogicVersions}, activeLogicVersion={ActiveLogicVersion}!", hello.SupportedLogicVersions, _logicVersion);
                    c_clientConnectionRefused.WithLabels("ClientLogicVersionTooNew").Inc();
                    SendToClient(new Handshake.LogicVersionMismatch(acceptedLogicVersions));
                }
                await GracefulStopAsync();
                return;
            }

            // Login protocol version checks.
            if (hello.LoginProtocolVersion != MetaplayCore.LoginProtocolVersion)
            {
                if (IsPlatformInMaintenance(hello.Platform))
                {
                    SendToClient(Handshake.OngoingMaintenance.Instance);
                }
                else
                {
                    // Version 2 introduced LoginProtocolVersionMismatch. Before that, we reject the version as a logic version failure.
                    if (hello.LoginProtocolVersion < 2)
                        SendToClient(new Handshake.LogicVersionMismatch(acceptedLogicVersions));
                    else
                        SendToClient(new Handshake.LoginProtocolVersionMismatch(MetaplayCore.LoginProtocolVersion));
                }

                c_clientConnectionRefused.WithLabels("ClientLoginProtocolVersionMismatch").Inc();
                await GracefulStopAsync();
                return;
            }

            // Keep some info from the hello message
            _helloClientVersion              = clientVersion;
            _helloAppLaunchId                = hello.AppLaunchId;
            _helloClientSessionNonce         = hello.ClientSessionNonce;
            _helloClientSessionConnectionNdx = hello.ClientSessionConnectionNdx;
            _helloPlatform                   = hello.Platform;
            _helloLogicVersionRange          = hello.SupportedLogicVersions;
            _helloProtocolHash               = hello.FullProtocolHash;
            _helloBuildNumber                = hello.BuildNumber;
            _helloCommitId                   = hello.CommitId;

            // Wait for login or client to request new account creation
            _status = Status.WaitingForLoginOrCreateNewGuestAccountRequest;
            // \todo [petri] add metric for client versions (sanity check version, to avoid generating new time series for hack clients)

            (SystemOptions systemOpts, GooglePlayStoreOptions playStoreOpts, Web3Options web3Opts, PlayerIncidentOptions incidentOptions, EnvironmentOptions envOpts) = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions, GooglePlayStoreOptions, Web3Options, PlayerIncidentOptions, EnvironmentOptions>();

            int pushIncidentRatePercent = 0;
            if (incidentOptions.EnableUploads)
                pushIncidentRatePercent = incidentOptions.PushUploadPercentageSessionStartFailed;

            SendToClient(new Handshake.ClientHelloAccepted(
                serverOptions: new Handshake.ServerOptions(
                    pushUploadPercentageSessionStartFailedIncidentReport:   pushIncidentRatePercent,
                    enableWireCompression:                                  _enableWireCompression,
                    deletionRequestSafetyDelay:                             MetaDuration.FromTimeSpan(systemOpts.PlayerDeletionDefaultDelay),
                    gameServerGooglePlaySignInOAuthClientId:                playStoreOpts.GooglePlayClientId,
                    immutableXLinkApiUrl:                                   EthereumNetworkProperties.TryGetPropertiesForNetwork(web3Opts.ImmutableXNetwork)?.ImmutableXLinkApiUrl,
                    gameEnvironment:                                        envOpts.Environment
                    )));
        }

        async Task ReceiveClientAbandon(Handshake.ClientAbandon abandon)
        {
            _log.Debug("Client abandoned opened connection: {AbandonInfo}", PrettyPrint.Compact(abandon));
            await GracefulStopAsync().ConfigureAwait(false);
        }

        async Task ReceiveCreateGuestAccountRequest(Handshake.CreateGuestAccountRequest newGuestAccountRequest)
        {
            bool isInMaintenance = IsPlatformInMaintenance(_helloPlatform);

            // If new user tries to log in during maintenance, just send failure.
            // (New player cannot be a developer, and hence cannot bypass the maintenance).
            if (isInMaintenance)
            {
                SendToClient(Handshake.OngoingMaintenance.Instance);
                await GracefulStopAsync();
                return;
            }

            // If not in maintenance, check client version
            if (!CheckClientHelloLogicVersionRange())
            {
                await GracefulStopAsync();
                return;
            }

            // Otherwise, create a new account and inform client.
            RegisterAccountResponse newAccount = await WaitLongRunningOperationAsync(TryCreateNewAccount(_log));
            if (newAccount == null)
            {
                await GracefulStopAsync();
                return;
            }

            SendToClient(new Handshake.CreateGuestAccountResponse(newAccount.PlayerId, newAccount.DeviceId, newAccount.AuthToken));
            _status = Status.WaitingForLogin;
        }

        async Task ReceiveLoginRequest(Handshake.LoginRequest loginReq)
        {
            bool isInMaintenance = IsPlatformInMaintenance(_helloPlatform);

            // If not in maintenance, do version check early and exit early.
            // \note: if we are in maintentenance, the version check is done later. We want to prioritize OngoingMaintenance error
            //        over LogicVersionMismatch error. If LogicVersionMismatch is given too early, end-user could update their game
            //        and then get OngoingMaintenance error which is disapointing. It's better to let user wait for the maintenance
            //        to be over.
            if (!isInMaintenance && !CheckClientHelloLogicVersionRange())
            {
                await GracefulStopAsync();
                return;
            }

            if (TryBlockBot(loginReq.IsBot))
            {
                _log.Warning("Blocked a login attempt by a bot: playerId={PlayerIdHint}", loginReq.PlayerIdHint);
                await GracefulStopAsync();
                return;
            }

            // Authenticate an existing account.
            // \note: In exceptional cases, the DB can be very very slow. Keep client informed so
            //        it won't assuem the connection was lost.
            LoginAuthParams authParams = await WaitLongRunningOperationAsync(TryAuthenticateExistingAccount(loginReq));
            if (authParams == null)
            {
                await GracefulStopAsync();
                return;
            }

            // If in maintenance, inform client and shut down. If player is developer, pass on to LogicVersion check.
            if (isInMaintenance)
            {
                // If player is not a developer and there is maintenance, inform client and shut down.
                if (!GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(authParams.PlayerId))
                {
                    SendToClient(Handshake.OngoingMaintenance.Instance);
                    await GracefulStopAsync();
                    return;
                }

                // If player IS a developer (and hence bypassed maintenance check), check LogicVersion since it wasn't checked earlier
                if (!CheckClientHelloLogicVersionRange())
                {
                    await GracefulStopAsync();
                    return;
                }
            }

            // Inform client about login success, sending corrections (or new account info).
            SendToClient(new Handshake.LoginSuccessResponse(loggedInPlayerId: authParams.PlayerId));

            _log.Info("Logged in {PlayerId}, key={AuthKey})", authParams.PlayerId, authParams.Key);

            _loginPlayerId = authParams.PlayerId;
            _loginDebugDiagnostics = loginReq.DebugDiagnostics;
            _loginGamePayload = loginReq.GamePayload;
            _loginNewSessionAuthKey = authParams.Key;
            _status = Status.WaitingForSession;
        }

        /// <summary>
        /// Returns false if connection cannot continue. Caller should GracefulStopAsync();
        /// </summary>
        bool CheckClientHelloLogicVersionRange()
        {
            // Only accept the current ActiveLogicVersion
            // \todo [petri] this could be loosened in some situations: if players in game cannot interact in ways that would cause versioning problems, we could allow multiple versions -- PlayerActor needs to implement this also
            MetaVersionRange acceptedLogicVersions = new MetaVersionRange(_logicVersion, _logicVersion);

            if (_helloLogicVersionRange.MaxVersion < acceptedLogicVersions.MinVersion)
            {
                _log.Info("Client version is too old: {ClientSupportedLogicVersions}, activeLogicVersion={ActiveLogicVersion}", _helloLogicVersionRange, _logicVersion);
                c_clientConnectionRefused.WithLabels("ClientLogicVersionTooOld").Inc();
                SendToClient(new Handshake.LogicVersionMismatch(acceptedLogicVersions));
                return false;
            }
            else if (_helloLogicVersionRange.MinVersion > acceptedLogicVersions.MaxVersion)
            {
                _log.Warning("Refusing client from future: clientSupportedLogicVersions={ClientSupportedLogicVersions}, activeLogicVersion={ActiveLogicVersion}!", _helloLogicVersionRange, _logicVersion);
                c_clientConnectionRefused.WithLabels("ClientLogicVersionTooNew").Inc();
                SendToClient(new Handshake.LogicVersionMismatch(acceptedLogicVersions));
                return false;
            }
            else // client supports ActiveLogicVersion
            {
                // If SupportedLogicVersions match exactly, warn about mismatching protocol hash.
                if (_helloLogicVersionRange == MetaplayCore.Options.SupportedLogicVersions && _helloProtocolHash != MetaSerializerTypeRegistry.FullProtocolHash)
                {
                    c_protocolHashMimatch.Inc();
                    _log.Info(
                        "Full protocol hash mismatch: client=0x{ClientProtocolHash:X8} vs server=0x{ServerProtocolHash:X8} (ClientVersion={ClientVersion}, BuildNumber={BuildNumber}, CommitId={CommitId})",
                        _helloProtocolHash,
                        MetaSerializerTypeRegistry.FullProtocolHash,
                        _helloClientVersion,
                        _helloBuildNumber,
                        _helloCommitId);
                }
            }

            return true;
        }

        async Task ReceiveSessionStartRequest(SessionProtocol.SessionStartRequest sessionRequest)
        {
            InternalSessionStartNewRequest startRequest = new InternalSessionStartNewRequest(
                logicVersion:                   _logicVersion,
                appLaunchId:                    _helloAppLaunchId,
                clientSessionNonce:             _helloClientSessionNonce,
                clientSessionConnectionNdx:     _helloClientSessionConnectionNdx,
                clientVersion:                  _helloClientVersion,
                deviceGuid:                     sessionRequest.DeviceGuid,
                debugDiagnostics:               _loginDebugDiagnostics,
                loginGamePayload:               _loginGamePayload,
                sessionResourceProposal:        sessionRequest.ResourceProposal,
                sessionGamePayload:             sessionRequest.GamePayload,
                deviceModel:                    sessionRequest.DeviceModel,
                playerTimeZoneInfo:             sessionRequest.TimeZoneInfo,
                playerLocation:                 Geolocation.Instance.TryGetPlayerLocation(_srcAddr),
                isDryRun:                       sessionRequest.IsDryRun,
                supportedArchiveCompressions:   sessionRequest.SupportedArchiveCompressions,
                clientPlatform:                 _helloPlatform,
                authKey:                        _loginNewSessionAuthKey);

            Stopwatch                       sw                      = Stopwatch.StartNew();
            int                             numRetries              = 0;
        retry:
            EntityId                        sessionId               = PlayerIdToSessionId(_loginPlayerId);
            EntitySubscription              sessionSubscription;
            InternalSessionStartNewResponse sessionResponse;
            try
            {
                // \note: Subscribe can take a long time. Keep client informed of the forward progress.
                (sessionSubscription, sessionResponse) = await WaitLongRunningOperationAsync(SubscribeToAsync<InternalSessionStartNewResponse>(sessionId, EntityTopic.Owner, startRequest));
            }
            catch (InternalSessionStartNewRefusal refusal)
            {
                // Refusal for a reason.

                if (refusal.Result == InternalSessionStartNewRefusal.ResultCode.ConnectionIsStale)
                {
                    // Connection was stale (a newer connection has already taken ownership). Just throw this away.
                    _log.Warning("Client connection from {SourceAddress} for player {PlayerId} was deemed stale and was silently rejected. [{Milliseconds}ms]", IPAddressRedactionUtil.ToPrivacyProtectingString(_srcAddr), _loginPlayerId, sw.ElapsedMilliseconds);
                    await GracefulStopAsync();
                    return;
                }

                if (refusal.Result == InternalSessionStartNewRefusal.ResultCode.ResourceCorrection)
                {
                    _log.Debug("Session {SessionId} start rejected, needs resource correction [{Milliseconds}ms]", sessionId, sw.ElapsedMilliseconds);
                    _hasSentResourceCorrection = true;

                    SendToClient(new SessionProtocol.SessionStartResourceCorrection(sessionRequest.QueryId, refusal.ResourceCorrection));
                    return;
                }

                SessionProtocol.SessionStartFailure.ReasonCode errorCode;
                string debugOnlyErrorMessage;
                if (refusal.Result == InternalSessionStartNewRefusal.ResultCode.DryRunSuccess)
                {
                    _log.Debug("Session {SessionId} start dry-run success [{Milliseconds}ms]", sessionId, sw.ElapsedMilliseconds);
                    errorCode = SessionProtocol.SessionStartFailure.ReasonCode.DryRun;
                    debugOnlyErrorMessage = null;
                }
                else if (refusal.Result == InternalSessionStartNewRefusal.ResultCode.PlayerIsBanned)
                {
                    _log.Debug("Session {SessionId} start rejected, player is banned [{Milliseconds}ms]", sessionId, sw.ElapsedMilliseconds);
                    errorCode = SessionProtocol.SessionStartFailure.ReasonCode.Banned;
                    debugOnlyErrorMessage = null;
                }
                else if (refusal.Result == InternalSessionStartNewRefusal.ResultCode.EntityIsRestarting)
                {
                    numRetries++;
                    if (numRetries <= 1)
                    {
                        _log.Debug("Session {SessionId} start temporarily rejected as the SessionActor entity was restarting. Trying again.", sessionId, sw.ElapsedMilliseconds);
                        goto retry;
                    }

                    _log.Warning("Session start failed. Got EntityIsRestarting after too many attempts ({NumAttempts}). [{Milliseconds}ms]", numRetries, sw.ElapsedMilliseconds);
                    errorCode = SessionProtocol.SessionStartFailure.ReasonCode.InternalError;
                    debugOnlyErrorMessage = default;
                }
                else
                {
                    _log.Debug("Session {SessionId} start failed due to an error [{Milliseconds}ms]", sessionId, sw.ElapsedMilliseconds);
                    errorCode = SessionProtocol.SessionStartFailure.ReasonCode.InternalError;

                    if (RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>().EnableDevelopmentFeatures)
                        debugOnlyErrorMessage = $"got unexpected Result from SessionActor: {refusal.Result}";
                    else
                        debugOnlyErrorMessage = null;
                }

                SendToClient(new SessionProtocol.SessionStartFailure(sessionRequest.QueryId, errorCode, debugOnlyErrorMessage));
                return;
            }
            catch (Exception e)
            {
                // Unclean error

                string debugOnlyErrorMessage = null;
                if (RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>().EnableDevelopmentFeatures)
                    debugOnlyErrorMessage = Util.ShortenStringToUtf8ByteLength(e.ToString(), maxNumBytes: 128 * 1024); // limit to 128KB

                SendToClient(new SessionProtocol.SessionStartFailure(sessionRequest.QueryId, SessionProtocol.SessionStartFailure.ReasonCode.InternalError, debugOnlyErrorMessage));
                await GracefulStopAsync();

                // Let this crash
                throw;
            }

            // Session started succesfully.

            bool isDeveloperBypass = IsPlatformInMaintenance(_helloPlatform) &&
                GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(_loginPlayerId);

            _log.Debug("Subscribed to {SessionId} [{Milliseconds}ms]", sessionId, sw.ElapsedMilliseconds);

            SendToClient(new SessionProtocol.SessionStartSuccess(
                queryId:                        sessionRequest.QueryId,
                logicVersion:                   _logicVersion,
                sessionToken:                   sessionResponse.Token,
                scheduledMaintenanceMode:       sessionResponse.MetaAuxiliaryInfo.ScheduledMaintenanceMode,
                playerId:                       _loginPlayerId,
                playerState:                    sessionResponse.MetaAuxiliaryInfo.PlayerInitialState,
                localizationVersions:           sessionResponse.MetaAuxiliaryInfo.LocalizationVersions,
                activeExperiments:              sessionResponse.MetaAuxiliaryInfo.ActiveExperiments,
                developerMaintenanceBypass:     isDeveloperBypass,
                entityStates:                   sessionResponse.MetaAuxiliaryInfo.EntityInitialStates,
                gamePayload:                    sessionResponse.GamePayload,
                correctedDeviceGuid:            sessionResponse.MetaAuxiliaryInfo.CorrectedDeviceGuid,
                resumptionToken:                SessionResumptionTokenUtil.GenerateResumptionToken(_loginPlayerId, sessionResponse.Token)
                ));

            // Switch to active state
            _status = Status.Session;
            _sessionId = sessionId;
            _sessionSubscription = sessionSubscription;
        }

        async Task ReceiveLoginAndSessionResumeRequest(Handshake.LoginAndResumeSessionRequest loginAndResumeSessionRequest)
        {
            c_sessionResumeAttempts.Inc();

            bool isInMaintenance = IsPlatformInMaintenance(_helloPlatform);

            // If not in maintenance, do early version check
            if (!isInMaintenance && !CheckClientHelloLogicVersionRange())
            {
                c_sessionResumeFails.WithLabels("in_maintenance").Inc();
                await GracefulStopAsync();
                return;
            }

            // Check the request was generated by this cluster invocation.
            if (!SessionResumptionTokenUtil.ValidateResumptionToken(loginAndResumeSessionRequest.ClaimedPlayerId, loginAndResumeSessionRequest.SessionToResume.Token, loginAndResumeSessionRequest.ResumptionToken))
            {
                _log.Warning("Session {ClaimedSessionToken} resume failed. Invalid resume token.", loginAndResumeSessionRequest.SessionToResume.Token);
                c_sessionResumeFails.WithLabels("bad_resume_token").Inc();
                SendToClient(new SessionProtocol.SessionResumeFailure());
                await GracefulStopAsync();
                return;
            }

            // If in maintenance, inform client and shut down. If player is developer, pass on to LogicVersion check.
            if (isInMaintenance)
            {
                if (!GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(loginAndResumeSessionRequest.ClaimedPlayerId))
                {
                    c_sessionResumeFails.WithLabels("in_maintenance").Inc();
                    SendToClient(Handshake.OngoingMaintenance.Instance);
                    await GracefulStopAsync();
                    return;
                }

                // Check LogicVersion if it wasn't checked earlier
                if (!CheckClientHelloLogicVersionRange())
                {
                    c_sessionResumeFails.WithLabels("in_maintenance").Inc();
                    await GracefulStopAsync();
                    return;
                }
            }

            _loginPlayerId = loginAndResumeSessionRequest.ClaimedPlayerId;
            _loginDebugDiagnostics = loginAndResumeSessionRequest.DebugDiagnostics;
            _loginGamePayload = loginAndResumeSessionRequest.GamePayload;

            InternalSessionResumeRequest resumeRequest = new InternalSessionResumeRequest(
                logicVersion:                   _logicVersion,
                appLaunchId:                    _helloAppLaunchId,
                clientSessionNonce:             _helloClientSessionNonce,
                clientSessionConnectionNdx:     _helloClientSessionConnectionNdx,
                clientVersion:                  _helloClientVersion,
                debugDiagnostics:               _loginDebugDiagnostics,
                loginGamePayload:               _loginGamePayload,
                sessionToResume:                loginAndResumeSessionRequest.SessionToResume);

            Stopwatch                       sw                      = Stopwatch.StartNew();
            EntityId                        sessionId               = PlayerIdToSessionId(_loginPlayerId);
            EntitySubscription              sessionSubscription;
            InternalSessionResumeResponse   sessionResponse;
            try
            {
                // \note: Subscribe CANNOT take long time. The Session is either already there (fast), or a new empty SessionActor is created just to reject us (fast).
                (sessionSubscription, sessionResponse) = await SubscribeToAsync<InternalSessionResumeResponse>(sessionId, EntityTopic.Owner, resumeRequest);
            }
            catch (InternalSessionResumeRefusal refusal)
            {
                // Refusal for a reason.

                if (refusal.Result == InternalSessionResumeRefusal.ResultCode.ConnectionIsStale)
                {
                    // Connection was stale (a newer connection has already taken ownership). Just throw this away.
                    _log.Warning("Client connection from {SourceAddress} for player {PlayerId} was deemed stale and was silently rejected. [{Milliseconds}ms]", IPAddressRedactionUtil.ToPrivacyProtectingString(_srcAddr), _loginPlayerId, sw.ElapsedMilliseconds);
                    c_sessionResumeFails.WithLabels("conn_stale").Inc();
                    await GracefulStopAsync();
                    return;
                }

                _log.Debug("Session {SessionId} resume failed [{Milliseconds}ms]", sessionId, sw.ElapsedMilliseconds);
                c_sessionResumeFails.WithLabels("refusal").Inc();
                SendToClient(new SessionProtocol.SessionResumeFailure());
                return;
            }
            catch
            {
                // Unclean error
                c_sessionResumeFails.WithLabels("error").Inc();

                // \note: no need to send error message. If this is transient error, retry will fix. If not, the new-session path
                //        (in ReceiveSessionStartRequest) will eventually deliver the error message.
                SendToClient(new SessionProtocol.SessionResumeFailure());

                // Let this crash
                throw;
            }

            // Success

            _log.Debug("Subscribed to {SessionId} [{Milliseconds}ms]", sessionId, sw.ElapsedMilliseconds);

            SendToClient(new SessionProtocol.SessionResumeSuccess(
                serverSessionAcknowledgement:   sessionResponse.ServerAcknowledgement,
                sessionToken:                   sessionResponse.Token,
                scheduledMaintenanceMode:       sessionResponse.ScheduledMaintenanceMode));

            // Switch to active state
            _status = Status.Session;
            _sessionId = sessionId;
            _sessionSubscription = sessionSubscription;
            _sessionWasResumed = true;
        }

        async Task ReceiveSessionStartAbort(SessionProtocol.SessionStartAbort sessionAbort)
        {
            string phaseDescription;
            if (_status == Status.WaitingForSession)
            {
                if (!_hasSentResourceCorrection)
                {
                    // Failure before session was started.
                    c_sessionStartFailures.WithLabels("client_incident_after_login").Inc();
                    phaseDescription = "AfterLogin";
                }
                else
                {
                    // Failure before session was started, but after resource correction.
                    c_sessionStartFailures.WithLabels("client_incident_after_resource_correction").Inc();
                    phaseDescription = "AfterResourceCorrection";
                }
            }
            else
            {
                // Failure after session was started.
                c_sessionStartFailures.WithLabels("client_incident_after_session_start").Inc();
                phaseDescription = "AfterSessionStarted";

                // Shut down session subscription immediately.
                await UnsubscribeFromAsync(_sessionSubscription);
                _sessionSubscription = null;
                _sessionId = EntityId.None;
            }

            _log.Warning("Client aborted session due to a local problem. Report={Phase}, HasIncidentReport={HasIncidentReport}", phaseDescription, sessionAbort.HasReasonTrailer);

            if (!sessionAbort.HasReasonTrailer)
            {
                // Shut down connection.
                await GracefulStopAsync();
            }
            else
            {
                // There will be a trailer message containing the reason.
                _status = Status.WaitingForAbortReason;
            }
        }

        async Task ReceiveSessionStartAbortReasonTrailer(SessionProtocol.SessionStartAbortReasonTrailer sessionAbortTrailer)
        {
            PlayerIncidentOptions incidentOptions = RuntimeOptionsRegistry.Instance.GetCurrent<PlayerIncidentOptions>();

            int pushIncidentSendRatePermille = 0;
            if (incidentOptions.EnableUploads)
                pushIncidentSendRatePermille = 10 * incidentOptions.PushUploadPercentageSessionStartFailed;

            // Check if throttling should have removed this already.
            if (!KeyedStableWeightedCoinflip.FlipACoin(PlayerIncidentUtil.PushThrottlingKeyflipKey, (uint)PlayerIncidentUtil.GetSuffixFromIncidentId(sessionAbortTrailer.IncidentId), pushIncidentSendRatePermille))
            {
                _log.Warning("Session start abort had incident payload. Expected no payload due to throttling.");
            }
            else
            {
                _log.Warning("Received client session start incident report.");

                // Decode incident.
                PlayerIncidentReport.SessionStartFailed incident = (PlayerIncidentReport.SessionStartFailed)PlayerIncidentUtil.DecompressNetworkDeliveredIncident(sessionAbortTrailer.Incident, out int uncompressedPayloadSize);

                if (incident.IncidentId != sessionAbortTrailer.IncidentId)
                {
                    _log.Warning("Session start abort had invalid incident payload.");
                }
                else
                {
                    // Store information directly of the incident report if such were attached. Note that we re-encode the message, just in case.
                    byte[] uncompressedBytes = MetaSerialization.SerializeTagged<PlayerIncidentReport>(incident, MetaSerializationFlags.Persisted, logicVersion: null);
                    byte[] payload = CompressUtil.DeflateCompress(uncompressedBytes);

                    await PlayerIncidentStorage.PersistIncidentAsync(_loginPlayerId, incident, payload);

                    // Inform player about persisted incident
                    await EntityAskAsync<EntityAskOk>(_loginPlayerId, new InternalNewPlayerIncidentRecorded(incident.IncidentId, incident.OccurredAt, incident.Type,
                        incident.SubType, PlayerIncidentUtil.TruncateReason(incident.GetReason())));
                }
            }

            await GracefulStopAsync();
        }

        static async Task<RegisterAccountResponse> TryCreateNewAccount(IMetaLogger log)
        {
            c_registerPlayer.Inc();

            try
            {
                RegisterAccountResponse result = await Authenticator.RegisterAccountAsync(log);
                log.Info("Registered new {PlayerId} (deviceId={DeviceId})", result.PlayerId, result.DeviceId);
                return result;
            }
            catch (Exception ex)
            {
                log.Warning("Failed to register new player: {Exception}", ex);
                // \todo [petri] send message to client, so they can try again
                c_registerPlayerFail.Inc();
                return null;
            }
        }

        async Task<LoginAuthParams> TryAuthenticateExistingAccount(Handshake.LoginRequest req)
        {
            c_playerLogins.Inc();

            try
            {
                // Perform authentication
                LoginAuthParams accountInfo;
                Stopwatch swAuth = Stopwatch.StartNew();

                switch (req)
                {
                    case Handshake.DeviceLoginRequest deviceReq:
                    {
                        _log.Debug("Request device login for {PlayerId} (deviceId={DeviceId}, isBot={IsBot})", deviceReq.PlayerIdHint, deviceReq.DeviceId, req.IsBot);
                        EntityId playerId = await Authenticator.AuthenticateAccountByDeviceIdAsync(_log, deviceReq.DeviceId, deviceReq.AuthToken, claimedPlayerId: deviceReq.PlayerIdHint, deviceReq.IsBot);
                        accountInfo = new LoginAuthParams(
                            key:        new AuthenticationKey(AuthenticationPlatform.DeviceId, deviceReq.DeviceId),
                            playerId:   playerId);
                        break;
                    }
                    case Handshake.SocialAuthenticationLoginRequest socialReq:
                    {
                        _log.Debug("Request social auth login for {PlayerId} (platform={Platform})", socialReq.PlayerIdHint, socialReq.Claim.Platform);
                        SocialAuthenticationResult authResult = await Authenticator.AuthenticateAccountViaSocialPlatform(_log, asker: this, socialReq.Claim, claimedPlayerId: socialReq.PlayerIdHint, socialReq.IsBot);
                        accountInfo = new LoginAuthParams(
                            key:        authResult.AuthKeys.PrimaryAuthenticationKey,
                            playerId:   authResult.ExistingPlayerId);
                        break;
                    }
                    case Handshake.DualSocialAuthenticationLoginRequest socialReq:
                    {
                        _log.Debug("Request dual social auth login for {PlayerId} (platform={Platform})", socialReq.PlayerIdHint, socialReq.Claim.Platform);

                        // Log in using the social claim
                        SocialAuthenticationResult authResult;
                        try
                        {
                            authResult = await Authenticator.AuthenticateAccountViaSocialPlatform(_log, asker: this, socialReq.Claim, claimedPlayerId: socialReq.PlayerIdHint, socialReq.IsBot);
                        }
                        catch (NoBoundPlayerAccountForValidSocialAccount noPlayerError)
                        {
                            // The social account is not yet bound to any player account. Try to bind it now into the guest (device id) account.
                            // Note that we the deviceId/guest account still points to the same account.

                            // Verify the device ID is valid. Note that it doesn't need to point to the same game account as the social account.
                            EntityId guestAccountPlayerId = await Authenticator.AuthenticateAccountByDeviceIdAsync(_log, socialReq.DeviceId, socialReq.AuthToken, claimedPlayerId: socialReq.PlayerIdHint, isBot: false);

                            InternalPlayerLoginWithNewSocialAccountResponse response = await EntityAskAsync<InternalPlayerLoginWithNewSocialAccountResponse>(guestAccountPlayerId, new InternalPlayerLoginWithNewSocialAccountRequest(noPlayerError.AuthKeys));
                            if (response.Result == InternalPlayerLoginWithNewSocialAccountResponse.ResultCode.SocialAccountAdded)
                            {
                                // Social account bound. Hence login is successful.
                                authResult = new SocialAuthenticationResult(noPlayerError.AuthKeys, guestAccountPlayerId);
                            }
                            else
                                throw new InvalidOperationException($"Invalid social login add result: {response.Result}");
                        }

                        accountInfo = new LoginAuthParams(
                            key:        authResult.AuthKeys.PrimaryAuthenticationKey,
                            playerId:   authResult.ExistingPlayerId);
                        break;
                    }
                    default:
                        _log.Warning("Login request of unknown type: {LoginRequestType}", req.GetType().Name);
                        return null;
                }

                c_authDuration.Observe(swAuth.Elapsed.TotalSeconds);
                return accountInfo;
            }
            catch (Exception ex)
            {
                _log.Warning("Failed to authenticate {PlayerId}: {Ex}", req.PlayerIdHint, ex);
                // \todo [petri] send message to client?
                c_playerLoginFail.Inc();
                return null;
            }
        }

        static bool IsPlatformInMaintenance(ClientPlatform platform)
        {
            return GlobalStateProxyActor.IsInMaintenance &&
                !GlobalStateProxyActor.ActiveScheduledMaintenanceMode.Get().IsPlatformExcluded(platform);
        }

        static bool TryBlockBot(bool isBot)
        {
            // Block bots if not allowed in config
            SystemOptions systemOpts = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>();
            return (isBot && !systemOpts.AllowBotAccounts);
        }

        static EntityId PlayerIdToSessionId(EntityId playerId) => EntityId.Create(EntityKindCore.Session, playerId.Value);

        void SendToClient(MetaMessage message)
        {
            MetaDebug.Assert(message != null, "Trying to send a null message to client");

            MetaMessageSpec     messageSpec         = MetaMessageRepository.Instance.GetFromType(message.GetType());
            MessageDirection    messageDirection    = messageSpec.MessageDirection;

            if (messageDirection != MessageDirection.ServerToClient && messageDirection != MessageDirection.Bidirectional)
                throw new InvalidOperationException($"Trying to send message {messageSpec.Name} to client with invalid message direction: {messageDirection}");

            c_sentMessages.WithLabels(message.GetType().Name).Inc();

            // _log.Debug("SendToClient(): {0}", PrettyPrint.Compact(message));
            byte[] serialized = MetaSerialization.SerializeTagged(message, MetaSerializationFlags.SendOverNetwork, _logicVersion);
            int uncompressedPayloadSize = serialized.Length;

            // Check uncompressed size limit
            if (serialized.Length > WireProtocol.MaxPacketUncompressedPayloadSize)
                throw new InvalidOperationException($"Maximum packet uncompressed size exceeded for {messageSpec.Name} (size={serialized.Length}, max={WireProtocol.MaxPacketUncompressedPayloadSize})");

            // Compress large messages
            WirePacketCompression compressionMode = WirePacketCompression.None;
            if (_enableWireCompression && serialized.Length >= WireProtocol.CompressionThresholdBytes)
            {
                compressionMode = WirePacketCompression.Deflate;
                serialized = CompressUtil.DeflateCompress(serialized);
            }

            // Check on-wire size limit
            if (serialized.Length > WireProtocol.MaxPacketWirePayloadSize)
            {
                throw new InvalidOperationException(
                    $"Maximum packet on-wire size exceeded for {messageSpec.Name}" +
                    $" (size={serialized.Length}, uncompressedSize={uncompressedPayloadSize}," +
                    $" max={WireProtocol.MaxPacketWirePayloadSize}, uncompressedMax={WireProtocol.MaxPacketUncompressedPayloadSize})");
            }

            // Write bytes to socket
            WriteToSocket(WirePacketType.Message, compressionMode, serialized);
        }

        void WriteToSocket(WirePacketType packetType, WirePacketCompression compressionMode, byte[] payload)
        {
            uint flags = ((uint)packetType << WirePacketFlagBits.TypeOffset) | ((uint)compressionMode << WirePacketFlagBits.CompressionOffset);

            // Write header & payload
            int payloadSize = payload.Length;
            ByteString header = ByteString.FromBytes(new byte[] { (byte)flags, (byte)(payloadSize >> 16), (byte)(payloadSize >> 8), (byte)payloadSize });
            WriteBytesToSocket(header.Concat(ByteString.FromBytes(payload)));
        }

        /// <summary>
        /// Parsed and uncompressed contents of an incoming wire packet.
        /// </summary>
        readonly struct WirePacket
        {
            public readonly WirePacketType  PacketType;     // A valid WirePacketType (and not None).
            public readonly byte[]          Payload;        // Uncompressed payload.
            public readonly int             FullWireSize;   // Total number of bytes used by this packet on the wire: both the header and the possibly-compressed payload.

            public WirePacket(WirePacketType packetType, byte[] payload, int fullWireSize)
            {
                PacketType = packetType;
                Payload = payload ?? throw new ArgumentNullException(nameof(payload));
                FullWireSize = fullWireSize;
            }
        }

        /// <summary>
        /// Parse an incoming wire packet header from the buffer, or return null
        /// if there's not enough data in the buffer for the header.
        /// This does NOT validate the header, it only parses it.
        /// </summary>
        static WirePacketHeader? TryParsePacketHeaderRaw(ByteString incomingBuffer)
        {
            while (incomingBuffer.Count < WireProtocol.PacketHeaderSize)
                return null;

            uint flags = incomingBuffer[0];
            WirePacketType packetType = (WirePacketType)((flags >> WirePacketFlagBits.TypeOffset) & WirePacketFlagBits.TypeMask);
            WirePacketCompression compressionMode = (WirePacketCompression)((flags >> WirePacketFlagBits.CompressionOffset) & WirePacketFlagBits.CompressionMask);
            int payloadLength = (incomingBuffer[1] << 16) | (incomingBuffer[2] << 8) | incomingBuffer[3];

            return new WirePacketHeader(packetType, compressionMode, payloadLength);
        }

        /// <summary>
        /// Parse an incoming wire packet from the buffer, or return null
        /// if there's not enough data in the buffer for a complete packet.
        /// Throws (instead of returning null) if a malformed data is detected.
        /// </summary>
        static WirePacket? TryParsePacket(ByteString incomingBuffer)
        {
            // Parse header.
            // Note that this does not validate the header. We do that below.
            WirePacketHeader? headerMaybe = TryParsePacketHeaderRaw(incomingBuffer);
            if (!headerMaybe.HasValue)
                return null;
            WirePacketHeader header = headerMaybe.Value;

            WirePacketType          packetType      = header.Type;
            WirePacketCompression   compressionMode = header.Compression;
            int                     payloadLength   = header.PayloadSize;

            // Validate payloadLength
            if (payloadLength <= 0 || payloadLength > WireProtocol.MaxPacketWirePayloadSize)
                throw new InvalidOperationException($"Invalid payloadLength ({payloadLength}) header received");

            // Validate packet type
            if (packetType != WirePacketType.Message
             && packetType != WirePacketType.Ping
             && packetType != WirePacketType.PingResponse
             && packetType != WirePacketType.HealthCheck)
            {
                throw new InvalidOperationException($"Invalid packet type: {packetType}");
            }

            // Validate WirePacketCompression and translate it to CompressionAlgorithm
            CompressionAlgorithm compressionAlgorithm;
            switch (compressionMode)
            {
                case WirePacketCompression.None:    compressionAlgorithm = CompressionAlgorithm.None;       break;
                case WirePacketCompression.Deflate: compressionAlgorithm = CompressionAlgorithm.Deflate;    break;
                default:
                    throw new InvalidOperationException($"Invalid compression mode for packet: {compressionMode}");
            }

            // Check if there's enough data to read the whole packet
            int fullPacketSize = WireProtocol.PacketHeaderSize + payloadLength;
            if (incomingBuffer.Count < fullPacketSize)
                return null;

            // Full packet received, parse it
            byte[] wirePayload = incomingBuffer.Slice(WireProtocol.PacketHeaderSize, payloadLength).ToArray();

            // Decompress
            byte[] uncompressedPayload = CompressUtil.Decompress(wirePayload, compressionAlgorithm, maxDecompressedSize: WireProtocol.MaxPacketUncompressedPayloadSize);

            return new WirePacket(packetType, uncompressedPayload, fullPacketSize);
        }

        enum PacketParsingErrorReaction
        {
            Rethrow,
            DoNotRethrow,
        }

        PacketParsingErrorReaction HandlePacketParsingError(Exception exception)
        {
            // If we've received malformed data, we cannot continue.
            // Note that malformed data can sometimes result in a graceful shutdown,
            // so we disable further requests in case the actor wasn't instantly terminated.
            _disableRequestHandling = true;

            // If parsing errors at the first packet, there's a good chance that this isn't
            // a game connection at all, but something else, like an attempted HTTP request.
            // Detect if it looks like well-known kind of traffic, and if so, swallow the exception
            // to avoid logging errors.
            // \note A client->server game-magic protocol prefix would be a cleaner way for detecting game traffic.

            // Don't try to detect well-known traffic on anything but the first packet.
            if (_totalPacketsReceived > 0)
            {
                _log.Error("Got malformed packet from {SourceAddress} (totalBytesReceived={TotalBytesReceived}, totalBytesProcessed={TotalBytesProcessed}, totalPacketsReceived={TotalPacketsReceived}, incomingBufferSize={IncomingBufferSize})", IPAddressRedactionUtil.ToPrivacyProtectingString(_srcAddr), _totalBytesReceived, _totalBytesProcessed, _totalPacketsReceived, _incomingBuffer.Count);
                return PacketParsingErrorReaction.Rethrow;
            }

            // Check if well-known traffic.
            WellKnownTrafficInfo trafficInfo = WellKnownTrafficUtil.TryGetWellKnownTrafficInfo(_incomingBuffer);
            if (trafficInfo == null)
            {
                // Not well-known traffic. Log first couple of bytes, so they can be debugged, and just re-throw.
                _log.Error("Got malformed first packet from {SourceAddress} (totalBytesReceived={TotalBytesReceived}): [ {IncomingBufferPrefix} ... ]", IPAddressRedactionUtil.ToPrivacyProtectingString(_srcAddr), _totalBytesReceived, Util.BytesToString(_incomingBuffer.Take(5).ToArray()));
                return PacketParsingErrorReaction.Rethrow;
            }

            // Traffic is well-known - handle it more silently.
            // In particular, don't produce error-level logging and don't throw.
            _log.Info("Got malformed first packet from {SourceAddress} (totalBytesReceived={TotalBytesReceived}), and traffic is of well-known kind {TrafficKindLabel}: {TrafficDescription}. Shutting down. Exception: {Exception}", IPAddressRedactionUtil.ToPrivacyProtectingString(_srcAddr), _totalBytesReceived, trafficInfo.KindLabel, trafficInfo.Description, exception.Message);
            c_wellKnownMalformeds.WithLabels(trafficInfo.KindLabel).Inc();
            CloseSocket();
            RequestShutdown();
            return PacketParsingErrorReaction.DoNotRethrow;
        }

        HealthCheckTypeBits ParseHealthCheckType(byte[] data)
        {
            if (data.Length != 4)
                throw new InvalidOperationException("Invalid health check request");

            return (HealthCheckTypeBits)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);
        }

        byte[] FormatHealthCheckType(HealthCheckTypeBits type)
        {
            uint bits = (uint)type;
            return new byte[] {
                (byte)((bits >> 24) & 0xFF),
                (byte)((bits >> 16) & 0xFF),
                (byte)((bits >>  8) & 0xFF),
                (byte)((bits      ) & 0xFF)
            };
        }

        private async Task HandleIncomingPackets()
        {
            while (true)
            {
                if (_disableRequestHandling || _status == Status.StoppingGracefully)
                    return;

                // Parse the packet

                WirePacket? packetMaybe;

                try
                {
                    packetMaybe = TryParsePacket(_incomingBuffer);
                }
                catch (Exception ex)
                {
                    PacketParsingErrorReaction reaction = HandlePacketParsingError(ex);

                    if (reaction == PacketParsingErrorReaction.Rethrow)
                        throw;
                    else
                        return;
                }

                // If buffer doesn't yet hold a complete packet, break
                if (!packetMaybe.HasValue)
                    break;

                // Handle the packet

                WirePacket packet = packetMaybe.Value;

                MetaMessage message = null;
                HealthCheckTypeBits? healthCheck = null;

                // Handle packet types
                switch (packet.PacketType)
                {
                    case WirePacketType.Message:
                        message = MetaSerialization.DeserializeTagged<MetaMessage>(packet.Payload, MetaSerializationFlags.SendOverNetwork, resolver: null, _logicVersion);

                        // Dump first few received messages
                        // \note Disabled by default, but can be useful when debugging networking protocol issues
                        //if (_totalPacketsReceived <= 2 && _log.IsDebugEnabled)
                        //{
                        //    const int MaxLength = 256;
                        //    string base64 = Convert.ToBase64String(payload, 0, Math.Min(payload.Length, MaxLength));
                        //    bool hasMore = payload.Length > MaxLength;
                        //    _log.Debug("Network message #{PacketIndex} ({Type}, {NumBytes} byte payload): {Payload}{HasMore}", _totalPacketsReceived, message.GetType().Name, payloadLength, base64, hasMore ? "..." : ".");
                        //}
                        break;

                    case WirePacketType.Ping:
                        // got ping, we reply
                        WriteToSocket(WirePacketType.PingResponse, WirePacketCompression.None, packet.Payload);
                        break;

                    case WirePacketType.HealthCheck:
                        healthCheck = ParseHealthCheckType(packet.Payload);
                        break;

                    case WirePacketType.PingResponse:
                        // got pong
                        break;

                    default:
                        throw new InvalidOperationException("unrecognized wire flags");
                }

                // Update stats
                _totalPacketsReceived++;
                _totalBytesProcessed += packet.FullWireSize;

                // Skip to next message
                _incomingBuffer = _incomingBuffer.Slice(packet.FullWireSize);

                RenewConnectionWatchdog();

                // Handle message, if any
                if (message != null)
                    await OnNetworkMessage(message);
                else if (healthCheck.HasValue)
                    await OnHealthCheckRequest(healthCheck.Value);
            }
        }

        [CommandHandler]
        public async Task HandleActorTick(ActorTick tick)
        {
            // Handle timeouts and probes
            TimeSpan elapsedSinceLastMessage = DateTime.UtcNow - _lastMessageReceivedAt;
            if (elapsedSinceLastMessage >= ConnectionHardTimeout)
            {
                if (_status == Status.WaitingForHello)
                    _log.Warning("No messages for {TimeSinceLastMessage} (last communication at {LastCommunicationAt}) which exceeds the connection timeout ({ConnectionTimeout}), and no hello received from {SourceAddress}, closing connection..", elapsedSinceLastMessage, _lastMessageReceivedAt, ConnectionHardTimeout, IPAddressRedactionUtil.ToPrivacyProtectingString(_srcAddr));
                else
                    _log.Debug("No messages for {TimeSinceLastMessage} (last communication at {LastCommunicationAt}) which exceeds the connection timeout ({ConnectionTimeout}), closing connection..", elapsedSinceLastMessage, _lastMessageReceivedAt, ConnectionHardTimeout);

                await GracefulStopAsync();
            }
            else if (elapsedSinceLastMessage >= ConnectionProbeAfterTimeout && !_connectionProbeSent)
            {
                _log.Debug("No messages for {TimeSinceLastMessage} (last communication at {LastCommunicationAt}), sending ping probe", elapsedSinceLastMessage, _lastMessageReceivedAt, ConnectionProbeAfterTimeout);
                _connectionProbeSent = true;
                WriteToSocket(WirePacketType.Ping, WirePacketCompression.None, new byte[] { 0x57, 0x41, 0x4b, 0x45 }); // "WAKE"
            }
        }

        protected async Task OnReceiveSocketData(ByteString data)
        {
            //_log.Debug("Received data: {0} bytes", received.Data.Count);
            _totalBytesReceived += data.Count;
            _incomingBuffer     =  _incomingBuffer.Concat(data);
            await HandleIncomingPackets().ConfigureAwait(false);
        }

        protected void OnPeerClosedConnection()
        {
            // If connection was closed during session handshake, update metrics.

            if (_status == Status.WaitingForSession)
            {
                if (!_hasSentResourceCorrection)
                {
                    // Connection loss before session was started.
                    c_sessionStartFailures.WithLabels("client_lost_after_login").Inc();
                }
                else
                {
                    // Connection loss before session was started, but after resource correction.
                    c_sessionStartFailures.WithLabels("client_lost_after_resource_correction").Inc();
                }
            }
            else if (_status == Status.Session && _numSessionMessagesReceived == 0)
            {
                if (!_sessionWasResumed)
                    c_sessionStartFailures.WithLabels("client_lost_after_session_start").Inc();
            }
        }

        protected override async Task OnSubscriptionKicked(EntitySubscription subscription, MetaMessage message)
        {
            if (subscription == _sessionSubscription)
            {
                _log.Info("Kicked from owner {EntityId} [{Topic}]: {Message}", subscription.EntityId, subscription.Topic, PrettyPrint.Compact(message));
                _sessionSubscription = null;
                SendToClient(message);
                await GracefulStopAsync();
            }
            else
                _log.Warning("Got kicked from unknown entity {EntityId} [{Topic}]: {Message}", subscription.EntityId, subscription.Topic, PrettyPrint.Compact(message));
        }

        protected override Task HandleUnknownMessage(EntityId fromEntityId, MetaMessage message)
        {
            // \todo [petri] handle based on message flags
            //_log.Debug("Forwarding unknown message to client: {0}", message.GetType().Name);
            SendToClient(message);
            return Task.CompletedTask;
        }

        protected override Task OnSubscriptionLost(EntitySubscription subscription)
        {
            _log.Warning("Lost subscription to {Actor}", subscription.ActorRef);
            // \todo [petri] better handling
            RequestShutdown();
            return Task.CompletedTask;
        }

        protected override void PostStop()
        {
            // Only log if an actual client connection (otherwise load balancer connections become noisy)
            if (_sessionId.IsValid)
                _log.Debug("Stopped: sessionId={SessionId}, playerId={PlayerId}", _sessionId, _loginPlayerId);

            base.PostStop();
        }

        protected async Task GracefulStopAsync()
        {
            // No need to close twice
            if (_status == Status.StoppingGracefully)
                return;

            // Try to close socket gracefully
            CloseSocket();

            // Track stats
            if (_sessionId.IsValid)
            {
                // \todo [petri] analytics event

                // Log info about the connection's duration, and total and still-unprocessed traffic.

                TimeSpan connectionLength = DateTime.UtcNow - _startTime;

                WirePacketHeader? headerMaybe = TryParsePacketHeaderRaw(_incomingBuffer);
                if (headerMaybe.HasValue)
                {
                    WirePacketHeader    header          = headerMaybe.Value;
                    string              messageTypeName = TryPeekMessageTypeName(header, _incomingBuffer.Slice(index: WireProtocol.PacketHeaderSize));

                    _log.Info("Ending connection. Duration: {ConnectionLength}." +
                        " Total incoming: bytesReceived={TotalBytesReceived}, bytesProcessed={TotalBytesProcessed}, packetsReceived={TotalPacketsReceived}." +
                        " Remaining incoming: numBytes={NumIncomingBytes}, type={PacketType}, compression={PacketCompression}, size={PacketPayloadSize}, messageType={MessageTypeName}.",
                        connectionLength,
                        _totalBytesReceived, _totalBytesProcessed, _totalPacketsReceived,
                        _incomingBuffer.Count, header.Type, header.Compression, header.PayloadSize, messageTypeName ?? "<none>");
                }
                else
                {
                    _log.Info("Ending connection. Duration: {ConnectionLength}." +
                        " Total incoming: bytesReceived={TotalBytesReceived}, bytesProcessed={TotalBytesProcessed}, packetsReceived={TotalPacketsReceived}." +
                        " Remaining incoming bytes: {NumIncomingBytes}.",
                        connectionLength,
                        _totalBytesReceived, _totalBytesProcessed, _totalPacketsReceived,
                        _incomingBuffer.Count);
                }
            }

            // Unsubscribe from session (if subscribed)
            // \todo [nuutti] Should we under some conditions tell the session that it is OK to shutdown immediately?
            //                When we don't expect the user to come back and resume the session?
            if (_sessionSubscription != null)
            {
                await UnsubscribeFromAsync(_sessionSubscription);
                _sessionSubscription = null;
            }

            // Clear sessionId
            _sessionId = EntityId.None;
            _loginPlayerId = EntityId.None;

            // Shutdown entity
            // \todo [nuutti] Is there ever an actual need for ClientConnection to request a shutdown
            //                from the shard, instead of doing Context.Stop(Self) directly?
            //                Currently shard logs a warning if an entity terminates unexpectedly.
            //                EntityShard should be configurable to not warn about that.
            RequestShutdown();

            // Change to stopping status so we know to ignore any further messages coming from client
            _status = Status.StoppingGracefully;
        }

        /// <summary>
        /// Try to peek the message type code from a packet.
        /// <paramref name="buffer"/> is the incoming buffer without the header bytes.
        /// </summary>
        static string TryPeekMessageTypeName(WirePacketHeader header, ByteString buffer)
        {
            if (header.Type != WirePacketType.Message)
                return null;

            if (header.Compression != WirePacketCompression.None)
                return null;

            const int NumBytesNeeded = 1 + 5; // 1 byte for WireDataType, plus at most 5 bytes for message type code VarInt

            if (buffer.Count < NumBytesNeeded)
                return null;

            byte[] payloadPrefix = buffer.Slice(index: 0, count: NumBytesNeeded).ToArray();

            // \note Shouldn't throw, but be defensive.
            try
            {
                return MetaSerializationUtil.PeekMessageName(payloadPrefix);
            }
            catch (Exception ex)
            {
                return $"<error: {ex.Message}>";
            }
        }

        /// <summary>
        /// Awaits for a potentially long running task. If task is taking a long time, sends
        /// keep-alive messages to the client to keep it informed of the forward-progress.
        /// </summary>
        async Task<T> WaitLongRunningOperationAsync<T>(Task<T> longRunningTask)
        {
            TimeSpan initialDelay = TimeSpan.FromSeconds(1);
            TimeSpan period = TimeSpan.FromSeconds(5);

            try
            {
                return await longRunningTask.WaitAsync(initialDelay);
            }
            catch (TimeoutException)
            {
            }

            // \note: check IsCompleted manually to avoid infinite loop if longRunningTask throws TimeoutException too.
            while (!longRunningTask.IsCompleted)
            {
                SendToClient(Handshake.OperationStillOngoing.Instance);

                try
                {
                    return await longRunningTask.WaitAsync(period);
                }
                catch (TimeoutException)
                {
                }
            }

            // \note: `longRunningTask` is completed, but we `await` it unwrap any possible AggregateException
            return await longRunningTask;
        }

        protected abstract void CloseSocket();
        protected abstract void WriteBytesToSocket(ByteString data);
    }

    public class TcpClientConnection : ClientConnection
    {
        readonly IActorRef _socket;

        /// <inheritdoc />
        public TcpClientConnection(EntityId entityId, IActorRef socket, IPAddress srcAddress, int localPort) : base(entityId, srcAddress, localPort)
        {
            _socket = socket;

            // Finish TCP binding
            _socket.Tell(new Tcp.Register(_self));
        }

        protected override void CloseSocket()
        {
            _socket?.Tell(Tcp.Close.Instance);
        }

        protected override void WriteBytesToSocket(ByteString data)
        {
            _socket.Tell(Tcp.Write.Create(data));
        }

        [CommandHandler]
        public async Task HandleTcpReceived(Tcp.Received received)
        {
            await OnReceiveSocketData(received.Data);
        }

        [CommandHandler]
        public async Task HandleTcpClosed(Tcp.Closed closed)
        {
            if (_sessionId.IsValid)
                _log.Debug("Connection closed: {Cause}", closed.Cause);

            await GracefulStopAsync().ConfigureAwait(false);
        }

        [CommandHandler]
        public async Task HandleTcpPeerClosed(Tcp.PeerClosed closed)
        {
            if (_sessionId.IsValid)
                _log.Debug("Peer closed connection: {Cause}", closed.Cause);

            OnPeerClosedConnection();
            await GracefulStopAsync().ConfigureAwait(false);
        }

        [CommandHandler]
        public async Task HandleTcpErrorClosed(Tcp.ErrorClosed closed)
        {
            if (_sessionId.IsValid)
                _log.Debug("Connection closed due to error: {Cause}", closed.Cause);

            OnPeerClosedConnection();
            await GracefulStopAsync().ConfigureAwait(false);
        }

        [CommandHandler]
        public async Task HandleTerminated(Terminated terminated)
        {
            if (ActorUtil.Equals(terminated.ActorRef, _socket))
            {
                _log.Warning("Lost connection unexpectedly!");
                await GracefulStopAsync().ConfigureAwait(false);
            }
            else
                _log.Warning("Received Terminated for unknown {ActorRef} (address={Address}, confirmed={ExistenceConfirmed})", terminated.ActorRef, terminated.AddressTerminated, terminated.ExistenceConfirmed);
        }
    }
}
