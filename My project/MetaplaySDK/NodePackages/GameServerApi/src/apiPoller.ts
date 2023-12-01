// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import axios, { type AxiosInstance } from 'axios'
import { useGameServerApi } from './gameServerApi'

/**
 * List of HTTP methods that APIPoller supports. Currently only 'GET', but could
 * be any others.
 */
type ApiPollerHttpMethod = 'GET'

/**
 * Callback function to get the time between polls.
 * @returns Number of ms to wait between polls.
 */
type TimeoutCallback = () => number

/**
 * Callback function to return parameters to pass to the endpoint when polling.
 * @returns Parameters object or null if there are no parameters.
 */
type ParamsCallback = () => object | null

/**
 * Callback function that is called with the data that was returned by the API call.
 * @param data Data object that was returned by the API call.
 */
type HandlerCallback = (data: object) => void

/** Callback function that is called if an error occurs when fetching the data or during
 * processing of the handlerCallback.
  * @param exception Exception object.
*/
type ErrorCallback = (error: Error) => void

/**
 * ApiPoller class to simplify regular polling of endpoints for data.
 */
export class ApiPoller {
  private readonly timeout: number | TimeoutCallback
  private readonly method: ApiPollerHttpMethod
  private readonly endpoint: string
  private readonly fnParams: ParamsCallback | null
  private readonly fnHandler: HandlerCallback
  private readonly fnError: ErrorCallback | null
  private readonly gameServerApi: AxiosInstance
  private readonly abortController: AbortController

  // ID of an active timer, or null.
  private timeoutId: number | null

  // true if the poller is currently stopped.
  private isStopped: boolean

  // Internal count of number of ongoing poll() requests.
  private requestLockCount: number

  /**
   * Constructor for APIPoller.
   * @param timeout Either the number of ms to wait between polls, or a function that returns the number of ms.
   * @param method HTTP method (eg: 'get').
   * @param endpoint API endpoint address to poll (eg: '/someEndpoint').
   * @param fnParams Optional function to return parameters to pass to the endpoint.
   * @param fnCallback Called with data from the results of the API call.
   * @param fnError Optional. Called with error object if the request fails.
   */
  public constructor (timeout: number | TimeoutCallback, method: ApiPollerHttpMethod, endpoint: string, fnParams: ParamsCallback | null, fnHandler: HandlerCallback, fnError: ErrorCallback | null = null) {
    this.timeout = timeout
    this.method = method
    this.endpoint = endpoint
    this.fnParams = fnParams
    this.fnHandler = fnHandler
    this.fnError = fnError
    this.gameServerApi = useGameServerApi()
    this.abortController = new AbortController()

    this.timeoutId = null
    this.isStopped = false
    this.requestLockCount = 0

    this.poll().catch((error) => {
      console.error('Error in APIPoller constructor', error)
    })
  }

  /**
   * Stop the polling. Call in a component's beforeDestroy().
   */
  public stop (): void {
    if (!this.isStopped) {
      if (this.requestLockCount > 0) {
        this.abortController.abort()
      }
      if (this.timeoutId !== null) {
        clearTimeout(this.timeoutId)
      }
      this.isStopped = true
    }
  }

  /**
   * Restart polling.
   */
  public async restart () {
    if (this.isStopped) {
      this.isStopped = false
      await this.poll()
    }
  }

  /**
   * Get the data now, then continue polling. Use if you want to update a component
   * right now without waiting for the usual poll cycle.
   */
  public async getNow (): Promise<void> {
    if (!this.isStopped && this.requestLockCount === 0) {
      if (this.timeoutId !== null) {
        this.abortController.abort()
        clearTimeout(this.timeoutId)
      }
      await this.poll()
    }
  }

  /**
   * Internal function to poll the data once and then set up a timer for the next poll.
   */
  private async poll (): Promise<void> {
    try {
      this.requestLockCount++
      let params: object | null = {}
      if (this.fnParams) {
        params = this.fnParams()
      }
      const result = (await this.gameServerApi.request({
        url: this.endpoint,
        method: this.method,
        signal: this.abortController.signal,
        params
      })
      ).data
      this.fnHandler(result)
    } catch (e: unknown) {
      if (axios.isCancel(e)) {
        // This error came from us cancelling the request, so we should just ignore it.
      } else {
        if (this.fnError) {
          if (e instanceof Error) {
            this.fnError(e)
          }
          // TODO: What if this isn't an Error?
        } else {
          // TODO: refactor to throw an error and handle in async/await try/catch instead?
          console.error(`Error during API polling of ${this.endpoint}: ${e}`)
        }
      }
    } finally {
      this.requestLockCount--
      if (!this.isStopped) {
        const timeout = typeof this.timeout === 'function' ? this.timeout() : this.timeout
        this.timeoutId = window.setTimeout(() => {
          void this.poll()
        }, timeout)
      }
    }
  }
}
