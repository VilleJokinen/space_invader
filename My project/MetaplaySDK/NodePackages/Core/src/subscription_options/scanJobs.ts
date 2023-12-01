// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyKeepForever,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
  getPollingPolicyOnceOnly,
} from '@metaplay/subscriptions'

/**
 * The options for a list of all scan jobs.
 */
export function getAllScanJobsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.scan_jobs.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/databaseScanJobs?jobHistoryLimit=20'),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000)
  }
}

/**
 * The options for a list of all available maintenance job types.
 */
export function getAllMaintenanceJobTypesSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.scan_jobs.manage',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet('/maintenanceJobs'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever()
  }
}
