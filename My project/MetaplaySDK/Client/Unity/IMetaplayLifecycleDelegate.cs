// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Analytics;
using Metaplay.Core.Client;
using Metaplay.Unity.DefaultIntegration;

namespace Metaplay.Unity
{
    public interface IMetaplayLifecycleDelegate
    {
        /// <summary>
        /// A game session has been successfully established with the backend.
        /// <see cref="MetaplayClient.PlayerContext"/> and <see cref="MetaplayClient.PlayerModel"/>
        /// are available to the application.
        /// The callee can now move into a game scene.
        /// </summary>
        void                    OnSessionStarted        ();
        /// <summary>
        /// Game session was terminated (e.g. due to connection error).
        ///
        /// <see cref="MetaplayClient.PlayerContext"/> and <see cref="MetaplayClient.PlayerModel"/>
        /// will still be available, so that the game can continue to visualize the game scene
        /// based on them. Since there is no connection to the server, PlayerModel won't receive
        /// updates from the server, nor will client-initiated updates be persisted to the server.
        ///
        /// The callee can visualize the error based on <paramref name="connectionLost"/>.
        /// </summary>
        void                    OnSessionLost           (ConnectionLostEvent connectionLost);
        /// <summary>
        /// Game session could not be established.
        ///
        /// <see cref="MetaplayClient.PlayerContext"/> and <see cref="MetaplayClient.PlayerModel"/>
        /// are unavailable.
        ///
        /// The callee can visualize the error based on <paramref name="connectionLost"/>.
        /// </summary>
        void                    OnFailedToStartSession  (ConnectionLostEvent connectionLost);
    }

    /// <summary>
    /// Describes the termination of a Metaplay connection.
    /// This type is used regardless of how far along the connection
    /// was when it ended: it may have not yet been connected to the
    /// the server, or the game session may have already been started,
    /// or something inbetween.
    /// </summary>
    /// <remarks>
    /// This is not used for transient connection errors where
    /// a new connection was transparently created by the Metaplay SDK.
    /// </remarks>
    public class ConnectionLostEvent
    {
        /// <summary>
        /// Rough reason for connection loss.
        /// This can be used to select an appropriate player-facing error message.
        /// </summary>
        public ConnectionLostReason     Reason              { get; }

        /// <summary>
        /// An English-language player-facing text based on <see cref="Reason"/>.
        /// Stopgap until the SDK comes with proper translations for multiple languages.
        /// </summary>
        public string                   EnglishLocalizedReason => GetEnglishLocalizedReasonText(Reason);

        /// <summary>
        /// Code specifying the error in more detail.
        /// Defined in <see cref="MetaplayClientConnectionErrors"/>.
        /// For the player, this is opaque in that it is not intended
        /// to be directly useful for them, but may be useful for
        /// diagnosing a problem if the player reports the code when
        /// contacting customer support.
        ///
        /// For example: 2400
        /// </summary>
        public int                      TechnicalErrorCode { get; }

        /// <summary>
        /// An identifier specifying the error in more detail,
        /// intended for analytics. This is human-readable
        /// but still technical, and corresponds to
        /// <see cref="TechnicalErrorCode"/>.
        ///
        /// For example: logic_version_client_too_old
        /// </summary>
        public string                   TechnicalErrorString { get; }

        /// <summary>
        /// Optional info augmenting <see cref="TechnicalErrorString"/>,
        /// intended for analytics.
        /// Contains additional dynamic parameters for certain
        /// kinds of errors, such as version numbers in case of
        /// a client-server version mismatch.
        ///
        /// For example: client_5_to_6_server_7_to_8
        /// </summary>
        public string                   ExtraTechnicalInfo { get; }

        /// <summary>
        /// Whether auto-reconnect is likely appropriate for this
        /// connection error. For example, this is true when the
        /// session was terminated due to the app being in background
        /// for too long, and false when the connection was lost
        /// because the server could not be reached.
        ///
        /// This is simply a suggestion for the game. The game can
        /// choose to ignore this, and/or can base its reactions also
        /// on other information, such as <see cref="Reason"/>.
        /// </summary>
        public bool                     AutoReconnectRecommended { get; }

        /// <summary>
        /// The <see cref="ConnectionState"/>, that caused this
        /// connection loss. Provides technical details about the
        /// error, useful for debugging.
        /// </summary>
        public ConnectionState          TechnicalError { get; }

        /// <summary>
        /// Technical statistics concerning the connection at the time of the error.
        /// </summary>
        public ConnectionStatistics.CurrentConnectionStats ConnectionStats { get; }

        /// <summary>
        /// Analytics event describing this connection failure.
        /// </summary>
        public ClientEventBase          AnalyticsEvent => new ClientEventConnectionFailure(
            technicalErrorString: TechnicalErrorString,
            extraTechnicalInfo: ExtraTechnicalInfo,
            technicalErrorCode: TechnicalErrorCode,
            playerFacingReason: Reason.ToString());

        public ConnectionLostEvent(
            MetaplayClientErrorInfo errorInfo,
            string extraTechnicalInfo,
            bool autoReconnectRecommended,
            ConnectionState technicalError,
            ConnectionStatistics.CurrentConnectionStats connectionStats)
        {
            Reason = errorInfo.Reason;
            TechnicalErrorCode = errorInfo.TechnicalCode;
            TechnicalErrorString = errorInfo.TechnicalString ?? "missing";
            ExtraTechnicalInfo = extraTechnicalInfo ?? "";
            AutoReconnectRecommended = autoReconnectRecommended;
            TechnicalError = technicalError ?? new ConnectionStates.TerminalError.Unknown();
            ConnectionStats = connectionStats;
        }

        /// <summary>
        /// Helper constructor without the extraTechnicalInfo parameter.
        /// </summary>
        public ConnectionLostEvent(
            MetaplayClientErrorInfo errorInfo,
            bool autoReconnectRecommended,
            ConnectionState technicalError,
            ConnectionStatistics.CurrentConnectionStats connectionStats)
            : this(
                  errorInfo: errorInfo,
                  extraTechnicalInfo: "",
                  autoReconnectRecommended: autoReconnectRecommended,
                  technicalError: technicalError,
                  connectionStats: connectionStats)
        {
        }

        string GetEnglishLocalizedReasonText(ConnectionLostReason reason)
        {
            switch (reason)
            {
                case ConnectionLostReason.CouldNotConnect:
                    return "Failed to connect to the server.";

                case ConnectionLostReason.NoInternetConnection:
                    return "No internet connection detected.";

                case ConnectionLostReason.ConnectionLost:
                    return "Connection lost.";

                case ConnectionLostReason.ServerMaintenance:
                    return "The server is under maintenance. Please reconnect a bit later.";

                case ConnectionLostReason.ClientVersionTooOld:
                    return "The server has been updated. Please update your client.";

                case ConnectionLostReason.DeviceLocalStorageError:
                    return "Cannot write to the device's storage. Please check that there's space remaining and write permission is enabled.";

                case ConnectionLostReason.PlayerIsBanned:
                    return "This player account is banned.";

                case ConnectionLostReason.InternalError:
                default:
                    return "An internal error occurred.";
            }
        }
    }

    public enum ConnectionLostReason
    {
        /// <summary>
        /// Failure or timeout when attempting to connect to server or download game
        /// resources (like game configs).
        /// </summary>
        CouldNotConnect,

        /// <summary>
        /// Like <see cref="CouldNotConnect"/>, and additionally heuristics indicate
        /// the device might not have internet connectivity at the moment.
        /// </summary>
        NoInternetConnection,

        /// <summary>
        /// Connection to backend was lost after it had previously been established.
        /// This can be caused by a number of reasons, from actual connectivity issues
        /// to the session being terminated due to an admin action.
        /// </summary>
        ConnectionLost,

        /// <summary>
        /// Server is in maintenance, or otherwise refuses to accept the game client at
        /// the moment. <see cref="MetaplaySDK.MaintenanceMode"/> contains information
        /// if it's a known scheduled maintenance break.
        /// </summary>
        ServerMaintenance,

        /// <summary>
        /// Client is older than supported by the server, and needs to be updated.
        /// </summary>
        ClientVersionTooOld,

        /// <summary>
        /// Failed to persist crucial data (account credentials) to the device's
        /// persistent storage.
        /// </summary>
        DeviceLocalStorageError,

        /// <summary>
        /// Player is banned.
        /// </summary>
        PlayerIsBanned,

        /// <summary>
        /// An internal error occurred in the server, client, or the system as a whole.
        /// Likely a bug in the SDK or the game that the player cannot directly do
        /// anything about, other than try again or contact customer support.
        /// </summary>
        InternalError,
    }
}
