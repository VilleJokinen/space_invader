// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
  getCacheRetentionPolicyDeleteImmediately,
} from '@metaplay/subscriptions'

/**
 * The options for a list of all offers.
 * @param customEvaluationTime The time to evaluate the offers at. If not provided, the current time will be used.
 */
export function getAllOffersSubscriptionOptions (customEvaluationTime?: string): SubscriptionOptions {
  return {
    permission: 'api.activables.view',
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet(customEvaluationTime ? `/offers?time=${customEvaluationTime.replace('+', '%2B')}` : '/offers'),
    cacheRetentionPolicy: customEvaluationTime ? getCacheRetentionPolicyDeleteImmediately() : getCacheRetentionPolicyTimed(10000),
  }
}

/**
 * Subscribe to a single offer group.
 * @param offerGroupId The ID of the offerGroup to get the subscription options for.
 * @returns Subscription object.
 */
export function getSingleOfferGroupSubscriptionOptions (offerGroupId: string): SubscriptionOptions {
  return {
    permission: 'api.activables.view',
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet(`/offers/offerGroup/${offerGroupId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000)
  }
}

/**
 * Subscribe to a single offer.
 * @param offerId The ID of the offer to get the subscription options for.
 * @returns Subscription object.
 */
export function getSingleOfferSubscriptionOptions (offerId: string): SubscriptionOptions {
  return {
    permission: 'api.activables.view',
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet(`/offers/offer/${offerId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000)
  }
}

/**
 * The options for a list of all offers for a player.
 * @param playerId The ID of the player to get the offers for.
 * @param customEvaluationTime The time to evaluate the offers at. If not provided, the current time will be used.
 */
export function getAllOffersForPlayerSubscriptionOptions (playerId: string, customEvaluationTime?: string): SubscriptionOptions {
  return {
    permission: 'api.activables.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(customEvaluationTime ? `/offers/${playerId}?time=${encodeURIComponent(customEvaluationTime)}` : `/offers/${playerId}`),
    cacheRetentionPolicy: customEvaluationTime ? getCacheRetentionPolicyDeleteImmediately() : getCacheRetentionPolicyTimed(5000),
  }
}
