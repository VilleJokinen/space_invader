<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!allNotificationCampaignsData"
  :meta-api-error="allNotificationCampaignsError"
  :alerts="pageAlerts"
  )
  template(#overview)
    meta-page-header-card
      template(#title) Create a New Notification Campaign
      template(#buttons)

        notification-form-button(
          id="new-campaign"
          v-if="coreStore.hello.featureFlags.pushNotifications"
          variant="primary"
          permission="api.notifications.edit"
          button-text="New Campaign"
          button-icon="plus-square"
          )

      p Notification campaigns are scheduled and localised push notifications that you can send to the players.
      div.small.text-muted Players who match the targeting criteria of a campaign will be sent a push notification in their selected language if they have enabled notifications on their device. Notifications are processed in batches by Firebase, so sending large campaigns may take up to a few hours to complete.

      b-alert.mt-5(show variant="secondary" v-if="!coreStore.hello.featureFlags.pushNotifications")
        p.font-weight-bolder.mt-2.mb-0 Push notifications not enabled
        p Push notifications have not been configured for this deployment. Set them up in your #[b-link(to="/runtimeOptions") runtime options]!

  template(#center-column)
    meta-list-card#broadcasts-list(
      title="All Notification Campaigns"
      :itemList="allNotificationCampaignsData"
      :searchFields="['id', 'params.name']"
      :filterSets="filterSets"
      :sortOptions="sortOptions"
      :defaultSortOption="1"
      :pageSize="20"
      icon="comment-alt"
    )
      template(#item-card="slot")
        MListItem
          span {{ slot.item.params.name }} #[span.font-size-normal.text-muted.small for {{ audienceSummary(slot.item.params) }}]

          template(#top-right)
            MBadge(:variant="getPhaseVariant(slot.item.phase)")
              template(#icon)
                fa-icon.mr-1(:icon="getPhaseIcon(slot.item.phase)")
              | {{ notificationPhaseDisplayString(slot.item.phase) }}

          template(#bottom-left)
            div(v-if="slot.item.phase === 'Scheduled'") Start time: #[meta-time(:date="slot.item.params.targetTime")]
            div(v-else-if="slot.item.phase === 'Sent'") Sent at: #[meta-time(:date="slot.item.params.targetTime")]
            div(v-else-if="slot.item.phase === 'Cancelled'") Cancelled
            div(v-else-if="slot.item.phase === 'DidNotRun'") {{ notificationPhaseDisplayString(slot.item.phase) }}
            div(v-else-if="slot.item.phase === 'Cancelling'")
              b-progress(
                :value="1"
                :max="1"
                animated
                variant="secondary"
              ).font-weight-bold
            div(v-else)
              b-progress(
                :value="slot.item.scannedRatioEstimate"
                :max="1"
                animated
                variant="success"
              ).font-weight-bold

          template(#bottom-right)
            div: b-link(:to="`/notifications/${slot.item.id}`") View campaign

  template(#default)
    meta-raw-data(:kvPair="allNotificationCampaignsData" name="campaigns")
</template>

<script lang="ts" setup>
import { BProgress } from 'bootstrap-vue'
import { computed, ref } from 'vue'

import { useCoreStore } from '../coreStore'
import { MBadge, type Variant, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getAllNotificationsSubscriptionOptions } from '../subscription_options/notifications'
import { getAllScanJobsSubscriptionOptions } from '../subscription_options/scanJobs'

import type { MetaPageContainerAlert } from '@metaplay/meta-ui'
import { MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption, maybePlural } from '@metaplay/meta-ui'
import NotificationFormButton from '../components/notifications/NotificationFormButton.vue'

import { notificationPhaseDisplayString } from '../coreUtils'

const coreStore = useCoreStore()

/**
 * Subscribe to notification campaigns data.
 */
const {
  data: allNotificationCampaignsData,
  error: allNotificationCampaignsError
} = useSubscription(getAllNotificationsSubscriptionOptions())

/**
 * Subscribe to database scan job data.
 */
const { data: databaseScanJobsData } = useSubscription(getAllScanJobsSubscriptionOptions())

/**
 * Get the color variant to be used in phase badge.
 * @param status current campaign phase.
 */
function getPhaseVariant (status: string): Variant {
  if (['Sent', 'Cancelled', 'Cancelling', 'DidNotRun'].includes(status)) return 'neutral'
  else if (status === 'Scheduled') return 'primary'
  else return 'success'
}

/**
 * Get the custom icon based on the campaign phase.
 * @param status current campaign phase.
 */
function getPhaseIcon (status: string): string {
  if (status === 'Sent') return 'paper-plane'
  else if (status === 'Scheduled') return 'calendar-alt'
  else if (status === 'Cancelled') return 'times'
  else if (status === 'DidNotRun') return 'bell-slash'
  else return 'paper-plane'
}

/**
 * Summary estimation of number of players targeted in the campaign.
 */
function audienceSummary (params: any): string | undefined {
  if (!params.targetPlayers?.length && !params.targetCondition) {
    return 'everyone'
  } else if (params.targetPlayers?.length && params.targetCondition) {
    return `Segment based and ${maybePlural(params.targetPlayers.length, 'player')}`
  } else if (params.targetPlayers?.length) {
    return `${maybePlural(params.targetPlayers.length, 'player')}`
  } else if (params.targetCondition) {
    return 'Segment based'
  }
}

/**
 * Sorting options for the list of notification campaigns.
 */
const sortOptions = ref([
  new MetaListSortOption('Time', 'params.targetTime', MetaListSortDirection.Ascending),
  new MetaListSortOption('Time', 'params.targetTime', MetaListSortDirection.Descending),
  new MetaListSortOption('Name', 'params.name', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'params.name', MetaListSortDirection.Descending)
])

/**
 * Filtering options for the list of notification campaigns.
 */
const filterSets = ref([
  new MetaListFilterSet('status',
    [
      new MetaListFilterOption('Scheduled', (x: any) => x.phase === 'Scheduled'),
      new MetaListFilterOption('Running', (x: any) => x.phase === 'Running'),
      new MetaListFilterOption('Sent', (x: any) => x.phase === 'Sent'),
      new MetaListFilterOption('Cancelled', (x: any) => x.phase === 'Cancelled' || x.phase === 'Cancelling'),
      new MetaListFilterOption('Did Not Run', (x: any) => x.phase === 'DidNotRun')
    ]
  )
])

/**
 * Alert messages to be displayed in at the top of the page.
 */
const pageAlerts = computed(() => {
  const allAlerts: MetaPageContainerAlert[] = []

  // Warning: Push notification jobs are globally paused.
  if (databaseScanJobsData.value?.globalPauseIsEnabled) {
    allAlerts.push({
      title: 'Push Notifications Paused',
      message: 'All database scan jobs are currently paused - this includes the job that sends push notifications. You can resume them from the scan jobs page.',
      variant: 'warning',
    })
  }
  return allAlerts
})

</script>
