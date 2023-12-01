<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!allScanJobsData"
  :meta-api-error="allScanJobsError"
  :alerts="alerts"
)
  template(#overview)
    meta-page-header-card(data-cy="scan-jobs-overview")
      template(#title) Database Scan Jobs

      p Scan jobs are slow-running workers that crawl the database and perform operations on its data.
      div.small.text-muted You can create your own scan jobs and tune the performance of the job runners to perform complicated operations on live games for hundreds of millions of players.

      template(#buttons)
        meta-action-modal-button(
          class="mr-2"
          id="pause-all-jobs"
          variant="warning"
          :action-button-text="!allScanJobsData?.globalPauseIsEnabled ? 'Pause Jobs' : 'Resume Jobs'"
          permission="api.scan_jobs.manage"
          modal-title="Pause All Database Scan Jobs"
          @show="willPauseAllJobs = allScanJobsData?.globalPauseIsEnabled"
          ok-button-text="Apply"
          :ok-button-disabled="willPauseAllJobs === allScanJobsData?.globalPauseIsEnabled"
          :on-ok="onPauseAllJobsOk"
          )
            p You can pause the execution of all database scan jobs. This can be helpful to debug the performance of slow-running jobs.
            div.d-flex.justify-content-between.mb-2
              span.font-weight-bold Scan Jobs Paused
              meta-input-checkbox(v-model="willPauseAllJobs" data-cy="pause-jobs-toggle" name="Pause all jobs" size="lg" showAs="switch")

        meta-action-modal-button(
          id="new-scan-job"
          action-button-text="Create Job"
          permission="api.scan_jobs.manage"
          modal-title="Create a New Maintenance Scan Job"
          @show="selectedJobKind = null"
          ok-button-text="Create Job"
          :ok-button-disabled="!selectedJobKind"
          :on-ok="onNewScanJobOk"
          )
            p Database maintenance jobs handle routine operations such as deleting players. They are safe to use in production, but it is still a good idea to try them once is staging to verify that the various jobs do what you expect them to to!

            div.mb-1.font-weight-bold Scan Job Type
            b-form-select(data-cy="job-kind-select" v-model="selectedJobKind" :options="jobKindOptions" :state="selectedJobKind != null")

            div.mt-2.small.text-muted(v-if="selectedJobKind") {{ selectedJobKind.spec.jobDescription }}

  template(#default)
    core-ui-placement(:placement-id="'ScanJobs/List'")

    meta-raw-data(:kvPair="allScanJobsData" name="scanJobs")
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import type { MetaPageContainerAlert } from '@metaplay/meta-ui'

import { useSubscription } from '@metaplay/subscriptions'
import { getAllMaintenanceJobTypesSubscriptionOptions, getAllScanJobsSubscriptionOptions } from '../subscription_options/scanJobs'

import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'

const gameServerApi = useGameServerApi()
const { data: allMainentenanceJobTypesData } = useSubscription(getAllMaintenanceJobTypesSubscriptionOptions())
const {
  data: allScanJobsData,
  error: allScanJobsError,
  refresh: allScanJobsRefresh,
} = useSubscription(getAllScanJobsSubscriptionOptions())

const jobKindOptions = computed(() => {
  const options: Array<{
    value: string | null
    text: string
  }> = []
  if (allMainentenanceJobTypesData.value?.supportedJobKinds) {
    options.push({
      value: null,
      text: 'Select Type'
    })
    for (const jobKindInfo of Object.values(allMainentenanceJobTypesData.value.supportedJobKinds) as any) {
      options.push({
        value: jobKindInfo,
        text: jobKindInfo.spec.jobTitle
      })
    }
  }
  return options
})

const selectedJobKind = ref<any>()
async function onNewScanJobOk (): Promise<void> {
  const job = (await gameServerApi.post('/maintenanceJobs', { jobKindId: selectedJobKind.value.id })).data
  showSuccessToast(`New job '${job.spec.jobTitle}' enqueued.`)
  allScanJobsRefresh()
}

const willPauseAllJobs = ref(false)
async function onPauseAllJobsOk (): Promise<void> {
  await gameServerApi.post('databaseScanJobs/setGlobalPause', { isPaused: willPauseAllJobs.value })
  showSuccessToast(`All scan jobs ${willPauseAllJobs.value ? 'paused' : 'resumed'}.`)
  allScanJobsRefresh()
}

const alerts = computed(() => {
  const allAlerts: MetaPageContainerAlert[] = []
  if (allScanJobsData.value?.globalPauseIsEnabled === true) {
    allAlerts.push({
      title: 'All Scan Jobs Paused',
      message: 'All database scan jobs are currently paused. You can resume them by clicking the Resume Jobs button.',
      variant: 'warning',
      dataCy: 'all-jobs-paused-alert',
    })
  }
  return allAlerts
})
</script>
