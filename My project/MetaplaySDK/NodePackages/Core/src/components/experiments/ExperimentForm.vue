<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
b-modal#action-edit-experiment(:title="title" size="md" @show="resetForm" @ok="submitForm" centered no-close-on-backdrop)
  b-alert(:show="warningMessage != null") {{ warningMessage }}

  div.mb-4
    h6 Rollout Settings
    div.p-3.bg-light.rounded.border
      b-row(align-h="between" no-gutters)
        span.font-weight-bold Rollout Enabled
        b-form-checkbox(v-model="rolloutEnabled" name="Rollout enabled" size="lg" switch)
      hr(v-show="rolloutEnabled")
      b-row(v-show="rolloutEnabled")
        b-col(md).mb-2
          div.mb-1.font-weight-bold Rollout %
          meta-input-number(
            v-model="rolloutPercentage"
            :min="0"
            :max="100"
            name="Rollout Percentage"
            )
        b-col(md).mb-2
          div.mb-1.font-weight-bold Capacity Limit
            MTooltip.ml-2(content="Maximum capacity may be surpassed by a few players if they join at the same time." noUnderline): MBadge(shape="pill") ?
          meta-input-number(
            v-model="maxCapacity"
            @input="maxCapacityChanged"
            :min="1"
            allowNull
            nullText="Unlimited"
            integer
            name="Max Capacity"
            )
      div.small.text-muted.mt-1
        span(v-if="rolloutEnabled") With rollout enabled, a percentage of your player base will be able to join the experiment, up to the optional capacity limit.
        span(v-else) Players will not be able to join an experiment with rollout disabled. You can use this to manually close an experiment to new players.

  div.mb-4
    h6 Audience
    message-audience-form.mb-2(v-model="audience" :isPlayerTargetingSupported="false")
    b-row(align-h="between" no-gutters)
      div.font-weight-bold Account Age
      b-form-group.mt-01.mb-0
        b-form-radio-group(v-model="enrollTrigger" size="sm")
          b-form-radio(value="Login") Everyone
          b-form-radio(value="NewPlayers") New players
    div.small.text-muted.mb-3(v-if="enrollTrigger !== 'Login'") Players can join the experiment at the time of account creation only.
    div.small.text-muted.mb-3(v-else) Players can join the experiment the next time they login.

  div.mb-4
    h6 Variant Rollout Percentages
    b-container.border.rounded.bg-light.p-3
      b-row(cols="1" cols-md="3")
        b-col(v-for="(value, key) in variantWeights" :key="key").mb-2
          div.mb-1.font-weight-bold {{ key }} %
          meta-input-number(
            v-model="value.weight"
            :state="validateVariantWeight(key)"
            @change="updateVariantWeights(key)"
            :min="0"
            :name="String(key)"
            )
      div.small.text-danger(v-if="totalWeights !== 100") Variant weights do not add up to 100%. #[b-link(@click="balanceVariantWeights()") Balance automatically]?
      div.small.text-warning(v-if="variantWeights && parseInt(variantWeights.Control.weight) === 0") Empty control group! Validating this experiment's results may not be possible.

  template(#modal-footer="{ ok, cancel }")
    meta-button(variant="secondary" @click="cancel") Cancel
    meta-button(variant="primary" :disabled="!isFormValid" @click="ok" safety-lock) {{ okButtonText }}
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { MTooltip, MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getSingleExperimentSubscriptionOptions } from '../../subscription_options/experiments'
import MessageAudienceForm from '../mails/MessageAudienceForm.vue'

const props = defineProps<{
  experimentId: string
  title?: string
  okButtonText?: string
  warningMessage?: string
}>()

const {
  data: experimentInfoData,
  refresh: experimentInfoRefresh,
} = useSubscription(getSingleExperimentSubscriptionOptions(props.experimentId))

const rolloutEnabled = ref(false)
const rolloutPercentage = ref<any>(null)
const enrollTrigger = ref<any>(null)
const hasCapacityLimit = ref(false)
const maxCapacity = ref<number | null>(null)
const variantWeights = ref<any>(null)
const audience = ref<any>(MessageAudienceForm.props.value.default())
const totalWeights = ref(0)

const isFormValid = computed(() => {
  if (rolloutPercentage.value === undefined) return false
  if (maxCapacity.value === undefined) return false
  if (!Object.keys(variantWeights.value).every(variantId => validateVariantWeight(variantId))) return false
  return true
})

function updateVariantWeights (changedVariantId: any) {
  if (variantWeights.value[changedVariantId].weight === '' || !variantWeights.value[changedVariantId].weight || parseInt(variantWeights.value[changedVariantId].weight) < 0) {
    variantWeights.value[changedVariantId].weight = 0
  }

  // Round all
  for (const variant of Object.values(variantWeights.value) as any) {
    variant.weight = Math.round(variant.weight)
  }

  totalWeights.value = Object.values(variantWeights.value).reduce((sum: number, variant: any) => sum + parseInt(variant.weight), 0)
}

function balanceVariantWeights () {
  // Count
  let totalWeightsTemp = Object.values(variantWeights.value).reduce((sum: number, variant: any) => sum + parseInt(variant.weight), 0)

  // Gracefully handle edge case where all variants have 0 weight
  if (totalWeightsTemp === 0) {
    for (const key in variantWeights.value) {
      variantWeights.value[key].weight = 1
      totalWeightsTemp++
    }
  }

  // Redistribute if needed
  if (totalWeightsTemp !== 100) {
    for (const variant of Object.values(variantWeights.value) as any) {
      variant.weight = Math.round(variant.weight / totalWeightsTemp * 100)
    }

    // Finally adjust control group to fix rounding errors
    totalWeightsTemp = Object.values(variantWeights.value).reduce((sum: number, variant: any) => sum + parseInt(variant.weight), 0)
    if (totalWeightsTemp !== 100) {
      variantWeights.value.Control.weight += (100 - totalWeightsTemp)
    }
  }

  totalWeights.value = Object.values(variantWeights.value).reduce((sum: number, variant: any) => sum + parseInt(variant.weight), 0)
}

function maxCapacityChanged (newCapacity: number | null) {
  hasCapacityLimit.value = newCapacity !== null
}

function validateVariantWeight (variantId: any) {
  const value = parseInt(variantWeights.value[variantId].weight)
  if (isNaN(value)) return false
  if (value < 0 || totalWeights.value !== 100) return false
  return true
}

function resetForm () {
  rolloutEnabled.value = !experimentInfoData.value.state.isRolloutDisabled
  rolloutPercentage.value = experimentInfoData.value.state.rolloutRatioPermille / 10
  enrollTrigger.value = experimentInfoData.value.state.enrollTrigger
  hasCapacityLimit.value = experimentInfoData.value.state.hasCapacityLimit
  maxCapacity.value = experimentInfoData.value.state.hasCapacityLimit ? experimentInfoData.value.state.maxCapacity : null

  variantWeights.value = {
    Control: { weight: experimentInfoData.value.state.controlWeight }
  }
  Object.entries(experimentInfoData.value.state.variants).forEach(([id, data]) => {
    variantWeights.value[id] = {
      weight: (data as any).weight
    }
  })
  balanceVariantWeights()
  audience.value = MessageAudienceForm.props.value.default()
  audience.value.targetCondition = experimentInfoData.value.state.targetCondition
}

const gameServerApi = useGameServerApi()
const emits = defineEmits(['ok'])
async function submitForm () {
  const config = {
    isRolloutDisabled: !rolloutEnabled.value,
    enrollTrigger: enrollTrigger.value,
    hasCapacityLimit: hasCapacityLimit.value,
    maxCapacity: hasCapacityLimit.value ? maxCapacity.value : null,
    rolloutRatioPermille: rolloutPercentage.value * 10,
    variantWeights: Object.fromEntries(Object.keys(variantWeights.value).map(key => [key === 'Control' ? null : key, variantWeights.value[key].weight])),
    variantIsDisabled: null,
    targetCondition: audience.value.targetCondition
  }
  await gameServerApi.post(`/experiments/${props.experimentId}/config`, config)
  showSuccessToast('Configuration set.')
  experimentInfoRefresh()
  emits('ok')
}
</script>
