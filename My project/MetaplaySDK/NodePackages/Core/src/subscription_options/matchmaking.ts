// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyKeepForever,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
} from '@metaplay/subscriptions'

/**
 * The options for a list of all matchmakers.
 */
export function getAllMatchmakersSubscriptionOptions (): SubscriptionOptions {
  return {
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet('/matchmakers'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
    permission: 'api.matchmakers.view',
  }
}

/**
 * The options for a single matchmaker subscription.
 * @param matchmakerId ID of the matchmaker to subscribe to.
 */
export function getSingleMatchmakerSubscriptionOptions (matchmakerId: string): SubscriptionOptions {
  return {
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/matchmakers/${matchmakerId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(60000),
    permission: 'api.matchmakers.view',
  }
}

/**
 * The options for a list of all matchmakers a player belongs to.
 * @param playerId ID of the player whose matchmakers to subscribe to.
 */
export function getAllMatchmakersForPlayerSubscriptionOptions (playerId: string): SubscriptionOptions {
  return {
    pollingPolicy: getPollingPolicyTimer(20000),
    fetcherPolicy: getFetcherPolicyGet(`/matchmakers/player/${playerId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(60000),
    permission: 'api.matchmakers.view',
  }
}

/**
 * The options for a list of the top players of a single matchmaker.
 * @param matchmakerId ID of the matchmaker to subscribe to.
 */
export function getTopPlayersOfSingleMatchmakerSubscriptionOptions (matchmakerId: string): SubscriptionOptions {
  return {
    pollingPolicy: getPollingPolicyTimer(20000),
    fetcherPolicy: getFetcherPolicyGet(`/matchmakers/${matchmakerId}/top`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(60000),
    permission: 'api.matchmakers.view',
  }
}
