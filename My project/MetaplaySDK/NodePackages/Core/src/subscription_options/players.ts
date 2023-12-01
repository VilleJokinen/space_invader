// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyKeepForever,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
  getFetcherPolicyFixed
} from '@metaplay/subscriptions'

/**
 * The options for a list of all players.
 */
export function getAllPlayersSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.players.view',
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet('/players'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * The options for a list of all active players.
 */
export function getAllActivePlayersSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.players.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/players/activePlayers'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * The options for a single player.
 * @param playerId The ID of the player to get the subscription options for.
 */
export function getSinglePlayerSubscriptionOptions (playerId: string): SubscriptionOptions {
  return {
    permission: 'api.players.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/players/${playerId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000),
  }
}

/**
 * The options for a list of all players who have their isDeveloper set as true.
 */
export function getDeveloperPlayersSubscriptionOptions (): SubscriptionOptions {
  return {
    // TODO Paul's initial comment: "permission: 'api.players.set_developer', we might want to a specific different permission in the future"
    // We are using same permission as above for now.
    permission: 'api.players.view',
    pollingPolicy: getPollingPolicyTimer(10_000),
    fetcherPolicy: getFetcherPolicyGet('/players/developers'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}
