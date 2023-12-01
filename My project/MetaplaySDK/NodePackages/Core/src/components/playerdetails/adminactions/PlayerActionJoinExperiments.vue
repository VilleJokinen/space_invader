<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Button to display the modal
meta-action-modal-button(
  id="action-join-experiment"
  permission="api.players.edit_experiment_groups"
  modal-title="Join an Experiment"
  modal-size="md"
  :ok-button-text="okButtonText"
  action-button-text="Join Experiment"
  :ok-button-disabled="!okButtonEnabled"
  :on-ok="joinOrUpdateExperiment"
  @show="resetModal"
  variant="warning"
  block
)
  //- Form
  b-form
    meta-alert(
      v-if="!experimentsAvailable"
      title="No Experiments Available"
      )
      | There are no active experiments available in this environment. First, set them up in your game configs and then configure them from the #[b-link(to="/experiments") experiments page].

    div(v-else)
      div.mb-2 You can manually enroll #[MBadge {{ playerData.model.playerName }}] in an active experiment, or change their variant in an experiment they are already in.
      div.mb-3.small.text-muted Note: Changing experiments will force the player to reconnect!

      b-form-group.mb-3
        div.font-weight-bold.mb-1 Experiment
        b-form-select(
          v-model="experimentFormInfo.experimentId"
          :options="experimentOptions"
          @change="updateExperimentSelection"
          :state="experimentFormInfo.experimentId !== null ? true : null"
        ).mb-1
        span.small.text-muted {{ experimentMessage }}
      b-form-group.mb-3
        div.font-weight-bold.mb-1 Variant
        b-form-select(
          v-model="experimentFormInfo.variantId"
          :options="variantOptions"
          :state="experimentFormInfo.variantId !== null"
          :disabled="!experimentFormInfo.experimentId"
        ).mb-1
        span.small.text-muted {{ variantMessage }}
      div.d-flex.justify-content-between.mb-3
        span.font-weight-bold Tester
          div.small.text-muted As a tester, this player can try out the experiment before it is enabled for everyone. This is a great way to test variants before rolling them out!
        meta-input-checkbox(v-model="experimentFormInfo.isTester" name="IsTester" size="lg" showAs="switch" :disabled="!experimentFormInfo.experimentId")

      meta-no-seatbelts(v-if="showSeatbeltsWarning" message="Players can never leave experiments once enrolled, but you can always change their variant. Moving a player to the control group has the same effect as removing a player from an experiment.")
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getAllExperimentsSubscriptionOptions } from '../../../subscription_options/experiments'
import { getSinglePlayerSubscriptionOptions } from '../../../subscription_options/players'

const props = defineProps<{
  /**
   * Id of the player to target the change action at.
   */
  playerId: string
}>()

/** Access to the pre-configured HTTP client. */
const gameServerApi = useGameServerApi()

/** Subscribe to target player's data. */
const {
  data: playerData,
  refresh: playerRefresh
} = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

/** Subscribe to experiment data. */
const {
  data: experimentsData
} = useSubscription(getAllExperimentsSubscriptionOptions())

/**
 * Type definition for the information collected on this form.
 */
interface ExperimentFormInfo {
  experimentId: string | null
  variantId: string | null
  isTester: boolean
}

/**
 * Experiment details collected using this form.
 */
const experimentFormInfo = ref(getNewExperimentFormInfo())

/**
 * Data needed to initialize the form.
 */
function getNewExperimentFormInfo (): ExperimentFormInfo {
  return {
    experimentId: null,
    variantId: null,
    isTester: false,
  }
}

/**
 * Checks that the experiments data has active experiments for the target player to join.
 */
const experimentsAvailable = computed((): boolean => {
  return experimentsData?.value.experiments.length
})

/**
 * Experiment options type definition.
 */
interface ExperimentOption {
  value: string | null
  text: string
  disabled?: boolean
}

/**
 * Experiment options that are to be selected from the dropdown.
 */
const experimentOptions = computed(() => {
  // Find experiments that are in a phase where the player is able to join.
  const experiments = Object.values(experimentsData.value.experiments)
    .filter((experiment: any) => ['Inactive', 'Testing', 'Ongoing', 'Paused'].includes(experiment.phase))

  // Create a list for the dropdown, including a "blank" option for when nothing is selected.
  const options: ExperimentOption[] = [
    { value: null, text: 'Select an experiment', disabled: true },
    ...experiments.map((experiment: any) => {
      return { value: experiment.experimentId, text: experiment.displayName }
    })
  ]

  return options
})

/**
 * Check if target player is enrolled in an experiment.
 */
const alreadyInSelectedExperiment = computed(() => {
  if (experimentFormInfo.value.experimentId === null) {
    return false
  }
  return Object.keys(playerData.value.model.experiments.experimentGroupAssignment).includes(experimentFormInfo.value.experimentId)
})

/**
 * Check if target player is already enrolled in an experiment variant.
 */
const alreadyInSelectedVariant = computed(() => {
  if (experimentFormInfo.value.variantId === null || experimentFormInfo.value.experimentId === null) {
    return false
  }
  const newVariantId = experimentFormInfo.value.variantId === 'Control group' ? null : experimentFormInfo.value.variantId
  return playerData.value.model.experiments.experimentGroupAssignment[experimentFormInfo.value.experimentId]?.variantId === newVariantId
})

/**
 * Text displayed if the target player is already a member of a selected experiment.
 */
const experimentMessage = computed(() => {
  if (alreadyInSelectedExperiment.value) {
    return `${playerData.value.model.playerName} is already a member of this experiment.`
  } else {
    return ''
  }
})

/**
 * Text displayed if the target player is already a member of a selected variant.
 */
const variantMessage = computed(() => {
  if (alreadyInSelectedVariant.value) {
    return `${playerData.value.model.playerName} is already a member of this variant.`
  } else {
    return ''
  }
})

/**
 * Text to be displayed on the 'Ok' button.
 */
const okButtonText = computed((): string => {
  if (alreadyInSelectedExperiment.value) {
    return 'Set variant'
  } else {
    return 'Enroll in experiment'
  }
})

/**
 * Enables the 'Ok' button when a valid experiment variant is selected.
 */
const okButtonEnabled = computed((): boolean => {
  return !!experimentFormInfo.value.variantId && !alreadyInSelectedVariant.value
})

/**
 * When true display 'No seatbelts' warning.
 */
const showSeatbeltsWarning = computed((): boolean => {
  return okButtonEnabled.value && !alreadyInSelectedExperiment.value
})

/**
 * Variant options type definition.
 */
interface VariantOption {
  value: string | null
  text: string
  disabled?: boolean
}

/**
 All available variants to be selected on the form dropdown.
 */
const variantOptions = ref<VariantOption[]>([])

/**
 * Update the selected experiment and/or variant option(s).
 */
async function updateExperimentSelection () {
  const experiments = playerData.value.experiments
  const isTesterExperiments = Object.entries(experiments).filter(([key, value]: any) => value.isPlayerTester)
  experimentFormInfo.value.isTester = isTesterExperiments.find(([key, value]) => key === experimentFormInfo.value.experimentId) !== undefined
  experimentFormInfo.value.variantId = null

  // Fetch the experiment details so that we can get the list of variants.
  variantOptions.value = []
  const response = await gameServerApi.get(`/experiments/${experimentFormInfo.value.experimentId}`)
  const options = Object.keys(response.data.state.variants).map((item) => ({ value: item, text: item }))
  variantOptions.value = [
    { value: null, text: 'Select an experiment variant', disabled: true },
    { value: 'Control group', text: 'Control group' },
    ...options
  ]
}

/**
 * Join or update a selected experiment.
 */
async function joinOrUpdateExperiment () {
  const message = alreadyInSelectedExperiment.value ? `${playerData.value.model.playerName} changed experiment variant.` : `${playerData.value.id} enrolled into the experiment.`
  const newVariantId = experimentFormInfo.value.variantId === 'Control group' ? null : experimentFormInfo.value.variantId
  await gameServerApi.post(`/players/${playerData.value.id}/changeExperiment`, { ExperimentId: experimentFormInfo.value.experimentId, VariantId: newVariantId, IsTester: experimentFormInfo.value.isTester })
  showSuccessToast(message)
  playerRefresh()
}

/**
 * Reset the modal.
 */
function resetModal () {
  experimentFormInfo.value = getNewExperimentFormInfo()
  variantOptions.value = [{ value: null, text: 'Select an experiment' }]
}
</script>
