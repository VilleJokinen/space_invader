// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using UnityEngine;
using System.Collections;
using System;
using Metaplay.Core;
using Metaplay.Core.Network;
using Metaplay.Core.Message;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Metaplay.Unity
{
    class MetaplayInitializationError : Exception
    {
        public MetaplayInitializationError(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Hooks MetaplaySDK into Unity.
    /// </summary>
    public class MetaplaySDKBehavior : MonoBehaviour
    {
        internal static MetaplaySDKBehavior Instance = null;
        private bool _wasAutoCreated;
        private bool _isDuplicate;

#if UNITY_EDITOR

        // Link quality simulator
        public enum LinkQualitySetting
        {
            Perfect = 0,
            Spotty = 1,
            NoReplies = 2,
            AllRejected = 3,
        }
        public LinkQualitySetting _simulatedLinkQuality;
        FaultInjectingMessageTransport _currentTransport;
        MetaTime _nextSpottyLinkUpdate;
        bool _spottyLinkIsHalted;

        // Spoofing of client platform as reported to server
        public ClientPlatform _appearAsPlatform = ClientPlatform.UnityEditor;
#endif

        private void Awake()
        {
            if (Instance != null)
            {
                MetaplaySDK.Logs.Metaplay.Info("MetaplaySDKBehavior already exists in the scene. Removing the new one (this one).");
                Destroy(this);
                _isDuplicate = true;
                return;
            }

            GameObject.DontDestroyOnLoad(this.gameObject);
            Instance = this;
        }

        private void OnDestroy()
        {
            if (_isDuplicate)
                return;

            if (Instance == this)
                Instance = null;
        }

        private void Start()
        {
            if (_isDuplicate)
                return;

            StartCoroutine(EndOfTheFrameUpdater());

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        private void OnApplicationQuit()
        {
            if (_isDuplicate)
                return;

            MetaplaySDK.OnApplicationQuit();
        }

        private void OnApplicationPause(bool pause)
        {
            if (_isDuplicate)
                return;

            MetaplaySDK.OnApplicationPause(pause);
        }

        internal static void EnsureExists(bool autoCreateMetaplaySDKBehavior)
        {
            if (autoCreateMetaplaySDKBehavior)
            {
                if (Instance == null)
                {
                    GameObject go = new GameObject("MetaplaySDKBehavior");
                    MetaplaySDKBehavior sdk = go.AddComponent<MetaplaySDKBehavior>();

                    sdk._wasAutoCreated = true;

                    Instance = sdk;
                }
                else
                {
                    if (!Instance._wasAutoCreated)
                        throw new MetaplayInitializationError("Manually placed MetaplaySDKBehavior already existed in the scene even though MetaplaySDKConfig.AutoCreateMetaplaySDKBehavior was set. Only one behavior is allowed.");
                }
            }
            else
            {
                if (Instance == null)
                    throw new MetaplayInitializationError("MetaplaySDKBehavior is missing. You should add MetaplaySDKBehavior to the scene and make sure it is not destroyed as long as MetaplaySDK is running, or set MetaplaySDKConfig.AutoCreateMetaplaySDKBehavior");
            }
        }

        internal static void AfterSDKInit()
        {
#if UNITY_EDITOR
            MetaplaySDK.Connection.EditorHookCreateTransport = Instance.EditorHookCreateTransport;
            ClientPlatformUnityUtil.EditorHookGetClientPlatform = () => Instance._appearAsPlatform;
            MetaplaySDK.LocalizationManager.EditorHookLocalizationUpdatedEvent = Instance.EditorHookLocalizationChangedEvent;
#endif
        }

#if UNITY_EDITOR
        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                MetaplaySDK.OnEditorExitingPlayMode();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                // The SDK hook needs to be triggered only AFTER editor playmode has ended so that any
                // playmode behaviours have already been destroyed. The appropriate signal for this is
                // PlayModeStateChange.EnteredEditMode rather than PlayModeStateChange.ExitingPlayMode
                // which gets triggered immediately when playmode exit is requested.
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                MetaplaySDK.OnEditorExitedPlayMode();
            }
        }
#endif

        public void Update()
        {
#if UNITY_EDITOR
            UpdateTransportState();
#endif
        }

        static IEnumerator EndOfTheFrameUpdater()
        {
            WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();
            for (;;)
            {
                yield return waitForEndOfFrame;
                try
                {
                    MetaplaySDK.UpdateAtEndOfTheFrame();
                }
                catch
                {
                }
            }
        }

#if UNITY_EDITOR
        void UpdateTransportState()
        {
            if (_currentTransport == null)
                return;

            // Simulated Connection quality
            switch (_simulatedLinkQuality)
            {
                case LinkQualitySetting.Perfect:
                    _currentTransport.Resume();
                    _spottyLinkIsHalted = false;
                    break;

                case LinkQualitySetting.Spotty:
                    if (MetaTime.Now >= _nextSpottyLinkUpdate)
                    {
                        var rnd = new System.Random();

                        if (rnd.Next(100) < 10)
                        {
                            MetaplaySDK.Logs.Metaplay.Warning("Spotty network updated: simulating loss of stream");
                            _currentTransport.InjectError(new StreamMessageTransport.StreamClosedError());
                            _spottyLinkIsHalted = false;
                        }
                        else if (rnd.Next(100) < 50)
                        {
                            if (!_spottyLinkIsHalted)
                                MetaplaySDK.Logs.Metaplay.Warning("Spotty network updated: pausing incoming traffic, simulating silent network loss");
                            _spottyLinkIsHalted = true;
                            _currentTransport.Halt();
                        }
                        else
                        {
                            if (_spottyLinkIsHalted)
                                MetaplaySDK.Logs.Metaplay.Warning("Spotty network updated: normal");
                            _spottyLinkIsHalted = false;
                            _currentTransport.Resume();
                        }
                        _nextSpottyLinkUpdate = MetaTime.Now + MetaDuration.FromMilliseconds(3000 + rnd.Next(2000));
                    }
                    break;

                case LinkQualitySetting.AllRejected:
                {
                    _currentTransport.Halt();
                    _currentTransport.InjectError(new TcpMessageTransport.ConnectionRefused());
                    _spottyLinkIsHalted = false;
                    break;
                }

                case LinkQualitySetting.NoReplies:
                default:
                    _currentTransport.Halt();
                    _spottyLinkIsHalted = false;
                    break;
            }
        }

        ServerConnection.CreateTransportFn EditorHookCreateTransport(ServerConnection.CreateTransportFn inner)
        {
            return (ServerGateway gateway) =>
            {
                IMessageTransport innerTransport = inner(gateway);
                _currentTransport = new FaultInjectingMessageTransport(innerTransport);
                _nextSpottyLinkUpdate = MetaTime.Epoch;
                UpdateTransportState();
                return _currentTransport;
            };
        }

        /// <summary>
        /// <inheritdoc cref="MetaplayLocalizationManager.EditorHookLocalizationUpdatedEvent"/>
        /// </summary>
        // \note: this event trampoline is to keep initial event handlers and editor handler statically separated
        public event Action EditorHookOnLocalizationUpdatedEvent;
        void EditorHookLocalizationChangedEvent()
        {
            try
            {
                EditorHookOnLocalizationUpdatedEvent?.Invoke();
            }
            catch
            {
            }
        }
#endif
    }
}
