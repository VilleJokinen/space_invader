// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyKeepForever,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
  getPollingPolicyOnceOnly,
} from '@metaplay/subscriptions'

/**
 * The options for the currently active localization Id.
 * TODO: merge to status endpoint to save on polling calls?
 */
export function getActiveLocalizationIdSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.localization.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/activeLocalizationId'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever()
  }
}

/**
 * The options for a list of all localizations.
 */
export function getAllLocalizationsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.localization.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/localizations?showArchived=true'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever()
  }
}

/**
 * The options to fetch a localization as a list
 * @param localizationId The ID of the localization to fetch.
 */
export function getSingleLocalizationSubscriptionOptions (localizationId: string): SubscriptionOptions {
  return {
    permission: 'api.localization.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet(`/localization/${localizationId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(30000)
  }
}
/**
 * The options to fetch a localization as a table
 * @param localizationId The ID of the localization to fetch.
 */
export function getSingleLocalizationTableSubscriptionOptions (localizationId: string): SubscriptionOptions {
  return {
    permission: 'api.localization.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet(`/localization/${localizationId}/table`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(30000)
  }
}
