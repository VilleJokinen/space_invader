// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview Defines the various polling policies for the subscription system. A polling policy is a way for a
 * subscription to decide when a subscription needs to re-fetch data.
 */

/**
 * Return a new instance of a polling policy that polls for new data based on a timer.
 * @param timeoutInMs How long to wait before the next poll.
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
export function getPollingPolicyTimer (timeoutInMs: number) {
  return new PollingPolicyTimer(timeoutInMs)
}

/**
 * Return a new instance of a polling policy that fetches new data only once, when the subscription is initially
 * created. Good for data that is not going to change during a user's session and can be safely cached.
 * @example
 * export function getRuntimeOptionsSubscriptionOptions (): SubscriptionOptions {
    return {
      permission: 'api.runtime_options.view',
      pollingPolicy: getPollingPolicyOnceOnly(),
      fetcherPolicy: getFetcherPolicyGet('/runtimeOptions'),
      cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
    }
  }
 */
export function getPollingPolicyOnceOnly () {
  return new PollingPolicyOnceOnly()
}

/**
 * Polling policy request callback interface.
 */
type RequestFetchCallback = () => void

/**
 * Abstract base class for all polling policies. The subscription manager will call 'start' when it wants a poller to
 * start thinking about when to next poll. The poller should call 'requestFetch' when it decides that this time has
 * come. If the subscription manager decides that polling should stop it will call 'cancel' If polling should
 * subsequently being again, the subscription manager will call `restart`.
 */
export abstract class PollingPolicy {
  /**
   * Called by the subscription manager to register a function to be called by `requestFetch`.
   * @param requestFetchCallback
   */
  public register (requestFetchCallback: RequestFetchCallback) {
    this.requestFetch = requestFetchCallback
  }

  /**
   * Tell the subscription manager to fetch data for this subscription.
   * The member points to a default implementation that raises an error to indicate that `register` was not called by
   * the subscription manager. This implementation will be replaced by a call to `register`.
   */
  protected requestFetch: RequestFetchCallback = () => {
    throw new Error('No requestFetch callback was registered')
  }

  /**
   * A data fetch has just completed. The poller should start thinking about when the next poll should occur. The
   * poller should call `requestFetch` when it decides that it is time to fetch more data.
   */
  public abstract start (): void

  /**
   * Stop trying to decide when to perform the next poll for data. Subscription manager will only call this when
   * polling is in progress, ie: there is no need for a poller to check whether polling is active.
   */
  public abstract cancel (): void

  /**
   * Restart polling after it was cancelled. This is called by the subscription manager when a subscription whose
   * subscribers have all unsubscribed gains a new subscriber, ie: the subscription was stopped because no-one was
   * listening but now someone is listening again.
   */
  public abstract restart (): void
}

/**
 * PollingPolicyTimer - Polling is requested based on a recurring timer.
 */
class PollingPolicyTimer extends PollingPolicy {
  /**
   * How long to wait before the next poll.
   */
  private readonly timeoutInMs: number

  /**
   * Stores the id returned from `setTimeout`.
   */
  private timerId: ReturnType<typeof setTimeout> | null

  /**
   * Constructor
   * @param timeoutInMs How long to wait before the next poll.
   */
  constructor (timeoutInMs: number) {
    super()
    this.timeoutInMs = timeoutInMs
    this.timerId = null
  }

  /**
   * Start a timer, request a fetch when it expires.
   */
  start () {
    this.timerId = setTimeout(() => {
      this.timerId = null
      this.requestFetch()
    }, this.timeoutInMs)
  }

  /**
   * Stop waiting for the timer to tell us when to fetch.
  */
  cancel () {
    if (this.timerId !== null) {
      clearTimeout(this.timerId)
      this.timerId = null
    }
  }

  /**
   * Immediately request a data fetch.
   */
  restart () {
    this.requestFetch()
  }
}

/**
 * PollingPolicyOnceOnly - Polling never occurs, so the data is only polled once when the subscription is initially
 * created.
 */
class PollingPolicyOnceOnly extends PollingPolicy {
  /**
   * We never start polling.
   */
  start () {
  }

  /**
   * We never have any polling to cancel.
   */
  cancel () {
  }

  /**
   * We never restart polling for data.
   */
  restart () {
  }
}
