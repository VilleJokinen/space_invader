// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview A set of functions to create subscriptions to guild API endpoints.
 */

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyKeepForever,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
} from '@metaplay/subscriptions'

/**
 * Subscribe to a list of all guilds from the `/guilds` endpoint.
 * @returns Subscription object.
 */
export function getAllGuildsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.guilds.view',
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet('/guilds'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to a list of all active guilds from the `/guilds/activeGuilds` endpoint.
 * @returns Subscription object.
 */
export function getActiveGuildsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.guilds.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/guilds/activeGuilds'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to a single guild.
 * @param guildId Guild Id.
 * @returns Subscription object.
 */
export function getSingleGuildSubscriptionOptions (guildId: string): SubscriptionOptions {
  return {
    permission: 'api.guilds.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/guilds/${guildId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000),
  }
}
