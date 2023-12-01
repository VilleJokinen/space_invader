// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Cloud;
using Metaplay.Cloud.Cluster;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Serialization;
using Metaplay.Core.TypeCodes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.BotClient
{
    // BotCoordinator

    public class BotCoordinator : EntityShard
    {
        public class Tick
        {
            private Tick() { }
            public static Tick Instance { get; } = new Tick();
        }

        public class PrintStats
        {
            private PrintStats() { }
            public static PrintStats Instance { get; } = new PrintStats();
        }

        [MetaMessage(MessageCodesCore.InitializeBot, MessageDirection.ServerInternal)]
        public class InitializeBot : MetaMessage
        {
        }

        public class BotStartedSession
        {
            public static BotStartedSession Instance = new BotStartedSession();
        }

        static readonly Prometheus.Gauge    c_expectedSessionDuration   = Prometheus.Metrics.CreateGauge("botclient_config_expected_session_duration_seconds", "BotClient expected session duration in seconds");
        static readonly Prometheus.Gauge    c_maxBots                   = Prometheus.Metrics.CreateGauge("botclient_config_max_bots", "BotClient maximum number of bots to spawn");
        static readonly Prometheus.Gauge    c_maxBotId                  = Prometheus.Metrics.CreateGauge("botclient_config_max_bot_id", "BotClient maximum id to use for a spawned bot");
        static readonly Prometheus.Gauge    c_spawnRate                 = Prometheus.Metrics.CreateGauge("botclient_config_spawn_rate", "BotClient number of new bots to spawn per second");

        EntityId                                _entityId           = EntityId.Create(EntityKindBotCore.BotCoordinator, 0);
        ICancelable                             _cancelUpdateTimer  = new Cancelable(Context.System.Scheduler);
        ICancelable                             _cancelPrintTimer   = new Cancelable(Context.System.Scheduler);
        RandomPCG                               _rnd                = RandomPCG.CreateNew();
        HashSet<EntityId>                       _botsInSession      = new HashSet<EntityId>();

        public BotCoordinator(EntityShardConfig shardConfig) : base(shardConfig)
        {
            _log.Info("BotCoordinator starting");

            // Message handlers
            Receive<Tick>(ReceiveTick);
            Receive<PrintStats>(ReceivePrintStats);
            ReceiveAsync<ShutdownSync>(ReceiveShutdownSync);
            Receive<BotStartedSession>(ReceiveBotStartedSession);
        }

        void ReceiveTick(Tick tick)
        {
            BotOptions botOpts = RuntimeOptionsRegistry.Instance.GetCurrent<BotOptions>();

            // If running normally, try spawning more bots
            if (ClusterCoordinatorActor.Phase == ClusterPhase.Running)
            {
                // If not at requested amount, spawn more
                int numBots = _entityStates.Count;
                if (numBots < botOpts.MaxBots)
                {
                    // Spawn a bot (with random id up to maxBotId)
                    long maxBotId = botOpts.MaxBotId > 0 ? botOpts.MaxBotId : 5 * botOpts.MaxBots;
                    long botIndex = _rnd.NextLong() % maxBotId;
                    EntityId botId = StaticShardingStrategy.CreateEntityId(_selfShardId, (ulong)botIndex);
                    _log.Verbose("Spawning bot {BotId} (index {BotIndex})", botId, botIndex);
                    MetaSerialized<MetaMessage> initializeBot = new MetaSerialized<MetaMessage>(new InitializeBot(), MetaSerializationFlags.IncludeAll, logicVersion: null);
                    _self.Tell(new CastMessage(botId, initializeBot, _entityId));
                }
            }

            // Keep ticking (randomize interval a bit)
            Random rnd = new Random();
            TimeSpan tickInterval = TimeSpan.FromSeconds((0.9 + 0.2 * rnd.NextDouble()) / botOpts.SpawnRate);
            Context.System.Scheduler.ScheduleTellOnce(tickInterval, _self, Tick.Instance, ActorRefs.NoSender, _cancelUpdateTimer);
        }

        void ReceivePrintStats(PrintStats print)
        {
            // Remove session markers from dead bots
            _botsInSession.RemoveWhere((EntityId botInSession) => !_entityStates.ContainsKey(botInSession));

            BotOptions botOpts = RuntimeOptionsRegistry.Instance.GetCurrent<BotOptions>();
            int numBots = _entityStates.Count;
            _log.Info("{NumBots} bots, {NumInSession} in session, {MaxBots} max", numBots, _botsInSession.Count, botOpts.MaxBots);

            // Expose config metrics
            c_expectedSessionDuration.Set(botOpts.ExpectedSessionDuration.TotalSeconds);
            c_maxBots.Set(botOpts.MaxBots);
            c_maxBotId.Set(botOpts.MaxBotId);
            c_spawnRate.Set(botOpts.SpawnRate);
        }

        async Task ReceiveShutdownSync(ShutdownSync async)
        {
            _log.Info("Shutting down..");

            // \todo [petri] shutdown all bots

            await Task.Delay(100);

            _self.Tell(PoisonPill.Instance);
            Sender.Tell(ShutdownComplete.Instance, _self);
        }

        void ReceiveBotStartedSession(BotStartedSession started)
        {
            if (_entityByActor.TryGetValue(Sender, out EntityState entity))
                _botsInSession.Add(entity.EntityId);
        }

        protected override void PreStart()
        {
            base.PreStart();

            // Tick timer (based on bot spawn rate)
            BotOptions botOpts = RuntimeOptionsRegistry.Instance.GetCurrent<BotOptions>();
            TimeSpan tickInterval = TimeSpan.FromSeconds(1.0 / botOpts.SpawnRate);
            Context.System.Scheduler.ScheduleTellOnce(tickInterval, _self, Tick.Instance, ActorRefs.NoSender, _cancelUpdateTimer);

            // Start stats printing
            TimeSpan printStatsInterval = TimeSpan.FromSeconds(5);
            Context.System.Scheduler.ScheduleTellRepeatedly(printStatsInterval, printStatsInterval, _self, PrintStats.Instance, ActorRefs.NoSender, _cancelPrintTimer);
        }

        protected override void PostStop()
        {
            _log.Info("Stop");
            base.PostStop();
            _cancelUpdateTimer.Cancel();
            _cancelPrintTimer.Cancel();
        }
    }
}
