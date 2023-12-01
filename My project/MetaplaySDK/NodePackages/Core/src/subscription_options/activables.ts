// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyKeepForever,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
  getPollingPolicyOnceOnly,
  getCacheRetentionPolicyDeleteImmediately,
} from '@metaplay/subscriptions'

// TODO: Move to hello?
/**
 * Get subscription options for whether `force phase` is enabled or not.
 */
export function getIsActivableForcePhaseEnabledSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.activables.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet('/activables/forcePhaseEnabled'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * The options to get all activables in the game.
 * @param customEvaluationTime Optional: Custom evaluation time. If not provided, the current game server time will be used.
 */
export function getAllActivablesSubscriptionOptions (customEvaluationTime?: string): SubscriptionOptions {
  return {
    permission: 'api.activables.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(customEvaluationTime ? `/activables?time=${encodeURIComponent(customEvaluationTime)}` : '/activables'),
    cacheRetentionPolicy: customEvaluationTime ? getCacheRetentionPolicyDeleteImmediately() : getCacheRetentionPolicyTimed(10000),
  }
}

/**
 * The options to get a single activable in the game.
 * @param kindId The category kind id (e.g "event").
 * @param activableId The ID of the activable to get the subscription options for.
 * @returns A subscription object.
 */
export function getSingleActivableSubscriptionOptions (kindId: string, activableId: string): SubscriptionOptions {
  return {
    permission: 'api.activables.view',
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet(`activables/activable/${kindId}/${activableId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000)
  }
}

/**
 * The options to get all activables in the game and their subjective state for a player.
 * @param playerId The ID of the player whose subjective state should be used.
 * @param customEvaluationTime Optional: Custom evaluation time. If not provided, the current game server time will be used.
 */
export function getAllActivablesForPlayerSubscriptionOptions (playerId: string, customEvaluationTime?: string): SubscriptionOptions {
  return {
    permission: 'api.activables.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(customEvaluationTime ? `/activables/${playerId}?time=${encodeURIComponent(customEvaluationTime)}` : `/activables/${playerId}`),
    cacheRetentionPolicy: customEvaluationTime ? getCacheRetentionPolicyDeleteImmediately() : getCacheRetentionPolicyTimed(5000),
  }
}
