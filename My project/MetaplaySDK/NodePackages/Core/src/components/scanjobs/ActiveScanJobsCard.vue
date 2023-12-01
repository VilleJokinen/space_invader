<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  data-cy="active-scan-jobs-card"
  icon="business-time"
  title="Active Scan Jobs"
  :itemList="activeAndEnqueuedJobs"
  :getItemKey="getItemKey"
  emptyMessage="No active scan jobs."
  )
  template(#item-card="slot")
    MCollapse(extraMListItemMargin data-cy="scan-jobs-entry")
      template(#header)
        MListItem
          | {{ slot.item.jobTitle }}

          template(#top-right)
            meta-action-modal-button(
              :actionButtonDisabled="!slot.item.canCancel"
              :id="`cancel-job-${slot.item.id}`"
              variant="danger"
              actionButtonSmall
              permission="api.scan_jobs.manage"
              subtle
              action-button-icon="trash-alt"
              :action-button-tooltip="slot.item.canCancel ? 'Cancel this scan job.' : slot.item.cannotCancelReason"
              ok-button-text="Cancel Job"
              modal-title="Cancel Scan Job"
              @show="jobToCancel = slot.item"
              :on-ok="cancelScanJob"
              class="tw-mr-1"
              )
              p(v-if="slot.item.phaseCategory === 'Upcoming'") This job has not yet been started. Cancelling an upcoming job will delete it, and it will not appear in the job history list.
              p(v-else) This job has been started and may already have executed partially. Cancelling an active job will terminate it as soon as possible and move it into the job history list.
            MTooltip(v-if="slot.item.anyWorkersFailed" content="Some workers failed during this job." noUnderline).mr-2.text-danger: fa-icon(icon="triangle-exclamation")
            MBadge(:variant="getJobVariant(slot.item)") {{ slot.item.phase }}
          template(#bottom-left)
            div(v-if="slot.item.phaseCategory === 'Upcoming'")
              div {{ slot.item.jobDescription }}
              div(v-if="slot.item.startTime") Scheduled to start #[meta-time(:date="slot.item.startTime")].
            div(v-else) Started #[meta-time(:date="slot.item.startTime")].
          template(v-if="slot.item.phaseCategory === 'Active'" #bottom-right)
            div.text-right Items scanned: #[meta-abbreviate-number(:value="slot.item.scanStatistics.numItemsScanned")]
            div.text-right Progress: {{ Math.round(slot.item.scanStatistics.scannedRatioEstimate * 10000) / 100 }}%

      //- TODO: migrate this to use generated UI
      pre.code-box.border.rounded.bg-light.mt-2 {{ slot.item }}
      //- meta-generated-content(
        :value="slot.item"
        )
</template>

<script lang="ts" setup>
import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { computed, ref } from 'vue'

import { MCollapse, MTooltip, MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getAllScanJobsSubscriptionOptions } from '../../subscription_options/scanJobs'

const {
  data: allScanJobsData,
  refresh: allScanJobsRefresh,
} = useSubscription(getAllScanJobsSubscriptionOptions())

const gameServerApi = useGameServerApi()

const activeAndEnqueuedJobs = computed(() => {
  const active = allScanJobsData.value.activeJobs
  const upcoming = allScanJobsData.value.upcomingJobs
  return active.concat(upcoming)
})

function getJobVariant (job: any) {
  if (job.phase === 'Paused') return 'danger'
  else if (job.phase === 'Running') return 'success'
  else if (job.phase === 'Enqueued') return 'warning'
  else if (job.phase === 'Scheduled') return 'primary'
  else return 'neutral'
}

const jobToCancel = ref<any>()
async function cancelScanJob () {
  const response = (await gameServerApi.put(`/databaseScanJobs/${jobToCancel.value.id}/cancel`)).data
  showSuccessToast(response.message)

  allScanJobsRefresh()
}

function getItemKey (job: any) {
  return job.id
}
</script>

<style scoped>
.progress-text-overlay {
  filter: drop-shadow(0px 1px 0px rgba(255, 255, 255, 0.2));
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  font-size: 0.65rem;
}
</style>
