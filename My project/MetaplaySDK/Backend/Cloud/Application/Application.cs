// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Analytics;
using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Options;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.SystemConsole.Themes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Application
{
    /// <summary>
    /// Defines an "Quick Link", which is a decorated link that appears in the sidebar of the LiveOps Dashboard.
    /// </summary>
    public class QuickLink
    {
        public string Icon  { get; private set; } = "";
        public string Title { get; private set; } = "";
        public string Uri   { get; private set; } = "";
        public string Color { get; private set; } = "";

        public QuickLink() { } // need public ctor for .NET Configuration
        public QuickLink(string icon, string title, string uri, string color)
        {
            Icon    = icon;
            Title   = title;
            Uri     = uri;
            Color   = color;
        }

        public void Validate()
        {
            if (string.IsNullOrEmpty(Title) || string.IsNullOrWhiteSpace(Uri))
                throw new InvalidOperationException($"Environment.QuickLink must specify Title and URI, got title='{Title}', Uri='{Uri}'.");
        }
    }

    [RuntimeOptions("Environment", isStatic: true, "Configuration options for the application runtime environment.")]
    public class EnvironmentOptions : RuntimeOptionsBase
    {
        // Generic
        [MetaDescription("The human readable environment name.")]
        public string                   Environment                 { get; private set; } =
            IsProductionEnvironment ? "prod" :
            IsStagingEnvironment ? "stage" :
            IsDevelopmentEnvironment ? "dev" :
            IsLocalEnvironment ? "local" :
            null;
        [MetaDescription("Enables the development features (such as cheats, in-app dev purchases, etc.) for all players.")]
        public bool                     EnableDevelopmentFeatures   { get; private set; } = IsLocalEnvironment || IsDevelopmentEnvironment;
        [MetaDescription("When persisting entities, perform a sanity check to see that they deserialize and decompress properly before writing to the database.")]
        public bool                     ExtraPersistenceChecks      { get; private set; } = IsLocalEnvironment || IsDevelopmentEnvironment;
        [MetaDescription("Enable keyboard input (only enabled locally).")]
        public bool                     EnableKeyboardInput         { get; private set; } = IsLocalEnvironment;
        [MetaDescription("List of links to relevant websites, viewable in the LiveOps Dashboard")]
        public QuickLink[]              QuickLinks                  { get; private set; }

        // Metrics
        [CommandLineAlias("-EnableMetrics")]
        [MetaDescription("Enables HTTP server which Prometheus uses to scrape the application metrics.")]
        public bool         EnableMetrics               { get; private set; } = IsServerApplication || IsCloudEnvironment; // When running locally, only enable metrics for server
        [MetaDescription("If `EnableMetrics` is enabled: The port on which to serve Prometheus metrics.")]
        public int          MetricPort                  { get; private set; } = (IsBotClientApplication && IsLocalEnvironment) ? 9091 : 9090; // Local BotClient defaults to 9091 (to avoid conflicts)
        [MetaDescription("Enables system HTTP endpoints at `/gracefulShutdown` and `/healthz`.")]
        public bool         EnableSystemHttpServer      { get; private set; } = IsCloudEnvironment;
        [MetaDescription("If `EnableSystemHttpServer` is enabled: The host on which to serve the system HTTP endpoints.")]
        public string       SystemHttpListenHost        { get; private set; } = IsCloudEnvironment ? "0.0.0.0" : "127.0.0.1";
        [MetaDescription("If `EnableSystemHttpServer` is enabled: The port on which to serve the system HTTP endpoints.")]
        public int          SystemHttpPort              { get; private set; } = 8888;

        // Lifecycle
        [CommandLineAlias("-ExitAfter")]
        [MetaDescription("Optional: The amount of time the application is allowed to run after initialization before automatically exiting. _Intended for automated testing only._")]
        public TimeSpan?    ExitAfter                   { get; private set; } = null;
        [MetaDescription("When enabled, exit the application immediately when an error event is logged. _Intended for automated testing only._")]
        public bool         ExitOnLogError              { get; private set; } = false;
        [MetaDescription("When enabled, exit the application if unknown sections or keys are specified in the runtime options `.yaml` files. _Intended for local development only._")]
        public bool         ExitOnUnknownOptions        { get; private set; } = IsLocalEnvironment;

        public override Task OnLoadedAsync()
        {
            // Default value for QuickLinks if not defined by user
            if (QuickLinks == null)
            {
                QuickLinks = new QuickLink[]
                {
                    new QuickLink(icon: null, title: "Metaplay Documentation", uri: "https://docs.metaplay.io/", color: "rgb(134, 199, 51)"),
                    new QuickLink(icon: null, title: "Metaplay Homepage", uri: "https://metaplay.io/", color: "rgb(134, 199, 51)"),
                };
            }

            foreach (QuickLink quickLink in QuickLinks)
                quickLink.Validate();

            return Task.CompletedTask;
        }
    }

    public enum LogColorTheme
    {
        None,               // No color theme
        SystemColored,      // SystemConsoleTheme.Colored
        SystemLiterate,     // SystemConsoleTheme.Literate
        AnsiCode,           // AnsiConsoleTheme.Code
        AnsiLiterate,       // AnsiConsoleTheme.Literate
    }

    public enum LogFormat
    {
        Text,   // Human-readable text format with colors, default for all builds now
        Json,   // Compact JSON format, intended for cloud use
    }

    [RuntimeOptions("Logging", isStatic: true, "Configuration options for logging.")]
    public class LoggingOptions : RuntimeOptionsBase
    {
        // Console logger
        [MetaDescription("Format to output logs in (`Text` or `Json`).")]
        public LogFormat        Format              { get; private set; } = LogFormat.Text;
        [MetaDescription("The format template for writing logs. See [Serilog](https://serilog.net/) documentation for more details.")]
        public string           FormatTemplate      { get; private set; } = "[{Timestamp:HH:mm:ss.fff} {Level:u3} {SourceContext}] {Message:lj}{NewLine}{Exception}";
        [MetaDescription("The color theme for writing logs (`None` or `AnsiteLiterate`).")]
        public LogColorTheme    ColorTheme          { get; private set; } = LogColorTheme.AnsiLiterate;
        [CommandLineAlias("-LogLevel")]
        [MetaDescription("The event logging level (`Verbose`, `Debug`, `Information`, `Warning`, or `Error`). Events below this level are not recorded.")]
        public LogLevel         Level               { get; private set; } = LogLevel.Debug;
    }

    /// <summary>
    /// Base class for a Metaplay server-side application. Sets up basic services like logging, Akka, Akka.Remote clustering,
    /// database access layer, Prometheus metrics, etc.
    /// </summary>
    public abstract class Application : IDisposable
    {
        public static Application Instance { get; private set; }

        protected IMetaLogger               _logger;
        protected Assembly[]                _localAssemblies;
        protected ExtendedActorSystem       _actorSystem;
        protected Metrics.RuntimeCollector  _runtimeMetricsCollector;
        protected Prometheus.MetricServer   _metricServer;
        protected WebApplication            _systemHttpServer;

        public ClusterConfig                ClusterConfig { get; private set; }
        protected List<IClusterService>     _clusterServices;
        protected ClusterNodeAddress        _selfAddress;
        protected EntitySharding            _entitySharding;
        protected IActorRef                 _clusterCoordinator;

        protected static ManualResetEventSlim   _allowProcessTerminate  = new ManualResetEventSlim(false);
        protected static ManualResetEventSlim   _isProcessFinished      = new ManualResetEventSlim(false);

        static readonly Prometheus.Gauge        c_buildInfoGauge            = Prometheus.Metrics.CreateGauge("metaplay_build_info", "Describes the launching Metaplay Application version. Value is the launch timestamp in milliseconds.", "build_number", "commit_id", "release");
        static readonly long                    c_applicationStartTimestamp = MetaTime.Now.MillisecondsSinceEpoch;

        /// <summary>
        /// Instantly terminate program with given exit code.
        /// </summary>
        /// <param name="exitCode"></param>
        public static void ForceTerminate(int exitCode, string reason)
        {
            Console.WriteLine("Force-terminating application with exitCode {0}: {1}", exitCode, reason);
            _isProcessFinished.Set();

            // Sleep a bit before exiting to give logger some time to flush all messages
            Thread.Sleep(500);
            Environment.Exit(exitCode);
        }

        public Application()
        {
            // Register instance
            if (Instance != null)
                throw new InvalidOperationException($"Duplicate Application initialization");
            Instance = this;

            // Print launch info. This is the first thing application prints.
            Console.WriteLine("Launching application, build: {0}, commit id: {1}, Metaplay release {2}", CloudCoreVersion.BuildNumber, CloudCoreVersion.CommitId ?? "<not set>", DeploymentOptions.CurrentMetaplayVersion.ToString());

            // Print GC mode
            Console.WriteLine("Garbage collector mode = {0}", System.Runtime.GCSettings.IsServerGC ? "server" : "workstation");
        }

        public virtual void Dispose()
        {
            // Dispose resources
            _runtimeMetricsCollector?.Dispose();

            // \todo [petri] more stuff needs disposing of?
        }

        public static Assembly[] LoadLocalAssemblies()
        {
            HashSet<string> processedSet = new HashSet<string>();
            List<Assembly> localAssemblies = new List<Assembly>() { Assembly.GetEntryAssembly() };

            Queue<Assembly> queue = new Queue<Assembly>();
            queue.Enqueue(Assembly.GetEntryAssembly());

            while (queue.TryDequeue(out Assembly assembly))
            {
                foreach (AssemblyName referencedAssembly in assembly.GetReferencedAssemblies())
                {
                    // Skip ignored assemblies
                    if (TypeScanner.ShouldIgnoreAssembly(referencedAssembly.FullName))
                        continue;

                    // Skip already processed assemblies
                    if (processedSet.Contains(referencedAssembly.FullName))
                        continue;
                    processedSet.Add(referencedAssembly.FullName);

                    Assembly loaded = Assembly.Load(referencedAssembly);
                    queue.Enqueue(loaded);
                    localAssemblies.Add(loaded);
                }
            }

            return localAssemblies.ToArray();
        }

        protected IClusterService CreateEntityShard(EntityKind entityKind)
        {
            return new ClusterServiceEntityShard(
                entityKind,
                _entitySharding,
                ClusterConfig,
                _selfAddress);
        }

        ConsoleTheme MapLogColorTheme(LogColorTheme theme)
        {
            switch (theme)
            {
                case LogColorTheme.None:            return ConsoleTheme.None;
                case LogColorTheme.SystemColored:   return SystemConsoleTheme.Colored;
                case LogColorTheme.SystemLiterate:  return SystemConsoleTheme.Literate;
                case LogColorTheme.AnsiCode:        return AnsiConsoleTheme.Code;
                case LogColorTheme.AnsiLiterate:    return AnsiConsoleTheme.Literate;

                default:
                    throw new ArgumentException($"Invalid LogColorTheme: {theme}");
            }
        }

        /// <summary>
        /// Pre-initialize Serilog with a simple console logger. Used during app init before the
        /// proper logger configuration is known.
        /// </summary>
        void PreInitLogger()
        {
            LoggerConfiguration config = new LoggerConfiguration();
            config.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
            config.WriteTo.Sink(new MetaplayOnErrorTerminateSink(), restrictedToMinimumLevel: LogEventLevel.Error);
            Serilog.Log.Logger = config.CreateLogger();

            _logger = MetaLogger.ForContext<Application>();
        }

        protected void InitLogger()
        {
            LoggingOptions logOpts = RuntimeOptionsRegistry.Instance.GetCurrent<LoggingOptions>();

            // Initialize logging level switch
            MetaLogger.SetLoggingLevel(logOpts.Level);

            // Initialize Serilog logger
            LoggerConfiguration config = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(MetaLogger.SerilogLogLevelSwitch)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning) // make ASP.NET a bit less noisy
                .MinimumLevel.Override("Metaplay.Server.AdminApi.AnonymousAuthenticationHandler", LogEventLevel.Information) // silence noisy auth success events
                .MinimumLevel.Override("Metaplay.Cloud.Application.MetaplaySystemHttpServer", LogEventLevel.Information); // silence noisy health check ok events

            // Configure log format: text or json
            switch (logOpts.Format)
            {
                case LogFormat.Text:
                    config.WriteTo.Console(
                        outputTemplate: logOpts.FormatTemplate,
                        formatProvider: CultureInfo.InvariantCulture,
                        theme:          MapLogColorTheme(logOpts.ColorTheme)
                        );
                    break;

                case LogFormat.Json:
                    config.WriteTo.Console(new CompactJsonFormatter());
                    break;

                default:
                    throw new InvalidOperationException($"Invalid LogFormat {logOpts.Format}, expecting 'Text' or 'Json'");
            }

            config.WriteTo.Sink(new MetaplayOnErrorTerminateSink(), restrictedToMinimumLevel: LogEventLevel.Error);

            // Create fully configured logger
            Serilog.Log.Logger = config.CreateLogger();

            // Configure MySql logging
            DatabaseOptions opts = RuntimeOptionsRegistry.Instance.GetCurrent<DatabaseOptions>();
            if (opts.Backend == DatabaseBackendType.MySql && opts.EnableMySqlLogging)
                MySqlConnector.Logging.MySqlConnectorLogManager.Provider = new MySqlConnector.Logging.SerilogLoggerProvider();
        }

        protected void InitAkka(EnvironmentOptions envOpts)
        {
            _logger.Information("Initializing Akka.NET");
            string akkaConfig = GenerateAkkaConfig();
            _actorSystem = (ExtendedActorSystem)ActorSystem.Create(MetaplayCore.Options.ProjectName, akkaConfig);
        }

        protected string GenerateAkkaConfig()
        {
            ClusteringOptions clusterOpts = RuntimeOptionsRegistry.Instance.GetCurrent<ClusteringOptions>();

            // Akka remoting config
            _logger.Information("Remoting on: {Hostname}:{Port}", clusterOpts.RemotingHost + clusterOpts.RemotingHostSuffix, clusterOpts.RemotingPort);
            string remotingHost = clusterOpts.RemotingHost + clusterOpts.RemotingHostSuffix;
            string remotingPort = clusterOpts.RemotingPort.ToString(CultureInfo.InvariantCulture);

            // \note If you change maximum-frame-size for the purpose of supporting larger PlayerModels,
            //       please consider also WireProtocol.MaxPacketUncompressedPayloadSize.
            const int remotingMaximumFrameSize  = 6*1024*1024 + 1024; // 6MB, plus some extra for overheads, so that a nice round 6MB payload should still be accepted.
            const int remotingSendBufferSize    = 2 * remotingMaximumFrameSize;
            const int remotingReceiveBufferSize = 2 * remotingMaximumFrameSize;

            StringBuilder sb = new StringBuilder(1024);
            sb.AppendLine("akka {");
            sb.AppendLine("    loglevel = DEBUG");
            sb.AppendLine("    actor.debug.unhandled = on");
            sb.AppendLine("");
            sb.AppendLine($"    loggers = [\"{typeof(MetaAkkaLogger).AssemblyQualifiedName}\"]");
            sb.AppendLine("");
            sb.AppendLine("    actor {");
            sb.AppendLine("        provider = remote");
            sb.AppendLine("");
            sb.AppendLine("        serializers {");
            sb.AppendLine($"            tagged = \"{typeof(AkkaTaggedSerializer).AssemblyQualifiedName}\"");
            sb.AppendLine($"            compact = \"{typeof(AkkaCompactSerializer).AssemblyQualifiedName}\"");
            sb.AppendLine($"            hyperion = \"{typeof(Akka.Serialization.HyperionSerializer).AssemblyQualifiedName}\"");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        serialization-bindings {");
            sb.AppendLine("            \"System.Object\" = hyperion");
            sb.AppendLine($"            \"{typeof(EntityShard.IRoutedMessage).AssemblyQualifiedName}\" = compact");
            sb.AppendLine($"            \"{typeof(MetaMessage).AssemblyQualifiedName}\" = compact");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    io.tcp {");
            sb.AppendLine("        batch-accept-limit = 100"); // accepted connections in parallel (to minimize wait times of socket accepts under load)
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    remote {");
            sb.AppendLine("        log-remote-lifecycle-events = info");
            sb.AppendLine("        log-received-messages = off");
            sb.AppendLine("        log-sent-messages = off");
            sb.AppendLine("");

            // Use very short quarantine to allow nodes to recover connecitivity
            // \note We must not use remote watches or remote actor deploys as they won't work with this!
            sb.AppendLine("        retry-gate-closed-for = 5s");
            sb.AppendLine("        prune-quarantine-marker-after = 1m");
            sb.AppendLine("");

            sb.AppendLine("        dot-netty.tcp {");
            sb.AppendLine("            enforce-ip-family = true");
            sb.AppendLine("            dns-use-ipv6 = false");
            sb.AppendLine("            hostname = 0.0.0.0");
            sb.AppendLine($"            public-hostname = {remotingHost}");
            sb.AppendLine($"            port = {remotingPort}");
            sb.AppendLine("");
            sb.AppendLine($"            maximum-frame-size  = {remotingMaximumFrameSize.ToString(CultureInfo.InvariantCulture)}b");
            sb.AppendLine($"            send-buffer-size    = {remotingSendBufferSize.ToString(CultureInfo.InvariantCulture)}b");
            sb.AppendLine($"            receive-buffer-size = {remotingReceiveBufferSize.ToString(CultureInfo.InvariantCulture)}b");
            sb.AppendLine("");
            sb.AppendLine("            batching {");
            sb.AppendLine("                enabled = true"); // \note use batching with default settings, Akka 1.4.14 should no longer suffer latency hits with low volumes
            //sb.AppendLine("                enabled = true");
            //sb.AppendLine("                max-pending-writes = 50");
            //sb.AppendLine("                max-pending-bytes = 16k");
            //sb.AppendLine("                flush-interval = 10ms");
            sb.AppendLine("            }");
            sb.AppendLine("            use-dispatcher-for-io = \"akka.actor.default-dispatcher\"");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        #transport-failure-detector {");
            sb.AppendLine("        #    heartbeat-interval = 4s");
            sb.AppendLine("        #    acceptable-heartbeat-pause = 20s");
            sb.AppendLine("        #}");
            sb.AppendLine("");
            sb.AppendLine("        watch-failure-detector {");
            sb.AppendLine("            heartbeat-interval = 3s");
            sb.AppendLine("        }");
            sb.AppendLine("        use-dispatcher = \"akka.actor.default-dispatcher\"");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        void InitClustering()
        {
            ClusteringOptions clusterOpts = RuntimeOptionsRegistry.Instance.GetCurrent<ClusteringOptions>();

            // Parse clustering config (and register globally)
            ClusterConfig = clusterOpts.ClusterConfig;

            // Store own node address
            _selfAddress = clusterOpts.SelfAddress;
            _logger.Information("Cluster self address: {NodeAddress}", _selfAddress);

            // Make sure we are part of cluster
            if (!ClusterConfig.IsMember(_selfAddress))
                throw new InvalidOperationException($"This node ({_selfAddress.HostName}:{_selfAddress.Port}) is not member of the specified cluster config");
        }

        void ValidateClusterServices(List<IClusterService> clusterServices)
        {
            // \todo [petri] perform validation later
            EntityKindMask requiredEntityKinds = GetRequiredEntityKinds(clusterServices);

            // Check that all EntityKind shards are included on at least one node of the cluster
            EntityKindMask foundEntityKinds = EntityKindMask.None;
            foreach (NodeSetConfig nodeSetConfig in ClusterConfig.NodeSets)
                foundEntityKinds |= nodeSetConfig.EntityKindMask;

            if ((foundEntityKinds & requiredEntityKinds) != requiredEntityKinds)
                throw new InvalidOperationException($"Missing EntityKinds from configured shards: {requiredEntityKinds & ~foundEntityKinds}");
        }

        async Task StartClusteringAsync()
        {
            // Initialize cluster services & check that all the EntityKinds are present in the provided shard config
            _clusterServices = InitClusterServices();
            ValidateClusterServices(_clusterServices);

            // Start cluster coordinator
            ClusteringOptions clusterOpts = RuntimeOptionsRegistry.Instance.GetCurrent<ClusteringOptions>();
            _clusterCoordinator = _actorSystem.ActorOf(Props.Create<ClusterCoordinatorActor>(MetaplayCore.Options.ProjectName, _selfAddress, ClusterConfig, _clusterServices, clusterOpts.Cookie), "cluster");

            // Wait for cluster to become ready (all nodes available)
            await ClusterCoordinatorActor.WaitForClusterConnectedAsync(_clusterCoordinator, _logger).ConfigureAwait(false);
        }

        EntityKindMask GetRequiredEntityKinds(List<IClusterService> clusterServices)
        {
            EntityKindMask fullMask = EntityKindMask.None;
            foreach (IClusterService svc in clusterServices)
            {
                if (svc is ClusterServiceEntityShard shard)
                    fullMask |= EntityKindMask.FromEntityKind(shard.EntityKind);
            }
            return fullMask;
        }

        protected abstract List<IClusterService> InitClusterServices();
        protected virtual Task PreInitializeAsync() => Task.CompletedTask;
        protected virtual Task InitializeAsync() => Task.CompletedTask;
        protected virtual Task StartAsync() => Task.CompletedTask;
        protected virtual Task StopAsync() => Task.CompletedTask;

        protected virtual void HandleKeyPress(ConsoleKeyInfo key)
        {
            // nada
        }

        /// <summary>
        /// Performs the minimal subset of application startup required by MetaplaySDK Core.
        /// </summary>
        void MinimalSdkInit()
        {
            // Pre-initialize logger before we have the full configuration
            PreInitLogger();

            // Register pretty printers
            PrettyPrinter.RegisterFormatter<IActorRef>((actor, isCompact) => actor.ToString());

            // Force-load local assemblies so they are available through reflection
            _localAssemblies = LoadLocalAssemblies();

            // Initialize Metaplay SDK core
            MetaplayCore.Initialize();
        }

        async Task PrepareAsync(string applicationName, string[] cmdLineArgs)
        {
            // Initialize BigQuery formatter for analytics events
            BigQueryFormatter.Initialize();

            // Initialize runtime options (and load initial values)
            await RuntimeOptionsRegistry.InitializeAsync(applicationName, cmdLineArgs).ConfigureAwait(false);

            // Initialize EntityConfigRegistry
            EntityConfigRegistry.Initialize();

            // Initialize Serilog with correct runtime options
            InitLogger();

            // Start system http server (do this early, so can start responding to k8s health checks).
            EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
            if (envOpts.EnableSystemHttpServer)
            {
                _logger.Information("Starting Metaplay system HTTP server on {SystemHttpHost}:{SystemHttpPort}..", envOpts.SystemHttpListenHost, envOpts.SystemHttpPort);
                _systemHttpServer = await MetaplaySystemHttpServer.StartAsync(envOpts);
                // \note server is never shutdown
            }

            // Generate the serializer .dll
            Type serializerType = GenerateSerializer();

            // Pre-initialize (before Akka)
            await PreInitializeAsync();

            // Initialize Akka.NET
            InitAkka(envOpts);

            // Initialize serialization
            // \todo [petri] small window for race conditions: other nodes connecting can try to use MetaSerialization before being initialize,
            //               should call MetaSerialization.Initialize() from within ActorSystem construction
            MetaSerialization.Initialize(serializerType, actorSystem: _actorSystem);

            // Initialize application (post Akka)
            await InitializeAsync();

            // Runtime monitoring
            _runtimeMetricsCollector = new Metrics.RuntimeCollector();

            // Start Prometheus metrics HTTP server
            if (envOpts.EnableMetrics)
            {
                _logger.Information("Starting Prometheus metric server (port={Port})", envOpts.MetricPort);
                try
                {
                    _metricServer = new Prometheus.MetricServer(envOpts.MetricPort);
                    _metricServer.Start();
                }
                catch (HttpListenerException ex)
                {
                    _logger.Warning("Unable to start Prometheus http server: {Exception}", ex);

                    // On Windows, inform the user that access needs to be granted to open the port
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        _logger.Warning("On Windows, grant access to open http port with: netsh http add urlacl url=http://+:{Port}/metrics USER=<username>", envOpts.MetricPort);
                }

                // Push version info into metrics. The value of the gauge is the timestamp to allow detecting both same version launching again and version changes.
                c_buildInfoGauge.WithLabels(CloudCoreVersion.BuildNumber, CloudCoreVersion.CommitId ?? "null", DeploymentOptions.CurrentMetaplayVersion.ToString()).Set((double)c_applicationStartTimestamp);
            }
        }

        /// <summary>
        /// Prepares the application to be run from a container image. This is run when container is built (in dockerfile),
        /// and any saved files will then be available in the final container image. Failure here will abort the container build.
        /// The main use is to pre-generate the serializer DLL and store it in the container image.
        /// </summary>
        void BakeContainerImage()
        {
            // Initialize early systems to catch initialization failures, such as missing attributes.
            BigQueryFormatter.Initialize();

            // Generate serializer. It is stored in the container image, allowing baked application to start quickly
            _ = GenerateSerializer();
        }

        async Task<int> RunApplicationAsync()
        {
            // Register SIGTERM handler which stops the application from immediately exiting. Instead,
            // wait for the application to gracefully terminate (eg, shutdown all entity shards).
            AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            {
                // \note This also gets invoked when process terminates normally, which we detect with _isProcessFinished
                if (!_isProcessFinished.IsSet)
                {
                    Console.WriteLine("Got SIGTERM, allow process to terminate..");
                    _allowProcessTerminate.Set();
                    _isProcessFinished.Wait();
                    Console.WriteLine("All systems gracefully shutdown, exiting..");
                }
                else
                    Console.WriteLine("Got SIGTERM when already waiting to exit process..");
            };

            try
            {
                // Initialize EntityActors
                EntityDispatcherRegistry.RegisterAll();

                // Initialize clustering base info
                InitClustering();

                // Initialize entity sharding
                _entitySharding = EntitySharding.Get(_actorSystem);

                // Start early actors (mainly entity shards)
                await StartAsync();

                if (_allowProcessTerminate.IsSet)
                {
                    // \note This may happen if application starts so slowly that the Kubernetes liveness checks fail
                    _logger.Warning("Process termination requested before initialization completed, exiting..");
                }
                else
                {
                    // Start clustering & all cluster services
                    await StartClusteringAsync();

                    // Force GC before going into event loop
                    GC.Collect(2, GCCollectionMode.Forced);

                    // Check if application should exit after specified time
                    EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
                    MetaTime? exitAt = null;
                    if (envOpts.ExitAfter.HasValue)
                        exitAt = MetaTime.Now + MetaDuration.FromTimeSpan(envOpts.ExitAfter.Value);

                    while (ClusterCoordinatorActor.Phase != ClusterPhase.Terminated)
                    {
                        // Wait a bit to avoid consuming all CPU
                        await Task.Delay(100).ConfigureAwait(false);

                        // Check if should exit
                        if (exitAt != null && MetaTime.Now >= exitAt.Value)
                        {
                            Console.WriteLine("Exiting automatically after running for the duration specified with ExitAfter..");
                            ClusterCoordinatorActor.RequestClusterShutdown();
                            _allowProcessTerminate.Set();
                            exitAt = null; // prevent invoking multiple times
                        }

                        // Poll key presses (if enabled)
                        // \todo [petri] If input is redirected (running under test suite), this operation blocks!
                        if (envOpts.EnableKeyboardInput && TryGetConsoleKey(out ConsoleKeyInfo key))
                        {
                            // Key shortcuts:
                            // Q/ESC -- shut cluster down gracefully
                            // E,W,I,D -- set log level to error/warning/info/debug
                            switch (key.Key)
                            {
                                case ConsoleKey.Q:
                                case ConsoleKey.Escape:
                                    Console.WriteLine("Received keypress requesting to gracefully shutdown..");
                                    ClusterCoordinatorActor.RequestClusterShutdown();
                                    _allowProcessTerminate.Set();
                                    break;

                                case ConsoleKey.G:
                                    Console.WriteLine("Forcing garbage collect (all generations)");
                                    GC.Collect();
                                    break;

                                case ConsoleKey.E:
                                    Console.WriteLine("Log level set to: ERROR");
                                    MetaLogger.SetLoggingLevel(LogLevel.Error);
                                    break;

                                case ConsoleKey.W:
                                    Console.WriteLine("Log level set to: WARNING");
                                    MetaLogger.SetLoggingLevel(LogLevel.Warning);
                                    break;

                                case ConsoleKey.I:
                                    Console.WriteLine("Log level set to: INFO");
                                    MetaLogger.SetLoggingLevel(LogLevel.Information);
                                    break;

                                case ConsoleKey.D:
                                    Console.WriteLine("Log level set to: DEBUG");
                                    MetaLogger.SetLoggingLevel(LogLevel.Debug);
                                    break;

                                case ConsoleKey.V:
                                    Console.WriteLine("Log level set to: VERBOSE");
                                    MetaLogger.SetLoggingLevel(LogLevel.Verbose);
                                    break;

                                default:
                                    HandleKeyPress(key);
                                    break;
                            }
                        }
                    }

                    // Wait a bit to allow Akka logger to process its messages
                    await Task.Delay(200);
                }

                // Shutdown application
                _logger.Information("Shutting down application..");
                await StopAsync();

                // Stop EntitySharding
                _logger.Information("Shutting down EntitySharding..");
                await _entitySharding.ShutdownAsync(TimeSpan.FromSeconds(5));

                // Stop clustering
                await _clusterCoordinator.GracefulStop(TimeSpan.FromSeconds(5));

                // Check if we're allowed to terminate: in Kubernetes, exiting the process would just cause the pod to get restarted
                // so we just wait here until a SIGTERM is received.
                if (!_allowProcessTerminate.IsSet)
                {
                    _logger.Information("Waiting for allowProcessTerminate (SIGTERM)..");
                    while (!_allowProcessTerminate.Wait(1000))
                    {
                        _logger.Information("Still waiting for allowProcessTerminate (SIGTERM)..");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Unexpected exception while running the application, exiting immediately: {Exception}", ex);
                throw;
            }
            finally
            {
                // Allow application to exit (SIGTERM handler waits for the _isProcessFinished before finally terminating)
                _logger.Information("Allowing process to terminate");
                _isProcessFinished.Set();
            }

            return 0;
        }

        /// <summary>
        /// Initializes and runs the application. Returns Exit code.
        /// </summary>
        /// <param name="applicationSymbolicName"> Symbolic name of the application. For game server, this should be "Server". For botclient, this should be "BotClient". </param>
        protected async Task<int> RunApplicationMainAsync(string applicationSymbolicName, string[] cmdLineArgs)
        {
            MinimalSdkInit();

            // Note: EFCore6 `dotnet ef migrations add` start the application with arguments: "--applicationName Server, ...",
            //       in which case, we exit immediately.
            if (applicationSymbolicName == "Server" && cmdLineArgs.Length >= 1 && cmdLineArgs[0] == "--applicationName")
            {
                Console.WriteLine("Detected EFCore run due to: cmdLineArgs = '{0}', exiting..", string.Join(" ", cmdLineArgs));
                await RunDummyIHostAsync();
                return 0;
            }

            if (cmdLineArgs.Length == 1 && cmdLineArgs[0] == "--MetaplayBakeForContainer")
            {
                Console.WriteLine("Baking serializer for container image");
                BakeContainerImage();
                return 0;
            }

            await PrepareAsync(applicationName: applicationSymbolicName, cmdLineArgs);
            return await RunApplicationAsync();
        }

        Type GenerateSerializer()
        {
            _logger.Information("Generating serializer..");

            string executablePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string errorPath = Path.Join(executablePath, "Errors");
            Assembly assembly = RoslynSerializerCompileCache.GetOrCompileAssembly(
                outputDir: executablePath,
                dllFileName: "Metaplay.Generated.dll",
                errorDir: errorPath,
                useMemberAccessTrampolines: false);

            return assembly.GetType("Metaplay.Generated.TypeSerializer");
        }

        static bool TryGetConsoleKey(out ConsoleKeyInfo key)
        {
            // Handle redirected input (used from nested process run under Unity or tests)
            if (Console.IsInputRedirected)
            {
                // Check for EOF
                // \note Blocking operation!
                if (Console.In.Peek() == -1)
                {
                    key = default;
                    return false;
                }

                int keyChar = Console.In.Read();
                ConsoleKey consoleKey = ConsoleKey.Escape;

                if (Enum.TryParse<ConsoleKey>(keyChar.ToString(CultureInfo.InvariantCulture).ToUpperInvariant(), out ConsoleKey ck))
                    consoleKey = ck;

                key = new ConsoleKeyInfo((char)keyChar, consoleKey, shift: false, alt: false, control: false);
                return true;
            }

            // Regular console input
            if (!Console.KeyAvailable)
            {
                key = default;
                return false;
            }

            key = Console.ReadKey();
            return true;
        }

        /// <summary>
        /// Creates a dummy IHost, starts it and then shuts it down. This can be used to simulate normal Application
        /// lifecycle.
        /// <para>
        /// This is useful with EFCore and ASP.Net Core, where not running a "proper" application IHosting lifecycle produces
        /// warnings. Running a minimal dummy IHost application hides these warnings.
        /// </para>
        /// </summary>
        static async Task RunDummyIHostAsync()
        {
            IHostBuilder builder = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IHostLifetime>(new SimpleHostLifetime());
                });
            using (IHost dummyHost = builder.Build())
            {
                await dummyHost.StartAsync();
                await dummyHost.StopAsync();
            }
        }
    }
}
