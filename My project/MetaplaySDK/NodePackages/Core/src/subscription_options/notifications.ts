// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview A set of functions to create subscriptions to notification API endpoints.
 */

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyTimed,
  getCacheRetentionPolicyKeepForever,
  getPollingPolicyTimer,
} from '@metaplay/subscriptions'

/**
 * Subscribe to a list of all notifications from the `/notifications` endpoint.
 * @returns Subscription object.
 */
export function getAllNotificationsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.notifications.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/notifications'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever()
  }
}

/**
 * Subscribe to a single notification.
 * @param notificationId Notification Id.
 * @returns Subscription object.
 */
export function getSingleNotificationSubscriptionOptions (notificationId: string): SubscriptionOptions {
  return {
    permission: 'api.notifications.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/notifications/${notificationId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000)
  }
}
