// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Util.Internal;
using Metaplay.Cloud;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Server
{
    [MetaMessage(MessageCodesCore.DiagnosticToolPing, MessageDirection.ServerInternal)]
    public class DiagnosticToolPing : MetaMessage
    {
        public int PingId { get; set; }

        public DiagnosticToolPing() { }
        public DiagnosticToolPing(int pingId) { PingId = pingId; }
    }

    [MetaMessage(MessageCodesCore.DiagnosticToolPong, MessageDirection.ServerInternal)]
    public class DiagnosticToolPong : MetaMessage
    {
        public int PingId { get; set; }

        public DiagnosticToolPong() { }
        public DiagnosticToolPong(int pingId) { PingId = pingId; }
    }

    #region EntityAskTesting

    [MetaMessage(MessageCodesCore.TestEntityAskFailureRequest, MessageDirection.ServerInternal)]
    public class TestEntityAskFailureRequest : MetaMessage
    {
        public bool Controlled { get; private set; }

        TestEntityAskFailureRequest() { }
        public TestEntityAskFailureRequest(bool controlled)
        {
            Controlled = controlled;
        }
    }

    [MetaMessage(MessageCodesCore.TestAsyncHandlerEntityAskFailureRequest, MessageDirection.ServerInternal)]
    public class TestAsyncHandlerEntityAskFailureRequest : MetaMessage
    {
        public bool Controlled { get; private set; }

        TestAsyncHandlerEntityAskFailureRequest() { }
        public TestAsyncHandlerEntityAskFailureRequest(bool controlled)
        {
            Controlled = controlled;
        }
    }

    #endregion

    [EntityConfig]
    internal sealed class DiagnosticToolConfig : EphemeralEntityConfig
    {
        public override EntityKind          EntityKind              => EntityKindCloudCore.DiagnosticTool;
        public override Type                EntityActorType         => typeof(DiagnosticToolActor);
        public override IShardingStrategy   ShardingStrategy        => new ServiceShardingStrategy(EntityKindCloudCore.DiagnosticTool);
        public override TimeSpan            ShardShutdownTimeout    => TimeSpan.FromSeconds(5);
    }

    public class DiagnosticToolActor : EphemeralEntityActor
    {
        // Metrics

        static readonly Prometheus.HistogramConfiguration c_pingDurationConfig = new Prometheus.HistogramConfiguration
        {
            Buckets     = new double[] { 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1.0, 2.0, 5.0 },
            LabelNames  = new string[] { "peer" },
        };

        static readonly Prometheus.Counter      c_pingsSent     = Prometheus.Metrics.CreateCounter("metaplay_diagnostics_pings_sent_total", "Cumulative number of pings sent, by peer", "peer");
        static readonly Prometheus.Counter      c_pingsReceived = Prometheus.Metrics.CreateCounter("metaplay_diagnostics_pings_received_total", "Cumulative number of pings received, by peer", "peer");
        static readonly Prometheus.Counter      c_pingTimeouts  = Prometheus.Metrics.CreateCounter("metaplay_diagnostics_ping_timeouts_total", "Cumulative number of ping timeouts, by peer", "peer");
        static readonly Prometheus.Histogram    c_pingDurations = Prometheus.Metrics.CreateHistogram("metaplay_diagnostics_ping_duration", "Duration of successful EntityAsks by message type", c_pingDurationConfig);

        /// <summary>
        /// State of a peer node, including its <see cref="ClusterNodeAddress"/> and <see cref="EntityId"/>.
        /// </summary>
        class PeerState
        {
            public readonly ClusterNodeAddress  Address;
            public readonly EntityId            EntityId;

            public PeerState(ClusterNodeAddress address, EntityId entityId)
            {
                Address = address;
                EntityId = entityId;
            }

            public override string ToString() => $"DiagnosticTool.Peer({Address}, {EntityId})";
        }

        /// <summary>
        /// State of a sent ping message to a peer node.
        /// </summary>
        public class PingState
        {
            public readonly int         PingId;
            public readonly EntityId    PeerId;
            public readonly MetaTime    PingSentAt;

            public PingState(int pingId, EntityId peerId, MetaTime pingSentAt)
            {
                PingId = pingId;
                PeerId = peerId;
                PingSentAt = pingSentAt;
            }
        }

        // Members

        PeerState[]                 _peers;
        int                         _nextPingId     = 100;
        Dictionary<int, PingState>  _activePings    = new Dictionary<int, PingState>();

        const int                       MaxPeersToPing      = 10;
        static readonly MetaDuration    PingExpireDuration  = MetaDuration.FromSeconds(10);

        protected override sealed AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        public DiagnosticToolActor(EntityId entityId) : base(entityId)
        {
            // Resolve all peers
            List<ClusterNodeAddress> addresses = Application.Instance.ClusterConfig.GetNodesForEntityKind(EntityKindCloudCore.DiagnosticTool);
            _peers =
                Enumerable.Range(0, addresses.Count)
                .Select(ndx =>
                    new PeerState(
                        address:    addresses[ndx],
                        entityId:   EntityId.Create(EntityKindCloudCore.DiagnosticTool, (uint)ndx)
                    )
                )
                //.Where(peerId => peerId != _entityId)
                .ToArray();

            //_log.Info("Peers: {Peers}", string.Join(", ", _peers));
        }

        protected override Task Initialize()
        {
            // Start tick timer
            StartRandomizedPeriodicTimer(TimeSpan.FromSeconds(1), ActorTick.Instance);

            return Task.CompletedTask;
        }

        protected override async Task OnShutdown()
        {
            await base.OnShutdown();
        }

        string GetPeerName(EntityId peerId)
        {
            if (peerId == _entityId)
                return "self"; // \note converting own name to self, to separate local vs remote pings
            else
                return peerId.ToString(); // \todo [petri] return node names instead?
        }

        ClusterNodeAddress GetPeerAddress(EntityId entityId) =>
            _peers.FirstOrDefault(peer => peer.EntityId == entityId)?.Address;

        [CommandHandler]
        void HandleActorTick(ActorTick _)
        {
            // Clean up old pings
            MetaTime now = MetaTime.Now;
            MetaTime pingExpireAt = now - PingExpireDuration;
            List<PingState> expiredPings = _activePings.Values.Where(state => state.PingSentAt < pingExpireAt).ToList();
            foreach (PingState state in expiredPings)
            {
                _log.Warning("Expired ping #{PingId} to {PeerId} on {PeerAddress}, sent {SentAgo} ago", state.PingId, state.PeerId, GetPeerAddress(state.PeerId), now - state.PingSentAt);
                c_pingTimeouts.WithLabels(GetPeerName(state.PeerId)).Inc();
                _activePings.Remove(state.PingId);
            }

            // Ping set of random peers
            RandomPCG rnd = RandomPCG.CreateNew();
            _peers
                .Shuffle(rnd)
                .Take(MaxPeersToPing)
                .ForEach(peer =>
                {
                    int pingId = _nextPingId++;
                    PingState pingState = new PingState(pingId, peer.EntityId, now);
                    _activePings.Add(pingId, pingState);

                    c_pingsSent.WithLabels(GetPeerName(peer.EntityId)).Inc();

                    CastMessage(peer.EntityId, new DiagnosticToolPing(pingId));
                });
        }

        [MessageHandler]
        void HandlePing(EntityId fromEntityId, DiagnosticToolPing ping)
        {
            c_pingsReceived.WithLabels(GetPeerName(fromEntityId)).Inc();

            //_log.Debug("Received ping #{PingId} from {PeerId}, replying..", ping.PingId, fromEntityId);
            CastMessage(fromEntityId, new DiagnosticToolPong(ping.PingId));
        }

        [MessageHandler]
        void HandlePong(EntityId fromEntityId, DiagnosticToolPong pong)
        {
            if (_activePings.Remove(pong.PingId, out PingState state))
            {
                MetaDuration duration = MetaTime.Now - state.PingSentAt;
                c_pingDurations.WithLabels(GetPeerName(fromEntityId)).Observe(duration.ToSecondsDouble());
                //_log.Debug("Received pong #{PingId} from {PeerId}, duration={Duration}", state.PingId, fromEntityId, duration);
            }
            else
            {
                _log.Warning("Received pong from {PeerId} to unknown ping #{PingId}", fromEntityId, pong.PingId);
            }
        }

        [EntityAskHandler]
        public Task<EntityAskOk> HandleTestEntityAskFailureRequest(TestEntityAskFailureRequest request)
        {
            if (request.Controlled)
                throw new InvalidEntityAsk("Controlled exception");
            else
                throw new Exception("Uncontrolled exception");
        }

        [EntityAskHandler]
        public async Task<EntityAskOk> HandleTestAsyncHandlerEntityAskFailureRequest(TestAsyncHandlerEntityAskFailureRequest request)
        {
            await Task.Yield();
            if (request.Controlled)
                throw new InvalidEntityAsk("Controlled exception");
            else
                throw new Exception("Uncontrolled exception");
        }

    }
}
