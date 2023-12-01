// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview A set of functions to create subscriptions to 'general' API endpoints. This file is where
 * subscriptions that don't fit in to any other category belong.
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
 * Generalized subscription for any simple GET endpoint. If you want to subscribe to some data and there isn't already
 * a specialized `getSubscriptionOptions` function, then this is probably the place to go. Data will be fetched every 2
 * seconds and cached for 30 seconds. If you want more specialized polling/caching then set up a new
 * `getSubscriptionOptions` function.
 * @param endpoint URL to fetch from.
 * @param permission Optional: Permission to check for.
 * @returns Subscription object.
 */
export function getSimpleEndpointSubscriptionOptions (endpoint: string, permission?: string): SubscriptionOptions {
  return {
    permission,
    pollingPolicy: getPollingPolicyTimer(2000),
    fetcherPolicy: getFetcherPolicyGet(endpoint),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(30000)
  }
}

/**
 * Subscribe to backend status from the `/status` endpoint.
 * @returns Subscription object.
 */
export function getBackendStatusSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.general.view',
    pollingPolicy: getPollingPolicyTimer(2000),
    fetcherPolicy: getFetcherPolicyGet('/status'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to broadcasts status from the `/broadcasts` endpoint.
 * @returns Subscription object.
 */
export function getBroadcastsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.broadcasts.view',
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet('/broadcasts'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to database item counts from the `/databaseItemCounts` endpoint.
 * @returns Subscription object.
 */
export function getDatabaseItemCountsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.general.view',
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet('/databaseItemCounts'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to database status from the `/databaseStatus` endpoint.
 * @returns Subscription object.
 */
export function getDatabaseStatusSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.database.status',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/databaseStatus'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to game data from the `/gameData` endpoint.
 * @returns Subscription object.
 */
export function getGameDataSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.general.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet('/gameData'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to player segments from the `/segmentation` endpoint. Note that this subscription fixes up the cases where
 * a segment has no display name or subscription set in the game configs.
 * @returns Subscription object.
 */
export function getPlayerSegmentsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.segmentation.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/segmentation'),
    dataMutator: (data: any) => {
      // Fixup segments that don't have display names or descriptions
      data.segments = data.segments.map((segment: any) => {
        segment.info.displayName = segment.info.displayName || segment.info.segmentId
        segment.info.description = segment.info.description || 'No description available.'
        return segment
      })
      return data
    },
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to a single player segment.
 * @param segmentId The ID of the segment to get the subscription options for.
 * @returns Subscription object.
 */
export function getSinglePlayerSegmentSubscriptionOptions (segmentId: string): SubscriptionOptions {
  return {
    permission: 'api.segmentation.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/segmentation/${segmentId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to runtime options from the `/runtimeOptions` endpoint.
 * @returns Subscription object.
 */
export function getRuntimeOptionsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.runtime_options.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet('/runtimeOptions'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to other environment links from the `/quickLinks` endpoint.
 * @returns Subscription object.
 */
export function getQuickLinksSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.quick_links.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet('/quickLinks'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to dashboard options from the `/dashboardOptions` endpoint.
 * @returns Subscription object.
 */
export function getDashboardOptionsSubscriptionOptions (): SubscriptionOptions {
  return {
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet('/dashboardOptions'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to static config from the `/staticConfig` endpoint.
 * @returns Subscription object.
 */
export function getStaticConfigSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.general.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet('/staticConfig'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * Subscribe to incident reports config from the `/incidentReports` endpoint.
 * @param count Maximum number of incidents to fetch.
 * @param fingerprint Optional: Incident fingerprint type. If supplied then only
 *    incidents that match this fingerprint will be fetched.
 * @returns Subscription object.
 */
export function getIncidentReportsSubscriptionOptions (count?: number, fingerprint?: string): SubscriptionOptions {
  const endpoint = `/incidentReports/${fingerprint ?? 'latest'}/${count ?? ''}`
  return {
    permission: 'api.incident_reports.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(endpoint),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000)
  }
}
