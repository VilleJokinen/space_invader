// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Event;
using Prometheus;
using System;

namespace Metaplay.Cloud.Metrics
{
    /// <summary>
    /// Collect metrics from Akka.NET actor messaging (dead letters and unhandled messages).
    /// </summary>
    public class ActorMessageCollector : MetaReceiveActor
    {
        static readonly Counter c_deadLetters       = Prometheus.Metrics.CreateCounter("game_dead_letters_total", "Number of messages sent to dead actors");
        static readonly Counter c_unhandledMessages = Prometheus.Metrics.CreateCounter("game_unhandled_messages_total", "Number of unhandled messages sent to actors");

        public ActorMessageCollector()
        {
            Receive<DeadLetter>(_ => c_deadLetters.Inc());
            Receive<UnhandledMessage>(_ => c_unhandledMessages.Inc());
        }

        protected override void PreStart()
        {
            Context.System.EventStream.Subscribe(_self, typeof(DeadLetter));
            Context.System.EventStream.Subscribe(_self, typeof(UnhandledMessage));

            base.PreStart();
        }

        protected override void PreRestart(Exception reason, object message)
        {
            Context.System.EventStream.Unsubscribe(_self, typeof(DeadLetter));
            Context.System.EventStream.Unsubscribe(_self, typeof(UnhandledMessage));

            base.PreRestart(reason, message);
        }
    }
}
