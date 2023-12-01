// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

export {
  getFetcherPolicyFixed,
  getFetcherPolicyGet,
  getFetcherPolicyPost,
} from './fetcherPolicies'

export {
  getCacheRetentionPolicyDeleteImmediately,
  getCacheRetentionPolicyKeepForever,
  getCacheRetentionPolicyTimed,
} from './cacheRetentionPolicies'

export {
  getPollingPolicyOnceOnly,
  getPollingPolicyTimer,
} from './pollingPolicies'

export {
  type SubscriptionDetails,
  type SubscriptionOptions,
  fetchSubscriptionDataOnceOnly,
  useDynamicSubscription, // Deprecated in R25
  useStaticSubscription, // Deprecated in R25
  useSubscription,
  useManuallyManagedStaticSubscription,
} from './subscriptions'
