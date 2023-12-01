// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import axios from 'axios'

import { useGameServerApiStore } from './gameServerApiStore'

/**
 * Returns the axios instance for the game server API.
 */
export function useGameServerApi () {
  return gameServerApi
}

// showErrorToast(err.response.data.error.message + ' ' + err.response.data.error.details, `Backend Error ${err.response.data.error.statusCode}`)
let errorVisualizationHandler: ((title: string, message: string) => void) | undefined

/**
 * HTTP requests can fail for a number of reasons. This function allows you to register a handler that will be called
 * whenever a request fails so that it can be shown on screen. The handler will be called with a pre-formatted title
 * and message.
 */
export function registerErrorVisualizationHandler (handler: (title: string, message: string) => void) {
  errorVisualizationHandler = handler
}

// From here onwards, we create and configure the gameServerApi instance.

// First, create the Axios instance.
const gameServerApi = axios.create({
  // NOTE: this route is now hardcoded and that might be a problem if we ever change the backend.
  baseURL: '/api'
})

// Pause all requests while the page is in the background.
let delayedRequestsQueue: object[] | null = null

function handleVisiblityChange () {
  if (document.hidden) {
    // Start pooling up connections.
    if (delayedRequestsQueue === null) {
      delayedRequestsQueue = []
    }
  } else if (delayedRequestsQueue !== null) {
    const requestsToDispatch = delayedRequestsQueue
    delayedRequestsQueue = null

    requestsToDispatch.forEach((query: any) => {
      const [resolve, value] = query
      resolve(value)
    })
  }
}

// Delayed request handling for every call.
gameServerApi.interceptors.request.use(
  async req => {
    if (delayedRequestsQueue !== null) {
      // eslint-disable-next-line @typescript-eslint/no-non-null-assertion -- we know it's not null here.
      return await new Promise((resolve, reject) => { delayedRequestsQueue!.push([resolve, req]) })
    } else {
      return req
    }
  },
  async err => {
    return await Promise.reject(err)
  }
)

// Hook into requests so that we can track when the server is unreachable.
gameServerApi.interceptors.request.use(
  req => {
    return req
  },
  async err => {
    if (errorVisualizationHandler) errorVisualizationHandler('Error Connecting to Backend', err.message)
    console.error('🛑 Error Connecting to Backend:', err.message)
    return await Promise.reject(err)
  }
)

// Hook into responses so that we can track the number of in-flight requests and respond to errors nicely.
gameServerApi.interceptors.response.use(
  async res => {
    const gameServerApiStore = useGameServerApiStore()
    if (!gameServerApiStore.isConnected) {
      gameServerApiStore.isConnected = true
      gameServerApiStore.hasConnected = true
    }
    return res
  },
  async err => {
    const gameServerApiStore = useGameServerApiStore()
    // If the error is not intended.
    if (!axios.isCancel(err)) {
      let message = err.message
      let messageHandled = false

      if (message === 'Network Error') {
        if (gameServerApiStore.hasConnected) {
          // Network error after connection has been established, connection to backend lost.
          gameServerApiStore.isConnected = false
          messageHandled = true
        } else {
          message = 'Could not reach the game server. Is your internet connection ok?'
        }
      }

      if (!messageHandled) {
        // If the returned error was a MetaplayHttpException, use that
        if (err.response?.data?.error) {
          if (errorVisualizationHandler) errorVisualizationHandler(`Backend Error ${err.response.data.error.statusCode}`, err.response.data.error.message + ' ' + err.response.data.error.details)
          // console.error(`Backend Error: ${err.response.data.error.statusCode}`, err.response.data.error.message + ' ' + err.response.data.error.details)
        } else {
          // Otherwise go with the Axios error (less useful)
          if (errorVisualizationHandler) errorVisualizationHandler('Backend Error', message)
          // console.error('Backend Error:', message)
        }
      }
    }

    return await Promise.reject(err)
  }
)

handleVisiblityChange()
document.addEventListener('visibilitychange', handleVisiblityChange, false)
