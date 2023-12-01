// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { EventSourcePolyfill } from 'event-source-polyfill'
import { useGameServerApiStore } from './gameServerApiStore'

/**
 * Callback function that is called with message data when an event is received
 * @param data Message data object that was received
 */
 type MessageCallback = (data: object) => void

/**
 * SseHandler class to simplify handling server sent events
 *
 * Usage:
 *  sse = new SseHandler('/api/sse-endpoint')
 *  sse.addMessageHandler('myMessage', data => console.log(data))
 *  sse.start()
 */
export class SseHandler {
  private readonly endpoint: string
  private eventSource: EventSourcePolyfill | null
  private fnMessageHandlers: { [messageName: string]: MessageCallback }

  /**
   * @param endpoint API endpoint address to connect to (eg: '/api/sse')
   */
  public constructor (endpoint: string) {
    this.endpoint = endpoint
    this.eventSource = null
    this.fnMessageHandlers = {}
  }

  /**
   * Add a handler for a message type
   * @param messageName Name of the message to handle
   * @param fnMessageHandler The callback to handle the message
   */
  public addMessageHandler (messageName: string, fnMessageHandler: MessageCallback): void {
    if (!this.fnMessageHandlers[messageName]) {
      this.fnMessageHandlers[messageName] = fnMessageHandler
    } else {
      console.error(`See message handler for ${messageName} already registered - ignoring`)
    }
  }

  /**
   * Open the connection and start receiving messages. Handlers for messages that were
   * added through addMessageHandler() will be automatically called if a matching
   * message is received
   */
  public async start (): Promise<void> {
    if (!this.eventSource) {
      // eslint-disable-next-line @typescript-eslint/no-this-alias -- Refactor this later. Not urgent now.
      const _this = this
      const gameServerApiStore = useGameServerApiStore()

      // Grab the auth token if one exists
      let token = null

      // Note that this could return null if the auth provider does
      // not have a bearer token
      token = gameServerApiStore.auth.bearerToken

      // Create the event stream, with or without a bearer token
      if (token !== null) {
        this.eventSource = new EventSourcePolyfill(this.endpoint, {
          headers: {
            Authorization: `Bearer ${token}`
          }
        })
      } else {
        this.eventSource = new EventSourcePolyfill(this.endpoint)
      }
      if (this.eventSource === null) {
        throw new Error('EventSourcePolyfill failed')
      }

      // Subscribe to messages form the event stream
      this.eventSource.onmessage = event => {
        try {
          const data = JSON.parse(event.data)
          const messageName = data.name
          if (messageName === undefined) {
            console.error(`Sse message had no name field: ${JSON.stringify(data)}`)
          } else {
            if (_this.fnMessageHandlers[messageName]) {
              try {
                _this.fnMessageHandlers[messageName](data.value)
              } catch (err) {
                console.error(`Sse handler failed to handle message: ${event.data}`)
              }
            } else {
              console.warn(`Unhandled Sse message: ${messageName}`)
            }
          }
        } catch (err) {
          console.error(`Failed to parse Sse message data: ${event.data}, reason: ${err}`)
        }
      }
    }
  }

  /**
   * Close the connection and stop receiving messages
   */
  public stop (): void {
    if (this.eventSource) {
      this.eventSource.close()
      this.eventSource = null
    }
  }
}
