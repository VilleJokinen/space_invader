// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Akka.IO;
using Metaplay.Cloud;
using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.Entity.EntityStatusMessages;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Network;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;

namespace Metaplay.Server
{
    // ConnectionListener

    public class ConnectionListener : EntityShard
    {
        internal static Prometheus.Counter c_acceptedConnections = Prometheus.Metrics.CreateCounter("game_connections_accepted", "Number of accepted incoming connections", "port", "type");
        internal static Prometheus.Gauge   c_currentConnections  = Prometheus.Metrics.CreateGauge("game_connections_current", "Number of currently active client connections", "type");

        static readonly TimeSpan UpdateMetricsInterval = TimeSpan.FromSeconds(1);
        internal class UpdateMetrics { public static readonly UpdateMetrics Instance = new UpdateMetrics(); }

        ulong           _runningConnId  = 1;
        List<IActorRef> _listenSockets  = new List<IActorRef>();

        ClusterPhase    _clusterPhase;

        public ConnectionListener(EntityShardConfig shardConfig) : base(shardConfig)
        {
            _clusterPhase       = ClusterCoordinatorActor.Phase;

            RegisterHandlers();
        }

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
            Receive<Tcp.Bound>(ReceiveTcpBound);
            Receive<Tcp.Connected>(ReceiveTcpConnected);
            Receive<Tcp.Received>(ReceiveTcpReceived);
            Receive<Tcp.ConnectionClosed>(ReceiveTcpConnectionClosed);
            Receive<ShutdownSync>(ReceiveShutdownSync);
            Receive<UpdateMetrics>(ReceiveUpdateMetrics);
        }

        void ReceiveTcpBound(Tcp.Bound bound)
        {
            _listenSockets.Add(Sender);
        }

        void ReceiveTcpConnected(Tcp.Connected connected)
        {
            _log.Debug("New connection on {LocalAddress}", connected.LocalAddress);

            IPAddress srcAddr;
            if (connected.RemoteAddress is IPEndPoint remoteEp)
                srcAddr = remoteEp.Address;
            else
                srcAddr = IPAddress.None;

            int port = (connected.LocalAddress is IPEndPoint ipAddress) ? ipAddress.Port : -1;
            c_acceptedConnections.WithLabels(port.ToString(CultureInfo.InvariantCulture), "Tcp").Inc();

            // Only spawn connection, if client connections are enabled (connections are still accepted for readiness checks, even if disabled)
            if (IsAllowingIncomingConnections(out ProtocolStatus rejectionReason))
            {
                // Create connectionId such that it always maps back to this connection shard
                EntityId connectionId = StaticShardingStrategy.CreateEntityId(_selfShardId, _runningConnId++);
                EntityState _ = GetOrSpawnEntity(connectionId, (entityId) =>
                {
                    // \todo[jarkko]: Unify this with EntityShard.SpawnEntityActor
                    IActorRef actor = Context.ActorOf(Props.Create<TcpClientConnection>(connectionId, Sender, srcAddr, port), connectionId.ToString());
                    Context.Watch(actor);
                    actor.Tell(InitializeEntity.Instance);
                    return actor;
                });
            }
            else
            {
                // If not accepting connections, accept with self to send message and terminate connection immediately
                // \todo [petri] use another actor for denying connections?
                _log.Debug("Rejecting connection attempt: reason={Reason}!", rejectionReason);
                Sender.Tell(new Tcp.Register(_self));
                byte[] protocolHeader = WireProtocol.EncodeProtocolHeader(rejectionReason, MetaplayCore.Options.GameMagic);
                Sender.Tell(Tcp.Write.Create(ByteString.FromBytes(protocolHeader)));
                Sender.Tell(Tcp.Close.Instance);
            }
        }

        void ReceiveTcpReceived(Tcp.Received tcp)
        {
            // ignore received bytes
        }

        void ReceiveTcpConnectionClosed(Tcp.ConnectionClosed closed)
        {
            // ignore closed connections
        }

        void ReceiveShutdownSync(ShutdownSync shutdown)
        {
            // \todo [petri] implement synchronous shutdown!
            _self.Tell(PoisonPill.Instance);
            Sender.Tell(ShutdownComplete.Instance, _self);
        }

        void ReceiveUpdateMetrics(UpdateMetrics update)
        {
            c_currentConnections.WithLabels("Tcp").Set(_entityStates.Count);
        }

        protected override void PreStart()
        {
            // Listen to cluster lifecycle & maintenance mode events
            Context.System.EventStream.Subscribe(_self, typeof(ClusterPhaseUpdateEvent));

            // Update lifecycle & maintenance mode states after registering to the change notifications
            UpdateAllowConnections();

            // Listen to connections on port
            SystemOptions systemOpts = RuntimeOptionsRegistry.Instance.GetCurrent<SystemOptions>();
            _log.Info("Listening to client connections on ports {ClientPorts}", string.Join(", ", systemOpts.ClientPorts.Select(port => port.ToString(CultureInfo.InvariantCulture))));
            foreach (int port in systemOpts.ClientPorts)
            {
                IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, port);
                Context.System.Tcp().Tell(new Tcp.Bind(_self, endpoint, backlog: 1024));
            }
            foreach (int port in systemOpts.ClientPorts)
            {
                IPEndPoint endpoint = new IPEndPoint(IPAddress.IPv6Any, port);
                Context.System.Tcp().Tell(new Tcp.Bind(_self, endpoint, backlog: 1024));
            }

            // Start metrics timer
            Context.System.Scheduler.ScheduleTellRepeatedly(TimeSpan.Zero, UpdateMetricsInterval, _self, UpdateMetrics.Instance, ActorRefs.NoSender);

            base.PreStart();
        }

        protected override void PostStop()
        {
            _log.Info("Stop and unbind {NumListenSockets} sockets", _listenSockets.Count);
            foreach (IActorRef listenSocket in _listenSockets)
                listenSocket.Tell(Tcp.Unbind.Instance);
            base.PostStop();
        }
    }
}
