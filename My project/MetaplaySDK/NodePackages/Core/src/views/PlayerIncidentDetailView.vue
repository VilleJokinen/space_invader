<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!playerIncidentData"
  :meta-api-error="playerIncidentError"
  :alerts="alerts"
  :variant="isCloseToDeletionDate ? 'warning' : undefined"
  )
  template(#error-card-message)
    p Oh no, something went wrong while trying to access the incident!

  template(#overview)
    meta-alert(
        v-if="isCloseToDeletionDate"
        title="This incident will be deleted soon!"
      )
      div This incident report will be deleted
        |  #[span.font-weight-bold #[meta-time(:date="playerIncidentData.deletionDateTime" showAs="timeago")]]
        |  at #[meta-time(:date="playerIncidentData.deletionDateTime" showAs="time" disableTooltip)]
        |  on #[meta-time(:date="playerIncidentData.deletionDateTime" showAs="date" disableTooltip)].
      div.mt-1 Incidents are deleted every now and then, we recommend saving the data elsewhere if it is important.
    meta-page-header-card(:id="playerIncidentData.incidentId")
      template(#title) Player Incident Report
      template(#subtitle)
        span.text-danger  {{ playerIncidentData.exceptionMessage }}
        b-alert(show v-if="playerIncidentData.subType === 'NullReferenceException'").mt-3
          div.font-weight-bolder Did you know?
          div Null reference exceptions are a very common category of errors in Unity that do not cause the game crash outright, but often cause the game to appear stuck or otherwise 'broken' for the players.

      span.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
      b-table-simple.mt-1(small responsive)
        b-tbody
          b-tr
            b-td Type
            b-td.text-right {{ playerIncidentData.type }}
          b-tr
            b-td Subtype
            b-td.text-right {{ playerIncidentData.subType }}
          b-tr
            b-td Uploaded At
            b-td.text-right #[meta-time(:date="playerIncidentData.uploadedAt" showAs="datetime")]
          b-tr
            b-td Occurred At (device time)
            b-td.text-right #[meta-time(:date="playerIncidentData.occurredAt" showAs="datetime")]
          b-tr
            b-td Deletion Time
            b-td.text-right #[meta-time(:date="playerIncidentData.deletionDateTime" showAs="datetime")]
          b-tr
            b-td Player ID
            b-td.text-right: router-link(:to="`/players/${route.params.playerId}`") {{ route.params.playerId }}

  template(#default)
    core-ui-placement(placementId="PlayerIncidents/Details" :incidentId="incidentId" :playerId="playerId")

    meta-raw-data(:kvPair="playerIncidentData" name="incident")
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import type { MetaPageContainerAlert } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'

import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import { getPlayerIncidentSubscriptionOptions } from '../subscription_options/incidents'
import { routeParamToSingleValue } from '../coreUtils'
import { DateTime } from 'luxon'

const route = useRoute()
const playerId = routeParamToSingleValue(route.params.playerId)
const incidentId = routeParamToSingleValue(route.params.incidentId)

const {
  data: playerIncidentData,
  error: playerIncidentError,
} = useSubscription(getPlayerIncidentSubscriptionOptions(playerId, incidentId))

const alerts = computed(() => {
  const allAlerts: MetaPageContainerAlert[] = []

  if (!playerId) {
    allAlerts.push({
      title: 'No player ID parameter detected!',
      message: 'Fetching incident reports requires a player ID.',
      variant: 'danger',
      dataCy: 'incident-no-playerId'
    })
  } else if (!incidentId) {
    allAlerts.push({
      title: 'No incident ID parameter detected!',
      message: 'Fetching incident reports requires an incident ID',
      variant: 'warning',
      dataCy: 'incident-no-incidentId'
    })
  }

  return allAlerts
})

const isCloseToDeletionDate = computed((): boolean => {
  return playerIncidentData.value && DateTime.fromISO(playerIncidentData.value.deletionDateTime).diffNow('days').days <= 3
})
</script>
