<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(data-cy="system-maintenance-mode-card" v-if="backendStatusData")
  b-card.mb-2
    b-card-title
      b-row(align-v="center" no-gutters)
        fa-icon(:icon="['far', 'window-close']").mr-2
        span Maintenance Mode
        MBadge.ml-2(variant="success" v-if="backendStatusData.maintenanceStatus.isInMaintenance") On
        MBadge.ml-2(variant="primary" v-else-if="backendStatusData.maintenanceStatus.scheduledMaintenanceMode") Scheduled
        MBadge.ml-2(v-else) Off

    div(v-if="backendStatusData.maintenanceStatus.isInMaintenance").mb-3
      div Maintenance mode started #[meta-time(:date="backendStatusData.maintenanceStatus.scheduledMaintenanceMode.startAt")].
      div(v-if="backendStatusData.maintenanceStatus.scheduledMaintenanceMode.estimationIsValid") Estimated duration: {{ backendStatusData.maintenanceStatus.scheduledMaintenanceMode.estimatedDurationInMinutes }} minutes
      div(v-else) Duration: #[MBadge None]
      div Affected platforms: #[MBadge(v-for="plat in maintenancePlatformsOnServer" :key="plat").mr-1 {{ plat }} ]

    div(v-else-if="backendStatusData.maintenanceStatus.scheduledMaintenanceMode")
      p
        | Maintenance mode has been scheduled to start #[meta-time(:date="backendStatusData.maintenanceStatus.scheduledMaintenanceMode.startAt")] on #[meta-time(:date="backendStatusData.maintenanceStatus.scheduledMaintenanceMode.startAt" showAs="datetime")]
      p.m-0(v-if="backendStatusData.maintenanceStatus.scheduledMaintenanceMode.estimationIsValid") Estimated duration: {{ backendStatusData.maintenanceStatus.scheduledMaintenanceMode.estimatedDurationInMinutes }} minutes
      p.m-0(v-else) Duration: #[MBadge Off]
      p Affected platforms: #[MBadge(v-for="plat in maintenancePlatformsOnServer" :key="plat").mr-1 {{ plat }} ]

    div(v-else)
      p Maintenance mode will prevent players from logging into the game. Use it to make backend downtime more graceful for players.

    div.text-right
      meta-action-modal-button(
        id="maintenance-mode"
        permission="api.system.edit_maintenance"
        action-button-text="Edit Settings"
        modal-title="Update Maintenance Mode Settings"
        @show="resetModal"
        :ok-button-text="okButtonDetails.text"
        :ok-button-icon="okButtonDetails.icon"
        :ok-button-disabled="!isFormValid"
        :on-ok="setMaintenance"
        )
        b-form
          div.d-flex.justify-content-between.mb-2
            div.font-weight-bold Maintenance Mode Enabled
            meta-input-checkbox(v-model="maintenanceEnabled" name="Maintenance mode enabled" size="lg" showAs="switch")

          MInputDateTime(
            :model-value="maintenanceDateTime"
            @update:model-value="onMaintenanceDateTimeChange"
            :disabled="!maintenanceEnabled"
            min-date-time="now"
            label="Start Time"
            )

          p.mt-1.text-muted(v-if="!isMaintenanceDateTimeInFuture && maintenanceEnabled") Maintenance mode will start immediately.
          p.mt-1.text-muted(v-else-if="maintenanceEnabled") Maintenance mode will start #[meta-time(:date="maintenanceDateTime")].
          p.mt-1.text-muted(v-else) Maintenance mode off.

          b-form-group.mb-2
            label(for="platforms").font-weight-bold Platforms
            b-form-checkbox-group(name="platforms" v-model="maintenancePlatforms" :options="platforms" :disabled="!maintenanceEnabled")

          b-form-group.mb-2
            label(for="duration") #[span.font-weight-bold Estimated duration] (in minutes)
            b-form-input(v-model="maintenanceDuration" placeholder="Leave blank to skip" :disabled="!maintenanceEnabled" :state="maintenanceEnabled ? isDurationValid : null" required type="number" @blur="maintenanceDuration = parseInt(maintenanceDuration) > 0 ? parseInt(maintenanceDuration) : ''")
            span.small.text-muted This is just a number you can display on the client to the players. Maintenance mode will not turn off automatically based on duration.
//- TODO: This is a placeholder for until we migrate to tailwind with unified styling and extract this logic to own component.
div(v-else)
  b-card.mb-2
    b-card-title
      b-row(align-v="center" no-gutters)
        fa-icon(:icon="['far', 'window-close']").mr-2
        span Maintenance Mode
    p.text-danger Failed to load the current maintenance mode status.
    MErrorCallout(:error="backendStatusError")
</template>

<script lang="ts" setup>
import { DateTime } from 'luxon'
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { MBadge, MErrorCallout, MInputDateTime } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getBackendStatusSubscriptionOptions } from '../../subscription_options/general'

const props = withDefaults(defineProps<{
  platforms?: string[]
}>(), {
  platforms: () => ['iOS', 'Android', 'WebGL', 'UnityEditor'],
})

const gameServerApi = useGameServerApi()
const {
  data: backendStatusData,
  refresh: backendStatusTriggerRefresh,
  error: backendStatusError
} = useSubscription(getBackendStatusSubscriptionOptions())

const maintenanceEnabled = ref<boolean>()
const maintenanceDateTime = ref<DateTime>(DateTime.now())
const maintenanceDuration = ref<any>(null)
const maintenancePlatforms = ref<any>(null)

const maintenancePlatformsOnServer = computed(() => {
  return props.platforms.filter(platform => !backendStatusData.value.maintenanceStatus.scheduledMaintenanceMode?.platformExclusions?.includes(platform))
})

const isDurationValid = computed(() => {
  if (maintenanceDuration.value === '' || maintenanceDuration.value > 0) {
    return true
  } else {
    return false
  }
})

const isFormValid = computed(() => {
  if (!maintenanceEnabled.value && !(backendStatusData.value.maintenanceStatus.isInMaintenance || backendStatusData.value.maintenanceStatus.scheduledMaintenanceMode)) {
    return false
  }
  if (!maintenanceEnabled.value) {
    return true
  }
  if (!isDurationValid.value) {
    return false
  }
  if (maintenancePlatforms.value.length === 0) {
    return false
  }
  return true
})

function onMaintenanceDateTimeChange (value?: DateTime) {
  if (!value) return
  maintenanceDateTime.value = value
}

const isMaintenanceDateTimeInFuture = computed(() => {
  return maintenanceDateTime.value.diff(DateTime.now()).toMillis() >= 0
})

async function setMaintenance () {
  if (maintenanceEnabled.value) {
    const payload = {
      StartAt: maintenanceDateTime.value.toISO(),
      EstimatedDurationInMinutes: maintenanceDuration.value ? maintenanceDuration.value : 0,
      EstimationIsValid: !!maintenanceDuration.value,
      PlatformExclusions: props.platforms.filter(platform => !maintenancePlatforms.value.includes(platform))
    }
    await gameServerApi.put('maintenanceMode', payload)

    if (isMaintenanceDateTimeInFuture.value) {
      const message = 'Maintenance mode enabled.'
      showSuccessToast(message)
    } else {
      const message = 'Maintenance mode scheduled.'
      showSuccessToast(message)
    }
  } else {
    await gameServerApi.delete('maintenanceMode')
    const message = 'Maintenance mode disabled.'
    showSuccessToast(message)
  }
  backendStatusTriggerRefresh()
}

const okButtonDetails = computed(() => {
  if (maintenanceEnabled.value) {
    if (isMaintenanceDateTimeInFuture.value) {
      return {
        text: 'Schedule',
        icon: 'calendar-alt',
      }
    } else {
      return {
        text: 'Set Immediately',
        icon: ['far', 'window-close'],
      }
    }
  } else {
    return {
      text: 'Save Settings',
    }
  }
})

function resetModal () {
  maintenanceDuration.value = backendStatusData.value.maintenanceStatus.scheduledMaintenanceMode?.estimationIsValid ? backendStatusData.value.maintenanceStatus.scheduledMaintenanceMode.estimatedDurationInMinutes : ''
  maintenanceDateTime.value = backendStatusData.value.maintenanceStatus.scheduledMaintenanceMode ? DateTime.fromISO(backendStatusData.value.maintenanceStatus.scheduledMaintenanceMode.startAt) : DateTime.now().plus({ minutes: 60 })
  maintenanceEnabled.value = !!((backendStatusData.value.maintenanceStatus.isInMaintenance || backendStatusData.value.maintenanceStatus.scheduledMaintenanceMode))
  maintenancePlatforms.value = props.platforms.filter(platform => !backendStatusData.value.maintenanceStatus.scheduledMaintenanceMode?.platformExclusions?.includes(platform))
}
</script>
