<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!singleMatchmakerData"
  :meta-api-error="singleMatchmakerError"
  )
  template(#error-card-message)
    p Oh no, something went wrong while trying to access the matchmaker!
  template(#overview)
    //- Overview
    meta-page-header-card(data-cy="matchmaker-overview-card" :id="matchmakerId")
      template(#title) {{ singleMatchmakerData.data.name }}
      template(#subtitle) {{ singleMatchmakerData.data.description }}
      template(#caption) Save file size: #[meta-button(link data-cy="model-size-link" permission="api.database.inspect_entity" :to="`/entities/${matchmakerId}/dbinfo`") #[meta-abbreviate-number(:value="singleMatchmakerData.data.stateSizeInBytes" unit="byte")]]

      div.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td Number of participants
            b-td.text-right {{ singleMatchmakerData.data.playersInBuckets }}
          b-tr
            b-td Current capacity
            b-td.text-right(v-if="singleMatchmakerData?.data?.bucketsOverallFillPercentage > 0") {{ Math.round(singleMatchmakerData.data.bucketsOverallFillPercentage * 10000) / 100 }}%
            b-td.text-right.font-italic.text-muted(v-else) None
          //- b-tr
            b-td Number of matches during the last hour
            b-td.text-right TBD
          b-tr
            b-td Last rebalanced
            b-td.text-right: meta-time(:date="singleMatchmakerData.data.lastRebalanceOperationTime")

      template(#buttons)
        //- Simulation modal
        meta-action-modal-button(
          id="simulate-matchmaking"
          permission="api.matchmakers.test"
          action-button-text="Simulate"
          modal-title="Simulate Matchmaking"
          modal-size="lg"
          only-close
          :onOk="async () => {}"
          @hide="simulationResult = undefined"
          )
            b-row
              b-col(sm).mb-5.border-right-md.mb-sm-3
                div
                  p You can use this tool to preview the matches this matchmaker would return for a given matchmaking ranking (MMR).
                div.mb-2
                  meta-generated-form(
                    :typeName="singleMatchmakerData.queryJsonType"
                    v-model="simulationData"
                    @status="isSimulationFormValid = $event"
                    addTypeSpecifier)
                  //- div.font-weight-bold.mb-1 MMR
                  //- meta-input-number(v-model="mmr" name="mmr" :min="1" :state="isMmrValid" placeholder="1337" required data-cy="simulate-matchmaking-mmr-input")
                div.text-right
                  b-button(variant="primary" :disabled="!isSimulationFormValid" @click="simulateMatchmaking" data-cy="simulate-matchmaking-ok-button") Simulate

              b-col(sm).mb-3
                h5 Matchmaking Results #[span.small.text-no-transform(v-if="simulationResult?.numTries") After #[meta-plural-label(:value="simulationResult.numTries" label="iteration")]]

                //- Simulation is running.
                div(v-if="isSimulationRunning").w-100.text-center.pt-3.text-muted.font-italic
                  span Simulating...

                //- Error.
                div(v-else-if="simulationResult?.response?.data?.error")
                  MErrorCallout(:error="simulationResult.response.data.error")

                //- Results.
                div(v-else-if="simulationResult?.response?.responseType === 'Success' && previewCandidateData")
                  MList(showBorder class="tw-px-3")
                    MListItem {{ previewCandidateData.model.playerName }}
                      template(#top-right) {{ previewCandidateData.id }}
                      template(#bottom-right): meta-button(link permission="api.players.view" :to="`/players/${simulationResult.response.bestCandidate}`") View player

                //- No results.
                div(v-else-if="simulationResult?.response?.responseType").pt-4
                  p No matches found!

                //- Haven't run the simulation at all yet.
                div(v-else).w-100.text-center.pt-3.text-muted.font-italic
                  span Simulation not run yet.

        //- Re-balance modal
        meta-action-modal-button.ml-1(
          id="rebalance-matchmaker"
          permission="api.matchmakers.admin"
          action-button-text="Rebalance"
          modal-title="Rebalance Matchmaker"
          ok-button-text="Rebalance"
          :ok-button-disabled="!singleMatchmakerData.data.hasEnoughDataForBucketRebalance"
          :onOk="rebalanceMatchmaker"
          )
              p Rebalancing this matchmaker will re-distribute participants to the configured matchmaking buckets.
              p.text-muted.small Matchmakers automatically rebalance themselves over time. Manually triggering the rebalancing is mostly useful for manual testing during development.

              b-alert(:show="!singleMatchmakerData.data.hasEnoughDataForBucketRebalance" variant="danger" data-cy="not-enough-data") This matchmaker does not have enough data to rebalance. Please wait until the matchmaker has been populated with enough data from players.

        //- Reset modal
        meta-action-modal-button.ml-1(
          id="reset-matchmaker"
          permission="api.matchmakers.admin"
          variant="warning"
          action-button-text="Reset"
          modal-title="Reset Matchmaker"
          ok-button-text="Reset"
          :onOk="resetMatchmaker"
          )
            p Resetting this matchmaker will immediately re-initialize it.
            p.text-muted.small Resetting is safe to do in a production environment, but might momentarily degrade the matchmaking experience for live players as it takes a few minutes for the matchmaker to re-populate.

  template(#default)
    core-ui-placement(placementId="Matchmakers/Details" :matchmakerId="matchmakerId")

    meta-raw-data(:kvPair="singleMatchmakerData" name="singleMatchmakerData")
</template>

<script lang="ts" setup>
import { Chart as ChartJS, Title, Tooltip, BarElement, CategoryScale, LogarithmicScale } from 'chart.js'
import { ref } from 'vue'

import { showSuccessToast } from '@metaplay/meta-ui'
import { MErrorCallout, MList, MListItem } from '@metaplay/meta-ui-next'
import { useGameServerApi } from '@metaplay/game-server-api'
import { fetchSubscriptionDataOnceOnly, useSubscription } from '@metaplay/subscriptions'

import MetaGeneratedForm from '../components/generatedui/components/MetaGeneratedForm.vue'
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import { getSingleMatchmakerSubscriptionOptions, getTopPlayersOfSingleMatchmakerSubscriptionOptions } from '../subscription_options/matchmaking'
import { getSinglePlayerSubscriptionOptions } from '../subscription_options/players'
import useHeaderbar from '../useHeaderbar'

ChartJS.register(Title, Tooltip, BarElement, CategoryScale, LogarithmicScale)

const props = defineProps<{
  /**
   * ID of the matchmaker to display.
   */
  matchmakerId: string
}>()

/**
 * Subscribe to the data, error and refresh of a single matchmaker based on its id.
 */
const {
  data: singleMatchmakerData,
  error: singleMatchmakerError,
  refresh: singleMatchmakerRefresh
} = useSubscription(getSingleMatchmakerSubscriptionOptions(props.matchmakerId))

// Update the headerbar title dynamically as data changes.
useHeaderbar().setDynamicTitle(singleMatchmakerData, (singleMatchmakerData) => `Manage ${(singleMatchmakerData.value)?.data.name || 'Matchmaker'}`)

// Top players.
const {
  refresh: topPlayersRefresh,
} = useSubscription(getTopPlayersOfSingleMatchmakerSubscriptionOptions(props.matchmakerId))

// Reset modal.
async function resetMatchmaker () {
  await useGameServerApi().post(`matchmakers/${props.matchmakerId}/reset`)
  singleMatchmakerRefresh()
  topPlayersRefresh()
}

// Rebalance modal.
async function rebalanceMatchmaker () {
  await useGameServerApi().post(`matchmakers/${props.matchmakerId}/rebalance`)
  showSuccessToast(`${props.matchmakerId} rebalanced successfully.`)
  singleMatchmakerRefresh()
}

// Simulation modal.
const simulationData = ref(null)
const isSimulationFormValid = ref(false)
const simulationResult = ref<any>(null)
const previewCandidateData = ref()
const isSimulationRunning = ref(false)

async function simulateMatchmaking () {
  try {
    isSimulationRunning.value = true

    // Fetch simulation results.
    const response = await useGameServerApi().post(`matchmakers/${props.matchmakerId}/test`, simulationData.value)
    simulationResult.value = response.data

    if (simulationResult.value.response?.responseType === 'Success') {
      // Fetch data for the best matching player.
      const previewCandidateId = simulationResult.value.response.bestCandidate
      previewCandidateData.value = await fetchSubscriptionDataOnceOnly(getSinglePlayerSubscriptionOptions(previewCandidateId))
    }

    isSimulationRunning.value = false
  } catch (e) {
    simulationResult.value = {
      response: {
        data: {
          error: e
        }
      }
    }
  }
}

</script>
