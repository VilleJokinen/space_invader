// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Session;
using Metaplay.Core.TypeCodes;
using System;

namespace Metaplay.Core.Message
{
    /// <summary>
    /// Handshake messages.
    /// </summary>
    public static class Handshake
    {
        /// <summary>
        /// Initial message sent by the server to the client.
        ///
        /// This message is preceded by the protocol header bytes.
        /// </summary>
        [MetaMessage(MessageCodesCore.ServerHello, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class ServerHello : MetaMessage
        {
            [MetaMember(1)] public string       ServerVersion       { get; private set; }   // Version string of the server (mainly for informational purposes)
            [MetaMember(2)] public string       BuildNumber         { get; private set; }   // Build number of the server (mainly for informational purposes)
            [MetaMember(3)] public uint         FullProtocolHash    { get; private set; }   // Hash of the network protocol (includes all members/types of public messages, actions, and models)
            [MetaMember(4)] public string       CommitId            { get; private set; }   // Commit hash of the server (for compatibility checks)

            ServerHello() { }
            public ServerHello(string serverVersion, string buildNumber, uint fullProtocolHash, string commitId)
            {
                ServerVersion       = serverVersion;
                BuildNumber         = buildNumber;
                FullProtocolHash    = fullProtocolHash;
                CommitId            = commitId;
            }
        }

        /// <summary>
        /// Initial message sent by the client to the server.
        /// </summary>
        [MetaMessage(MessageCodesCore.ClientHello, MessageDirection.ClientToServer, hasExplicitMembers: true), MessageRoutingRuleProtocol]
        public class ClientHello : MetaMessage
        {
            [MetaMember(1)] public string               ClientVersion               { get; private set; }
            [MetaMember(2)] public string               BuildNumber                 { get; private set; }
            [MetaMember(3)] public MetaVersionRange     SupportedLogicVersions      { get; private set; }
            [MetaMember(4)] public uint                 FullProtocolHash            { get; private set; }
            [MetaMember(5)] public string               CommitId                    { get; private set; }
            [MetaMember(6)] public MetaTime             Timestamp                   { get; private set; }
            [MetaMember(7)] public uint                 AppLaunchId                 { get; private set; }
            [MetaMember(8)] public uint                 ClientSessionNonce          { get; private set; }
            [MetaMember(9)] public uint                 ClientSessionConnectionNdx  { get; private set; }
            [MetaMember(10)] public ClientPlatform      Platform                    { get; private set; }
            [MetaMember(11)] public int                 LoginProtocolVersion        { get; private set; }

            ClientHello() { }
            public ClientHello(string clientVersion, string buildNumber, MetaVersionRange supportedLogicVersions, uint fullProtocolHash, string commitId, MetaTime timestamp, uint appLaunchId, uint clientSessionNonce, uint clientSessionConnectionNdx, ClientPlatform platform, int loginProtocolVersion)
            {
                ClientVersion               = clientVersion;
                BuildNumber                 = buildNumber;
                SupportedLogicVersions      = supportedLogicVersions ?? throw new ArgumentNullException(nameof(supportedLogicVersions));
                FullProtocolHash            = fullProtocolHash;
                CommitId                    = commitId;
                Timestamp                   = timestamp;
                ClientSessionConnectionNdx  = clientSessionConnectionNdx;
                ClientSessionNonce          = clientSessionNonce;
                AppLaunchId                 = appLaunchId;
                Platform                    = platform;
                LoginProtocolVersion        = loginProtocolVersion;
            }
        }

        /// <summary>
        /// Client abandons a connection intentionally.
        /// </summary>
        [MetaMessage(MessageCodesCore.ClientAbandon, MessageDirection.ClientToServer, hasExplicitMembers: true), MessageRoutingRuleProtocol]
        public class ClientAbandon : MetaMessage
        {
            [MetaSerializable]
            public enum AbandonSource
            {
                PrimaryConnection = 0,
                NetworkProbe = 1,
            }

            [MetaMember(1)] public MetaTime         ConnectionStartedAt     { get; private set; }
            [MetaMember(2)] public MetaTime         ConnectionAbandonedAt   { get; private set; }
            [MetaMember(3)] public MetaTime         AbandonedCompletedAt    { get; private set; }
            [MetaMember(4)] public AbandonSource    Source                  { get; private set; }

            ClientAbandon() { }
            public ClientAbandon(MetaTime connectionStartedAt, MetaTime connectionAbandonedAt, MetaTime abandonedCompletedAt, AbandonSource source)
            {
                ConnectionStartedAt = connectionStartedAt;
                ConnectionAbandonedAt = connectionAbandonedAt;
                AbandonedCompletedAt = abandonedCompletedAt;
                Source = source;
            }
        }

        /// <summary>
        /// Client should redirect to the specified server. This is mainly intended to be used for handling
        /// "clients from the future", i.e., clients that are yet to be published, but still need to connect
        /// to production server, which doesn't yet support them.
        ///
        /// For example, when submitting a new build to App Store review, it needs to have the production server
        /// endpoint configured, but the production server doesn't necessarily know how to handle the upcoming
        /// client version. In such cases, the new client can be redirected to connect to another server which
        /// supports the upcoming client version.
        /// </summary>
        [MetaMessage(MessageCodesCore.RedirectToServer, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class RedirectToServer : MetaMessage
        {
            [MetaMember(1)] public ServerEndpoint   RedirectToEndpoint  { get; private set; }

            RedirectToServer() { }
            public RedirectToServer(ServerEndpoint redirectToEndpoint) { RedirectToEndpoint = redirectToEndpoint; }
        }

        /// <summary>
        /// The client's <see cref="MetaplayCoreOptions.SupportedLogicVersions"/> is not compatible with server.
        /// In case the client is too old, the client must be updated. In that case, the user should be
        /// directed to the relevant app store or distribution channel to get an update.
        ///
        /// It is also possible for the client LogicVersion to be too high. This generally happens if
        /// the client is newer than the server and the server-side future client redirect has not been
        /// configured properly.
        ///
        /// Note: <see cref="ServerAcceptedLogicVersions"/> may only contain the ActiveLogicVersion as
        /// both min and max. This happens if server only supports a single LogicVersion at a time, to
        /// keep reasoning about versions simple on the server.
        /// </summary>
        [MetaMessage(MessageCodesCore.LogicVersionMismatch, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class LogicVersionMismatch : MetaMessage
        {
            [MetaMember(1)] public MetaVersionRange     ServerAcceptedLogicVersions { get; private set; }

            LogicVersionMismatch() { }
            public LogicVersionMismatch(MetaVersionRange serverAcceptedLogicVersions)
            {
                ServerAcceptedLogicVersions = serverAcceptedLogicVersions;
            }
        }

        /// <summary>
        /// Client's login protocol version is not compatible with the server.
        /// </summary>
        [MetaMessage(MessageCodesCore.LoginProtocolVersionMismatch, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class LoginProtocolVersionMismatch : MetaMessage
        {
            [MetaMember(1)] public int ServerAcceptedProtocolVersion { get; private set; }

            LoginProtocolVersionMismatch() { }
            public LoginProtocolVersionMismatch(int serverAcceptedProtocolVersion)
            {
                ServerAcceptedProtocolVersion = serverAcceptedProtocolVersion;
            }
        }

        /// <summary>
        /// Client requests creation of a new guest account.
        /// </summary>
        [MetaMessage(MessageCodesCore.CreateGuestAccountRequest, MessageDirection.ClientToServer, hasExplicitMembers: true), MessageRoutingRuleProtocol]
        public class CreateGuestAccountRequest : MetaMessage
        {
            public CreateGuestAccountRequest() { }
        }

        /// <summary>
        /// Server returns the credentials for the newly created guest account.
        /// </summary>
        [MetaMessage(MessageCodesCore.CreateGuestAccountResponse, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class CreateGuestAccountResponse : MetaMessage
        {
            [MetaMember(1)] public EntityId             PlayerId            { get; private set; }
            [MetaMember(2)] public string               DeviceId            { get; private set; }
            [Sensitive]
            [MetaMember(3)] public string               AuthToken           { get; private set; }

            CreateGuestAccountResponse() { }
            public CreateGuestAccountResponse(EntityId playerId, string deviceId, string authToken)
            {
                PlayerId = playerId;
                DeviceId = deviceId;
                AuthToken = authToken;
            }
        }

        /// <summary>
        /// Game-specific data from client, used for login.
        /// </summary>
        [MetaSerializable]
        public interface ILoginRequestGamePayload
        {
        }

        /// <summary>
        /// Client requests login. Login credentials are defined in subclasses.
        /// </summary>
        public abstract class LoginRequest : MetaMessage
        {
            [MetaMember(3)] public EntityId                     PlayerIdHint        { get; private set; }   // Client's speculated PlayerId
            [MetaMember(4)] public bool                         IsBot               { get; private set; }   // Is the client a bot?
            [MetaMember(5)] public LoginDebugDiagnostics        DebugDiagnostics    { get; private set; }
            [MetaMember(6)] public ILoginRequestGamePayload     GamePayload         { get; private set; }

            protected LoginRequest() { }
            public LoginRequest(EntityId playerIdHint, bool isBot, LoginDebugDiagnostics debugDiagnostics, ILoginRequestGamePayload gamePayload)
            {
                PlayerIdHint = playerIdHint;
                IsBot = isBot;
                DebugDiagnostics = debugDiagnostics;
                GamePayload = gamePayload;
            }
        }

        /// <summary>
        /// Client requests login. Login is done with DeviceId/AuthToken credentials. These credentials
        /// are automatically generated for each device. They roughly correspond to username/password
        /// combination.
        /// </summary>
        [MetaMessage(MessageCodesCore.DeviceLoginRequest, MessageDirection.ClientToServer, hasExplicitMembers: true), MessageRoutingRuleProtocol]
        public sealed class DeviceLoginRequest : LoginRequest
        {
            [MetaMember(1)] public string   DeviceId    { get; private set; }
            [Sensitive]
            [MetaMember(2)] public string   AuthToken   { get; private set; }

            DeviceLoginRequest() { }
            public DeviceLoginRequest(string deviceId, string authToken, EntityId playerIdHint, bool isBot, LoginDebugDiagnostics debugDiagnostics, ILoginRequestGamePayload gamePayload)
                : base(playerIdHint, isBot, debugDiagnostics, gamePayload)
            {
                DeviceId = deviceId;
                AuthToken = authToken;
            }
        }

        /// <summary>
        /// Client requests login. Login is done with Social Platform credentials.
        /// </summary>
        [MetaMessage(MessageCodesCore.SocialAuthenticationLoginRequest, MessageDirection.ClientToServer, hasExplicitMembers: true), MessageRoutingRuleProtocol]
        public sealed class SocialAuthenticationLoginRequest : LoginRequest
        {
            [MetaMember(100)] public SocialAuthenticationClaimBase Claim { get; set; }

            SocialAuthenticationLoginRequest() { }
            public SocialAuthenticationLoginRequest(SocialAuthenticationClaimBase claim, EntityId playerIdHint, bool isBot, LoginDebugDiagnostics debugDiagnostics, ILoginRequestGamePayload gamePayload)
                : base(playerIdHint, isBot, debugDiagnostics, gamePayload)
            {
                Claim = claim;
            }
        }

        /// <summary>
        /// Client requests login. Login is done with Social Platform credentials and deviceID.
        /// </summary>
        [MetaMessage(MessageCodesCore.DualSocialAuthenticationLoginRequest, MessageDirection.ClientToServer, hasExplicitMembers: true), MessageRoutingRuleProtocol]
        public sealed class DualSocialAuthenticationLoginRequest : LoginRequest
        {
            [MetaMember(100)] public SocialAuthenticationClaimBase  Claim       { get; set; }
            [MetaMember(101)] public string                         DeviceId    { get; private set; }
            [Sensitive]
            [MetaMember(102)] public string                         AuthToken   { get; private set; }

            DualSocialAuthenticationLoginRequest() { }
            public DualSocialAuthenticationLoginRequest(EntityId playerIdHint, bool isBot, LoginDebugDiagnostics debugDiagnostics, ILoginRequestGamePayload gamePayload, SocialAuthenticationClaimBase claim, string deviceId, string authToken)
                : base(playerIdHint, isBot, debugDiagnostics, gamePayload)
            {
                Claim = claim;
                DeviceId = deviceId;
                AuthToken = authToken;
            }
        }

        /// <summary>
        /// Per-connection options set by the server.
        /// </summary>
        [MetaSerializable]
        public struct ServerOptions
        {
            /// <summary>
            /// Percentage likelihood to push-upload a given SessionStartFailed incident when the client fails to start a session
            /// due to an incident. Push uploads are initiated by client and are used when normal pull methods are unsuitable, such
            /// as when repeated session start error prevents normal delivery from working.
            /// </summary>
            [MetaMember(1)] public int PushUploadPercentageSessionStartFailedIncidentReport;

            /// <summary>
            /// Whether the client should compress large packets sent to the server.
            /// </summary>
            /// <remarks>
            /// Regardless of this flag, the client should always accept compressed packets sent by the server.
            /// </remarks>
            [MetaMember(2)] public bool EnableWireCompression;

            /// <summary>
            /// Safety delay from user's account deletion request to the account being marked eligible for deletion. During
            /// this time the user may cancel the account deletion.
            /// </summary>
            [MetaMember(3)] public MetaDuration DeletionRequestSafetyDelay;

            /// <summary>
            /// OAuth2 ClientId of the Game Server. This is the Audience for Google Play authentication code request.
            /// </summary>
            [MetaMember(4)] public string GameServerGooglePlaySignInOAuthClientId;

            /// <summary>
            /// URL for the current ImmutableX environment Link Api, or <c>null</c> if ImmutableX is not enabled.
            /// </summary>
            [MetaMember(5)] public string ImmutableXLinkApiUrl;

            /// <summary>
            /// The current game environment of the server. Essentially, is this production or non-production server.
            /// </summary>
            [MetaMember(6)] public string GameEnvironment;

            public ServerOptions(int pushUploadPercentageSessionStartFailedIncidentReport, bool enableWireCompression, MetaDuration deletionRequestSafetyDelay, string gameServerGooglePlaySignInOAuthClientId, string immutableXLinkApiUrl, string gameEnvironment)
            {
                PushUploadPercentageSessionStartFailedIncidentReport = pushUploadPercentageSessionStartFailedIncidentReport;
                EnableWireCompression = enableWireCompression;
                DeletionRequestSafetyDelay = deletionRequestSafetyDelay;
                GameServerGooglePlaySignInOAuthClientId = gameServerGooglePlaySignInOAuthClientId;
                ImmutableXLinkApiUrl = immutableXLinkApiUrl;
                GameEnvironment = gameEnvironment;
            }
        }

        /// <summary>
        /// Server replies to an acceptable (i.e. not redirected or wrong version) ClientHello.
        /// </summary>
        [MetaMessage(MessageCodesCore.ClientHelloAccepted, MessageDirection.ServerToClient)]
        public class ClientHelloAccepted : MetaMessage
        {
            [MetaMember(1)] public ServerOptions ServerOptions { get; private set; }

            ClientHelloAccepted() { }
            public ClientHelloAccepted(ServerOptions serverOptions)
            {
                ServerOptions = serverOptions;
            }
        }

        /// <summary>
        /// Server replies to successful login request.
        /// </summary>
        [MetaMessage(MessageCodesCore.LoginSuccessResponse, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class LoginSuccessResponse : MetaMessage
        {
            [MetaMember(1)] public EntityId LoggedInPlayerId { get; private set; }

            LoginSuccessResponse() { }
            public LoginSuccessResponse(EntityId loggedInPlayerId)
            {
                LoggedInPlayerId = loggedInPlayerId;
            }
        }

        /// <summary>
        /// Server sends to client at any point before LoginSuccessResponse.
        /// </summary>
        [MetaMessage(MessageCodesCore.OngoingMaintenance, MessageDirection.ServerToClient, hasExplicitMembers: true)]
        public class OngoingMaintenance : MetaMessage
        {
            public static OngoingMaintenance Instance { get; } = new OngoingMaintenance();
            OngoingMaintenance() { }
        }

        /// <summary>
        /// Server informs client that the server is still processing the request.
        /// </summary>
        [MetaMessage(MessageCodesCore.OperationStillOngoing, MessageDirection.ServerToClient)]
        public class OperationStillOngoing : MetaMessage
        {
            public static OperationStillOngoing Instance { get; } = new OperationStillOngoing();

            OperationStillOngoing() { }
        }

        /// <summary>
        /// Request to login and resume an existing session.
        /// </summary>
        [MetaMessage(MessageCodesCore.LoginAndResumeSessionRequest, MessageDirection.ClientToServer, hasExplicitMembers: true), MessageRoutingRuleProtocol]
        public class LoginAndResumeSessionRequest : MetaMessage
        {
            [MetaMember(1)] public EntityId                 ClaimedPlayerId     { get; private set; }
            [MetaMember(2)] public SessionResumptionInfo    SessionToResume     { get; private set; }
            [Sensitive]
            [MetaMember(3)] public byte[]                   ResumptionToken     { get; private set; }
            [MetaMember(4)] public LoginDebugDiagnostics    DebugDiagnostics    { get; private set; }
            [MetaMember(5)] public ILoginRequestGamePayload GamePayload         { get; private set; }

            LoginAndResumeSessionRequest() { }
            public LoginAndResumeSessionRequest(EntityId claimedPlayerId, SessionResumptionInfo sessionToResume, byte[] resumptionToken, LoginDebugDiagnostics debugDiagnostics, ILoginRequestGamePayload gamePayload)
            {
                ClaimedPlayerId = claimedPlayerId;
                SessionToResume = sessionToResume;
                ResumptionToken = resumptionToken;
                DebugDiagnostics = debugDiagnostics;
                GamePayload = gamePayload;
            }
        }
    }
}
