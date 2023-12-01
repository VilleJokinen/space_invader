// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Akka.IO;
using Metaplay.Cloud;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Entity.EntityStatusMessages;
using Metaplay.Cloud.Options;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Network;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Server.WebSockets
{
    // WebSocketListener

    [RuntimeOptions("WebSockets", true, "Configuration options for web sockets in web player builds.")]
    public class WebSocketOptions : RuntimeOptionsBase
    {
        [CommandLineAlias("-WebSocketEnable")]
        [MetaDescription("Enables WebSocket connections to the server.")]
        public bool Enabled { get; private set; } = false;
        [CommandLineAlias("-WebSocketListenHost")]
        [MetaDescription("Host/interface that the Websocket Server listens on. Setting 0.0.0.0 listens on all IPv4 interfaces, 'localhost' only allows local connections.")]
        public string ListenHost { get; private set; } = IsLocalEnvironment ? "localhost" : "0.0.0.0";
        [CommandLineAlias("-WebSocketListenPorts")]
        [MetaDescription("Ports that the Websocket Server listens on. Defaults to 9380 if not specified.")]
        public int[] ListenPorts { get; private set; } = null;

        public override Task OnLoadedAsync()
        {
            if (ListenPorts == null)
                ListenPorts = new int[] { 9380 };

            if (ListenPorts.Length != 1)
            {
                 // \todo #websocket-ports Support multiple ports in options - see WebSocketListener.ReceiveWebSocketConnected.
                 //       Grep for #websocket-ports when fixing, and fix those places as well!
                throw new InvalidOperationException($"WebSockets:{nameof(ListenPorts)} must have exactly 1 entry. Now it has {ListenPorts.Length} entries: {string.Join(", ", ListenPorts)}");
            }

            return Task.CompletedTask;
        }
    }

    [EntityConfig]
    public class WebSocketConfig : EphemeralEntityConfig
    {
        public override EntityKind        EntityKind           => EntityKindCloudCore.WebSocketConnection;
        public override Type              EntityActorType      => typeof(WebSocketClientConnection);
        public override Type              EntityShardType      => typeof(WebSocketListener);
        public override bool              AllowEntitySpawn     => false;
        public override IShardingStrategy ShardingStrategy     => new StaticShardingStrategy();
        public override TimeSpan          ShardShutdownTimeout => TimeSpan.FromSeconds(60);
    }

    public class WebSocketConnected
    {
        public IPAddress            LocalAddress   { get; private set; }
        public IPAddress            RemoteAddress  { get; private set; }
        public WebSocket            Socket         { get; private set; }
        public TaskCompletionSource SocketFinished { get; private set; }

        public WebSocketConnected(WebSocket socket, TaskCompletionSource socketFinished, IPAddress localAddress, IPAddress remoteAddress)
        {
            Socket         = socket;
            SocketFinished = socketFinished;
            LocalAddress   = localAddress;
            RemoteAddress  = remoteAddress;
        }
    }

    public class WebSocketListener : EntityShard
    {
        static readonly TimeSpan UpdateMetricsInterval = TimeSpan.FromSeconds(1);

        // WebSockets require infra v0.2.4 or above.
        static readonly DeploymentVersion MinInfraVersion = new DeploymentVersion(0, 2, 4, null);

        internal class UpdateMetrics
        {
            public static readonly UpdateMetrics Instance = new UpdateMetrics();
        }

        ulong _runningConnId = 1;
        IHost _webSocketHost;

        ClusterPhase _clusterPhase;

        public WebSocketListener(EntityShardConfig shardConfig) : base(shardConfig)
        {
            RegisterHandlers();
        }

        // \todo [nomi] Deduplicate code with ConnectionListener
        bool IsAllowingIncomingConnections(out ProtocolStatus rejectionReason)
        {
            // Allow incoming connections if cluster is running & we're not in maintenance mode.
            switch (_clusterPhase)
            {
                case ClusterPhase.Starting:
                {
                    rejectionReason = ProtocolStatus.ClusterStarting;
                    return false;
                }

                case ClusterPhase.Stopping:
                case ClusterPhase.Terminated:
                {
                    rejectionReason = ProtocolStatus.ClusterShuttingDown;
                    return false;
                }

                case ClusterPhase.Connecting:
                {
                    rejectionReason = ProtocolStatus.ClusterStarting;
                    return false;
                }

                case ClusterPhase.Running:
                {
                    // fallthru
                    break;
                }
            }

            rejectionReason = ProtocolStatus.ClusterRunning;
            return true;
        }

        void UpdateAllowConnections()
        {
            bool oldAllowConnections = IsAllowingIncomingConnections(out var _);
            bool newAllowConnections;

            _clusterPhase = ClusterCoordinatorActor.Phase;

            newAllowConnections = IsAllowingIncomingConnections(out var _);

            // If stopped allowing connections, kick everyone out
            if (oldAllowConnections && !newAllowConnections)
            {
                _log.Info("Kicking out all {NumConnections} connections", _entityStates.Count);
                foreach (EntityState entity in _entityStates.Values)
                {
                    // \todo [petri] inform actor about reason
                    RequestEntityShutdown(entity);
                }
            }
        }

        void RegisterHandlers()
        {
            Receive<ClusterPhaseUpdateEvent>(update => UpdateAllowConnections());
            ReceiveAsync<ShutdownSync>(ReceiveShutdownSync);
            Receive<UpdateMetrics>(ReceiveUpdateMetrics);
            Receive<WebSocketConnected>(ReceiveWebSocketConnected);
        }

        void ReceiveWebSocketConnected(WebSocketConnected connected)
        {
            _log.Debug("New WebSocket connection on {LocalAddress}", connected.LocalAddress);

            IPAddress srcAddr = connected.RemoteAddress;
            int localPort = RuntimeOptionsRegistry.Instance.GetCurrent<WebSocketOptions>().ListenPorts.Single(); // \todo #websocket-ports Support multiple ports in options - how to get actual port here?

            ConnectionListener.c_acceptedConnections.WithLabels(localPort.ToString(CultureInfo.InvariantCulture), "WebSocket").Inc();

            // Only spawn connection, if client connections are enabled (connections are still accepted for readiness checks, even if disabled)
            if (IsAllowingIncomingConnections(out ProtocolStatus rejectionReason))
            {
                // Create connectionId such that it always maps back to this connection shard
                EntityId connectionId = StaticShardingStrategy.CreateEntityId(_selfShardId, _runningConnId++);

                EntityState _ = GetOrSpawnEntity(connectionId, (entityId) =>
                {
                    // \todo[jarkko]: Unify this with EntityShard.SpawnEntityActor
                    IActorRef actor = Context.ActorOf(Props.Create<WebSocketClientConnection>(connectionId, srcAddr, localPort, connected.Socket, connected.SocketFinished), connectionId.ToString());
                    Context.Watch(actor);
                    actor.Tell(InitializeEntity.Instance);
                    return actor;
                });
            }
            else
            {
                // If not accepting connections, accept with self to send message and terminate connection immediately
                _log.Debug("Rejecting WebSocket connection attempt: reason={Reason}!", rejectionReason);
                byte[] protocolHeader = WireProtocol.EncodeProtocolHeader(rejectionReason, MetaplayCore.Options.GameMagic);
                _ = connected.Socket
                    .SendAsync(protocolHeader, WebSocketMessageType.Binary, true, CancellationToken.None)
                    .ContinueWith(async _ =>
                    {
                        try
                        {
                            await connected.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        }
                        finally
                        {
                            connected.SocketFinished.SetResult();
                        }
                    }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }

        async Task ReceiveShutdownSync(ShutdownSync shutdown)
        {
            if (_webSocketHost != null)
            {
                _log.Info("Stopping WebSocket Listener Server");

                // Stop the host
                TimeSpan timeoutUntilGracefulShutdownBecomesForceShutdown = TimeSpan.FromSeconds(2);
                await _webSocketHost.StopAsync(timeoutUntilGracefulShutdownBecomesForceShutdown);
                _webSocketHost.Dispose();
                _webSocketHost = null;
            }

            _self.Tell(PoisonPill.Instance);
            Sender.Tell(ShutdownComplete.Instance, _self);
        }

        void ReceiveUpdateMetrics(UpdateMetrics update)
        {
            ConnectionListener.c_currentConnections.WithLabels("WebSocket").Set(_entityStates.Count);
        }

        protected override async Task InitializeAsync()
        {
            WebSocketOptions options = RuntimeOptionsRegistry.Instance.GetCurrent<WebSocketOptions>();
            if (options.Enabled)
            {
                // Check for required infra versions
                // \note These can be removed when SDK requires same or higher versions
                DeploymentOptions deployOpts = RuntimeOptionsRegistry.Instance.GetCurrent<DeploymentOptions>();
                if (deployOpts.InfrastructureVersion != null)
                {
                    DeploymentVersion infraVersion = DeploymentVersion.ParseFromString(deployOpts.InfrastructureVersion);
                    if (infraVersion < MinInfraVersion)
                        throw new InvalidOperationException($"Infrastructure version ({infraVersion}) is too old for WebSockets, minimum required is {MinInfraVersion}");
                }

                // Listen to cluster lifecycle & maintenance mode events
                Context.System.EventStream.Subscribe(_self, typeof(ClusterPhaseUpdateEvent));

                // Update lifecycle & maintenance mode states after registering to the change notifications
                UpdateAllowConnections();

                _webSocketHost = CreateHostBuilder(new string[] { }).Build();
                await _webSocketHost.StartAsync();

                // Start metrics timer
                Context.System.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, UpdateMetricsInterval, _self, UpdateMetrics.Instance, ActorRefs.NoSender);
            }

            await base.InitializeAsync();
        }

        protected override void PostStop()
        {
            if (_webSocketHost != null)
            {
                _log.Info("Stopping WebSocket server immediately");

                _webSocketHost.StopAsync(timeout: TimeSpan.Zero).ConfigureAwait(false).GetAwaiter().GetResult();
                _webSocketHost.Dispose();
                _webSocketHost = null;
            }

            base.PostStop();
        }


        public IHostBuilder CreateHostBuilder(string[] args)
        {
            IActorRef self = _self;
            return Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureWebHostDefaults(
                    webBuilder =>
                    {
                        webBuilder.UseStartup<WebSocketStartup>();

                        // Configure listen host/port
                        // \note only using HTTP as HTTPS is terminated in the load balancer
                        WebSocketOptions webSocketOptions = RuntimeOptionsRegistry.Instance.GetCurrent<WebSocketOptions>();

                        string urls = Invariant($"http://{webSocketOptions.ListenHost}:{webSocketOptions.ListenPorts.Single()}"); // \todo #websocket-ports Support multiple ports in options.
                        _log.Info($"Binding WebSocket server to listen on {urls}");
                        webBuilder.UseUrls(urls);
                    })
                .ConfigureServices(
                    services =>
                    {
                        // Register self for dependency injection
                        services.AddSingleton(self);
                        services.AddSingleton<IHostLifetime>(new SimpleHostLifetime());
                    });
        }
    }
}
