// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
  getCacheRetentionPolicyDeleteImmediately,
} from '@metaplay/subscriptions'

/**
 * The options for a list of all audit log events.
 * @param targetType Type of audit log events to retrieve.
 * @param targetId Id of target type to retrieve.
 * @param limit Optional: Maximum number of events to return. Defaults to 50.
 */
export function getAllAuditLogEventsSubscriptionOptions (targetType: string, targetId: string, limit: number = 50): SubscriptionOptions {
  return {
    permission: 'api.audit_logs.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/auditLog/search?targetType=${targetType}&targetId=${targetId}&limit=${limit}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000)
  }
}

/**
 * The options to query audit log events with advanced search.
 * @param targetType Type of audit log events to retrieve.
 * @param targetId Id of target type to retrieve.
 * @param sourceId Id of event source to retrieve.
 * @param sourceIpAddress Ip address of event source to retrieve.
 * @param sourceCountryIsoCode Iso code of event source to retrieve.
 * @param limit Reactive reference to (or just plain number) maximum number of events to return.
 */
export function getAuditLogEventsSearchSubscriptionOptions (targetType: string = '', targetId: string = '', sourceId: string = '', sourceIpAddress: string = '', sourceCountryIsoCode: string = '', limit: number = 50): SubscriptionOptions {
  let url = '/auditLog/advancedSearch?'
  if (targetType) url += `targetType=${targetType}&`
  if (targetId) url += `targetId=${targetId}&`
  if (sourceId) url += `source=${sourceId}&`
  if (sourceIpAddress) url += `sourceIpAddress=${sourceIpAddress}&`
  if (sourceCountryIsoCode) url += `sourceCountryIsoCode=${sourceCountryIsoCode}&`
  if (limit) url += `limit=${limit}&`

  return {
    permission: 'api.audit_logs.search',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(url),
    cacheRetentionPolicy: getCacheRetentionPolicyDeleteImmediately()
  }
}
