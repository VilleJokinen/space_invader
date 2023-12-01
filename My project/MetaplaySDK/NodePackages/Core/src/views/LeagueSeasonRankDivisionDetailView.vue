<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!singleDivisionData || !singleLeagueData"
  :alerts="alerts"
  )
  template(#overview)
    meta-page-header-card(
      data-cy="league-season-rank-division-overview"
      :title="`Division ${singleDivisionData.model.divisionIndex.division} `"
      :id="divisionId"
      )
      template(#subtitle)
        span #[router-link(:to="`/leagues/${leagueId}`") {{ singleLeagueData.details.leagueDisplayName }}] / #[router-link(:to="`/leagues/${leagueId}/${seasonId}`") {{ singleLeagueSeasonData.displayName }}] / Division {{singleDivisionData.model.divisionIndex.division}} of {{ singleLeagueSeasonData.ranks[rankId].rankName }}

      //- Standard view
      div.d-md-flex.justify-content-around.mt-5.mb-5.d-none(style="font-size: 130%")
        MBadge(:variant="currentPhase === 'Preview' ? 'primary' : 'neutral'").mx-md-2 Preview
        fa-icon(icon="arrow-right").mt-2
        MBadge(:variant="currentPhase === 'Ongoing' ? 'primary' : 'neutral'").mx-md-2 Ongoing
        fa-icon(icon="arrow-right").mt-2
        MBadge(:variant="currentPhase === 'Resolving' ? 'primary' : 'neutral'").mx-md-2 Resolving
        fa-icon(icon="arrow-right").mt-2
        MBadge(:variant="currentPhase === 'Concluded' ? 'primary' : 'neutral'").mx-md-2 Concluded

      //- Mobile view
      div.text-center(style="font-size: 130%").mt-5.mb-5.d-md-none
        MBadge(:variant="currentPhase === 'Preview' ? 'primary' : 'neutral'").mx-md-2 Preview
        div: fa-icon(icon="arrow-down")
        MBadge(:variant="currentPhase === 'Ongoing' ? 'primary' : 'neutral'").mx-md-2 Ongoing
        div: fa-icon(icon="arrow-down")
        MBadge(:variant="currentPhase === 'Resolving' ? 'primary' : 'neutral'").mx-md-2 Resolving
        div: fa-icon(icon="arrow-down")
        MBadge(:variant="currentPhase === 'Concluded' ? 'primary' : 'neutral'").mx-md-2 Concluded

      div.font-weight-bold.mb-1 #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small)
        b-tbody
          b-tr
            b-td Total participants
            b-td.text-right {{ currentParticipantCount }} / {{ singleDivisionData.maxParticipants }}
          b-tr
            b-td Created at
            b-td.text-right #[meta-time(:date="singleDivisionData.model.createdAt" showAs="datetime")]
          b-tr
            b-td Start time
            b-td.text-right #[meta-time(:date="singleDivisionData.model.startsAt" showAs="timeagoSentenceCase")]
          b-tr
            b-td End time
            b-td.text-right #[meta-time(:date="singleDivisionData.model.endsAt" showAs="timeagoSentenceCase")]

      template(#buttons)
        meta-action-modal-button(
          id="action-add-participant"
          action-button-text="Add Participant"
          ok-button-text="Add Participant"
          variant="warning"
          modal-title="Add Participant"
          :on-ok="forceAddParticipant"
          @show="resetParticipantModal"
          permission="api.leagues.participant_edit"
          :action-button-disabled="singleDivisionData.model.isConcluded"
          :ok-button-disabled="!chosenPlayer || singleDivisionData.model.isConcluded"
          :action-button-tooltip="singleDivisionData.model.isConcluded ? 'You can only add a participant to a division in an active season.': undefined"
          )
          p You can manually add new participants to this division.
          b-alert(:show="currentParticipantCount >= singleDivisionData.desiredParticipants" variant="danger").pb-0
            p(v-if="currentParticipantCount > singleDivisionData.maxParticipants") This division already has #[span.font-weight-bold {{ currentParticipantCount }} participants], which exceeds the maximum limit of #[span.font-weight-bold {{ singleDivisionData.maxParticipants }} participants]. Check with your game team if adding another participant might cause issues.
            p(v-else-if="currentParticipantCount === singleDivisionData.maxParticipants") You are about to exceed the maximum number of #[span.font-weight-bold {{ singleDivisionData.maxParticipants }} participants] for this division. Check with your game team if this might cause issues.
            p(v-else-if="currentParticipantCount > singleDivisionData.desiredParticipants") This division already has #[span.font-weight-bold {{ currentParticipantCount }} participants], which exceeds the recommended limit of #[span.font-weight-bold {{ singleDivisionData.desiredParticipants }} participants]. Check with your game team if adding another participant might cause issues.
            p(v-else-if="currentParticipantCount === singleDivisionData.desiredParticipants") You are about to exceed the recommended number of #[span.font-weight-bold {{ singleDivisionData.desiredParticipants }} participants] for this division. Check with your game team if this might cause issues.

          span.font-weight-bold Select Player
          meta-input-player-select.mt-1(
            :value="chosenPlayer"
            @input="chosenPlayer = $event"
            :ignorePlayerIds="Object.keys(singleDivisionData.model.participants)"
            )
          b-alert.mt-4.text-center(
            v-if="singleDivisionData.model.isConcluded"
            show variant="danger"
            ) The season has now concluded. You can no longer add participants to this division.
          meta-no-seatbelts.mt-4(
            v-else
            message="If the player is participating in a another division, their progress will be reset."
            :name="chosenPlayer?.name"
            )

    //- Navigation buttons
    div.mt-3.d-flex.justify-content-between
      meta-button.ml-1(
        link
        :disabled="!previousDivisionLink"
        :tooltip="previousDivisionTooltip"
        variant="primary"
        size="md"
        :to="previousDivisionLink")
        fa-icon(icon="arrow-left")
        |  View previous division

      meta-button.mr-1(
        link variant="primary"
        :disabled="!nextDivisionLink"
        :tooltip="nextDivisionTooltip"
        size="md"
        :to="nextDivisionLink")
        | View next division #[fa-icon(icon="arrow-right")]
  template(#center-column)
    core-ui-placement(placementId="Leagues/Season/RankDivision/Details" :leagueId="leagueId" :divisionId="divisionId")

    meta-raw-data(:kvPair="singleLeagueSeasonData" name="seasonData")
    meta-raw-data(:kvPair="singleDivisionData" name="divisionData")
</template>

<script lang="ts" setup>
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

import { useGameServerApi } from '@metaplay/game-server-api'
import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import type { MetaPageContainerAlert } from '@metaplay/meta-ui'
import { getSingleLeagueSubscriptionOptions, getSingleDivisionSubscriptionOptions, getSingleLeagueSeasonSubscriptionOptions } from '../subscription_options/leagues'

import { showErrorToast, showSuccessToast } from '@metaplay/meta-ui/src/toasts'

import { routeParamToSingleValue } from '../coreUtils'

import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'

const gameServerApi = useGameServerApi()
const route = useRoute()

/**
 * Entity Id of the division that we are currently viewing.
 */
const divisionId = routeParamToSingleValue(route.params.divisionId)

/**
 * Entity Id of the league that we are currently viewing.
 */
const leagueId = routeParamToSingleValue(route.params.leagueId)

/**
 * Season Id of the division that we are currently viewing.
 */
const seasonId = parseInt(routeParamToSingleValue(route.params.seasonId))

/**
 * Subscribe to League data so that we can get league name for subtitle breadcrumb.
 * TODO: pass league name down to season or division in the future.
 */
const {
  data: singleLeagueData,
} = useSubscription(getSingleLeagueSubscriptionOptions(leagueId))

/**
 * Subscribe to the displayed division's data.
 */
const {
  data: singleDivisionData,
} = useSubscription(getSingleDivisionSubscriptionOptions(divisionId))

/**
 * Subscribe to season data so that we can find information about the parent season.
 */
const {
  data: singleLeagueSeasonData
} = useSubscription(getSingleLeagueSeasonSubscriptionOptions(leagueId, seasonId))

/**
 * Rank Id for the division that we are currently viewing.
 */
const rankId = computed(() => {
  return singleDivisionData.value?.model.divisionIndex.rank
})

const currentParticipantCount = computed(() => {
  return Object.keys(singleDivisionData.value.model.participants).length
})

const alerts = computed((): MetaPageContainerAlert[] => {
  if (singleDivisionData.value?.model.isConcluded === true) return [{ title: 'Past Division', message: 'You are currently viewing a division from a past season.', variant: 'secondary' }]
  return []
})

// Navigating to Previous and Next division ---------------------------------------------------------------------------

/**
 * The number of divisions available in this season/rank.
 */
const numDivisions = computed((): number => {
  const rankId = singleDivisionData.value.model.divisionIndex.rank
  return singleLeagueSeasonData.value?.ranks[rankId].numDivisions || 0
})

/**
 * This will be populated to contain the link to the previous division in this rank.
 */
const previousDivisionLink = ref<string>()

/**
 * This will be populated to contain the link to the next division in this rank.
 */
const nextDivisionLink = ref<string>()

const previousDivisionTooltip = ref<string>()

const nextDivisionTooltip = ref<string>()

/**
 * When the division data is loaded, we can look up what the next/previous division links should point to.
 */
const unwatch = watch([singleDivisionData], async () => {
  const rank = singleDivisionData.value.model.divisionIndex.rank
  const division = singleDivisionData.value.model.divisionIndex.division

  // If we're not on division 0, then look up a link to the previous division.
  if (division > 0) {
    const response = await gameServerApi.get(`/divisions/id/${leagueId}/${seasonId}/${rank}/${division - 1}/`)
    previousDivisionLink.value = `/leagues/${leagueId}/${seasonId}/${response.data}`
    previousDivisionTooltip.value = undefined
  } else {
    previousDivisionTooltip.value = 'You are in the first division.'
  }

  // If we're not on the last division, then look up a link to the next division.
  if (division < numDivisions.value - 1) {
    const response = await gameServerApi.get(`/divisions/id/${leagueId}/${seasonId}/${rank}/${division + 1}/`)
    nextDivisionLink.value = `/leagues/${leagueId}/${seasonId}/${response.data}`
    nextDivisionTooltip.value = undefined
  } else {
    nextDivisionTooltip.value = 'You are in the last division.'
  }

  // We only want to do this once, so unwatch now.
  unwatch()
})

// Force division phase ----------------------------------------------------------------------------------------------

/**
 * Display name for the current division phase.
 * Either 'Preview' | 'Ongoing' | 'Resolving' | 'Concluded' | 'NoDivision'
 * TODO: move to API response
 */
const currentPhase = computed(() => (singleDivisionData.value?.seasonPhase as string) || 'NoDivision')

// Add Particiapnt to Division ----------------------------------------------------------------------------------------

/**
 * The selected player.
 */
const chosenPlayer = ref()

/**
 * Reset participant modal.
 */
function resetParticipantModal () {
  chosenPlayer.value = undefined
}

/**
 * Add a participant to a division.
 */
async function forceAddParticipant () {
  if (chosenPlayer.value) {
    const response = await gameServerApi.post(`/leagues/${leagueId}/participant/${chosenPlayer.value?.id}/add/${divisionId}`)
    if (response.data.success) {
      showSuccessToast('Participant successfully added!')
    } else {
      showErrorToast(response.data.errorMessage)
    }
  }
}

</script>
