// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static System.FormattableString;

namespace Metaplay.Cloud.Cluster
{
    public enum ClusteringMode
    {
        /// <summary>
        /// Static clustering with shard names/addresses of the form: '<hostName>:<port+shardNdx>', eg, '127.0.0.1:6000'
        /// </summary>
        Static,

        /// <summary>
        /// Kubernetes clustering with shard names of the form: '<shardType>-<shardNdx>.<shardType>:<port>', eg, 'logic-0.logic:6000'
        /// </summary>
        Kubernetes,
    }

    public class NodeSetConfig
    {
        public readonly ClusteringMode      Mode;
        public readonly string              ShardName;
        public readonly string              HostName;
        public readonly int                 Port;
        public readonly int                 NodeCount;
        public readonly EntityKindMask      EntityKindMask;

        public NodeSetConfig(ClusteringMode mode, string shardName, string hostName, int port, int nodeCount, EntityKindMask entityKindMask)
        {
            Mode            = mode;
            ShardName       = shardName;
            HostName        = hostName;
            Port            = port;
            NodeCount       = nodeCount;
            EntityKindMask  = entityKindMask;
        }

        public ClusterNodeAddress ResolveNodeAddress(int nodeIndex)
        {
            switch (Mode)
            {
                case ClusteringMode.Static:
                    return new ClusterNodeAddress(HostName, Port + nodeIndex);

                case ClusteringMode.Kubernetes:
                    return new ClusterNodeAddress(Invariant($"{HostName}-{nodeIndex}.{ShardName}"), Port);

                default:
                    throw new InvalidOperationException($"Unknown ClusteringMode: {Mode}");
            }
        }

        public bool IsAddressShardOwner(ClusterNodeAddress address)
        {
            return ResolveShardIndex(address, out int _);
        }

        public bool ResolveShardIndex(ClusterNodeAddress address, out int shardIndex)
        {
            if (address is null)
                throw new ArgumentNullException(nameof(address));

            switch (Mode)
            {
                case ClusteringMode.Static:
                    if (address.HostName == HostName && address.Port >= Port && address.Port < Port + NodeCount)
                    {
                        shardIndex = address.Port - Port;
                        return true;
                    }
                    else
                    {
                        shardIndex = -1;
                        return false;
                    }

                case ClusteringMode.Kubernetes:
                    string prefix = $"{HostName}-";
                    if (address.HostName.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        // Parse shard index from self hostname: 'logic-3.logic' gives 3
                        shardIndex = int.Parse(address.HostName.Substring(prefix.Length).Split('.')[0], CultureInfo.InvariantCulture);
                        return true;
                    }
                    else
                    {
                        shardIndex = -1;
                        return false;
                    }

                default:
                    throw new InvalidOperationException($"Unknown ClusteringMode: {Mode}");
            }
        }
    }

    public class ClusterConfig
    {
        public readonly ClusteringMode      Mode;
        public readonly List<NodeSetConfig> NodeSets;

        public ClusterConfig(ClusteringMode mode, List<NodeSetConfig> nodeSets)
        {
            Mode = mode;
            NodeSets = nodeSets;
        }

        public int GetTotalNodeCount()
        {
            return NodeSets.Sum(nodeSet => nodeSet.NodeCount);
        }

        public NodeSetConfig GetNodeSetConfigForAddress(ClusterNodeAddress address)
        {
            foreach (NodeSetConfig nodeSet in NodeSets)
            {
                if (nodeSet.IsAddressShardOwner(address))
                    return nodeSet;
            }

            throw new InvalidOperationException($"NodeSetConfig not found for {address}");
        }

        public int GetNodeGlobalIndex(ClusterNodeAddress address)
        {
            int baseIndex = 0;
            foreach (NodeSetConfig nodeSet in NodeSets)
            {
                if (nodeSet.ResolveShardIndex(address, out int shardIndex))
                    return baseIndex + shardIndex;

                baseIndex += nodeSet.NodeCount;
            }

            throw new InvalidOperationException($"Address {address} not part of cluster");
        }

        public List<NodeSetConfig> GetNodeSetsForEntityKind(EntityKind entityKind)
        {
            return NodeSets
                .Where(nodeSet => nodeSet.EntityKindMask.IsSet(entityKind))
                .ToList();
        }

        public int GetNodeCountForEntityKind(EntityKind entityKind)
        {
            return GetNodeSetsForEntityKind(entityKind).Sum(nodeSet => nodeSet.NodeCount);
        }

        public List<ClusterNodeAddress> GetNodesForEntityKind(EntityKind entityKind)
        {
            List<ClusterNodeAddress> addresses = new List<ClusterNodeAddress>();

            foreach (NodeSetConfig nodeSet in GetNodeSetsForEntityKind(entityKind))
            {
                for (int nodeNdx = 0; nodeNdx < nodeSet.NodeCount; nodeNdx++)
                    addresses.Add(nodeSet.ResolveNodeAddress(nodeNdx));
            }

            return addresses;
        }

        public bool ResolveNodeShardIndex(EntityKind entityKind, ClusterNodeAddress address, out int shardIndex)
        {
            int baseOffset = 0;
            foreach (NodeSetConfig nodeSet in GetNodeSetsForEntityKind(entityKind))
            {
                if (nodeSet.ResolveShardIndex(address, out int localIndex))
                {
                    shardIndex = baseOffset + localIndex;
                    return true;
                }
                else
                    baseOffset += nodeSet.NodeCount;
            }

            shardIndex = -1;
            return false;
        }

        public Dictionary<EntityKind, EntityShardId> GetNodeShardIds(ClusterNodeAddress address)
        {
            Dictionary<EntityKind, EntityShardId> entityShards = new Dictionary<EntityKind, EntityShardId>();

            EntityKindMask entityKindMask = GetNodeSetConfigForAddress(address).EntityKindMask;
            foreach (EntityKind entityKind in entityKindMask.GetKinds())
            {
                int shardBaseNdx = 0;
                foreach (NodeSetConfig nodeSet in NodeSets.Where(nodeSet => nodeSet.EntityKindMask.IsSet(entityKind)))
                {
                    if (nodeSet.ResolveShardIndex(address, out int shardIndex))
                    {
                        entityShards.Add(entityKind, new EntityShardId(entityKind, shardBaseNdx + shardIndex));
                        break;
                    }
                    else
                        shardBaseNdx += nodeSet.NodeCount;
                }
            }

            return entityShards;
        }

        public bool IsMember(ClusterNodeAddress address)
        {
            return NodeSets.Any(nodeSet => nodeSet.ResolveShardIndex(address, out int _));
        }
    }
}
