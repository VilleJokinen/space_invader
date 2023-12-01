// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Cluster;
using Metaplay.Core;
using System;

namespace Metaplay.Cloud.Sharding
{
    /// <summary>
    /// Provides mapping from a <see cref="EntityId"/> to the responsible <see cref="EntityShard"/> and the responsible <see cref="EntityId"/>
    /// within it. The concept of "responsible" is dependent on vantage point -- for example, EntityId of a service could be mapped to the
    /// Local shard and the Local service within it.
    /// </summary>
    public interface IShardingStrategy
    {
        /// <summary>
        /// For a given entity, returns the responsible <see cref="EntityShard"/> and the responsible <see cref="EntityId"/>
        /// within it.
        /// </summary>
        EntityShardId ResolveShardId(EntityId entityId);
    }

    /// <summary>
    /// Distributes entities uniformly to shards based on EntityId.
    /// </summary>
    public class HashedShardingStrategy : IShardingStrategy
    {
        readonly int _numShards;

        public HashedShardingStrategy(ClusterConfig clusterConfig, EntityKind kind)
        {
            int numShards = clusterConfig.GetNodeCountForEntityKind(kind);
            if (numShards <= 0)
                throw new ArgumentException($"Invalid amount ({numShards}) shard instances defined for {kind} shard in ClusterConfig. There must be at least one instance.");
            _numShards = numShards;
        }

        public EntityShardId ResolveShardId(EntityId entityId)
        {
            return new EntityShardId(entityId.Kind, (int)(entityId.Value % (uint)_numShards));
        }
    }

    /// <summary>
    /// Maps all entities uniformly to N service nodes, where each such node has a single reponsible entity.
    /// Overrides the target EntityId with the same value as the ShardId it is located at.
    /// </summary>
    public class ServiceShardingStrategy : IShardingStrategy
    {
        int _numServiceShards;

        public ServiceShardingStrategy(int numServiceShards)
        {
            MetaDebug.Assert(numServiceShards > 0, "Invalid amount ({0}) of service shards", numServiceShards);
            _numServiceShards = numServiceShards;
        }

        public ServiceShardingStrategy(EntityKind kind)
        {
            // \todo [petri] cleanup reference to Application.Instance.ClusterConfig
            int numShards = Application.Application.Instance.ClusterConfig.GetNodeCountForEntityKind(kind);
            if (numShards <= 0)
                throw new ArgumentException($"Invalid amount ({numShards}) shard instances defined for {kind} shard in ClusterConfig. There must be at least one instance.");
            _numServiceShards = numShards;
        }

        public EntityShardId ResolveShardId(EntityId entityId)
        {
            return new EntityShardId(entityId.Kind, (int)(entityId.Value % (uint)_numServiceShards));
        }
    }

    /// <summary>
    /// Shards entities based on routing decision payload encoded on EntityId. This allows for setting routing rules at runtime, but
    /// requires that all <see cref="EntityId"/>s are created with <see cref="CreateEntityId"/>.
    /// </summary>
    public class StaticShardingStrategy : IShardingStrategy
    {
        const int   ShardIndexBits  = 16;                                   // Number of bits to reserve for shardIndex
        const int   ShardIndexShift = EntityId.KindShift - ShardIndexBits;  // Use highest N bits for shardIndex
        const uint  ShardIndexMask  = (1 << ShardIndexBits) - 1;            // Mask for extracting shardIndex
        const ulong ValueMask       = (1ul << ShardIndexShift) - 1;

        public StaticShardingStrategy()
        {
        }

        public EntityShardId ResolveShardId(EntityId entityId)
        {
            return new EntityShardId(entityId.Kind, (int)(entityId.Value >> ShardIndexShift));
        }

        public static EntityId CreateEntityId(EntityShardId shardId, ulong runningId)
        {
            MetaDebug.Assert(shardId.Value >= 0 && shardId.Value <= ShardIndexMask, "Invalid shardId {0}, must be between 0 and {1}", shardId, ShardIndexMask);
            MetaDebug.Assert(runningId >= 0 && runningId <= ValueMask, "Invalid runningId for {0}: {1}", shardId.Kind, runningId);
            ulong value = ((ulong)(uint)shardId.Value << ShardIndexShift) | runningId;
            return EntityId.Create(shardId.Kind, value);
        }
    }
}
