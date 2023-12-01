// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
} from '@metaplay/subscriptions'

/**
 * The options for a list of all broadcasts.
 */
export function getAllBroadcastsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.broadcasts.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/broadcasts'),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(60000),
    //     dataMutator: (data: any) => {
    //       for (const broadcast of data) {
    //         decorateBroadcast(broadcast)
    //       }
    //       return data
    //     },
  }
}

/**
 * The options for a single broadcast.
 * @param broadcastId The ID of the broadcast to fetch.
 */
export function getSingleBroadcastSubscriptionOptions (broadcastId: string): SubscriptionOptions {
  return {
    permission: 'api.broadcasts.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/broadcasts/${broadcastId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000),
    //     dataMutator: (data: any) => {
    //       decorateBroadcast(data)
    //       return data
    //     },
  }
}
