// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyKeepForever,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
} from '@metaplay/subscriptions'

/**
 * The options for a list of all leagues.
 */
export function getAllLeaguesSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.leagues.view',
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet('/leagues'),
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever(),
  }
}

/**
 * The options to fetch a single league.
 * @param leagueId The ID of the league to get the subscription options for.
 */
export function getSingleLeagueSubscriptionOptions (leagueId: string): SubscriptionOptions {
  return {
    permission: 'api.leagues.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/leagues/${leagueId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000),
  }
}

/**
 * The options to fetch a single season of a particular league.
 * @param leagueId The ID of the league to get the subscription options for.
 * @param seasonId The ID of the season to get the subscription options for. Use `$active` as a shortcut for the
 *  currently active season.
 */
export function getSingleLeagueSeasonSubscriptionOptions (leagueId: string, seasonId: number | '$active'): SubscriptionOptions {
  return {
    permission: 'api.leagues.view',
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet(`/leagues/${leagueId}/${seasonId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000),
  }
}

/**
 * The options to fetch a single division of a particular league.
 * @param divisionId The ID of the division to get the subscription options for.
 */
export function getSingleDivisionSubscriptionOptions (divisionId: string): SubscriptionOptions {
  return {
    permission: 'api.leagues.view',
    pollingPolicy: getPollingPolicyTimer(10000),
    fetcherPolicy: getFetcherPolicyGet(`/divisions/${divisionId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000),
  }
}

/**
 * The options to fetch a all leagues for a single participant.
 * @param participantId The ID of the participant to get the subscription options for.
 */
export function getAllLeaguesForSingleParticipant (participantId: string): SubscriptionOptions {
  return {
    permission: 'api.leagues.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/leagues/participant/${participantId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000),
  }
}

/**
 * The options to fetch active divisions for a league season.
 * @param leagueId The ID of the league we are fetching divisions for.
 * @param season The number of the league season.
 */
export function getActiveDivisionsForLeagueSeasonSubscriptionOptions (leagueId: string, seasonId: number): SubscriptionOptions {
  return {
    permission: 'api.leagues.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/divisions/active/${leagueId}/${seasonId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000),
  }
}
