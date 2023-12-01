// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using System;
using System.Threading.Tasks;

namespace Metaplay.Cloud
{
    /// <summary>
    /// Implementation of <see cref="IClusterService"/> which calls user-provided start and end methods.
    /// </summary>
    public class ClusterServiceLocal : IClusterService
    {
        public string       Name        { get; private set; }
        public Func<Task>   StartFunc   { get; private set; }
        public Func<Task>   StopFunc    { get; private set; }

        public ClusterServiceLocal(string name, Func<Task> start, Func<Task> stop)
        {
            Name = name;
            StartFunc = start;
            StopFunc = stop;
        }

        public async Task Start() => await StartFunc();
        public async Task Stop() => await StopFunc();
    }

    /// <summary>
    /// Implementation of <see cref="IClusterService"/> which starts/stops a corresponding EntityShard>
    /// </summary>
    public class ClusterServiceEntityShard : IClusterService
    {
        public EntityKind                       EntityKind { get; private set; }

        readonly IMetaLogger                    _log;
        readonly EntitySharding                 _entitySharding;
        readonly ClusterConfig                  _clusterConfig;
        readonly ClusterNodeAddress             _selfAddress;
        readonly EntityConfigBase               _entityConfig;

        IActorRef                               _shardActor;

        public string Name => EntityKind.ToString();

        public ClusterServiceEntityShard(
            EntityKind entityKind,
            EntitySharding entitySharding,
            ClusterConfig clusterConfig,
            ClusterNodeAddress selfAddress)
        {
            if (!EntityKindRegistry.IsValid(entityKind))
                throw new ArgumentException($"Must provide valid EntityKind, got {entityKind}", nameof(entityKind));

            EntityKind = entityKind;
            _log = MetaLogger.ForContext($"EntityShard/{entityKind}");
            _entitySharding = entitySharding ?? throw new ArgumentNullException(nameof(entitySharding));
            _clusterConfig = clusterConfig ?? throw new ArgumentNullException(nameof(clusterConfig));
            _selfAddress = selfAddress ?? throw new ArgumentNullException(nameof(selfAddress));
            _entityConfig = EntityConfigRegistry.Instance.GetConfig(entityKind);
        }

        public async Task Start()
        {
            if (_shardActor != null)
                throw new InvalidOperationException($"Trying to start already running EntityShard {EntityKind}");

            _shardActor = await TryStartShardAsync();
        }

        public async Task Stop()
        {
            // \note If shard doesn't exist on this node, it's never started
            if (_shardActor != null)
            {
                await StopShardAsync(_shardActor);
                _shardActor = null;
            }
        }

        async Task<IActorRef> TryStartShardAsync()
        {
            // Register EntityKind
            _entitySharding.RegisterEntityKind(_clusterConfig, EntityKind, _entityConfig.ShardingStrategy);

            // If the shard should be running locally, start it
            bool isOwnedShard = _clusterConfig.ResolveNodeShardIndex(EntityKind, _selfAddress, out int selfShardIndex);
            if (isOwnedShard)
            {
                _log.Information("Initializing EntityShard {EntityKind} (shardIndex={ShardIndex})", EntityKind, selfShardIndex);
                EntityShardConfig shardConfig = new EntityShardConfig(EntityKind, _clusterConfig, selfShardIndex);
                return await _entitySharding.StartShardAsync(shardConfig);
            }
            else
            {
                _log.Information("EntityShard for {EntityKind} not active on this node, skipping", EntityKind, selfShardIndex);
                return null;
            }
        }

        async Task StopShardAsync(IActorRef shardActor)
        {
            try
            {
                TimeSpan shutdownTimeout = _entityConfig.ShardShutdownTimeout;
                Task<ShutdownComplete> askTask = shardActor.Ask<ShutdownComplete>(ShutdownSync.Instance, shutdownTimeout);

                while (true)
                {
                    Task completed = await Task.WhenAny(askTask, Task.Delay(1000));
                    if (completed == askTask)
                    {
                        // Await the task in order to propagate the exception in case it faulted.
                        // In particular the timeout exception is relevant, in case shutdownTimeout
                        // was reached.
                        await askTask;
                        return;
                    }

                    _log.Information("Shutting down EntityShard {ShardName}..", EntityKind);
                }
            }
            catch (Exception ex)
            {
                // Warn on errors (eg, timeout)
                _log.Warning("Exception while shutting down EntityShard {ShardName}: {Exception}", EntityKind, ex);
            }
        }
    }
}
