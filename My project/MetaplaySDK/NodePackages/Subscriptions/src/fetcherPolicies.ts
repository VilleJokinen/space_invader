// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview Defines the various fetcher policies for the subscriptions system. A fetcher policy is a way for a
 * subscription to async fetch data and record either the data that was fetched, or the error that ocurred when trying
 *  to fetch the data.
 */

import { cloneDeep } from 'lodash-es'
import axios, { type AxiosInstance } from 'axios'

import { useGameServerApi } from '@metaplay/game-server-api'

/**
 * Create a fetcher policy that fetches data with a GET request from an HTTP endpoint.
 * @param endpoint The endpoint to fetch the data from.
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
export function getFetcherPolicyGet (endpoint: string) {
  return new FetcherPolicyGet(endpoint)
}

/**
 * Create a fetcher policy that fetches data with a POST request from an HTTP endpoint.
 * @param endpoint The endpoint to fetch the data from.
 * @param bodyData The body data to send with the POST.
 * @example
 * export function getAllActivePlayersSubscriptionOptions (): SubscriptionOptions {
    return {
      permission: 'api.players.view',
      pollingPolicy: getPollingPolicyTimer(5000),
      fetcherPolicy: getFetcherPolicyPost('/players/activePlayers', { body: 'extra data' }),
      cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
    }
  }
 */
export function getFetcherPolicyPost (endpoint: string, bodyData: object) {
  return new FetcherPolicyPost(endpoint, bodyData)
}

/**
 * Create a fetcher policy that returns a fixed value. Good for local development and testing.
 * @param data The fixed data to return.
 */
export function getFetcherPolicyFixed (data: any) {
  return new FetcherPolicyFixed(data)
}

/**
 * Fetcher policy data callback interface.
 */
type DataCallback = (data: any) => void

/**
 * Fetcher policy error callback interface.
 */
type ErrorCallback = (error: Error, isFatal: boolean) => void

/**
 * Abstract base class for all fetcher policies. The subscription manager will call `fetch` to tell the fetcher to begin
 * fetching data. The fetcher should call 'setData' when that fetch succeeds, or `setError` if it fails. If the
 * subscription manager wishes to stop the request whilst it is still in-flight it will call `cancel`.
 */
export abstract class FetcherPolicy {
  /**
   * Constructor.
   * @param uniqueKey Unique ID for this subscription.
   */
  constructor (uniqueKey: string) {
    this.uniqueKey = uniqueKey
  }

  /**
   * Called by the subscription manager to register functions to be called by `setData` and `setError`.
   * @param dataCallback
   * @param errorCallback
   */
  public register (setDataCallback: DataCallback, setErrorCallback: ErrorCallback) {
    this.setData = setDataCallback
    this.setError = setErrorCallback
  }

  /**
   * Set the data so that subscribers can see it. This should be called from within fetch when the data has actually
   * arrived.
   * The member points to a default implementation that raises an error to indicate that `register` was not called by
   * the subscription manager. This implementation will be replaced by a call to `register`.
   * @param data The data that was received.
   */
  protected setData: DataCallback = () => {
    throw new Error('No data callback was registered')
  }

  /**
   * Signal to the subscriber system that an error was encountered when trying to fetch the data. This should be called
   * from within fetch when the fetcher has failed.
   * The member points to a default implementation that raises an error to indicate that `register` was not called by
   * the subscription manager. This implementation will be replaced by a call to `register`.
   * @param error The error that ocurred.
   * @param isFatal If false then the subscriber system will try to fetch again, if true then this subscription will no
   *    longer be polled and the error will be set so that the client code can see it.
   */
  protected setError: ErrorCallback = () => {
    throw new Error('No error callback was registered')
  }

  /**
   * Start fetching data. Subscription manager will only call this once at a time, ie: there is for a fetcher to check
   * whether a fetch is already in-flight as long as they implement cancel() properly.
   */
  public abstract fetch (): void

  /**
   * Cancel the fetching of data. Subscription manager will only call when a fetch is in progress, ie: there is no need
   * for a fetcher to check whether a fetch is currently in-flight.
   */
  public abstract cancel (): void

  /**
   * Each subscription has a unique ID.
   */
  private readonly uniqueKey: string

  /**
   * Return this subscription's unique ID.
   */
  public getUniqueKey (): string {
    return this.uniqueKey
  }
}

/**
 * FetcherPolicyGet - Fetch data from an HTTP GET endpoint.
 */
class FetcherPolicyGet extends FetcherPolicy {
  /**
   * The endpoint to fetch the data from.
   */
  private readonly endpoint: string

  /**
   * Abort controller is used to cancel a request while it is still in-flight.
   */
  private abortController: AbortController | null

  /**
   * The game server API connection axios client.
   */
  private readonly gameServerApi: AxiosInstance

  /**
   * Constructor.
   * @param endpoint API endpoint to fetch the data from.
   */
  constructor (endpoint: string) {
    super(endpoint)
    this.endpoint = endpoint
    this.abortController = null
    this.gameServerApi = useGameServerApi()
  }

  /**
   * Begin fetching the data. Uses Axios.
   */
  public fetch () {
    // Set up a new abort controller if it doesn't already exist.
    if (!this.abortController) {
      this.abortController = new AbortController()
    }

    // Make the request through Axios.
    this.gameServerApi.request({
      method: 'get',
      url: this.endpoint,
      signal: this.abortController.signal,
      params: {}
    })
      .then((data) => {
        // Data was received successfully
        this.setData(data.data)
      })
      .catch((error) => {
        // There was an error in fetching the data.
        if (!axios.isCancel(error)) {
          const message = error.message
          if (message === 'Network Error') {
            // Network errors are temporary - we should still keep polling.
            this.setError(error, false)
          } else {
            // A genuine fatal error ocurred.
            this.setError(error, true)
          }
        }
      })
  }

  /**
   * Cancel an in-flight request using the abort controller.
   */
  public cancel () {
    if (this.abortController) {
      this.abortController.abort()

      // Ensure that the next request gets a brand new abort controller otherwise, if it re-uses the same one, it will
      // immediately be aborted
      this.abortController = null
    }
  }
}

/**
 * FetcherPolicyPost - Fetch data from an HTTP POST endpoint.
 */
class FetcherPolicyPost extends FetcherPolicy {
  /**
   * The endpoint to fetch the data from.
   */
  private readonly endpoint: string

  /**
   * Body data to pass along with the request.
   */
  private readonly bodyData: object

  /**
   * Abort controller is used to cancel a request while it is still in-flight.
   */
  private abortController: AbortController | null

  /**
   * The game server API connection axios client.
   */
  private readonly gameServerApi: AxiosInstance

  /**
   * Constructor.
   * @param endpoint API endpoint to fetch the data from.
   */
  constructor (endpoint: string, bodyData: object) {
    super(endpoint + JSON.stringify(bodyData))
    this.endpoint = endpoint
    this.bodyData = bodyData
    this.abortController = null
    this.gameServerApi = useGameServerApi()
  }

  /**
   * Begin fetching the data. Uses Axios.
   */
  public fetch () {
    // Set up a new abort controller if it doesn't already exist.
    if (!this.abortController) {
      this.abortController = new AbortController()
    }

    // Make the request through Axios.
    this.gameServerApi.request({
      method: 'post',
      url: this.endpoint,
      signal: this.abortController.signal,
      params: {},
      data: this.bodyData
    })
      .then((data) => {
        // Data was received successfully
        this.setData(data.data)
      })
      .catch((error) => {
        // There was an error in fetching the data.
        if (!axios.isCancel(error)) {
          const message = error.message
          if (message === 'Network Error') {
            // Network errors are temporary - we should still keep polling.
            this.setError(error, false)
          } else {
            // A genuine fatal error ocurred.
            this.setError(error, true)
          }
        }
      })
  }

  /**
   * Cancel an in-flight request using the abort controller.
   */
  public cancel () {
    if (this.abortController) {
      this.abortController.abort()

      // Ensure that the next request gets a brand new abort controller otherwise, if it re-uses the same one, it will
      // immediately be aborted
      this.abortController = null
    }
  }
}

/**
 * FetcherPolicyFixed - Pretends to fetch data but actually returns a pre-defined value instead. Useful for development
 * and testing.
 */
class FetcherPolicyFixed extends FetcherPolicy {
  /**
   * The fixed data to return.
   */
  private readonly data: any

  /**
   * Constructor
   * @param data The fixed data to return.
   */
  constructor (data: any) {
    super(getNextUniqueKey())

    // Take a deep (ie: immutable) copy of the data.
    this.data = cloneDeep(data)
  }

  /**
   * Begin fetching the data. The fetch will always immediately succeed.
   */
  public fetch () {
    this.setData(this.data)
  }

  /**
   * Since the fetch request always immediately succeeds it cannot be canceled, so there is nothing to do here.
   */
  public cancel () {
  }
}

/**
 * FetcherPolicyMockIfFailed.
 */
export type FetcherMockCallback = (endpoint: string) => any
class FetcherPolicyMockIfFailed extends FetcherPolicy {
  private readonly endpoint: string

  private readonly mockFunction: FetcherMockCallback

  constructor (endpoint: string, mockFunction: FetcherMockCallback) {
    super(endpoint)
    this.endpoint = endpoint
    this.mockFunction = mockFunction
  }

  public fetch () {
    const gameServerApi = useGameServerApi()
    gameServerApi.request({
      method: 'get',
      url: this.endpoint,
      params: {}
    })
      .then((data) => {
        // Data was received successfully
        this.setData(data.data)
      })
      .catch((error) => {
        if (error.response.status === 404 || error.response.status === 500) {
          const fakeResponse = this.mockFunction(this.endpoint)
          this.setData(fakeResponse)
          return
        }

        // There was an error in fetching the data.
        if (!axios.isCancel(error)) {
          const message = error.message
          if (message === 'Network Error') {
            // Network errors are temporary - we should still keep polling.
            this.setError(error, false)
          } else {
            // A genuine fatal error ocurred.
            this.setError(error, true)
          }
        }
      })
  }

  public cancel () {
  }
}

/**
 * UID helper.
 */
let uniqueKeyIndex = 0
function getNextUniqueKey (): string {
  return (uniqueKeyIndex++).toString()
}
