// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.Cloud.Entity
{
    public partial class EntityActor
    {
        /// <summary>
        /// Helper for managing the lifetime of a Subscription. Guard makes a <see cref="EntitySubscription"/>
        /// (Async) Disposable, which allows managing the lifetime of a subscription in a <c>await using</c> block.
        /// When Guard is Disposed, the Subscription is Unsubscribed. The Guarded subscription may be removed or
        /// changed during the lifetime of the Guard.
        /// </summary>
        protected struct EntitySubscriptionGuard : IAsyncDisposable
        {
            EntityActor         _owner;
            EntitySubscription  _sub;

            /// <summary>
            /// True, if guard contains no subscription, i.e. if <see cref="TryStealOwned"/> will fail.
            /// </summary>
            public bool IsEmpty => _sub == null;

            /// <summary>
            /// Creates a guard containing a given subscription.
            /// </summary>
            public EntitySubscriptionGuard(EntityActor owner, EntitySubscription subscription)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                _sub = subscription;
            }

            /// <summary>
            /// Creates an empty guard. A subscription may be assigned into this with <see cref="Assign(EntitySubscription)"/>.
            /// </summary>
            public EntitySubscriptionGuard(EntityActor owner)
            {
                _owner = owner ?? throw new ArgumentNullException(nameof(owner));
                _sub = null;
            }

            /// <summary>
            /// Sets the guarded subscription. Throws if there already is a guarded subscription. <paramref name="subscription"/>
            /// must be non-null.
            /// </summary>
            public void Assign(EntitySubscription subscription)
            {
                if (subscription == null)
                    throw new ArgumentNullException(nameof(subscription));
                CheckNotDisposed();
                if (_sub != null)
                    throw new InvalidOperationException("Cannot assign a subscription into a Guard. A subscription is already assigned.");
                _sub = subscription;
            }

            /// <summary>
            /// Removes the subscription from the Guard. If there is no subscription in the guard, throws.
            /// </summary>
            public EntitySubscription StealOwned()
            {
                CheckNotDisposed();
                EntitySubscription sub = _sub;
                if (sub == null)
                    throw new InvalidOperationException("Guard has no owned Subscription");
                _sub = null;
                return sub;
            }

            /// <summary>
            /// Removes the subscription from the Guard. If there is no subscription in the guard, returns null.
            /// </summary>
            public EntitySubscription TryStealOwned()
            {
                CheckNotDisposed();
                EntitySubscription sub = _sub;
                _sub = null;
                return sub;
            }

            void CheckNotDisposed()
            {
                if (_owner == null)
                    throw new ObjectDisposedException(nameof(EntitySubscriptionGuard));
            }

            async ValueTask IAsyncDisposable.DisposeAsync()
            {
                // already disposed?
                EntityActor owner = _owner;
                if (owner == null)
                    return;
                _owner = null;

                EntitySubscription sub = _sub;
                _sub = null;

                if (sub != null)
                    await owner.UnsubscribeFromAsync(sub);
            }
        }

        /// <summary>
        /// Helper for managing the lifetime of a Subscription set. Guard makes a set of <see cref="EntitySubscription"/>s
        /// (Async) Disposable, which allows managing the lifetime of a subscription in a <c>await using</c> block.
        /// When Guard is Disposed, all Subscriptions still in the Guard are Unsubscribed. The Guarded subscriptions may be added
        /// or removed during the lifetime of the Guard.
        /// </summary>
        protected struct EntitySubscriptionSetGuard : IAsyncDisposable
        {
            EntityActor                             _owner;
            public OrderedSet<EntitySubscription>   Subscriptions { get; private set; }

            public EntitySubscriptionSetGuard(EntityActor owner)
            {
                _owner = owner;
                Subscriptions = new OrderedSet<EntitySubscription>();
            }

            async ValueTask IAsyncDisposable.DisposeAsync()
            {
                // already disposed?
                EntityActor owner = _owner;
                if (owner == null)
                    return;
                _owner = null;

                OrderedSet<EntitySubscription> subs = Subscriptions;
                Subscriptions = null;

                if (subs != null)
                {
                    List<EntitySubscription> subsReversed = new List<EntitySubscription>(subs);
                    subsReversed.Reverse();
                    foreach (EntitySubscription sub in subsReversed)
                        await owner.UnsubscribeFromAsync(sub);
                }
            }
        }
    }
}
