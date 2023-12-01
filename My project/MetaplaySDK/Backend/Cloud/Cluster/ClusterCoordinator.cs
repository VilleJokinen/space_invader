// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Akka.Remote;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Cloud.Cluster
{
    /// <summary>
    /// Physical address of a cluster node (hostname and port).
    /// </summary>
    public class ClusterNodeAddress : IEquatable<ClusterNodeAddress>
    {
        public readonly string  HostName;
        public readonly int     Port;

        public ClusterNodeAddress(string hostName, int port)
        {
            HostName = hostName;
            Port = port;
        }

        public bool Equals(ClusterNodeAddress other) => (HostName == other.HostName) && (Port == other.Port);

        public static bool operator ==(ClusterNodeAddress a, ClusterNodeAddress b) => a.Equals(b);
        public static bool operator !=(ClusterNodeAddress a, ClusterNodeAddress b) => !a.Equals(b);

        public override int     GetHashCode ()              => HostName.GetHashCode() + Port;
        public override bool    Equals      (object obj)    => (obj is ClusterNodeAddress addr) ? Equals(addr) : false;
        public override string  ToString    ()              => Invariant($"{HostName}:{Port}");
    }

    /// <summary>
    /// Represents the lifecycle phase that a given <c>ClusterService</c> is in at the moment.
    /// </summary>
    public enum ClusterServicePhase
    {
        Waiting,
        //Starting,
        Running,
        //Stopping,
        Stopped
    }

    /// <summary>
    /// Represents a lifecycle phase in which the cluster should be started or shut down.
    /// Each phase represents an action or EntityShard which should be completed on all
    /// nodes before continuing further.
    /// </summary>
    public interface IClusterService
    {
        string Name { get; }

        Task Start(); // \todo [petri] timeout, cts
        Task Stop(); // \todo [petri] timeout, cts
    }

    /// <summary>
    /// Current lifecycle phase that the cluster is in.
    /// </summary>
    /// <remarks>The status only goes in one direction: Connecting -> Starting -> Running -> Stopping -> Terminated</remarks>
    public enum ClusterPhase
    {
        Connecting,     // Cluster is waiting for all nodes to report in
        Starting,       // Cluster entity/service shards are being started, new nodes can join instantly
        Running,        // Cluster is in a running state, shards on new nodes don't need to wait
        Stopping,       // Cluster services are being stopped
        Terminated,     // All cluster services have been stopped, ready to exit
    }

    /// <summary>
    /// Inform cluster peers about our current cluster & service states.
    /// </summary>
    public class ClusterUpdateNodeState
    {
        public ClusterPhase             ClusterPhase                { get; private set; }   // Lifecycle phase of whole cluster (can only progress forward)
        public int                      NumClusterServicesRunning   { get; private set; }   // Number of globally confirmed services running on the cluster (can only progress forward)
        public int                      NumNodeServicesRunning      { get; private set; }   // Number of services running on the sending node
        public ClusterServicePhase[]    ServiceStates               { get; private set; }   // State of each service on the sending node (\todo [petri] should not be needed?)
        public string                   Cookie                      { get; private set; }   // Optional cluster cookie (only allow connections if cookies match), null means ignore

        public ClusterUpdateNodeState(ClusterPhase clusterPhase, int numClusterServicesRunning, int numNodeServicesRunning, ClusterServicePhase[] serviceStates, string cookie)
        {
            ClusterPhase                = clusterPhase;
            NumClusterServicesRunning   = numClusterServicesRunning;
            NumNodeServicesRunning      = numNodeServicesRunning;
            ServiceStates               = serviceStates;
            Cookie                      = cookie;
        }
    }

    /// <summary>
    /// Published event within the node to inform other actors about cluster status changes.
    /// </summary>
    public class ClusterPhaseUpdateEvent
    {
        public readonly ClusterPhase Phase;

        public ClusterPhaseUpdateEvent(ClusterPhase status) => Phase = status;
    }

    public class ClusterNodeLostEvent
    {
        public readonly ClusterNodeAddress Address;

        public ClusterNodeLostEvent(ClusterNodeAddress address) => Address = address;
    }

    /// <summary>
    /// Coordinates cluster start/shutdown sequence.
    /// </summary>
    public class ClusterCoordinatorActor : MetaReceiveActor
    {
        public class TryProgress
        {
            public static TryProgress Instance { get; } = new TryProgress();
        }

        static Prometheus.Gauge c_clusterPhase              = Prometheus.Metrics.CreateGauge("metaplay_cluster_phase", "Metaplay cluster phase", "phase");
        static Prometheus.Gauge c_clusterExpectedNodes      = Prometheus.Metrics.CreateGauge("metaplay_cluster_expected_nodes_current", "Number of expected nodes in the cluster");
        static Prometheus.Gauge c_clusterNodesConnected     = Prometheus.Metrics.CreateGauge("metaplay_cluster_connected_nodes_current", "Number of connected-to nodes in the cluster");
        static Prometheus.Gauge c_clusterServicesRunning    = Prometheus.Metrics.CreateGauge("metaplay_cluster_services_running_current", "Number of cluster services running on this node");
        // \todo [petri] SHARD: more relevant metrics, especially node health-related ones

        const string CoordinatorName = "cluster";

        readonly string                 _actorSystemName;
        readonly ClusterNodeAddress     _selfAddress;
        readonly ClusterState           _clusterState;
        readonly string                 _cookie;
        IClusterService[]               _services       = null;
        ClusterServicePhase[]           _serviceStates  = null;

        public static bool              IsLeaderNode    { get; private set; }
        string                          Role            => IsLeaderNode ? "leader" : "follower";

        static ManualResetEventSlim     s_clusterShutdownRequested  = new ManualResetEventSlim(false);
        static IActorRef                s_instance                  = null;

        static volatile ClusterPhase    s_phase                     = ClusterPhase.Connecting;
        int                             _numClusterServicesRunning  = 0;
        public static ClusterPhase      Phase                       => s_phase;

        static readonly TimeSpan        TickInterval                = TimeSpan.FromMilliseconds(5_000);
        ICancelable                     _cancelUpdateTimer          = new Cancelable(Context.System.Scheduler);

        public class InitiateShutdownRequest { public static readonly InitiateShutdownRequest Instance = new InitiateShutdownRequest(); }

        public class ClusterStateRequest { public static ClusterStateRequest Instance => new ClusterStateRequest(); }
        public class ClusterStateResponse
        {
            public ClusterPhase ClusterPhase        { get; private set; }
            public int          NumTotalNodes       { get; private set; }
            public int          NumNodesConnected   { get; private set; }

            public ClusterStateResponse(ClusterPhase clusterPhase, int numTotalNodes, int numNodesConnected)
            {
                ClusterPhase        = clusterPhase;
                NumTotalNodes       = numTotalNodes;
                NumNodesConnected   = numNodesConnected;
            }
        }

        public ClusterCoordinatorActor(string actorSystemName, ClusterNodeAddress selfAddress, ClusterConfig config, List<IClusterService> clusterServices, string cookie)
        {
            _actorSystemName    = actorSystemName;
            _selfAddress        = selfAddress;
            _clusterState       = new ClusterState(config, selfAddress);
            _cookie             = cookie;

            // Store services
            _services       = clusterServices.ToArray();
            _serviceStates  = new ClusterServicePhase[_services.Length];

            // First node of first nodeSet is the leader node
            bool isMember = config.NodeSets[0].ResolveShardIndex(selfAddress, out int shardIndex);
            IsLeaderNode = isMember && (shardIndex == 0);

            RegisterHandlers();
        }

        public static bool IsReady()
        {
            return s_phase == ClusterPhase.Running;
        }

        public static void RequestClusterShutdown()
        {
            // Mark shutdown as requested (required in case actor is not yet up)
            s_clusterShutdownRequested.Set();

            // If instance is already up, send request to start shutdown sequence
            if (s_instance != null)
                s_instance.Tell(InitiateShutdownRequest.Instance);
        }

        public static async Task<ClusterStateResponse> RequestStatusAsync(IActorRef actor)
        {
            return await actor.Ask<ClusterStateResponse>(ClusterStateRequest.Instance);
        }

        public static async Task WaitForClusterConnectedAsync(IActorRef actor, IMetaLogger logger)
        {
            // \todo [petri] configurable timeout
            const int MaxIters = 5 * 60 * 10; // 5min (assuming 100ms iters)
            for (int iter = 0; iter < MaxIters; iter++)
            {
                ClusterStateResponse state = await RequestStatusAsync(actor);
                if (state.ClusterPhase == ClusterPhase.Connecting)
                {
                    if ((iter % 10) == 0)
                        logger.Debug("Waiting for cluster nodes to connect ({NumNodesConnected}/{NumTotalNodes} connected)..", state.NumNodesConnected, state.NumTotalNodes);
                }
                else
                {
                    logger.Information("Cluster state={Status} with {NumNodesConnected}/{NumTotalNodes} nodes connected!", state.ClusterPhase, state.NumNodesConnected, state.NumTotalNodes);
                    return;
                }

                // Sleep a bit before trying again
                await Task.Delay(100);
            }

            throw new InvalidOperationException("Timeout while waiting for cluster to spin up, giving up!");
        }

        void UpdateClusterState(ClusterPhase peerPhase)
        {
            // If peer has a newer status status than we do, switch to it!
            ClusterPhase oldPhase = s_phase;
            ClusterPhase newPhase = oldPhase;
            if ((int)peerPhase > (int)oldPhase)
            {
                _log.Information("Changing status from {OldPhase} to {NewPhase} due to peer!", oldPhase, peerPhase);
                newPhase = peerPhase;
            }

            // When all nodes connected to self, start initializing ClusterServices
            if (newPhase == ClusterPhase.Connecting)
            {
                if (_clusterState.GetNumConnectedNodes() == _clusterState.GetNumTotalNodes())
                {
                    _log.Information("Connection established to all cluster peers");
                    newPhase = ClusterPhase.Starting;
                }
            }

            // Make sure we're in updated state
            SetClusterPhase(newPhase, reason: "Peer");
        }

        void SetClusterPhase(ClusterPhase newPhase, string reason)
        {
            // If status changed, publish event
            ClusterPhase oldPhase = s_phase;
            if (newPhase != oldPhase)
            {
                _log.Information("<{Role}> Switching phase from {OldPhase} to {NewPhase} (reason={Reason})", Role, oldPhase, newPhase, reason);
                s_phase = newPhase;

                // Send notifications to listeners
                Context.System.EventStream.Publish(new ClusterPhaseUpdateEvent(newPhase));

                // Inform peers of phase changes
                SendStateToPeers();
            }
        }

        void SendStateToPeers()
        {
            //_log.Debug("Sending state to peers: NumClusterServicesRunning={NumClusterServicesRunning}, NumLocalServicesRunning={NumLocalServicesRunning}", _numClusterServicesRunning, _serviceStates.Count(phase => phase == ClusterServicePhase.Running));

            // Send own state to connected peers
            foreach (RuntimeNodeSetState nodeSetState in _clusterState.NodeSets)
            {
                foreach (RuntimeNodeState nodeState in nodeSetState.NodeStates)
                {
                    if (!nodeState.IsSelf && nodeState.IsConnected)
                        SendStatusMessage(nodeState.Address, includeCookie: false);
                }
            }
        }

        void RegisterHandlers()
        {
            Receive<AssociatedEvent>(ReceiveAssociatedEvent);
            Receive<DisassociatedEvent>(ReceiveDisassociatedEvent);
            Receive<RemotingLifecycleEvent>(ReceiveRemotingLifecycleEvent);
            ReceiveAsync<ActorTick>(ReceiveActorTick);
            ReceiveAsync<TryProgress>(ReceiveTryProgress);
            ReceiveAsync<InitiateShutdownRequest>(ReceiveInitiateShutdownRequest);
            ReceiveAsync<ClusterUpdateNodeState>(ReceiveClusterUpdateNodeState);
            Receive<ClusterStateRequest>(ReceiveClusterStateRequest);
        }

        void ReceiveAssociatedEvent(AssociatedEvent associated)
        {
            // Try to immediately connect to remote node
            Address address = associated.RemoteAddress;
            _log.Information("<{Role}> Associated with {RemoteAddress}: {Event}", Role, address, associated);
            RuntimeNodeState nodeState = _clusterState.GetNodeState(new ClusterNodeAddress(address.Host, address.Port.Value));
            TryConnect(nodeState);
        }

        void ReceiveDisassociatedEvent(DisassociatedEvent disassociated)
        {
            // Handle node disconnects
            _log.Warning("<{Role}> DisassociatedEvent for {RemoteAddress}: {Event}", Role, disassociated.RemoteAddress, disassociated);
            ClusterNodeAddress clusterAddress = new ClusterNodeAddress(disassociated.RemoteAddress.Host, disassociated.RemoteAddress.Port.Value);
            RuntimeNodeState nodeState = _clusterState.GetNodeState(clusterAddress);
            if (nodeState.IsConnected)
            {
                // Mark node as disconnected
                nodeState.SetDisconnected();

                // Send cluster event
                Context.System.EventStream.Publish(new ClusterNodeLostEvent(clusterAddress));
            }
        }

        void ReceiveRemotingLifecycleEvent(RemotingLifecycleEvent lifecycle)
        {
            _log.Information("********** Lifecycle event {EventType}: {Event}", lifecycle.GetType().Name, lifecycle.ToString());
        }

        async Task ReceiveActorTick(ActorTick tick)
        {
            // Report metrics
            int numConnectedNodes = _clusterState.GetNumConnectedNodes();
            int numTotalNodes = _clusterState.GetNumTotalNodes();
            _log.Information("<{Role}> Tick: phase={Phase}, {NumConnectedNodes}/{NumTotalNodes} nodes connected", Role, s_phase, numConnectedNodes, numTotalNodes);
            foreach (ClusterPhase phase in EnumUtil.GetValues<ClusterPhase>())
                c_clusterPhase.WithLabels(phase.ToString()).Set(s_phase == phase ? 1.0 : 0.0);
            c_clusterExpectedNodes.Set(numTotalNodes);
            c_clusterNodesConnected.Set(numConnectedNodes);
            c_clusterServicesRunning.Set(_serviceStates.Count(phase => phase == ClusterServicePhase.Running));

            // Update connections to peers (except if already terminated)
            if (s_phase != ClusterPhase.Terminated)
                TryConnect();

            // Try to progress cluster lifecycle
            await ProgressClusterStateAsync();
        }

        async Task ReceiveTryProgress(TryProgress _)
        {
            // Try to progress cluster lifecycle
            await ProgressClusterStateAsync();
        }

        async Task ReceiveInitiateShutdownRequest(InitiateShutdownRequest shutdown)
        {
            // Force phase to shutting down (propagates to all peers)
            SetClusterPhase(ClusterPhase.Stopping, reason: "ShutdownRequested");

            // Start shutting down services
            await ProgressClusterStateAsync().ConfigureAwait(false);
        }

        async Task ReceiveClusterUpdateNodeState(ClusterUpdateNodeState peerState)
        {
            Address address = Sender.Path.Address;

            // Check if newly discovered node
            RuntimeNodeState nodeState = _clusterState.GetNodeState(new ClusterNodeAddress(address.Host, address.Port.Value));
            if (!nodeState.IsConnected)
            {
                _log.Information("<{Role}> New node connected: {Node}, ClusterPhase={ClusterPhase}, NumClusterServicesRunning={NumClusterServicesRunning}, NumNodeServicesRunning={NumNodeServicesRunning}, Cookie={Cookie}", Role, Sender, peerState.ClusterPhase, peerState.NumClusterServicesRunning, peerState.NumNodeServicesRunning, peerState.Cookie);

                // Check for cookie match
                if (!string.IsNullOrEmpty(peerState.Cookie) && peerState.Cookie != _cookie)
                {
                    _log.Warning("Peer node {Node} connected with mismatched cookie (used {PeerCookie}, own {OwnCookie}), ignoring peer", Sender, peerState.Cookie, _cookie);
                    return;
                }
            }
            else
                _log.Information("<{Role}> Received node state update from {Node}: ClusterPhase={ClusterPhase}, NumClusterServicesRunning={NumClusterServicesRunning} NumNodeServicesRunning={NumNodeServicesRunning}", Role, Sender, peerState.ClusterPhase, peerState.NumClusterServicesRunning, peerState.NumNodeServicesRunning);

            // If they included cookie, it's a new connection, so respond immediately
            if (peerState.Cookie != null)
                SendStatusMessage(nodeState.Address, includeCookie: false);

            // Store node state (also sets IsConnected)
            nodeState.UpdateState(peerState.ClusterPhase, peerState.NumNodeServicesRunning, peerState.ServiceStates);

            // Update globally running services
            if (s_phase == ClusterPhase.Starting || s_phase == ClusterPhase.Running)
                _numClusterServicesRunning = Math.Max(_numClusterServicesRunning, peerState.NumClusterServicesRunning);
            else if (s_phase == ClusterPhase.Stopping || s_phase == ClusterPhase.Terminated)
                _numClusterServicesRunning = Math.Min(_numClusterServicesRunning, peerState.NumClusterServicesRunning);

            // Try to make lifecycle progress
            await ProgressClusterStateAsync();

            // Update cluster status according to peer
            // \todo [petri] SHARD: combine this with ProgressClusterState() somehow?
            UpdateClusterState(peerState.ClusterPhase);
        }

        void ReceiveClusterStateRequest(ClusterStateRequest req)
        {
            Sender.Tell(new ClusterStateResponse(s_phase, _clusterState.GetNumTotalNodes(), _clusterState.GetNumConnectedNodes()));
        }

        protected override void PreStart()
        {
            base.PreStart();

            // Store global instance
            if (s_instance != null)
                throw new InvalidOperationException($"Singleton instance of ClusterCoordinatorActor already registered!");
            s_instance = _self;

            // Start update timer
            Context.System.Scheduler.ScheduleTellRepeatedly(TimeSpan.FromSeconds(1), TickInterval, _self, ActorTick.Instance, ActorRefs.NoSender, _cancelUpdateTimer);

            // Subscribe to Akka.Remote Association events (Associated, Disassociated, AssociationError)
            Context.System.EventStream.Subscribe(_self, typeof(RemotingLifecycleEvent));
        }

        protected override void PostStop()
        {
            // Cancel updater
            _cancelUpdateTimer.Cancel();

            // Unsubscribe from Akka.Remote Association events (Associated, Disassociated, AssociationError)
            Context.System.EventStream.Unsubscribe(_self, typeof(RemotingLifecycleEvent));

            // Clear global instance
            s_instance = null;

            base.PostStop();
            _log.Information("<{Role}> Cluster coordinator stopped", Role);
        }

        // \todo [petri] resolve ActorRef ?
        public ActorSelection GetRemoteCoordinatorActorSelection(ClusterNodeAddress address) =>
            Context.System.ActorSelection(Invariant($"akka.tcp://{_actorSystemName}@{address.HostName}:{address.Port}/user/{CoordinatorName}"));

        void SendStatusMessage(ClusterNodeAddress targetAddress, bool includeCookie)
        {
            ActorSelection selection = GetRemoteCoordinatorActorSelection(targetAddress);
            int numLocalServicesRunning = _serviceStates.Count(phase => phase == ClusterServicePhase.Running);
            selection.Tell(new ClusterUpdateNodeState(s_phase, _numClusterServicesRunning, numLocalServicesRunning, _serviceStates, includeCookie ? _cookie : null));
        }

        int GetClusterServicesStartedOnAllNodes()
        {
            // \note This computes the actually number of running cluster services based on actual node states.
            // The value can get overwritten if the cluster state has progressed further than an individual node
            // (can happen due to node resets).

            // Resolve the minimum number of ClusterServices started on each node (including self)
            int numLocalServicesRunning = _serviceStates.Count(phase => phase == ClusterServicePhase.Running);
            int numGlobalServicesRunning =
                _clusterState.NodeSets
                    .SelectMany(nodeSet => nodeSet.NodeStates)
                    .Where(nodeState => !nodeState.IsSelf)
                    .Select(nodeState => nodeState.NumServicesRunning)
                    .Append(numLocalServicesRunning)
                    .Min();
            return numGlobalServicesRunning;
        }

        int GetClusterServicesStartedOnAnyNode()
        {
            // \note This computes the actually number of running cluster services based on actual node states.
            // The value can get overwritten if the cluster state has progressed further than an individual node
            // (can happen due to node resets).

            // Resolve the maximum number of ClusterServices started on any node (including self)
            int numLocalServicesRunning = _serviceStates.Count(phase => phase == ClusterServicePhase.Running);
            int numGlobalServicesRunning =
                _clusterState.NodeSets
                    .SelectMany(nodeSet => nodeSet.NodeStates)
                    .Where(nodeState => !nodeState.IsSelf)
                    .Select(nodeState => nodeState.NumServicesRunning)
                    .Append(numLocalServicesRunning)
                    .Max();
            return numGlobalServicesRunning;
        }

        async Task ProgressClusterStateAsync()
        {
            int oldNumClusterServicesRunning = _numClusterServicesRunning;
            bool stateUpdated = false;

            switch (s_phase)
            {
                case ClusterPhase.Connecting:
                    // When established connection to all peers, switch to Starting phase
                    if (_clusterState.GetNumConnectedNodes() == _clusterState.GetNumTotalNodes())
                    {
                        _log.Information("<{Role}> Connection established to all cluster peers", Role);
                        SetClusterPhase(ClusterPhase.Starting, reason: "PeersConnected");
                        stateUpdated = true;
                    }
                    break;

                case ClusterPhase.Starting:
                case ClusterPhase.Running:
                    // Handle shutdown request
                    if (s_clusterShutdownRequested.IsSet && s_phase < ClusterPhase.Stopping)
                    {
                        // Force phase to shutting down (propagates to all peers)
                        SetClusterPhase(ClusterPhase.Stopping, reason: "ShutdownRequested");
                        stateUpdated = true;
                    }
                    else
                    {
                        // Allow starting one more service beyond what is globally started (running on all ones)
                        for (int svcNdx = 0; svcNdx < Math.Min(_numClusterServicesRunning + 1, _services.Length); svcNdx++)
                        {
                            if (_serviceStates[svcNdx] == ClusterServicePhase.Waiting)
                            {
                                IClusterService svc = _services[svcNdx];
                                _log.Information("Starting ClusterService {ClusterService}", svc.Name);

                                try
                                {
                                    await svc.Start();

                                    // Mark service as Running (and inform peers)
                                    _serviceStates[svcNdx] = ClusterServicePhase.Running;
                                    stateUpdated = true;
                                }
                                catch (Exception ex)
                                {
                                    // If only a single error, print that.
                                    Exception printableException;
                                    if (ex is EntityShardStartException shardStartException && shardStartException.Exceptions.Length == 1)
                                        printableException = shardStartException.Exceptions[0];
                                    else
                                        printableException = ex;

                                    _log.Error("Failed to start ClusterService {ClusterService}: {Exception}", svc.Name, printableException);
                                    stateUpdated = true;

                                    // Exit immediately and wait for process to get restarted
                                    Application.Application.ForceTerminate(exitCode: 100 + svcNdx, reason: Invariant($"Failed to start ClusterService #{svcNdx}: {svc.Name}"));
                                }
                            }
                        }

                        // Update number of ClusterServices started on all nodes (wait for service to start on all nodes before progressing)
                        _numClusterServicesRunning = Math.Max(_numClusterServicesRunning, GetClusterServicesStartedOnAllNodes());
                    }
                    break;

                case ClusterPhase.Stopping:
                    // Stop ClusterServices up to one beyond what is currently agreed state of cluster
                    int targetIndex = Math.Max(0, _numClusterServicesRunning - 1);
                    _log.Information("Terminating down to index {TargetIndex} (of {TotalServices})", targetIndex, _services.Length);
                    for (int svcNdx = _services.Length - 1; svcNdx >= targetIndex; svcNdx--)
                    {
                        if (_serviceStates[svcNdx] == ClusterServicePhase.Running)
                        {
                            IClusterService svc = _services[svcNdx];
                            _log.Information("Stopping ClusterService {ClusterService}..", svc.Name);
                            try
                            {
                                await svc.Stop();
                                _log.Information("Stopped ClusterService {ClusterService}", svc.Name);
                            }
                            catch (Exception ex)
                            {
                                _log.Warning("Failed to stop ClusterService {ClusterService}: {Exception}", svc.Name, ex);
                            }

                            // Mark service as Stopped (and inform peers)
                            _serviceStates[svcNdx] = ClusterServicePhase.Stopped;
                            stateUpdated = true;
                        }
                    }

                    // Update number of ClusterServices started on any node (wait for service to stop on all nodes before progressing)
                    _numClusterServicesRunning = Math.Min(_numClusterServicesRunning, GetClusterServicesStartedOnAnyNode());
                    break;

                case ClusterPhase.Terminated:
                    // nothing
                    break;

                default:
                    throw new InvalidOperationException($"Invalid ClusterPhase {s_phase}");
            }

            // If number of ClusterServices changed, trigger a state update
            if (_numClusterServicesRunning != oldNumClusterServicesRunning)
            {
                //_log.Debug("Switching from {OldNumClusterServicesRunning} ClusterServices running to {NewNumClusterServicesRunning} (of {TotalClusterServices})", oldNumClusterServicesRunning, _numClusterServicesRunning, _services.Length);
                stateUpdated = true;
            }

            // If all ClusterServices started, move on to Running phase (and informs peers)
            if (s_phase == ClusterPhase.Starting && _numClusterServicesRunning == _services.Length)
                SetClusterPhase(ClusterPhase.Running, reason: "ClusterServicesStarted");
            else if (s_phase == ClusterPhase.Stopping && _numClusterServicesRunning == 0)
                SetClusterPhase(ClusterPhase.Terminated, reason: "ClusterServicesTerminated");
            else if (stateUpdated) // otherwise just inform peers
                SendStateToPeers();

            // Immediately try to progress further (needed for single-shard clusters to make progress)
            // \todo [petri] this is a bit of a hack
            if (stateUpdated)
                _self.Tell(TryProgress.Instance);
        }

        void TryConnect(RuntimeNodeState node)
        {
            if (!node.IsConnected)
            {
                _log.Debug("<{Role}> Connecting to {Node}", Role, node.Address);

                // Fetch IP addresses of host (to verify DNS is working)
                try
                {
                    // Resolve address first (Kubernetes StatefulSets take a while to propagate DNS changes)
                    //IPHostEntry hostEntry = Dns.GetHostEntry(node.Address.HostName);
                    //_log.Debug("GetHostEntry({0}) = {1}, addresses: {2}", node.Address.HostName, hostEntry.HostName, string.Join(", ", hostEntry.AddressList.Select(ip => ip.ToString())));

                    //_log.Debug("Sending status update to {0} (self={1})", node.Address, _selfAddress);
                    SendStatusMessage(node.Address, includeCookie: true);
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.HostNotFound)
                        _log.Debug("GetHostEntry({HostName}): host not found", node.Address.HostName);
                    else
                        _log.Warning("GetHostEntry({HostName}) failed with code {SocketErrorCode}: {Exception}", node.Address.HostName, e.SocketErrorCode, e);
                }
            }
        }

        void TryConnect()
        {
            // Try to connect to all peer nodes
            foreach (RuntimeNodeSetState nodeSet in _clusterState.NodeSets)
            {
                foreach (RuntimeNodeState node in nodeSet.NodeStates)
                {
                    if (node.Address != _selfAddress)
                        TryConnect(node);
                }
            }
        }
    }
}
