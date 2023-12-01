// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#if UNITY_WEBGL && !UNITY_EDITOR
#   define UNITY_WEBGL_BUILD
#endif

using Metaplay.Client.Messages;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.IO;
using Metaplay.Core.Localization;
using Metaplay.Core.Memory;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Network;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Core.Tasks;
using Metaplay.Unity.IncidentReporting;
using Metaplay.Unity.Localization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

#if UNITY_2017_1_OR_NEWER && !UNITY_2021_3_OR_NEWER
#   error Metaplay SDK only supports Unity versions 2021.3 and above!
#endif

#if UNITY_WEBGL && !METAPLAY_ENABLE_WEBGL
#   error You must define METAPLAY_ENABLE_WEBGL to enable WebGL support in Metaplay. You should define it for all platforms (not just WebGL) in order to always get warnings about features which are poorly supported in WebGL. You can do this by adding a file called csc.rsp in your Assets folder, with a line saying "-define:METAPLAY_ENABLE_WEBGL" (without the quotes).
#endif

namespace Metaplay.Unity
{
    public struct MetaplaySDKConfig
    {
        public BuildVersion BuildVersion;

        /// <summary>
        /// True, if MetaplaySDK should create and inject MetaplaySDKBehavior GameObject script into the scene.
        /// False, if MetaplaySDKBehavior should be expected in the scene.
        /// </summary>
        public bool AutoCreateMetaplaySDKBehavior;
        public ConnectionConfig ConnectionConfig;
        public MetaplayOfflineOptions OfflineOptions;
        public IMetaplayConnectionDelegate ConnectionDelegate;
        public IMetaplayLocalizationDelegate LocalizationDelegate;
        public IMetaplayClientSocialAuthenticationDelegate SocialAuthenticationDelegate; // Optional

        public ISessionContextProvider SessionContext;
        /// <summary>
        /// Callback triggered when application exit is requested. The integration should deinitialize any runtime
        /// state and call MetaplaySDK.Stop(). Currently only used in editor when editor playmode ends. Supplying
        /// this callback is required when using editor playmode without domain reload, if there is no custom
        /// MetaplaySDK lifecycle management.
        /// </summary>
        public Action ExitRequestedCallback;
    }

    public enum ApplicationPauseStatus
    {
        /// <summary>
        /// Application is running, and was running previous frame as well.
        /// </summary>
        Running,

        /// <summary>
        /// Application is about to suspend execution at the end of this of frame, or in a few frames.
        /// OnApplicationPause(pause = true) has been called.
        /// </summary>
        Pausing,

        /// <summary>
        /// Application was unpaused between the last frame and this frame.
        /// OnApplicationPause(pause = false) was called at the start of this frame.
        /// </summary>
        ResumedFromPauseThisFrame,
    }

    public readonly struct MaintenanceModeState : IEquatable<MaintenanceModeState>
    {
        public enum ScheduleStatus
        {
            /// <summary>
            /// There is no ongoing maintenance break, nor is there known maintenance break coming in the future.
            /// </summary>
            NotScheduled = 0, // \note: this is 0 to make this struct zero-initialize into a valid, NotScheduled state.

            /// <summary>
            /// The game backend is now down for maintenance.
            /// </summary>
            Ongoing,

            /// <summary>
            /// The game backend will be soon down for maintenance.
            /// </summary>
            Upcoming,
        }

        /// <summary>
        /// Status of the maintenance mode. Maintenance mode may be ongoing, upcoming (scheduled for future), or
        /// not scheduled.
        /// </summary>
        public readonly ScheduleStatus  Status;

        /// <summary>
        /// The start time of the maintenance break.
        ///
        /// <para>
        /// If <see cref="Status"/> is <see cref="ScheduleStatus.Ongoing"/>, this is guaranteed to be in the past or the current time.
        /// </para>
        /// <para>
        /// If <see cref="Status"/> is <see cref="ScheduleStatus.Upcoming"/>, this is NOT guaranteed to be in the future. A scheduled maintenance
        /// may take a few moments to start and notify clients of it actually starting.
        /// </para>
        /// <para>
        /// If <see cref="Status"/> is <see cref="ScheduleStatus.NotScheduled"/>, this value is <c>epoch</c>.
        /// </para>
        /// </summary>
        public readonly MetaTime        MaintenanceStartAt;

        /// <summary>
        /// The estimated time when the maintenance should be complete, if known.
        ///
        /// <para>
        /// If <see cref="Status"/> is <see cref="ScheduleStatus.Ongoing"/> or <see cref="ScheduleStatus.Upcoming"/>, this value is the estimated ending
        /// time as entered by the server operator when the maintenance was started. If no ending time was given, or it could not be fetched, this will be
        /// <c>null</c>. As maintenance breaks may take longer than estimated, this value may be in the past.
        /// </para>
        /// <para>
        /// If <see cref="Status"/> is <see cref="ScheduleStatus.NotScheduled"/>, this value is <c>null</c>.
        /// </para>
        /// </summary>
        public readonly MetaTime?       EstimatedMaintenanceOverAt;

        MaintenanceModeState(ScheduleStatus status, MetaTime maintenanceStartAt, MetaTime? estimatedMaintenanceOverAt)
        {
            Status = status;
            MaintenanceStartAt = maintenanceStartAt;
            EstimatedMaintenanceOverAt = estimatedMaintenanceOverAt;
        }

        public static bool operator ==(MaintenanceModeState lhs, MaintenanceModeState rhs)
        {
            return lhs.Status                       == rhs.Status
                && lhs.MaintenanceStartAt           == rhs.MaintenanceStartAt
                && lhs.EstimatedMaintenanceOverAt   == rhs.EstimatedMaintenanceOverAt;
        }
        public static bool operator !=(MaintenanceModeState lhs, MaintenanceModeState rhs) => !(lhs == rhs);

        public bool Equals(MaintenanceModeState other) => this == other;
        public override bool Equals(object other) => other is MaintenanceModeState mms && this == mms;
        public override int GetHashCode() => Util.CombineHashCode(Status.GetHashCode(), MaintenanceStartAt.GetHashCode(), EstimatedMaintenanceOverAt.GetHashCode());

        public static MaintenanceModeState CreateNotScheduled()
        {
            return new MaintenanceModeState(ScheduleStatus.NotScheduled, MetaTime.Epoch, null);
        }
        public static MaintenanceModeState CreateUpcoming(MetaTime maintenanceStartAt, MetaTime? estimatedMaintenanceOverAt)
        {
            return new MaintenanceModeState(ScheduleStatus.Upcoming, maintenanceStartAt, estimatedMaintenanceOverAt);
        }
        public static MaintenanceModeState CreateOngoing(MetaTime maintenanceStartAt, MetaTime? estimatedMaintenanceOverAt)
        {
            // maintenanceStartAt must be in the past, if set
            MetaTime now = MetaTime.Now;
            if (maintenanceStartAt > now)
                maintenanceStartAt = now;
            return new MaintenanceModeState(ScheduleStatus.Ongoing, maintenanceStartAt, estimatedMaintenanceOverAt);
        }
    }

    public static class MetaplaySDK
    {
#if UNITY_EDITOR
        /// <summary>
        /// Path to the Metaplay specific Temp folder on Editor. Does not have a trailing /.
        /// </summary>
        public const string UnityTempDirectory = "Temp/Metaplay"; // Use Unity's Temp/ directory
#endif

        public static readonly MetaplayLogs Logs = InitializeLogChannels();

        static MetaplayLogs InitializeLogChannels()
        {
            // \note These are initialized without proper log levels etc. being configured.
            // These are configured in MetaplaySDK.InitLogging(). The UnityLogger instance
            // will not be replaced.
            MetaplayLogs logs = new MetaplayLogs(new UnityLogger());
            return logs;
        }

        public static MessageDispatcher MessageDispatcher { get; private set; }
        public static NetworkDiagnosticsManager NetworkDiagnosticsManager { get; private set; }
        public static ITimelineHistory TimelineHistory { get; private set; }
        public static IncidentRepository IncidentRepository { get; private set; }
        public static IncidentTracker IncidentTracker { get; private set; }
        public static IncidentUploader IncidentUploader { get; private set; }
        public static MetaplayConnection Connection { get; private set; }
        public static MetaplayLocalizationManager LocalizationManager { get; private set; }
        public static LocalizationLanguage ActiveLanguage { get; internal set; }

        /// <summary>
        /// Social authentication utility. This reference is never null. Any social claims before the SDK has been initialized are buffered and
        /// executed when connection is established. Any social claims or results after SDK <see cref="Stop"/> and before the subsequent <see cref="Start"/>
        /// are ignored.
        /// </summary>
        public static SocialAuthManager SocialAuthentication { get; private set; } = SocialAuthManager.CreateBufferingManager();

        /// <summary>
        /// Path where downloadable resources (such as game configs) are downloaded to. Conventional path format: DownloadCachePath/Type/ConfigVersion
        /// </summary>
        public static string DownloadCachePath { get; private set; }

        /// <summary>
        /// Unique identifier for this application launch.
        /// </summary>
        public static Guid AppLaunchId = Guid.NewGuid();

        /// <summary>
        /// Contains the latest information on the upcoming maintenance break. This value is updated
        /// on every successful login and when* <c>UpdateScheduledMaintenanceMode</c> message is received,
        /// or whenever the connection transitions into <see cref="ConnectionStates.TerminalError.InMaintenance"/>
        /// error state. In any case, <see cref="MaintenanceModeChanged"/> event is fired.
        /// <para>
        /// *) This value is guaranteed to update before any game message listener observes the message. Hence accessing
        /// this value from the message handler will observe in the most up-to-date value.
        /// </para>
        /// </summary>
        public static MaintenanceModeState MaintenanceMode { get; private set; }

        /// <summary>
        /// Invoked after <see cref="MaintenanceMode"/> changed.
        /// <para>
        /// Called during <see cref="MetaplaySDK.Update"/> if <see cref="MaintenanceMode"/> updated during the update.
        /// </para>
        /// </summary>
        public static event Action MaintenanceModeChanged;

        /// <summary>
        /// Time of previous EndOfFrame event. On the first frame, set to the time of initialization.
        /// </summary>
        public static MetaTime ApplicationPreviousEndOfTheFrameAt { get; private set; }

        /// <summary>
        /// Duration of the previous application pause. Value is updated when OnApplicationPause(false) is received.
        /// If no pauses have occurred, this is <see cref="MetaDuration.Zero"/>.
        /// </summary>
        public static MetaDuration ApplicationLastPauseDuration { get; private set; } = MetaDuration.Zero;

        /// <summary>
        /// Updated when OnApplicationPause(true) is received.
        /// If no pauses have occurred, this is <see cref="MetaTime.Epoch"/>.
        /// </summary>
        public static MetaTime ApplicationLastPauseBeganAt { get; private set; } = MetaTime.Epoch;

        /// <summary>
        /// Updated when application pauses, unpauses, and at the end of the frame.
        /// </summary>
        public static ApplicationPauseStatus ApplicationPauseStatus { get; private set; } = ApplicationPauseStatus.Running;

        /// <summary>
        /// The duration the application should tolerate being paused during the current pause event. Exceeding this time
        /// will result in Session closing with <see cref="ConnectionStates.TransientError.AppTooLongSuspended"/> error.
        /// This field only meaningful when <see cref="ApplicationPauseStatus"/> is <see cref="ApplicationPauseStatus.Pausing"/>
        /// or <see cref="ApplicationPauseStatus.ResumedFromPauseThisFrame"/>.
        /// <para>
        /// The value is by default <see cref="MetaplaySDKConfig.ConnectionConfig"/>.<see cref="ConnectionConfig.MaxSessionRetainingPauseDuration"/>
        /// but may be overriden by <see cref="OnApplicationAboutToBePaused(string, MetaDuration)"/>.
        /// </para>
        /// </summary>
        public static MetaDuration ApplicationPauseMaxDuration { get; private set; } = MetaDuration.Zero;

        /// <summary>
        /// The maximum duration the application will paused as declared in recent <see cref="OnApplicationAboutToBePaused(string, MetaDuration)"/>,
        /// or <c>null</c> otherwise. This field only meaningful when <see cref="ApplicationPauseStatus"/> is <see cref="ApplicationPauseStatus.Pausing"/>
        /// or <see cref="ApplicationPauseStatus.ResumedFromPauseThisFrame"/>.
        /// </summary>
        public static MetaDuration? ApplicationPauseDeclaredMaxDuration { get; private set; } = MetaDuration.Zero;

        /// <summary>
        /// The reason string application provided for the pause with <see cref="OnApplicationAboutToBePaused(string, MetaDuration)"/>
        /// if <see cref="ApplicationPauseStatus"/> is <see cref="ApplicationPauseStatus.Pausing"/> or <see cref="ApplicationPauseStatus.ResumedFromPauseThisFrame"/>.
        /// Null otherwise.
        /// </summary>
        public static string ApplicationPauseReason { get; private set; } = null;

        /// <summary>
        /// The base address of the currently active CDN (Content Delivery Network) endpoint. The value may vary at runtime if the
        /// client is redirected to another server or if client changes whether it prefers IPv4 or IPv6 connection. Empty in offline
        /// mode.
        /// </summary>
        public static MetaplayCdnAddress CdnAddress { get; private set; }

        /// <summary>
        /// The version of this client build.
        /// </summary>
        public static BuildVersion BuildVersion { get; private set; }

        /// <summary>
        /// Unique identifier for the device.
        /// </summary>
        internal static string DeviceGuid { get; private set; }

        /// <summary>
        /// The last known PlayerId of the this client. This will be <c>None</c> until asynchronouly loaded during SDK startup
        /// (when game has been launched before) or when server generates a new account for application first launches.
        /// </summary>
        public static EntityId PlayerId { get; set; }

        /// <summary>
        /// True between <see cref="Start"/> and <see cref="Stop"/>. Otherwise false.
        /// </summary>
        public static bool IsRunning => _runState != null;

        public static ISessionContextProvider SessionContext { get; private set; }

        /// <summary>
        /// The set of experiments the current player is a member of. Values are set when the session
        /// is established, i.e. <c>Connection.State.Status == Connected</c>.
        /// </summary>
        public static OrderedDictionary<PlayerExperimentId, ExperimentMembershipStatus> ActiveExperiments { get; private set; }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only hook which is invoked just before Unity Editor exits Play Mode. This event is
        /// not available on non-editor environments. Attached event handlers are removed automatically
        /// upon first Play Mode exit, making this suitable for fire-and-forget registrations.
        /// </summary>
        // \todo: should not be in MetaplaySDK but instead in some other Editor support library.
        public static event Action EditorHookOnExitingPlayMode;
#endif

        static string _persistentDataPath = null;

        /// <summary>
        /// Wrapper for Unity's <see cref="Application.persistentDataPath" /> that returns simpler paths
        /// in WebGL builds where persistence is implemented using localStorage and IndexedDB.
        /// </summary>
        public static string PersistentDataPath
        {
            get { return _persistentDataPath ?? throw new InvalidOperationException("Cannot access MetaplaySDK.PersistentDataPath before calling MetaplaySDK.Initialize()"); }
            private set { _persistentDataPath = value; }
        }

        /// <summary>
        /// Location of DeviceGuid blob.
        /// </summary>
        static string GetDeviceGuidPath() => Path.Combine(PersistentDataPath, "MetaplayDeviceGuid.dat");
        static int DeviceGuidFileVersion = 1;

#if UNITY_EDITOR
        // Make sure PersistentDataPath is always available in Editor.
        [InitializeOnLoadMethod]
        private static void InitializeForEditor()
        {
            PersistentDataPath = Application.persistentDataPath;
        }
#endif

#if UNITY_IOS
        /// <summary>
        /// The BundleID of this iOS app. This field is only available on iOS platforms.
        /// </summary>
        public static string AppleBundleId { get; private set; }
#endif

        // Internal state that only exists between Start() and Stop()
        class RunState
        {
            public Stack<Action> CleanupTasks = new Stack<Action>();
            public Action ExitRequestedCallback;
        }

        static MetaTime? _currentPauseStartTime;
        static bool _isFirstFrame;
        static bool _onlyOnceInitCompleted;
        static MetaTime _expectedPauseHintedAt;
        static string _expectedPauseReason;
        static MetaDuration _expectedPauseDuration;
        static bool _maintenanceModeChanged;
        static Task _pendingFlushBeforeAppSuspend;
        static DateTime _pendingFlushBeforeAppSuspendDeadline;
        static RunState _runState;
        static bool _pendingClientUnpausedMessage;

        public static void Start(MetaplaySDKConfig config)
        {
            MetaDebug.Assert(!IsRunning, "Duplicate MetaplaySDK.Start()");

            try
            {
                _runState = new RunState();
                _runState.ExitRequestedCallback = config.ExitRequestedCallback;

#if UNITY_WEBGL && !UNITY_EDITOR
                PersistentDataPath = "/persistent";
#else
                PersistentDataPath = Application.persistentDataPath;
#endif

#if UNITY_IOS
                AppleBundleId = Application.identifier;
#endif
                
                if (config.ConnectionDelegate == null)
                    throw new ArgumentNullException(nameof(config.ConnectionDelegate));
                if (config.LocalizationDelegate == null)
                    throw new ArgumentNullException(nameof(config.LocalizationDelegate));

                MetaplaySDKBehavior.EnsureExists(config.AutoCreateMetaplaySDKBehavior);

                InitSerialization();

                // Initialize logic systems
                Stopwatch sw = Stopwatch.StartNew();
                MetaplayCore.Initialize();
                
                EnvironmentConfig            environmentConfig = IEnvironmentConfigProvider.Get();

                LogLevel                     defaultLogLevel   = environmentConfig.ClientDebugConfig.LogLevel;
                Dictionary<string, LogLevel> logLevelOverrides = environmentConfig.ClientDebugConfig.LogLevelOverrides.ToDictionary(spec => spec.ChannelName, spec => spec.LogLevel);
                InitLogging(defaultLogLevel, logLevelOverrides, _runState.CleanupTasks);

                Logs.Metaplay.Info("MetaplayCore.Initialize() {ms}ms", sw.ElapsedMilliseconds);

                // Initialize WebGL runtime systems
#if UNITY_WEBGL && !UNITY_EDITOR
                MetaplayWebGL.Initialize(OnApplicationQuit);
#endif

                MessageDispatcher = new MessageDispatcher(Logs.Message);
                MessageDispatcher.AddListener<DisconnectedFromServer>(OnDisconnectedFromServer);
                // Silence warnings from unhandled messages
                MessageDispatcher.AddListener<SessionPong>(OnIgnoredMessage);
                MessageDispatcher.AddListener<UpdateScheduledMaintenanceMode>(OnIgnoredMessage);
                MessageDispatcher.AddListener<SessionAcknowledgementMessage>(OnIgnoredMessage);
                MessageDispatcher.AddListener<ConnectedToServer>(OnIgnoredMessage);
                MessageDispatcher.AddListener<Handshake.ClientHelloAccepted>(OnIgnoredMessage);
                MessageDispatcher.AddListener<Handshake.LoginSuccessResponse>(OnIgnoredMessage);
                MessageDispatcher.AddListener<Handshake.CreateGuestAccountResponse>(OnIgnoredMessage);
                MessageDispatcher.AddListener<SessionProtocol.SessionResumeSuccess>(OnIgnoredMessage);
                MessageDispatcher.AddListener<SessionProtocol.SessionStartFailure>(OnIgnoredMessage);
                MessageDispatcher.AddListener<SessionProtocol.SessionStartResourceCorrection>(OnIgnoredMessage);
                MessageDispatcher.AddListener<ConnectionHandshakeFailure>(OnIgnoredMessage);
                MessageDispatcher.AddListener<MessageTransportInfoWrapperMessage>(OnIgnoredMessage);
                MessageDispatcher.AddListener<Handshake.OperationStillOngoing>(OnIgnoredMessage);

                NetworkDiagnosticsManager = new NetworkDiagnosticsManager();

#if UNITY_EDITOR
                TimelineHistory = new TimelineHistory();
                _runState.CleanupTasks.Push(() => { TimelineHistory.Dispose(); TimelineHistory = null; });
#else
                TimelineHistory = null;
#endif

                ServerEndpoint initialEndpoint = environmentConfig.ConnectionEndpointConfig.GetServerEndpoint();

                CdnAddress = CreateInitialCdnAddress(initialEndpoint);
                DeviceGuid = ReadDeviceGuid();

                BuildVersion = new BuildVersion(
                    version: config.BuildVersion.Version ?? "undefined",
                    buildNumber: config.BuildVersion.BuildNumber ?? "undefined",
                    commitId: config.BuildVersion.CommitId ?? "undefined");

                Connection = new MetaplayConnection(initialEndpoint, config.ConnectionConfig, config.ConnectionDelegate, new MetaplayConnectionSDKHook(), config.OfflineOptions);
                _runState.CleanupTasks.Push(() => Connection.Close(flushEnqueuedMessages: false));

                // Initialize incident reporting. Note that the repository is only set-up once
                if (_onlyOnceInitCompleted == false)
                    IncidentRepository = new IncidentRepository();

                IncidentTracker = new IncidentTracker();
                _runState.CleanupTasks.Push(() => IncidentTracker.Dispose());
                IncidentUploader = new IncidentUploader();

                // Initialize localization systems
                if (_onlyOnceInitCompleted == false)
                {
                    BuiltinLanguageRepository.Initialize();
                    LocalizationManager = new MetaplayLocalizationManager();
                }

                LocalizationManager.Start(config.LocalizationDelegate);
                _runState.CleanupTasks.Push(() => LocalizationManager.Stop());

                DownloadCachePath = Path.Combine(PersistentDataPath, "GameConfigCache");

                SetMaintenanceMode(MaintenanceModeState.CreateNotScheduled());

                if (_onlyOnceInitCompleted == false)
                {
                    ApplicationPreviousEndOfTheFrameAt = MetaTime.Now;
                    ActiveExperiments = new OrderedDictionary<PlayerExperimentId, ExperimentMembershipStatus>();
                    _onlyOnceInitCompleted = true;
                    _isFirstFrame = true;
                }

                MetaplaySDKBehavior.AfterSDKInit();
                LocalizationManager.AfterSDKInit();

                SessionContext = config.SessionContext;

                // Create a new authentication manager and copy buffered claims to it. If existing manager was not a buffering
                // manager, i.e. this SDK was Closed and Started, the buffer will be null.
                SocialAuthentication = SocialAuthManager.CreateRealManager(config.SocialAuthenticationDelegate, SocialAuthentication.BufferedClaims);
                _runState.CleanupTasks.Push(() =>
                {
                    // Stop ongoing authentication operations
                    SocialAuthentication.Stop();
                    // Create a dummy manager for the duration from Stop() until next Start.
                    SocialAuthentication = SocialAuthManager.CreateStoppedManager();
                });
            }
            catch (Exception ex)
            {
                Logs.Metaplay.Error("MetaplaySDK.Start() failed and will not be operational: {ex}", ex);
                Stop();
                throw ex;
            }
        }

        public static void Stop()
        {
            MetaDebug.Assert(IsRunning, "MetaplaySDK.Stop() without matching Start()");
            // \note: MetaplaySDKBehavior is kept intact. It will do lifecycle measurement.

            foreach (Action action in _runState.CleanupTasks)
                action.Invoke();

            IncidentTracker = null;
            _maintenanceModeChanged = false;

            // \note: SocialAuthentication remains non-null after stopping to keep old behavior.

            _runState = null;
        }

        /// <summary>
        /// Change the server endpoint to connect to.
        /// This closes the current connection (if any). Starting from the next connection,
        /// the new endpoint will be used.
        /// </summary>
        /// <remarks>
        /// This is intended as a development utility.
        /// </remarks>
        public static void ChangeServerEndpoint(ServerEndpoint endpoint)
        {
            if (Connection.State is ConnectionStates.Connecting || Connection.State is ConnectionStates.Connected)
                Connection.CloseWithError(flushEnqueuedMessages: true, new ClientTerminatedConnectionConnectionError());

            CdnAddress = CreateInitialCdnAddress(endpoint);
            Connection.ChangeServerEndpoint(endpoint);
        }

        static MetaplayCdnAddress CreateInitialCdnAddress(ServerEndpoint endpoint)
        {
            // Initial CDN address. Prefer initially IPv4 but this will get updated when the connection to server is established.
            if (!endpoint.IsOfflineMode)
                return MetaplayCdnAddress.Create(endpoint.CdnBaseUrl, prioritizeIPv4: true);
            else
                return MetaplayCdnAddress.Empty;
        }

        static void UnobservedTaskExceptionHandler(object sender, UnobservedTaskExceptionEventArgs args)
        {
            // Unwrap outermost layer when there is only one Aggegate([Inner])
            AggregateException ae = args.Exception.Flatten();
            Logs.Metaplay.Warning("Unobserved {Exception}", ae.InnerExceptions.Count == 1 ? ae.InnerException : ae);
        }

        static void InitLogging(LogLevel defaultLogLevel, Dictionary<string, LogLevel> logLevelOverrides, Stack<Action> cleanupTasks)
        {
            Logs.Initialize(defaultLogLevel, logLevelOverrides);
            cleanupTasks.Push(() => Logs.Reset());

            Logs.Metaplay.Info("Logger config: level={DefaultLogLevel} overrides={Overrides}", defaultLogLevel, PrettyPrint.Compact(logLevelOverrides));

            // Register additional log sources
            TaskScheduler.UnobservedTaskException += UnobservedTaskExceptionHandler;
            cleanupTasks.Push(() => TaskScheduler.UnobservedTaskException -= UnobservedTaskExceptionHandler);
        }

        static void InitSerialization()
        {
#if UNITY_EDITOR
            MetaSerialization.CheckInitialized();
#else
            if (!MetaSerialization.IsInitialized)
            {
                // Load Metaplay.Generated.dll packaged in build. The assembly is built by SerializerBuilder.BuildDll() that
                // should get invoked in the game build IPreprocessBuildWithReport hook.
                Assembly assembly = Assembly.Load("Metaplay.Generated");
                if (assembly == null)
                    throw new InvalidOperationException($"Could not load Metaplay.Generated.dll");
                InitSerializationFromAssembly(assembly, forceRunInitEagerly: false);
            }
#endif
        }

        /// <summary>
        /// Initializes the serialization subsystem. If <paramref name="forceRunInitEagerly"/> is set, internal
        /// lazily init structures are initialized eagerly. This is only useful for debug checks.
        /// </summary>
        public static void InitSerializationFromAssembly(Assembly assembly, bool forceRunInitEagerly)
        {
            Type generatedSerializer = assembly.GetType("Metaplay.Generated.TypeSerializer");
            if (generatedSerializer == null)
                throw new InvalidOperationException($"Could not locate generated TypeSerializer");

            if (forceRunInitEagerly)
                RuntimeHelpers.RunClassConstructor(generatedSerializer.TypeHandle);

            MetaSerialization.Initialize(generatedSerializer);
        }

        /// <summary>
        /// Update logic for the Metaplay SDK. Should be called on every frame from your game's main loop.
        /// </summary>
        public static void Update()
        {
            if (!IsRunning)
                return;

            // Update incident tracker cached values
            IncidentTracker?.UpdateEarly();

            // Check for too long update cycle
            MetaTime        now                         = MetaTime.Now;
            MetaDuration    durationSincePreviousFrame  = now - ApplicationPreviousEndOfTheFrameAt;
            bool            connectionIsRunning         = Connection.State.Status == ConnectionStatus.Connecting || Connection.State.Status == ConnectionStatus.Connected;
            MetaDuration    maxFrameDuration;

            switch (ApplicationPauseStatus)
            {
                case ApplicationPauseStatus.Pausing:
                case ApplicationPauseStatus.ResumedFromPauseThisFrame:
                {
                    maxFrameDuration = ApplicationPauseMaxDuration;
                    break;
                }

                case ApplicationPauseStatus.Running:
                default:
                {
                    maxFrameDuration = Connection.Config.MaxSessionRetainingFrameDuration;
                    break;
                }
            }

            if (connectionIsRunning && durationSincePreviousFrame > maxFrameDuration)
            {
                Connection.CloseWithError(flushEnqueuedMessages: false, new ConnectionStates.TransientError.AppTooLongSuspended());
            }

            Connection.InternalUpdate();

            // Update incident tracker cached values
            IncidentTracker?.UpdateAfterConnection();

            if (_maintenanceModeChanged)
            {
                _maintenanceModeChanged = false;
                try
                {
                    MaintenanceModeChanged?.Invoke();
                }
                catch(Exception ex)
                {
                    Logs.Metaplay.Error("MaintenanceModeChanged handler failed with {0}", ex);
                }
            }

            // Wait for flush to complete until we allow app to go to background
            if (_pendingFlushBeforeAppSuspend != null)
            {
                int millisecondsToWait = (int)(_pendingFlushBeforeAppSuspendDeadline - DateTime.UtcNow).TotalMilliseconds;
                if (millisecondsToWait > 0)
                {
                    if (!_pendingFlushBeforeAppSuspend.Wait(millisecondsToWait))
                        Logs.Metaplay.Warning("Network flush on before app suspend is taking too much time. Will not block suspend any further.");
                }
                _pendingFlushBeforeAppSuspend = null;
            }
        }

        /// <summary>
        /// Start generating a new <see cref="NetworkDiagnosticReport"/> with default settings. Generating the report takes
        /// some time (up to 5 sec for now, at which point any remaining attempts are given up on). After the report is
        /// completed or the timeout happens, a callback method is called (on the calling thread).
        /// </summary>
        /// <param name="callback">Method to call when the report generation is finished (or timeout occurs).</param>
        // \todo [petri] support cancellation?
        public static void StartNewNetworkDiagnosticsReport(Action<NetworkDiagnosticReport> callback)
        {
            // \todo [petri] network diagnostics tool makes difference assumptions about how gateways are handled, fix them
            ServerEndpoint endpoint = Connection.Endpoint;
            List<int> gameServerPorts = new List<int> { endpoint.PrimaryGateway.ServerPort }.Concat(endpoint.BackupGateways.Select(gw => gw.ServerPort)).ToList();

            NetworkDiagnosticsManager.StartNewReport(
                gameServerHost4:    MetaplayHostnameUtil.GetV4V6SpecificHost(endpoint.PrimaryGateway.ServerHost, isIPv4: true),
                gameServerHost6:    MetaplayHostnameUtil.GetV4V6SpecificHost(endpoint.PrimaryGateway.ServerHost, isIPv4: false),
                gameServerPorts:    gameServerPorts,
                gameServerUseTls:   endpoint.PrimaryGateway.EnableTls,
                cdnHostname4:       GetCDNHostnameFromBaseUrl(MetaplaySDK.CdnAddress.IPv4BaseUrl),
                cdnHostname6:       GetCDNHostnameFromBaseUrl(MetaplaySDK.CdnAddress.IPv6BaseUrl),
                timeout:            TimeSpan.FromSeconds(5),
                callback:           callback);
        }

        static string GetCDNHostnameFromBaseUrl(string cdnBaseUrl)
        {
            if (string.IsNullOrEmpty(cdnBaseUrl))
                return "";
            return new Uri(cdnBaseUrl).Host;
        }

        /// <summary>
        /// Enqueues operation to be completed on the Unity Thread. If called from Unity Thread,
        /// the operation is also enqueued for later execution and it is not executed synchronously.
        /// </summary>
        public static Task<TResult> RunOnUnityThreadAsync<TResult>(Func<TResult> op)
        {
            return MetaTask.Run(op, MetaTask.UnityMainScheduler);
        }

        /// <inheritdoc cref="RunOnUnityThreadAsync{TResult}(Func{TResult})"/>
        public static Task RunOnUnityThreadAsync(Action op)
        {
            return MetaTask.Run(op, MetaTask.UnityMainScheduler);
        }

        static void OnIgnoredMessage(MetaMessage message)
        {
        }

        static void OnDisconnectedFromServer(MetaMessage message)
        {
            // \todo [jarkko]: uses SDK hook for start, and game-hook for Stop. Tiny race possibility where
            //                 stop could be lost.
            SocialAuthentication.OnSessionStopped();
        }

        internal static void UpdateAtEndOfTheFrame()
        {
            MetaTime timeNow = MetaTime.Now;

            ApplicationPreviousEndOfTheFrameAt = timeNow;
            if (ApplicationPauseStatus == ApplicationPauseStatus.ResumedFromPauseThisFrame)
            {
                ApplicationPauseStatus = ApplicationPauseStatus.Running;
                ApplicationPauseMaxDuration = Connection.Config.MaxSessionRetainingPauseDuration;
                ApplicationPauseDeclaredMaxDuration = null;
                ApplicationPauseReason = null;
            }

            _isFirstFrame = false;

            if (IsRunning)
            {
                // \todo: update integrations

                IncidentUploader.LateUpdate(timeNow);
            }

            if (_pendingClientUnpausedMessage)
            {
                _pendingClientUnpausedMessage = false;
                if (Connection.State.Status == ConnectionStatus.Connected)
                    Connection.SendToServer(new ClientLifecycleHintUnpaused());
            }
        }

        internal static void OnApplicationQuit()
        {
            Logs.Metaplay.Info("MetaplaySDK.OnApplicationQuit()");

            // If in offline mode, persist state
            if (Connection != null && Connection.Endpoint.IsOfflineMode)
                Connection.OfflineServer?.TryPersistState();
        }

        internal static void OnApplicationPause(bool pause)
        {
            MetaTime now = MetaTime.Now;

            if (pause)
            {
                if (!_currentPauseStartTime.HasValue)
                {
                    MetaDuration maxDuration;
                    string pauseReason;
                    MetaDuration? declaredDurationMaybe;
                    if (!IsRunning)
                    {
                        Logs.Metaplay.Info("Application paused while MetaplaySDK wasn't running");
                        maxDuration = new ConnectionConfig().MaxSessionRetainingPauseDuration; // \note No game configuration available yet - use default.
                        pauseReason = null;
                        declaredDurationMaybe = null;
                    }
                    else if (_expectedPauseHintedAt == MetaTime.Epoch)
                    {
                        Logs.Metaplay.Info("Application paused");
                        maxDuration = Connection.Config.MaxSessionRetainingPauseDuration;
                        pauseReason = null;
                        declaredDurationMaybe = null;
                    }
                    else if (now - _expectedPauseHintedAt < MetaDuration.FromSeconds(1))
                    {
                        Logs.Metaplay.Info("Application paused with a declared reason: {Reason}", _expectedPauseReason);
                        maxDuration = MetaDuration.Max(Connection.Config.MaxSessionRetainingPauseDuration, _expectedPauseDuration);
                        pauseReason = _expectedPauseReason;
                        declaredDurationMaybe = maxDuration;
                    }
                    else
                    {
                        Logs.Metaplay.Info("Application paused (declared reason missed due to timeout: {Reason})", _expectedPauseReason);
                        maxDuration = Connection.Config.MaxSessionRetainingPauseDuration;
                        pauseReason = null;
                        declaredDurationMaybe = null;
                    }

                    _currentPauseStartTime = now;
                    ApplicationPauseStatus = ApplicationPauseStatus.Pausing;
                    ApplicationPauseMaxDuration = maxDuration;
                    ApplicationLastPauseBeganAt = now;
                    ApplicationPauseDeclaredMaxDuration = declaredDurationMaybe;
                    ApplicationPauseReason = pauseReason;

                    // Flush any pending messages to the server.
                    TryFlushNetworkMessages();

                    // Send the pause start information to the server
                    if (Connection?.State?.Status == ConnectionStatus.Connected)
                        Connection.SendToServer(new ClientLifecycleHintPausing(declaredDurationMaybe, pauseReason));

                    // If in offline mode, persist state
                    if (Connection != null && Connection.Endpoint.IsOfflineMode)
                        Connection.OfflineServer?.TryPersistState();

                    // We have flushed the messages to the session layer. Wait until the data
                    // has been flushed to the socket layer too. Set a timeout of 100 milliseconds
                    // to avoid blocking too long.
                    // Note that we don't wait here. Unity issues one extra frame update after this
                    // notification, so we will wait at the next Update().
                    MessageTransportWriteFence writeFence = Connection?.TryEnqueueTransportWriteFence();
                    if (writeFence != null)
                    {
                        _pendingFlushBeforeAppSuspend = writeFence.WhenComplete;
                        _pendingFlushBeforeAppSuspendDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
                    }

                    if (IsRunning)
                    {
                        Connection.InternalOnApplicationPause(ApplicationPauseMaxDuration);
                    }
                }
                else
                {
                    Logs.Metaplay.Debug("Received application pause while application was already paused. Ignored.");
                }
            }
            else
            {
                if (_currentPauseStartTime.HasValue)
                {
                    ApplicationLastPauseDuration = now - _currentPauseStartTime.Value;
                    ApplicationPauseStatus = ApplicationPauseStatus.ResumedFromPauseThisFrame;
                    _currentPauseStartTime = null;
                    _expectedPauseHintedAt = MetaTime.Epoch;

                    Logs.Metaplay.Info("Application resumed from pause, duration was {PauseDuration}", ApplicationLastPauseDuration);

                    if (IsRunning)
                    {
                        Connection.InternalOnApplicationResume();

                        // Signal to the server that we are Unpausing. All systems that have pending messages will be flushing messages
                        // on the first update. After those message, we hint unpause completion, See UpdateAtEndOfTheFrame().
                        if (Connection.State.Status == ConnectionStatus.Connected)
                        {
                            Connection.SendToServer(new ClientLifecycleHintUnpausing());
                            _pendingClientUnpausedMessage = true;
                        }
                    }
                }
                else if (_isFirstFrame)
                {
                    // Resume on first frame is ignored.
                }
                else
                {
                    Logs.Metaplay.Debug("Received application unpause while application was already running. Ignored.");
                }
            }
        }

#if UNITY_EDITOR

        /// <summary>
        /// Called just before Editor exits PlayMode.
        /// </summary>
        internal static void OnEditorExitingPlayMode()
        {
            EditorHookOnExitingPlayMode?.Invoke();
            EditorHookOnExitingPlayMode = null;
        }

        /// <summary>
        /// Called when Editor has Exited PlayMode and entered EditMode.
        /// </summary>
        internal static void OnEditorExitedPlayMode()
        {
            if (IsRunning && _runState.ExitRequestedCallback != null)
            {
                _runState.ExitRequestedCallback.Invoke();
                if (IsRunning)
                {
                    UnityEngine.Debug.LogWarning("ExitRequestedCallback did not call Metaplay.Stop()!");
                }
            }
            // In Editor mode, we need to clear the only-once-init flag so that it gets run in next PlayMode start
            // as if this was a new process launch. Otherwise if domain reload is not enabled, the static values
            // would keep their original values and these values would be set to the values they were on the first
            // PlayMode launch. Same for AppLaunchId.
            _onlyOnceInitCompleted = false;
            AppLaunchId = Guid.NewGuid();
        }
#endif

        /// <summary>
        /// <para>
        /// Hints the MetaplaySDK that Application is expected to Pause for a moment, and then continue execution. MetaplaySDK
        /// uses this to extend the session lifetime over the duration of the expected pause. If the application is not paused
        /// soon after this call, this call has no effect.
        /// </para>
        /// <para>
        /// This is useful in cases such as showing an Ad or opening an external app from which user is expected to return after
        /// a short while without losing the session state.
        /// </para>
        /// </summary>
        /// <param name="reason">Developer name for the reason of the pause. Only used in logging.</param>
        /// <param name="keepSessionAliveInBackgroundDuration">Duration the connection is kept alive in the background.</param>
        public static void OnApplicationAboutToBePaused(string reason, MetaDuration keepSessionAliveInBackgroundDuration)
        {
            _expectedPauseHintedAt = MetaTime.Now;
            _expectedPauseReason = reason;
            _expectedPauseDuration = keepSessionAliveInBackgroundDuration;

            TryFlushNetworkMessages();
        }

        static void TryFlushNetworkMessages()
        {
            try
            {
                Connection?.Delegate?.FlushPendingMessages();
            }
            catch(Exception ex)
            {
                Logs.Metaplay.Warning("Error while flushing messages: {ex}", ex);
            }
        }

        static void SetMaintenanceMode(MaintenanceModeState maintenanceMode)
        {
            if (MetaplaySDK.MaintenanceMode.Equals(maintenanceMode))
                return;

            MetaplaySDK.MaintenanceMode = maintenanceMode;
            MetaplaySDK._maintenanceModeChanged = true;
        }

        static string ReadDeviceGuid()
        {
            byte[] data = AtomicBlobStore.TryReadBlob(GetDeviceGuidPath());
            if (data == null)
                return null;

            using (IOReader reader = new IOReader(data))
            {
                int ver = reader.ReadInt32();
                if (ver != DeviceGuidFileVersion)
                {
                    Logs.Metaplay.Warning("Found incompatible DeviceGuid file version {foundVersion}, expected {expectedVersion}", ver, DeviceGuidFileVersion);
                    return null;
                }
                return reader.ReadString(128);
            }
        }

        static void SetDeviceGuid(string deviceGuid)
        {
            DeviceGuid = deviceGuid;
            using (SegmentedIOBuffer buffer = new SegmentedIOBuffer())
            {
                using (IOWriter writer = new IOWriter(buffer))
                {
                    writer.WriteInt32(DeviceGuidFileVersion);
                    writer.WriteString(deviceGuid);
                }

                if (!AtomicBlobStore.TryWriteBlob(GetDeviceGuidPath(), buffer.ToArray()))
                    Logs.Metaplay.Error("Storing DeviceGuid as blob {path} failed", GetDeviceGuidPath());
            }
        }

        #region MetaplayConnectionSDKHook

        private class MetaplayConnectionSDKHook : IMetaplayConnectionSDKHook
        {
            public MetaplayConnectionSDKHook()
            {
            }

            void IMetaplayConnectionSDKHook.OnCurrentCdnAddressUpdated(MetaplayCdnAddress currentAddress)
            {
                MetaplaySDK.CdnAddress = currentAddress;
            }
            void IMetaplayConnectionSDKHook.OnScheduledMaintenanceModeUpdated(MaintenanceModeState maintenanceMode)
            {
                SetMaintenanceMode(maintenanceMode);
            }
            void IMetaplayConnectionSDKHook.OnSessionStarted(SessionProtocol.SessionStartSuccess sessionStart)
            {
                MetaplaySDK.ActiveExperiments.Clear();
                if (sessionStart.ActiveExperiments != null)
                {
                    foreach (EntityActiveExperiment sessionActiveExperiment in sessionStart.ActiveExperiments)
                        MetaplaySDK.ActiveExperiments.Add(sessionActiveExperiment.ExperimentId, ExperimentMembershipStatus.FromSessionInfo(sessionActiveExperiment));
                }

#if UNITY_WEBGL_BUILD
                // Setup IMX login
                if (MetaplayCore.Options.FeatureFlags.EnableImmutableXLinkClientLibrary)
                    ImmutableXApiBridge.SetApiUrl(new ImmutableXApiBridge.SetApiUrlAsyncRequest() { ApiUrl = Connection.ServerOptions.ImmutableXLinkApiUrl });
#endif

                MetaplayConfigManager.OnSessionStarted();
                SocialAuthentication.OnSessionStarted();
            }

            string IMetaplayConnectionSDKHook.GetDeviceGuid()
            {
                return DeviceGuid;
            }

            void IMetaplayConnectionSDKHook.SetDeviceGuid(string deviceGuid)
            {
                SetDeviceGuid(deviceGuid);
            }
        }

        #endregion
    }

    public class ClientTerminatedConnectionConnectionError : ConnectionStates.TransientError
    {
    }
}
