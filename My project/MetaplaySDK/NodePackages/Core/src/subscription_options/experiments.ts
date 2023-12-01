// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview A set of functions to create subscriptions to experiment API endpoints.
 */

import {
  type SubscriptionOptions,
  getFetcherPolicyGet,
  getCacheRetentionPolicyKeepForever,
  getCacheRetentionPolicyTimed,
  getPollingPolicyTimer,
} from '@metaplay/subscriptions'

/**
 * Subscribe to a list of all experiments from the `/experiments` endpoint. Note that this subscription fixes up the
 * cases where an experiment has no display name or subscription set in the game configs.
 * @returns Subscription object.
 */
export function getAllExperimentsSubscriptionOptions (): SubscriptionOptions {
  return {
    permission: 'api.experiments.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet('/experiments'),
    dataMutator: (data: any) => {
      // Fixup experiments that don't have display names or descriptions.
      data.experiments = data.experiments.map((experiment: any) => {
        experiment.displayName = experiment.displayName || experiment.experimentId
        experiment.description = experiment.description || 'No description available.'
        return experiment
      })
      return data
    },
    cacheRetentionPolicy: getCacheRetentionPolicyKeepForever()
  }
}

/**
 * Subscribe to a single experiment.
 * @param experimentId Experiment Id.
 * @returns Subscription object.
 */
export function getSingleExperimentSubscriptionOptions (experimentId: string): SubscriptionOptions {
  return {
    permission: 'api.experiments.view',
    pollingPolicy: getPollingPolicyTimer(5000),
    fetcherPolicy: getFetcherPolicyGet(`/experiments/${experimentId}`),
    cacheRetentionPolicy: getCacheRetentionPolicyTimed(10000)
  }
}
