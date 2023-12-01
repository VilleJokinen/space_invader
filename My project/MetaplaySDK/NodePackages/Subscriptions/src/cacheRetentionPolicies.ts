// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview Defines the various invalidation policies for the subscription system. An invalidation policy is a way
 * for the subscription system to decide when to invalidate data and remove it from the cache once a subscription no
 * longer has any active subscribers.
 */

/**
 * Create a cache retention policy that will clear the cache after a period of time.
 * @param timeoutInMs How long the data lives in the cache before being evicted.
 * @example TBD
 */
export function getCacheRetentionPolicyTimed (timeoutInMs: number) {
  return new CacheRetentionPolicyTimed(timeoutInMs)
}

/**
 * Create a cache retention policy that will never clear the cache.
 * @example
 * export function getAllActivePlayersSubscriptionOptions (): SubscriptionOptions {
    return {
      permission: 'api.players.view',
      pollingPolicy: getPollingPolicyTimer(5000),
      fetcherPolicy: getFetcherPolicyGet('/players/activePlayers'),
      cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
    }
  }
 */
export function getCacheRetentionPolicyKeepForever () {
  return new CacheRetentionPolicyKeepForever()
}

/**
 * Create a cache retention policy that will immediately clear the cache.
 */
export function getCacheRetentionPolicyDeleteImmediately () {
  return new CacheRetentionPolicyDeleteImmediately()
}

/**
 * Invalidation policy callback interface.
 */
type InvalidateCallback = () => void

/**
 * Abstract base class for all invalidation policies. The subscription manager will call `start` when it thinks that
 * the invalidator should start considering when to evict the data. This happens when a subscription no longer has any
 * active subscribers. The invalidator should call `invalidate` when it thinks that the data should be evicted. If the
 * subscription manager decides to keep the data in the cache (ie: a new subscriber has appeared) then it will call
 * `cancel`.
 */
export abstract class CacheRetentionPolicy {
  /**
   * Called by the subscription manager to register a function to be called by `invalidate`.
   * @param invalidateCallback
   */
  register (invalidateCallback: InvalidateCallback) {
    this.invalidate = invalidateCallback
  }

  /**
   * Tell the subscription manager to invalidate the cached data.
   * The member points to a default implementation that raises an error to indicate that `register` was not called by
   * the subscription manager. This implementation will be replaced by a call to `register`.
   */
  protected invalidate: InvalidateCallback = () => {
    throw new Error('No invalidate callback was registered')
  }

  /**
   * Start thinking about when to evict data from the cache. Subscription manager will call this when a subscription
   * loses it's last subscriber.
   */
  abstract start (): void

  /**
   * Stop thinking about when to evict data from the cache. Subscription manager will call then when a subscription
   * that had no subscribers gains a new one.
   */
  abstract cancel (): void
}

/**
 * CacheRetentionPolicyTimed - Data is invalidated after a period of time.
 */
class CacheRetentionPolicyTimed extends CacheRetentionPolicy {
  /**
   * How long the data lives in the cache before being evicted.
   */
  private readonly timeoutInMs: number

  /**
   * Stores the id returned from `setTimeout`.
   */
  private timerId: ReturnType<typeof setTimeout> | null

  /**
   * Constructor
   * @param timeoutInMs How long the data lives in the cache before being evicted.
   */
  constructor (timeoutInMs: number) {
    super()
    this.timeoutInMs = timeoutInMs
    this.timerId = null
  }

  /**
   * Start a timer. Evict the data when this timer expires.
   */
  start () {
    this.timerId = setTimeout(() => {
      this.invalidate()
      this.timerId = null
    }, this.timeoutInMs)
  }

  /**
   * Stop the eviction timer.
   */
  cancel () {
    if (this.timerId !== null) {
      clearTimeout(this.timerId)
      this.timerId = null
    }
  }
}

/**
 * CacheRetentionPolicyKeepForever - Data is *never* invalidated.
 */
class CacheRetentionPolicyKeepForever extends CacheRetentionPolicy {
  /**
   * Nothing to do here - we never need to think about when the data will be evicted.
   */
  start () {
  }

  /**
   * Nothing to do here.
   */
  cancel () {
  }
}

/**
 * CacheRetentionPolicyDeleteImmediately - Data is invalidated immediately.
 */
class CacheRetentionPolicyDeleteImmediately extends CacheRetentionPolicy {
  /**
   * Data will always be immediately evicted.
   */
  start () {
    this.invalidate()
  }

  /**
   * Nothing to do here.
   */
  cancel () {
  }
}
