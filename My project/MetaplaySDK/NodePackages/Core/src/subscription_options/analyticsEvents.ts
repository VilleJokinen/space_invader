// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview A set of functions to create subscriptions to analytics API endpoints.
 * Note: naming this file just "analytics" puts it on ad-block lists.
 */

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyKeepForever,
  getPollingPolicyOnceOnly,
} from '@metaplay/subscriptions'

/**
 * The options for fetching the database schema preview for an analytics event.
 * @param eventId The ID of the event to fetch the schema preview for.
 */
export function getAnalyticsEventBigQueryExampleSubscriptionOptions (eventId: string): SubscriptionOptions {
  return {
    permission: 'api.analytics_events.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet(`/analyticsEvents/${eventId}/bigQueryExample`),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever()
  }
}

/**
 * The options to fetch a list of all analytics events.
 */
export function getAllAnalyticsEventsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.analytics_events.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet('/analyticsEvents'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever()
  }
}
