// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getFetcherPolicyPost,
  getCacheRetentionPolicyKeepForever,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
  getPollingPolicyOnceOnly,
} from '@metaplay/subscriptions'

/**
 * The options for the currently active game config Id.
 * TODO: merge to status endpoint to save on polling calls?
 */
export function getActiveGameConfigIdSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.game_config.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/activeGameConfigId'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever()
  }
}

/**
 * The options for a list of all game configs.
 */
export function getAllGameConfigsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.game_config.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/gameConfig?showArchived=true'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever()
  }
}

/**
 * The options for a list of all game configs.
 */
export function getAllMinimalGameConfigsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.game_config.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/gameConfig/minimal?showArchived=true'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever()
  }
}

/**
 * The options to fetch overview library counts a single game config.
 * @param gameConfigId The ID of the game config to fetch.
 */
export function getSingleGameConfigCountsSubscriptionOptions (gameConfigId: string): SubscriptionOptions {
  return {
    permission: 'api.game_config.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet(`/gameConfig/${gameConfigId}/count`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(30000)
  }
}

/**
 * The options to fetch library contents from a game config.
 * @param gameConfigId The ID of the game config to fetch.
 * @param libraryId The ID of the library to fetch.
 * @param experimentId The ID of the experiment that we are interested in.
 */
export function getSingleGameConfigLibraryDetailsSubscriptionOptions (gameConfigId: string, libraryId: string, experimentId?: string): SubscriptionOptions {
  const body: { Libraries: string[], Experiments?: string[] } = {
    Libraries: [libraryId],
  }

  if (experimentId) {
    body.Experiments = [experimentId]
  }

  return {
    permission: 'api.game_config.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyPost(
      `/gameConfig/${gameConfigId}/details`,
      body
    ),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(30000)
  }
}
