// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import axios, { AxiosError } from 'axios'

import { DisplayError } from '@metaplay/meta-ui-next'

/**
 * Handler for Metaplay API errors.
 * @param error Axios error object
 */
export function handleMetaplayApiError (error: Error) {
  if (axios.isAxiosError(error)) {
    const axiosErrorObject = error as AxiosError
    const metaplayErrorObject = ((axiosErrorObject.response?.data || {}) as any).error
    if (metaplayErrorObject) {
      // If it looks like we have a standard Metaplay extended API error object then we can get some more useful
      // information out of it.

      // Basic error details.
      const displayError = new DisplayError(
        metaplayErrorObject.message ?? 'Metaplay API Error',
        metaplayErrorObject.details ?? 'None',
        metaplayErrorObject.statusCode)

      // Add API request details.
      const apiRequestObject = axiosErrorObject.config
      if (apiRequestObject) {
        const apiRequest = {
          Path: apiRequestObject.url ?? 'None',
          Method: apiRequestObject.method ?? 'None',
          Body: apiRequestObject.data ?? 'None',
        }
        displayError.addDetail('API Request', apiRequest)
      }

      // Add server stack trace.
      if (metaplayErrorObject.stackTrace) {
        displayError.addDetail('Server Stack', metaplayErrorObject.stackTrace)
      }

      return displayError
    }
  }
  return undefined
}
