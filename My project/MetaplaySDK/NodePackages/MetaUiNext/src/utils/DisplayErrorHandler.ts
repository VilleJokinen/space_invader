// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoveriew This file contains the `createDisplayError` function which converts `Error` objects into a generic
 * format that `MErrorCallout` can display in a user friendly way. This function has a set of registered handlers that
 * can convert different `Error` types. There are default handlers for standard `Error` and derived `AxiosError`
 * objects. You can also register your own custom handlers if you have your own bespoke `Error` objects.
 *
 * To create your own custom error handler, let's suppose you have a custom `Error` object with a single extra
 * property, 'time', which records the time that the error was created. You would create a handler like this:
 *  ```
    export function handleCustomError (error: object) {
      // Check if the given error is the type we are interested in.
      if (error instanceOf MyError) {
        // Create a DisplayError to show to the user.
        const displayError = new DisplayError(
          'My Error',
          error.message,
        )

        // Add extended technical details.
        displayError.addDetail('Time', error.time)
        return displayError
      }

      // If this wasn't one of our error objects then we return 'undefined' to indicate that we didn't handle it.
     return undefined
    }
*   ```
*
*  Then register the handler somewhere early on in your app's initialization code:
  ```
  // Register the custom error handler.
  registerHandler(handleCustomError)
*  ```
*
* Note that handlers are called in the reverse order that they were registered: last first. This means that your custom
* handlers will always be called before the built-in ones.
*/

/**
 * Note: The list of handlers is just a single chain right now - an `Error` object is passed through this chain until
 * it reaches a handler that can handle it. At this point the error is considered handled and traversal of the chain
 * stops. If there are two handlers that could both handle an error but one could do a better job then there is
 * currently no way to express this. This might need to be changed in the future.
 */

import axios, { AxiosError } from 'axios'

/**
 * Technical error details that a developer is interested in.
 * @example Stack trace, API request details.
 */
export interface ErrorDetails {
  /**
   * Text indicating the type of technical details displayed below.
   * @example 'Server stack trace'
   */
  title: string
  /**
   * The technical error details that a developer is interested in.
   * @example The error stack trace.
   */
  content: string | string[] | { [key: string]: any }
}

/**
 * Custom error class for describing an error.
 */
export class DisplayError {
  /**
   * Title of the error.
   * @example 'Axios Error'
   */
  title: string
  /**
   * Human readable description of the error.
   */
  message: string
  /**
   * Optional: Complementary text/number identifying the type of error.
   * @example HTTP status code or custom error code.
   */
  badgeText?: string | number
  /**
   * Optional: Technical error details that a developer is interested in.
   */
  details?: ErrorDetails[] | undefined

  constructor (title: string, message: string, badgeText?: string | number, details?: ErrorDetails[] | undefined) {
    this.title = title
    this.message = message
    this.badgeText = badgeText
    this.details = details
  }

  /**
   * Method to add technical error details to the DisplayError.
   * @param title Title of the technical error details.
   * @param content The technical error details that a developer is interested in.
   * @example addDetail('Javascript stack', e.stack)
   */
  addDetail (title: string, content: string | string[] | { [key: string]: any }) {
    this.details = this.details ?? []
    this.details.push({ title, content })
  }
}

// Registration of handlers. ------------------------------------------------------------------------------------------

/**
 * Error handler function type. Defines a type of function that takes an `Error` object, if it understands that object,
 * returns a `DisplayError`. If the handler does not understand the object then it should return `undefined`.
 */
type ErrorHandler = (error: Error) => DisplayError | undefined

/**
 * Array of error handlers.
 */
const handlers: ErrorHandler[] = []

/**
 * Function to add error handlers in given order.
 * New handlers are always added to the beginning of the array.
 * @param handler The error handler function to add.
 */
export function registerHandler (handler: ErrorHandler) {
  handlers.unshift(handler)
}

// Default error handlers. --------------------------------------------------------------------------------------------

/**
 * Handler for JavaScript errors.
 * @param error The error object we are interested in.
 */
function handleError (error: Error) {
  if (error instanceof Error) {
    return new DisplayError(
      'JavaScript Error',
      error.message,
      error.name,
      [
        {
          title: 'Javascript stack',
          content: error.stack ?? 'None'
        }
      ]
    )
  }
  return undefined
}
registerHandler(handleError)

/**
 * Handler for Axios errors.
 * @param error The error object we are interested in.
 */
function handleAxiosError (error: Error) {
  if (axios.isAxiosError(error)) {
    const axiosErrorObject = error as AxiosError
    const apiRequestObject = axiosErrorObject.config

    // Basic error details.
    const displayError = new DisplayError(
      axiosErrorObject.response?.statusText ?? 'Axios Error',
      axiosErrorObject.message ?? axiosErrorObject.response?.statusText,
      axiosErrorObject.response?.status)

    // Add API request details.
    if (apiRequestObject) {
      const apiRequest = {
        path: apiRequestObject.url ?? 'None',
        method: apiRequestObject.method ?? 'None',
        body: apiRequestObject?.data ?? 'None',
      }
      displayError.addDetail('API Request', apiRequest)
    }

    // Add API response details.
    displayError.addDetail('API Response', axiosErrorObject.response?.data as any || 'None')

    return displayError
  }
  return undefined
}
registerHandler(handleAxiosError)

// Main functionalty. -------------------------------------------------------------------------------------------------

/**
 * Function to create a DisplayError object from an given error object.
 * @param error The error object we are interested in.
 * @returns A DisplayError object.
 */
export function createDisplayError (error: Error) {
  // Give all registered handlers a chance to handle this error.
  // Note: Custom error handlers are always called before the default handlers.
  let displayError
  for (const handler of handlers) {
    displayError = handler(error)
    if (displayError) break
  }

  // In the unexpected situation that none of the handlers understood this error then use this fallback
  // to show *something* useful.
  if (!displayError) {
    displayError = new DisplayError(
      'Unknown Error',
      'Something went wrong. The developers will be interested in this error message:',
    )
    displayError.addDetail('Unhandled Error', JSON.stringify(error))
  }
  return displayError
}
