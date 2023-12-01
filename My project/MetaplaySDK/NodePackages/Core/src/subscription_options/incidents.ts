// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview A set of functions to create subscriptions to incident API endpoints.
 */

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyTimed,
  getPollingPolicyOnceOnly,
} from '@metaplay/subscriptions'

/**
 * Subscribe to a single player incident.
 * @param playerId Player Id.
 * @param incidentId Incident Id.
 * @returns Subscription object.
 */
export function getPlayerIncidentSubscriptionOptions (playerId?: string, incidentId?: string): SubscriptionOptions {
  return {
    permission: 'api.incident_reports.view',
    pollingPolicy: getPollingPolicyOnceOnly(),
    fetcherPolicy: getFetcherPolicyGet(`/players/${playerId}/incidentReport/${incidentId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000)
  }
}
