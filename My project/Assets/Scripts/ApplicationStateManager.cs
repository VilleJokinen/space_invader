// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#define UNITY_WEBGL_BUILD
#endif

using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Unity;
using Metaplay.Unity.DefaultIntegration;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// This file contains Metaplay sample code. It can be adapted to suit your project's needs or you can
// replace the functionality completely with your own.
namespace Metaplay.Sample
{
    /// <summary>
    /// Declare concrete MetaplayClient class for type-safe access to player context from game code.
    /// </summary>
    public class MetaplayClient : MetaplayClientBase<PlayerModel>
    {
    }

    /// <summary>
    /// Manages the application's lifecycle, including mock loading state, Metaplay server connectivity, and failure states.
    /// This class is a simplified version of a state manager that a real game would have, but in such a manner that the
    /// integration of Metaplay into such a state manager is exemplified.
    ///
    /// Also implements <see cref="IMetaplayLifecycleDelegate"/> to get callbacks from Metaplay on connectivity events and
    /// error states.
    /// </summary>
    public class ApplicationStateManager : MonoBehaviour, IMetaplayLifecycleDelegate
    {
        /// <summary>
        /// Represents the state of the application.
        /// </summary>
        public enum ApplicationState
        {
            /// <summary>
            /// Application is being started.
            /// </summary>
            AppStart,

            /// <summary>
            /// Connecting to the server. A real application would also load its assets here.
            /// </summary>
            Initializing,

            /// <summary>
            /// Session with server has been established and we're playing the game.
            /// </summary>
            Game,
        }

        // When connection to server is not established, display connection status.
        public GameObject       ConnectionStatusCanvas;     // Canvas that contains the connection status info. Shown only when no active connection exists.
        public Text             ConnectionStatusText;       // Text to display status of connection.
        public Text             ConnectingSpinner;          // Spinner in connecting state.
        public GameObject       ConnectionErrorPopup;       // Popup shown when a connection error happens.
        public Text             ConnectionErrorInfoText;    // Info text within ConnectionErrorPopup describing the error.
        public GameManager      GameManagerPrefab;          // Prefab for the in-game state, spawned when a session with the server is established.

        // Runtime state
        ApplicationState        _applicationState = ApplicationState.AppStart;  // Begin in the AppStart state.
        GameManager             _gameManager;                                   // Instance of the GameManager, spawned when the player state is received from the server.

        void Awake()
        {
            // When the app starts, make sure the connection error info is hidden.
            ConnectionErrorPopup.SetActive(false);
        }

        void Start()
        {
            // Initialize Metaplay SDK.
            MetaplayClient.Initialize(new MetaplayClientOptions
            {
                // Hook all the lifecycle and connectivity callbacks back to this class.
                LifecycleDelegate = this,
            });

            // Switch to initializing state, to start connecting to the server.
            SwitchToState(ApplicationState.Initializing);
        }

        void Update()
        {
            // Update Metaplay connections and game logic
            MetaplayClient.Update();

            // Update connection UI (visible when session is not active)
            UpdateConnectionStatusUI();
        }

        /// <summary>
        /// Switch the application's state and perform actions relevant to the state transition.
        /// </summary>
        /// <param name="newState"></param>
        void SwitchToState(ApplicationState newState)
        {
            Debug.Log($"Switching to state {newState} (from {_applicationState})");

            switch (newState)
            {
                case ApplicationState.AppStart:
                    // Cannot enter, app starts in this state.
                    break;

                case ApplicationState.Initializing:
                    // Simulate the transition away from the Game scene by destroying the GameManager instance.
                    // In addition to the Game scene, it's possible to arrive here from Initializing state itself,
                    // in case the connection fails before a session was started. In that case there is no
                    // GameManager instance.
                    if (_gameManager != null)
                    {
                        Destroy(_gameManager.gameObject);
                        _gameManager = null;
                    }

                    // Make sure connection error info is hidden.
                    ConnectionErrorPopup.SetActive(false);

                    // Start connecting to the server.
                    MetaplayClient.Connect();
                    break;

                case ApplicationState.Game:
                    // Make sure connection error info is hidden.
                    ConnectionErrorPopup.SetActive(false);

                    // Start the game. Simulate the transition to in-game state by spawning the GameManager.
                    // You might want to use scene transition instead.
                    _gameManager = Instantiate(GameManagerPrefab);
                    
                    MetaplayClient.PlayerModel.ClientListener = _gameManager;
                    break;

            }

            // Store the new state.
            _applicationState = newState;
        }

        /// <summary>
        /// When connection isn't yet established, show the status of connection.
        /// </summary>
        void UpdateConnectionStatusUI()
        {
            // Only show connectivity info if we don't have an established connection.
            ConnectionStatusCanvas.SetActive(MetaplayClient.PlayerContext == null);

            // Show connection status text & progress indicator.
            ConnectionStatus connectionStatus = MetaplayClient.Connection.State.Status;
            ConnectionStatusText.text = connectionStatus.ToString();
            ConnectingSpinner.gameObject.SetActive(connectionStatus == ConnectionStatus.Connecting);
            ConnectingSpinner.text = "........".Substring(0, (int)(Time.time * 3.0f) % 8);
        }

        /// <summary>
        /// Handler for Reconnect button (shown after a connection attempt has failed).
        /// </summary>
        public void OnClickReconnect()
        {
            // Switch back to initializing state, to start reconnecting.
            SwitchToState(ApplicationState.Initializing);
        }

        #region IMetaplayLifecycleDelegate

        /// <summary>
        /// A session has been successfully negotiated with the server. At this point, we also have the
        /// relevant state initialized on the client, so we can move on to the game state.
        /// </summary>
        void IMetaplayLifecycleDelegate.OnSessionStarted()
        {
            // Hook up to updates in PlayerModel.

            // Switch to the in-game state.
            SwitchToState(ApplicationState.Game);

            // At this point, the player state is available. For example, the following are now valid:
            // Access player state members: MetaplayClient.PlayerModel.CurrentTime
            // Execute player actions: MetaplayClient.PlayerContext.ExecuteAction(..);
        }

        /// <summary>
        /// The current logical session has been lost and can no longer be resumed. This can happen for multiple
        /// reasons, for example, if the network connection is dropped for a sufficient long time, or if the
        /// application has been in the background for a long time, or if the server is in a maintenance mode.
        ///
        /// The application should react to this by showing a 'Connection Lost' dialog and present the player
        /// with a 'Reconnect' button.
        /// For some types of errors, it may be appropriate to omit the error popup, and auto-reconnect instead.
        /// </summary>
        /// <param name="connectionLost">Information about why the session loss happened.</param>
        void IMetaplayLifecycleDelegate.OnSessionLost(ConnectionLostEvent connectionLost)
        {
            if (connectionLost.AutoReconnectRecommended)
            {
                // For certain errors, we auto-reconnect straight away without
                // prompting the player. Note that AutoReconnectRecommended is
                // just a suggestion by the SDK and is based on the type of the
                // error. The game does not have to obey the suggestion.
                SwitchToState(ApplicationState.Initializing);
            }
            else
            {
                // Otherwise, show the connection error popup, with info text
                // and a reconnect button.
                // Despite losing the session, the game scene will linger until
                // the player clicks on the reconnect button.
                // MetaplayClient.PlayerModel is still available so that the
                // game scene can continue to access it. It will remain available
                // until the reconnection starts.
                ShowConnectionErrorPopup(connectionLost);
            }
        }

        /// <summary>
        /// Metaplay failed to establish a session with the server. Show the connection error and 'Reconnect'
        /// button so the player can try again.
        /// </summary>
        /// <param name="connectionLost">Information about why the failure happened.</param>
        void IMetaplayLifecycleDelegate.OnFailedToStartSession(ConnectionLostEvent connectionLost)
        {
            // Show the connection error popup, with info text and a reconnect button.
            // Note that we're not in the game scene since the error occurred before
            // the session was started. Furthermore, MetaplayClient.PlayerModel is
            // unavailable.
            ShowConnectionErrorPopup(connectionLost);
        }

        /// <summary>
        /// Show a popup with the details of a connection/session error,
        /// and a reconnect button.
        /// </summary>
        /// <param name="connectionLost"></param>
        void ShowConnectionErrorPopup(ConnectionLostEvent connectionLost)
        {
            ConnectionErrorInfoText.text = CreateConnectionLostInfoText(connectionLost);
            ConnectionErrorPopup.SetActive(true);
        }

        /// <summary>
        /// Convert a <see cref="ConnectionLostEvent"/> into a human-readable string, for displaying in the UI.
        /// This implementation shows quite a lot of technical detail which is useful for developers, but for
        /// real players, you'd want to show something more compact.
        /// </summary>
        /// <param name="connectionLost"></param>
        /// <returns>Technical description of the connection loss event, mainly intended for developers.</returns>
        static string CreateConnectionLostInfoText(ConnectionLostEvent connectionLost)
        {
            StringBuilder info = new StringBuilder();

            // EnglishLocalizedReason and TechnicalErrorCode should typically be shown to players.
            info.AppendLine($"* Reason: {connectionLost.EnglishLocalizedReason}");
            info.AppendLine($"* Technical code: {connectionLost.TechnicalErrorCode}");

            // TechnicalErrorString and ExtraTechnicalInfo are intended for analytics.
            info.AppendLine();
            info.AppendLine($"* Technical error string: {connectionLost.TechnicalErrorString}");
            if (!string.IsNullOrEmpty(connectionLost.ExtraTechnicalInfo))
                info.AppendLine($"* Additional technical info: {connectionLost.ExtraTechnicalInfo}");

            // More detailed technical info that's mainly useful for developers.
            info.AppendLine();
            info.AppendLine($"* Technical error: {PrettyPrint.Compact(connectionLost.TechnicalError)}");

            return info.ToString();
        }

        #endregion // IMetaplayLifecycleDelegate
    }
}
