// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Cluster
{
    public class ShardingTopologySpec
    {
        public string                               TopologyId              { get; private set; }
        public Dictionary<string, EntityKindMask>   ShardEntityKinds        { get; private set; }
        public EntityKindMask                       CombinedEntityKindMask  { get; private set; }

        ShardingTopologySpec(string topologyId, Dictionary<string, EntityKindMask> shardEntityKinds)
        {
            TopologyId = topologyId;
            ShardEntityKinds = shardEntityKinds;
            CombinedEntityKindMask = shardEntityKinds.Values.Aggregate(EntityKindMask.None, (acc, mask) => acc | mask);
        }

        public static ShardingTopologySpec Parse(string topologyId, Dictionary<EntityKind, EntityKind> implicitEntities, List<EntityKind> commonEntityKinds, Dictionary<string, List<EntityKind>> shardEntityKinds)
        {
            EntityKindMask commonEntityKindMask = new EntityKindMask(commonEntityKinds);
            return new ShardingTopologySpec(topologyId, ParseShardEntityKinds(implicitEntities, commonEntityKindMask, shardEntityKinds));
        }

        static Dictionary<string, EntityKindMask> ParseShardEntityKinds(Dictionary<EntityKind, EntityKind> implicitEntityKinds, EntityKindMask commonEntityKinds, Dictionary<string, List<EntityKind>> shardEntityKinds)
        {
            // Parse per-topologyKey entityKindMask (including commonEntityKinds)
            Dictionary<string, EntityKindMask> result = new();
            foreach ((string topologyKey, List<EntityKind> entityKinds) in shardEntityKinds)
            {
                EntityKindMask mask = commonEntityKinds | new EntityKindMask(entityKinds);
                result.Add(topologyKey, mask);
            }

            // Augment with implicit EntityKinds
            EntityKindMask combinedMask = result.Values.Aggregate(EntityKindMask.None, (acc, mask) => acc | mask);
            foreach ((EntityKind implicitKind, EntityKind nextToKind) in implicitEntityKinds)
            {
                // If implicitKind isn't set anywhere, apply to to wherever the nextToKind is set
                if (!combinedMask.IsSet(implicitKind))
                {
                    foreach (string topologyKey in result.Keys)
                    {
                        EntityKindMask mask = result[topologyKey];
                        if (mask.IsSet(nextToKind))
                            result[topologyKey] = mask | EntityKindMask.FromEntityKind(implicitKind);
                    }
                }

                // Check that there are no shards with empty mask
                foreach ((string topologyKey, EntityKindMask mask) in result)
                {
                    if (mask.IsEmpty)
                        throw new InvalidOperationException($"No EntityKinds in sharding topology '{topologyKey}'");
                }
            }

            return result;
        }
    }

    [RuntimeOptions("Clustering", isStatic: true, "Configuration options for defining the server cluster.")]
    public class ClusteringOptions : RuntimeOptionsBase
    {
        [MetaDescription("The clustering mode of the server (`Static` or `Kubernetes`).")]
        public ClusteringMode       Mode                { get; private set; } = (IsBotClientApplication || IsLocalEnvironment) ? ClusteringMode.Static : ClusteringMode.Kubernetes;
        [CommandLineAlias("-ShardingConfig")]
        [MetaDescription("The cluster sharding configuration. This can be a path to a `.json` file or inline JSON.")]
        public string               ShardingConfig      { get; private set; } = null;
        [MetaDescription("The namespace of the cluster's shared cookies.")]
        public string               Cookie              { get; private set; } = "defaultcookie";

        // Akka.Remoting
        [MetaDescription("The hostname of the machine or pod.")]
        public string               RemotingHost        { get; private set; } = "127.0.0.1";
        [MetaDescription("Optional: Suffix added to `HostName` to form the full hostname for Akka.Remote.")]
        public string               RemotingHostSuffix  { get; private set; } = "";
        [CommandLineAlias("-RemotingPort")]
        [MetaDescription("The port to use for Akka.Remote peer connections (set to 0 to use a random available port).")]
        public int                  RemotingPort        { get; private set; } = IsServerApplication ? 6000 : 0;

        [MetaDescription("The computed address of this cluster node.")]
        public ClusterNodeAddress   SelfAddress         => new ClusterNodeAddress(RemotingHost + RemotingHostSuffix, RemotingPort);
        [ComputedValue]
        [MetaDescription("The computed configuration of this cluster node.")]
        public ClusterConfig        ClusterConfig       { get; private set; }

        [MetaDescription("List of `EntityKinds` that exist on all shards.")]
        public List<EntityKind>     CommonEntityKinds   { get; private set; } = new();

        [MetaDescription("Mapping from shard name to list-of-EntityKinds for each shard type.")]
        public Dictionary<string, Dictionary<string, List<EntityKind>>> ShardingTopologies { get; private set; } = new();

        [ComputedValue]
        public Dictionary<string, ShardingTopologySpec> ResolvedShardingTopologies { get; private set; }

        [CommandLineAlias("-TopologyId")]
        public string               TopologyId          { get; private set; } = null;

        public override Task OnLoadedAsync()
        {
            ClusterConfig = ParseClusterConfig();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Check if provided string is a valid path. Used to distinguish between direct json config
        /// payloads and paths to .json files.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        bool IsValidPath(string str)
        {
            try
            {
                if (str.Contains("[") || str.Contains("]") || str.Contains("}") || str.Contains("{") || str.Contains("\"") || str.Contains(":"))
                    return false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Default various EntityKinds next to another EntityKind, if not explicitly specified in ShardingTopologies.
        // \todo [petri] move declarations to EntityConfigRegistry when available
        static readonly Dictionary<EntityKind, EntityKind> s_implicitEntities = new()
        {
            { EntityKindCloudCore.BackgroundTask,        EntityKindCloudCore.GlobalStateManager },
            { EntityKindCloudCore.SegmentSizeEstimator,  EntityKindCloudCore.GlobalStateManager },
            { EntityKindCloudCore.StatsCollectorManager, EntityKindCloudCore.GlobalStateManager },
        };

        ClusterConfig ParseClusterConfig()
        {
            // Parse all specified sharding topologies, so we can catch errors in any of them.
            Dictionary<string, ShardingTopologySpec> resolvedShardingTopologies = new();
            foreach ((string topologyId, Dictionary<string, List<EntityKind>> shardSetEntityKinds) in ShardingTopologies)
            {
                ShardingTopologySpec shardingSpec = ShardingTopologySpec.Parse(topologyId, s_implicitEntities, CommonEntityKinds, shardSetEntityKinds);
                resolvedShardingTopologies.Add(topologyId, shardingSpec);
            }
            ResolvedShardingTopologies = resolvedShardingTopologies;

            // Resolve union of EntityKinds from all topologySpecs
            EntityKindMask totalEntityKindMask = resolvedShardingTopologies.Values.Aggregate(EntityKindMask.None, (acc, spec) => acc | spec.CombinedEntityKindMask);

            // Resolve missing EntityKinds in any topologySpecs
            foreach ((string topologyId, ShardingTopologySpec spec) in resolvedShardingTopologies)
            {
                EntityKindMask missing = ~spec.CombinedEntityKindMask & totalEntityKindMask;
                if (!missing.IsEmpty)
                    throw new InvalidOperationException($"Missing EntityKinds from ShardingTopology '{topologyId}': {missing}");
            }

            // If ShardingConfig json is specified, parse it
            List<NodeSetConfig> nodeSetConfigs = new List<NodeSetConfig>();
            string configJson = ShardingConfig;
            if (configJson != null)
            {
                // If valid path, read the file contents (otherwise interpret as json)
                if (IsValidPath(configJson))
                    configJson = File.ReadAllText(configJson);

                // Parse NodeSetConfig for each shard type
                JArray shardTypes = (JArray)JsonConvert.DeserializeObject(configJson);
                int numShardTypes = shardTypes.Count;
                bool isStatic = Mode == ClusteringMode.Static;
                foreach (JToken elem in shardTypes)
                {
                    JObject shardConfig     = (JObject)elem;
                    bool    isSingleton     = shardConfig.Value<bool?>("singleton") ?? false;
                    string  shardName       = shardConfig.Value<string>("name");
                    string  topologyKey     = shardConfig.Value<string>("topologyKey");
                    string  hostName        = (isStatic && !isSingleton) ? shardConfig.Value<string>("hostName") : shardName;
                    int     remotingPort    = (isStatic && !isSingleton) ? shardConfig.Value<int>("remotingPort") : RemotingPort;
                    int     nodeCount       = isSingleton ? 1 : shardConfig.Value<int>("nodeCount");

                    // Singletons are only allowed if there's one shard type
                    if (isSingleton && numShardTypes > 1)
                        throw new InvalidOperationException($"Shard type '{shardName}' is configured as singleton, which is not allowed when multiple shard types are defined");

                    // Default topologyKey to shard name if not explicitly specified
                    if (topologyKey == null)
                        topologyKey = shardName;

                    MetaDebug.Assert(!string.IsNullOrEmpty(shardName), "Must have valid 'name' for shard");
                    MetaDebug.Assert(!string.IsNullOrEmpty(hostName), "Must have valid 'hostName' for shard {0}, got '{1}'", shardName, hostName);
                    MetaDebug.Assert(remotingPort > 0 && remotingPort < 65536, "Must have valid 'port' for shard {0}, got {1}", shardName, remotingPort);
                    MetaDebug.Assert(nodeCount > 0 && nodeCount < 10_000, "Must have valid nodeCount (replicas) for shard {0}, got {1}", shardName, nodeCount);

                    // Resolve which EntityKinds should live on each shard type
                    EntityKindMask shardEntityMask;
                    if (isSingleton)
                        shardEntityMask = EntityKindMask.All;
                    else
                    {
                        // Append all shard-specific EntityKinds from Options (if defined)
                        if (ShardingTopologies.Count == 0)
                            throw new InvalidOperationException($"No ShardingTopologies specified");

                        if (string.IsNullOrEmpty(TopologyId))
                            throw new InvalidOperationException($"TopologyId must be defined");

                        if (!resolvedShardingTopologies.TryGetValue(TopologyId, out ShardingTopologySpec shardingSpec))
                            throw new InvalidOperationException($"TopologyId '{TopologyId}' does not exist in ShardingTopologies");

                        if (!shardingSpec.ShardEntityKinds.TryGetValue(topologyKey, out shardEntityMask))
                            throw new InvalidOperationException($"ShardingTopology '{TopologyId}' does not contain definition for topologyKey '{topologyKey}'");
                    }

                    NodeSetConfig nodeSetConfig = new NodeSetConfig(Mode, shardName, hostName, remotingPort, nodeCount, shardEntityMask);
                    nodeSetConfigs.Add(nodeSetConfig);
                }

                // Return final config
                return new ClusterConfig(Mode, nodeSetConfigs);
            }
            else // if not specified, default to single-node config
            {
                return GetDefaultClusterConfig();
            }
        }

        ClusterConfig GetDefaultClusterConfig()
        {
            ClusteringMode mode = ClusteringMode.Static;
            List<NodeSetConfig> nodeSetConfigs = new List<NodeSetConfig>
            {
                new NodeSetConfig(mode, "Full", RemotingHost, RemotingPort, 1, EntityKindMask.All)
            };
            return new ClusterConfig(mode, nodeSetConfigs);
        }
    }
}
