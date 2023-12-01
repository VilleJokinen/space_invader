// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview A set of functions to create subscriptions to NFT API endpoints.
 */

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyKeepForever,
  getCacheRetentionPolicyTimed,
  getPollingPolicyOnceOnly,
  getPollingPolicyTimer,
} from '@metaplay/subscriptions'

/**
 * Subscribe to a general NFT configuration from the `/nft` endpoint.
 * Contains the configuration of ledgers as well as of collections.
 * @returns Subscription object.
 */
export function getGeneralNftInfoSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.nft.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet('/nft'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to a NFT collection
 * @param collectionId NFT collection Id.
 * @returns Subscription object.
 */
export function getSingleNftCollectionSubscriptionOptions (collectionId?: string): SubscriptionOptions {
  return {
    permission: 'api.nft.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/nft/${collectionId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000),
  }
}

/**
 * Subscribe to an NFT
 * @param collectionId NFT collection Id containing the NFT.
 * @param tokenId NFT Id.
 * @returns Subscription object.
 */
export function getSingleNftSubscriptionOptions (collectionId?: string, tokenId?: string): SubscriptionOptions {
  return {
    permission: 'api.nft.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/nft/${collectionId}/${tokenId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000),
  }
}
