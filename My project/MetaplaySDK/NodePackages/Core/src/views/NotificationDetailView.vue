<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!singleNotificationCampaignsData"
  :meta-api-error="singleNotificationCampaignsError"
  :alerts="pageAlerts"
  )
  template(#error-card-message)
    p Oh no, something went wrong while trying to access the notification!

  template(#overview)
    meta-page-header-card(:id="`${campaignInfo.id}`")
      template(#title) {{ campaignInfo.campaignParams.name }}
      template(#title-badge)
        //- Running
        MBadge.ml-2(v-if="campaignInfo.campaignPhase == 'Running'" :variant="phaseVariant")
          template(#icon)
            fa-icon(icon="broadcast-tower")
          | {{ campaignInfo.campaignPhase }}
        //- Cancelling
        MBadge.ml-2(v-else-if="campaignInfo.campaignPhase == 'Cancelling'" :variant="phaseVariant")
          template(#icon)
            fa-icon(icon="broadcast-tower")
          | {{ campaignInfo.campaignPhase }}
        //- Scheduled
        MBadge.ml-2(v-else-if="campaignInfo.campaignPhase == 'Scheduled'" :variant="phaseVariant")
          template(#icon)
            fa-icon(icon="calendar-alt")
          | {{ campaignInfo.campaignPhase }}
        //- Cancelled
        MBadge.ml-2(v-else-if="campaignInfo.campaignPhase == 'Cancelled'" :variant="phaseVariant")
          template(#icon)
            fa-icon(icon="times")
          | {{ campaignInfo.campaignPhase }}
        //- Did Not Run
        MBadge.ml-2(v-else-if="campaignInfo.campaignPhase == 'DidNotRun'" :variant="phaseVariant")
          template(#icon)
            fa-icon(icon="bell-slash")
          | {{ notificationPhaseDisplayString(campaignInfo.campaignPhase) }}
        //- Sent
        MBadge.ml-2(v-else)
          template(#icon)
            fa-icon(icon="paper-plane")
          | {{ campaignInfo.campaignPhase }}
      template(#buttons)
        notification-form-button(
          id="duplicate-campaign"
          button-text="Duplicate"
          variant="primary"
          :oldNotification="campaignFormInfo"
          @refresh="singleNotificationCampaignsRefresh"
          :update="false"
          )

        notification-form-button(
          id="edit-campaign"
          v-if="campaignInfo.campaignPhase === 'Scheduled'"
          button-text="Edit"
          variant="primary"
          :oldNotification="campaignFormInfo"
          @refresh="singleNotificationCampaignsRefresh"
          :update="true"
          ).ml-2

        meta-button(
          v-if="campaignInfo.campaignPhase == 'Running'"
          @click="cancelCampaign"
          variant="danger"
          permission="api.notifications.edit"
          ).ml-2 Cancel

        meta-button(
          modal="action-delete-campaign"
          variant="danger"
          :disabled="!canDeleteCampaign()"
          permission="api.notifications.edit"
          :tooltip="canDeleteCampaign() ? undefined : 'You cannot delete this campaign in its current phase.'"
          ).ml-2 Delete

      span.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small responsive).mt-1
        b-tbody
          b-tr
            b-td Audience Size Estimate
            b-td.text-right #[meta-audience-size-estimate(:sizeEstimate="isTargeted() ? singleNotificationCampaignsData.audienceSizeEstimate : undefined")]
          b-tr
            b-td Start Time
            b-td.text-right #[meta-time(:date="campaignInfo.campaignParams.targetTime" showAs="datetime")]

      span.font-weight-bold Progress
      b-row.mb-4
        b-col
          b-progress(
            v-if="campaignInfo.stats"
            :value="campaignInfo.stats.scanStats.scannedRatioEstimate"
            :max="1"
            show-progress
            :animated="campaignInfo.campaignPhase == 'Running'"
            :variant="phaseVariant"
            ).font-weight-bold
          b-progress(
            v-else
            :value="0"
            :max="1"
            show-progress
            :variant="phaseVariant"
            ).font-weight-bold

      //- Campaign statistics
      div(v-if="campaignInfo.campaignPhase !== 'DidNotRun'")
        span.font-weight-bold #[fa-icon(icon="chart-line")] Statistics
        b-row(align-h="center").mt-1
          b-col(xl="9" v-if="!campaignInfo.stats").text-center
            b-alert.mt-1(show variant="secondary") Stats will be collected after the campaign sending starts.
          b-col(v-else)
            b-table-simple(small responsive)
              b-tbody
                b-tr
                  b-td Duration
                  b-td.text-right #[meta-duration(:duration="jobDuration" showAs="humanizedSentenceCase")]
                b-tr
                  b-td Players Notified
                  b-td.text-right {{ abbreviateNumber(campaignInfo.stats.notificationStats.numPlayersNotified) }}
                b-tr
                  b-td Players Notifications Failed
                  b-td.text-right {{ abbreviateNumber(campaignInfo.stats.notificationStats.notificationFailedPlayers.count) }}
                b-tr
                  b-td Firebase Analytics Label
                  b-td.text-right {{ campaignInfo.campaignParams.firebaseAnalyticsLabel }}

  template(#default)
    meta-generated-section(title="Content" :value="campaignInfo.campaignParams.content")

    b-row(no-gutters align-v="center").mt-3.mb-2
      h3 Targeting
      h4.ml-2: MBadge(:variant="isTargeted() ? 'success' : 'neutral'") {{ isTargeted() ? 'On' : 'Off' }}

    b-row(align-h="center")
      b-col(md="6").mb-3
        targeting-card(ownerTitle="This notification", :targetCondition="campaignInfo.campaignParams.targetCondition")

      b-col(md="6").mb-3
        player-list-card(:playerIds="campaignInfo.campaignParams.targetPlayers" title="Individual Players" emptyMessage="No individual players targeted.")

    b-row(no-gutters align-v="center").mt-3.mb-2
     h3 Admin

    b-row(align-h="center").mb-3
      b-col(md="6")
        audit-log-card(targetType="$Notification" :targetId="String(campaignInfo.id)")

    meta-raw-data(:kvPair="campaignInfo" name="campaign")/

    //- Delete modal
    b-modal#action-delete-campaign(title="Delete Campaign" size="md" centered no-close-on-backdrop)
      div Deleting this Campaign will prevent any more players from receiving it. However, it will not be removed from the devices of those who have already received it.
      meta-no-seatbelts.mt-4/
      template(#modal-footer="{ ok, cancel }")
        meta-button(variant="secondary" @click="cancel") Cancel
        meta-button(@click="deleteCampaign" variant="primary" safety-lock) Delete Campaign
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { BProgress } from 'bootstrap-vue'
import { DateTime } from 'luxon'

import { useGameServerApi } from '@metaplay/game-server-api'
import { MBadge, type Variant } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSingleNotificationSubscriptionOptions } from '../subscription_options/notifications'
import { getAllScanJobsSubscriptionOptions } from '../subscription_options/scanJobs'

import { routeParamToSingleValue, notificationPhaseDisplayString } from '../coreUtils'
import { useRoute, useRouter } from 'vue-router'

import { abbreviateNumber, showSuccessToast } from '@metaplay/meta-ui'
import type { MetaPageContainerAlert } from '@metaplay/meta-ui'

import AuditLogCard from '../components/auditlogs/AuditLogCard.vue'
import PlayerListCard from '../components/global/PlayerListCard.vue'
import TargetingCard from '../components/mails/TargetingCard.vue'
import NotificationFormButton from '../components/notifications/NotificationFormButton.vue'
import MetaAudienceSizeEstimate from '../components/MetaAudienceSizeEstimate.vue'
import MetaGeneratedSection from '../components/generatedui/components/MetaGeneratedSection.vue'

const gameServerApi = useGameServerApi()
const route = useRoute()
const router = useRouter()

/**
 * Subscribe to notification campaign data.
 */
const {
  data: singleNotificationCampaignsData,
  refresh: singleNotificationCampaignsRefresh,
  error: singleNotificationCampaignsError
} = useSubscription(getSingleNotificationSubscriptionOptions(routeParamToSingleValue(route.params.id)))

/**
 * Subscribe to database scan job data.
 */
const {
  data: databaseScanJobsData,
} = useSubscription(getAllScanJobsSubscriptionOptions())

/**
 * Notification campaign details that are to be displayed in this component.
 */
const campaignInfo = computed(() => {
  return singleNotificationCampaignsData.value?.campaignInfo
})

/**
 * Color variant to be used in the progress bar.
 */
const phaseVariant = computed((): Variant => {
  if (campaignInfo.value) {
    if (campaignInfo.value?.campaignPhase === 'Running') { return 'success' }
    if (campaignInfo.value?.campaignPhase === 'Cancelling') { return 'warning' }
    if (campaignInfo.value?.campaignPhase === 'Scheduled') { return 'primary' }
  }
  // Cancelled, DidNotRun, and sent.
  return 'neutral'
})

/**
 * Amount of time taken to run the notification campaign.
 */
const jobDuration = computed(() => {
  const start = DateTime.fromISO(campaignInfo.value?.stats.startTime)
  const stop = campaignInfo.value?.stats.stopTime ? DateTime.fromISO(campaignInfo.value?.stats.stopTime) : DateTime.now()
  return stop.diff(start).toISO()
})

/**
 * When true indicates that the notification campaign is targeted to a specific audience.
 */
function isTargeted (): boolean {
  return campaignInfo.value?.campaignParams?.targetPlayers?.length || campaignInfo.value?.campaignParams?.targetCondition
}

/**
 * Notification campaign details to be edited or duplicated.
 */
const campaignFormInfo = computed(() => {
  if (campaignInfo.value) {
    return {
      id: campaignInfo.value?.id,
      name: campaignInfo.value?.campaignParams.name,
      targetTime: campaignInfo.value?.campaignParams.targetTime,
      content: campaignInfo.value?.campaignParams.content,
      debugEntityIdValueUpperBound: campaignInfo.value?.campaignParams.debugEntityIdValueUpperBound,
      debugFakeNotificationMode: campaignInfo.value?.campaignParams.debugFakeNotificationMode,
      firebaseAnalyticsLabel: campaignInfo.value?.campaignParams.firebaseAnalyticsLabel,
      targetPlayers: campaignInfo.value?.campaignParams.targetPlayers,
      targetCondition: campaignInfo.value?.campaignParams.targetCondition
    }
  } else {
    return undefined
  }
})

/**
 * Cannot delete a campaign when it is in certain phases.
 */
function canDeleteCampaign () {
  const allowablePhases = [
    'Scheduled',
    // Running,
    'Sent',
    'Cancelled',
    // Cancelling,
    'DidNotRun'
  ]
  return allowablePhases.includes(campaignInfo.value.campaignPhase)
}

/**
 * Delete the displayed notification campaign and navigates back to the notification list view.
 */
async function deleteCampaign (): Promise<void> {
  await gameServerApi.delete(`/notifications/${route.params.id}`)
  const message = 'Notification campaign deleted.'
  showSuccessToast(message)
  await router.push('/notifications')
}

/**
 * Cancel a running notification campaign.
 */
async function cancelCampaign (): Promise<void> {
  await gameServerApi.put(`/notifications/${route.params.id}/cancel`)
  showSuccessToast('Notification campaign cancellation started.')
  singleNotificationCampaignsRefresh()
}

/**
 * Alert messages to be displayed at the top of the page.
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
  if (campaignInfo.value?.campaignPhase === 'DidNotRun') {
    allAlerts.push({
      title: 'Campaign did not run',
      message: `${campaignInfo.value.campaignParams?.name} did not run because push notifications
      are disabled for this deployment. Contact your game team to set them up.`,
      variant: 'warning',
    })
  }

  return allAlerts
})

</script>
