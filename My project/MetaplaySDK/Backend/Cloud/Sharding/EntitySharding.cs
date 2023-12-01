// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Akka.Configuration;
using Akka.Remote;
using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Metaplay.Cloud.Sharding
{
    public static class IActorRefExtensions
    {
        // \todo [petri] this should be in Akka core?
        public static IActorRef GetOrElse(this IActorRef actorRef, Func<IActorRef> elseValue)
        {
            return actorRef.IsNobody() ? elseValue() : actorRef;
        }
    }

    public class EntityShardingCoordinator : MetaReceiveActor
    {
        public class StartShard
        {
            public readonly EntityShardConfig ShardConfig;

            public StartShard(EntityShardConfig shardConfig)
            {
                ShardConfig = shardConfig;
            }
        }

        public class StartShardSuccess
        {
            public readonly IActorRef   ShardActor;

            public StartShardSuccess(IActorRef shardActor)
            {
                ShardActor = shardActor;
            }
        }

        public class StartShardFailed
        {
            public readonly Exception Failure;
            public StartShardFailed(Exception failure)
            {
                Failure = failure;
            }
        }

        public EntityShardingCoordinator()
        {
            ReceiveAsync<StartShard>(ReceiveStartShard);
            Receive<ShutdownSync>(ReceiveShutdownSync);
        }

        async Task ReceiveStartShard(StartShard startShard)
        {
            // Start the EntityShard actor
            EntityShardConfig   shardConfig     = startShard.ShardConfig;
            EntityConfigBase    entityConfig    = EntityConfigRegistry.Instance.GetConfig(shardConfig.EntityKind);
            string              childName       = shardConfig.EntityKind.ToString();
            Type                entityShardType = entityConfig.EntityShardType;
            Props               shardProps      = Props.Create(entityShardType, shardConfig);
            IActorRef           shardActor      = Context.Child(childName).GetOrElse(() => Context.ActorOf(shardProps, childName));
            DateTime            timeoutAt       = DateTime.UtcNow + TimeSpan.FromMinutes(5);
            DateTime            nextWaitLogAt   = DateTime.UtcNow + TimeSpan.FromSeconds(1);

            // Wait until the shard actor reports it's ready or failed to start. Wait until the timeout while printing status messages periodically.
            Task<bool> waitUntilRunningQuery = null;
            for (;;)
            {
                // Deadline is strict. No last checks.
                if (DateTime.UtcNow > timeoutAt)
                {
                    Sender.Tell(new StartShardFailed(new TimeoutException($"Timeout while starting EntityShard {childName}")));
                    return;
                }

                // Printing every now and then to signal progress, or lack of.
                if (DateTime.UtcNow > nextWaitLogAt)
                {
                    nextWaitLogAt = DateTime.UtcNow + TimeSpan.FromSeconds(1);
                    _log.Info("Waiting for EntityShard {ShardName} to be ready..", childName);
                }

                if (waitUntilRunningQuery == null)
                    waitUntilRunningQuery = EntityShard.TryWaitUntilRunning(shardActor);

                // Poll query
                try
                {
                    bool isShardRunning = await waitUntilRunningQuery.WaitAsync(TimeSpan.FromMilliseconds(100));
                    if (isShardRunning)
                    {
                        _log.Info("EntityShard {ShardName} is ready!", childName);
                        Sender.Tell(new StartShardSuccess(shardActor));
                        return;
                    }

                    // Query completed (but shard isn't yet running).
                    waitUntilRunningQuery = null;
                }
                catch (TimeoutException)
                {
                    // EntityShard.TryWaitUntilRunning did not complete during the time given in WaitAsync. This means the query is not yet complete and we need to wait for more.
                    continue;
                }
                catch (EntityShardStartException ex)
                {
                    Sender.Tell(new StartShardFailed(ex));

                    // Let the shard die
                    await shardActor.Ask<ShutdownComplete>(ShutdownSync.Instance);
                    return;
                }
            }
        }

        void ReceiveShutdownSync(ShutdownSync shutdown)
        {
            _self.Tell(PoisonPill.Instance);
            Sender.Tell(ShutdownComplete.Instance);
        }
    }

    // \todo [petri] this is wonky, rewrite it properly
    public class EntityKindShardState
    {
        public readonly IShardingStrategy   Strategy;
        public readonly IActorRef[]         RemoteActors;

        public EntityKindShardState(IShardingStrategy strategy, IActorRef[] remoteActors)
        {
            Strategy = strategy;
            RemoteActors = remoteActors;
        }
    }

    /// <summary>
    /// Akka.NET extension for managing <see cref="EntityShard"/>.
    /// </summary>
    public class EntitySharding : IExtension
    {
        ExtendedActorSystem                             _actorSystem;
        IActorRef                                       _shardingCoordinator;

        object                                          _shardsLock     = new object();
        Dictionary<EntityKind, EntityKindShardState>    _shardStates    = new Dictionary<EntityKind, EntityKindShardState>();

        public static Config DefaultConfiguration()
        {
            // \todo [petri] implement me!
            return new Config();
            //return ConfigurationFactory.FromResource<EntitySharding>("Akka.Entity.Sharding.reference.conf");
        }

        public static EntitySharding Get(ActorSystem system)
        {
            return system.WithExtension<EntitySharding, EntityShardingProvider>();
        }

        public EntitySharding(ExtendedActorSystem system)
        {
            _actorSystem = system ?? throw new ArgumentNullException(nameof(system));

            //system.Settings.InjectTopLevelFallback(DefaultConfiguration());

            //Config config = system.Settings.Config.GetConfig("akka.entity.sharding");
            //_settings = new EntityShardingSettings(config);

            _shardingCoordinator = system.ActorOf(Props.Create<EntityShardingCoordinator>(), "shard");
        }

        public async Task<IActorRef> StartShardAsync(EntityShardConfig shardConfig)
        {
            // Spawn the EntityShard actor
            object reply = await _shardingCoordinator.Ask(new EntityShardingCoordinator.StartShard(shardConfig));
            switch (reply)
            {
                case EntityShardingCoordinator.StartShardSuccess success:
                    return success.ShardActor;

                case EntityShardingCoordinator.StartShardFailed failed:
                    // Rethrow with original stack
                    ExceptionDispatchInfo.Capture(failed.Failure).Throw();
                    throw new InvalidOperationException("unreachable");

                default:
                    throw new InvalidOperationException($"Failed to start shard {shardConfig.EntityKind}: {reply}");
            }
        }

        public async Task ShutdownAsync(TimeSpan timeout)
        {
            await _shardingCoordinator.Ask<ShutdownComplete>(ShutdownSync.Instance, timeout);
        }

        /// <summary>
        /// Register the shards for a given <see cref="EntityKind"/>. Resolve both remote shard actors and store local actor (may be null).
        /// </summary>
        /// <param name="actorSystem"></param>
        /// <param name="clusterConfig"></param>
        /// <param name="entityKind"></param>
        public void RegisterEntityKind(ClusterConfig clusterConfig, EntityKind entityKind, IShardingStrategy strategy)
        {
            lock (_shardsLock)
            {
                // Resolve which NodeSets have an EntityShard for entityKind
                List<NodeSetConfig> nodeSets = clusterConfig.GetNodeSetsForEntityKind(entityKind);
                int totalShardCount = nodeSets.Sum(nodeSet => nodeSet.NodeCount);
                IActorRef[] shardActors = new IActorRef[totalShardCount];

                // Initialize all shards (if has any)
                if (totalShardCount > 0)
                {
                    RemoteActorRefProvider provider = (RemoteActorRefProvider)_actorSystem.Provider;

                    int shardBaseNdx = 0;
                    foreach (NodeSetConfig nodeSet in nodeSets)
                    {
                        for (int shardNdx = 0; shardNdx < nodeSet.NodeCount; shardNdx++)
                        {
                            // Resolve EntityShard ActorRef on the target node
                            ClusterNodeAddress address = nodeSet.ResolveNodeAddress(shardNdx);
                            string actorPath = Invariant($"akka.tcp://{MetaplayCore.Options.ProjectName}@{address.HostName}:{address.Port}/user/shard/{entityKind}");
                            IActorRef actor = provider.ResolveActorRef(actorPath);
                            shardActors[shardBaseNdx + shardNdx] = actor;
                        }
                        shardBaseNdx += nodeSet.NodeCount;
                    }
                }

                // Store shard state
                _shardStates[entityKind] = new EntityKindShardState(strategy, shardActors);
            }
        }

        public void RegisterLocalShard(EntityShardId shardId, IActorRef actorRef)
        {
            // \todo [petri] remove uid from actorRef to avoid problems with EntityShard restarts?
            lock (_shardsLock)
            {
                EntityKindShardState shardState = _shardStates[shardId.Kind];
                shardState.RemoteActors[shardId.Value] = actorRef;
            }
        }

        public EntityKindShardState GetShardStatesForKind(EntityKind entityKind)
        {
            lock (_shardsLock)
            {
                return _shardStates[entityKind];
            }
        }

        public EntityKindShardState TryGetShardStatesForKind(EntityKind entityKind)
        {
            lock (_shardsLock)
            {
                if (_shardStates.TryGetValue(entityKind, out EntityKindShardState state))
                    return state;
                else
                    return null;
            }
        }
    }
}
