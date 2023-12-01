// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Metaplay.Cloud.Cluster
{
    public class RuntimeNodeState
    {
        public readonly RuntimeNodeSetState NodeSet;
        public readonly int                 Index;
        public readonly ClusterNodeAddress  Address;
        public readonly bool                IsSelf;

        public bool                         IsConnected         { get; private set; }
        public ClusterPhase                 ClusterPhase        { get; private set; }
        public int                          NumServicesRunning  { get; private set; }
        public ClusterServicePhase[]        ServicePhases       { get; private set; }

        public RuntimeNodeState(RuntimeNodeSetState nodeSet, int index, ClusterNodeAddress address, bool isSelf)
        {
            // Configuration
            NodeSet         = nodeSet;
            Index           = index;
            Address         = address;
            IsSelf          = isSelf;

            // Runtime state
            IsConnected     = isSelf;
            ClusterPhase    = ClusterPhase.Connecting;
            ServicePhases   = null;
        }

        public void SetDisconnected()
        {
            IsConnected = false;
            // \todo [petri] forget ClusterPhase and ServicesPhases?
        }

        public void UpdateState(ClusterPhase clusterPhase, int numServicesRunning, ClusterServicePhase[] servicesPhases)
        {
            IsConnected = true;
            ClusterPhase = clusterPhase;
            NumServicesRunning = numServicesRunning;
            ServicePhases = servicesPhases;
        }
    }

    public class RuntimeNodeSetState
    {
        public readonly NodeSetConfig           Config;
        public readonly List<RuntimeNodeState>  NodeStates  = new List<RuntimeNodeState>();

        public RuntimeNodeSetState(ClusterConfig clusterConfig, NodeSetConfig nodeSetConfig, ClusterNodeAddress selfAddress)
        {
            Config = nodeSetConfig;

            for (int nodeNdx = 0; nodeNdx < nodeSetConfig.NodeCount; nodeNdx++)
            {
                ClusterNodeAddress address = nodeSetConfig.ResolveNodeAddress(nodeNdx);
                NodeStates.Add(new RuntimeNodeState(this, nodeNdx, address, isSelf: address == selfAddress));
            }
        }
    }

    public class ClusterState
    {
        public readonly ClusterConfig               Config;
        public readonly List<RuntimeNodeSetState>   NodeSets = new List<RuntimeNodeSetState>();

        public ClusterState(ClusterConfig config, ClusterNodeAddress selfAddress)
        {
            Config = config;

            foreach (NodeSetConfig nodeSetConfig in config.NodeSets)
                NodeSets.Add(new RuntimeNodeSetState(config, nodeSetConfig, selfAddress));
        }

        //public void NodeDisconnected(ClusterNodeAddress address)
        //{
        //    foreach (RuntimeNodeSetState nodeSet in NodeSets)
        //    {
        //        if (nodeSet.Config.ResolveShardIndex(address, out int shardIndex))
        //            nodeSet.NodeStates[shardIndex].Disconnected();
        //    }
        //}
        //
        //public void UpdateNodeState(ClusterNodeAddress address, ClusterPhase clusterPhase, ClusterServicePhase[] servicePhases)
        //{
        //    foreach (RuntimeNodeSetState nodeSet in NodeSets)
        //    {
        //        if (nodeSet.Config.ResolveShardIndex(address, out int shardIndex))
        //            nodeSet.NodeStates[shardIndex].UpdateState(clusterPhase, servicePhases);
        //    }
        //}

        public RuntimeNodeState GetNodeState(ClusterNodeAddress address)
        {
            foreach (RuntimeNodeSetState nodeSet in NodeSets)
            {
                if (nodeSet.Config.ResolveShardIndex(address, out int shardIndex))
                    return nodeSet.NodeStates[shardIndex];
            }

            throw new InvalidOperationException($"GetNodeState(): invalid address {address}");
        }

        public int GetNumTotalNodes()
        {
            return NodeSets.Sum(nodeSet => nodeSet.NodeStates.Count);
        }

        public int GetNumConnectedNodes()
        {
            return NodeSets.Sum(nodeSet => nodeSet.NodeStates.Sum(nodeState => nodeState.IsConnected ? 1 : 0));
        }
    }
}
