// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core;
using System;

namespace Metaplay.Cloud
{
    // Ack

    public class Ack
    {
        public static Ack Instance { get; } = new Ack();
        private Ack() { }
    }

    // ActorTick

    public class ActorTick
    {
        public static ActorTick Instance { get; } = new ActorTick();
        private ActorTick() { }
    }

    // ShutdownSync
    // \note receives ShutdownComplete as response

    public class ShutdownSync
    {
        public static ShutdownSync Instance { get; } = new ShutdownSync();
        private ShutdownSync() { }
    }

    // ShutdownComplete

    public class ShutdownComplete
    {
        public static ShutdownComplete Instance { get; } = new ShutdownComplete();
        private ShutdownComplete() { }
    }

    /// <summary>
    /// Utility functions for Akka.NET actors.
    /// </summary>
    public static class ActorUtil
    {
        /// <summary>
        /// Reference equality comparison for <see cref="IActorRef"/> which handles nulls properly.
        /// </summary>
        /// <param name="a">The first actor reference to compare</param>
        /// <param name="b">The second actor reference to compare</param>
        /// <returns>True if a is equal to b, false otherwise</returns>
        // \todo [petri] use IActorRef.Equals() instead when it works with nulls, see: https://github.com/akkadotnet/akka.net/issues/3781
        public static bool Equals(IActorRef a, IActorRef b)
        {
            if (a is null)
                return b is null;
            else if (b is null)
                return false;
            else
                return a.Equals(b);
        }
    }

    /// <summary>
    /// Helper class on top of Akka.NET's <see cref="ReceiveActor"/>.
    /// Metaplay's actors are intended to inherit from this.
    /// </summary>
    public abstract class MetaReceiveActor : ReceiveActor
    {
        [Obsolete("Hiding ActorBase.Self on purpose because it is dangerous when used with .ConfigureAwait(false). Please use _self instead (from MetaReceiveActor).",
            error: true)]
        public new IActorRef Self => base.Self;

        /// <summary>
        /// The IActorRef of this Actor.
        /// </summary>
        protected readonly IActorRef _self;

        /// <summary>
        /// Logger interface to use when logging from the Actor.
        /// </summary>
        protected readonly IMetaLogger _log;

        protected MetaReceiveActor()
        {
            _self = base.Self;

            // Use the short actor name instead of full path.
            // E.g., akka://app/user/shard/GlobalStateManager/GlobalStateManager:00000000 shows as "GlobalStateManager:00000000".
            string actorName = _self.Path.Elements[^1];
            _log = MetaLogger.ForContext(actorName);
        }

        /// <summary>
        /// Asynchronously enqueues the <paramref name="message"/> to the inbox of the <paramref name="receiver"/> actor. Sender property is
        /// automatically set to this actor.
        /// </summary>
        public void Tell(IActorRef receiver, object message)
        {
            receiver.Tell(message, sender: _self);
        }
    }
}
