<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Container
div(class="pl-3 pr-3 pb-3 bg-light rounded border")
  //- Main toggle for enabling targeting
  b-row(align-h="between" no-gutters).mb-2.mt-3
    span.font-weight-bold Enable Targeting
    meta-input-checkbox(
      :value="enableTargeting"
      @input="enableTargeting = $event; update()"
      name="Enable targeting"
      size="lg"
      showAs="switch"
      )

  //- If targeting...
  div(v-if="enableTargeting").border-top.mt-3.pt-2
    //- Header
    div.mb-1.mt-2.font-weight-bold Select Player Segments

    //- Loading spinner
    div(v-if="!segmentOptions").w-100.text-center
      b-spinner(label="Loading segments...")/

    //- Error message (TODO: ship with core segments and get rid of this case)
    div(v-else-if="segmentOptions.length === 0").w-100.text-center.font-italic.text-muted.py-3 No segments defined in the currently active game configs.

    //- Segment selector
    div(v-else)
      div(class="tw-space-y-3 @lg:tw-space-y-0 @lg:tw-flex @lg:tw-space-x-2")
        //- Selection type
        MInputSegmentedSwitch(
          :model-value="segmentMatchingRule"
          @update:model-value="segmentMatchingRule = $event"
          :options="segmentMatchingOptions"
          class="tw-shrink-0"
          )

        //- Segment input
        meta-input-select(
          :value="chosenSegments"
          @input="chosenSegments = $event"
          :options="segmentOptions"
          :searchFields="['displayName']"
          placeholder="Select player segments..."
          multiselect
          )
          template(#option="{ option }")
            b-row(no-gutters align-h="between").mb-1
              div.font-weight-bold {{ option?.displayName }}
              div.text-right {{ option?.estimatedPlayerCount }}

          template(#selectedOption="{ option }")
            div {{ option?.displayName }}

      //- Hint text
      div.small.text-muted.text-center.mt-1
        span(v-if="segmentMatchingRule === 'all'") Players must match all of the selected segments.
        span(v-else) Players must match at least one of the selected segments.

    //- Individual players
    div(v-if="isPlayerTargetingSupported").w-100
      div.mb-1.mt-3
        span.font-weight-bold Select Individual Players
        span.text-muted.small.ml-1 ({{ playerList.length }}/{{ maxTargetPlayerListSize }} selected)

      MInputTextArea(
        :model-value="playerBatchImportString"
        @update:modelValue="onPlayerListInput"
        :variant="playerInputTextAreaVariant"
        :disabled="maxTargetPlayerListSize === 0"
        :placeholder="maxTargetPlayerListSize === 0 ? 'To use this feature, set a maximum number of target player in your runtime options.': 'Comma separated list of IDs: Player:XXXXXXXXXX, Player:YYYYYYYYYY, ...'"
        :hintMessage="playerBatchImportString && !isPlayerInputValid ? playerListValidationError : ''"
        )

      div(v-if="invalidPlayerIds && invalidPlayerIds.length > 0").text-danger.small.mt-1
        span Invalid player IDs: {{ invalidPlayerIds.slice(0, 20).join(', ') }}
        span(v-if="invalidPlayerIds.length > 20")  and {{ invalidPlayerIds.length - 20 }} more..
        meta-clipboard-copy(:contents="invalidPlayerIds.join(',')")
</template>

<script lang="ts" setup>
import { computed, nextTick, ref, watch, onMounted } from 'vue'

import { abbreviateNumber } from '@metaplay/meta-ui'
import { useGameServerApi } from '@metaplay/game-server-api'
import { useSubscription } from '@metaplay/subscriptions'

import { getPlayerSegmentsSubscriptionOptions, getRuntimeOptionsSubscriptionOptions } from '../../subscription_options/general'
import type { TargetingOptions } from './mailUtils'
import type { MetaInputSelectOption } from '@metaplay/meta-ui'
import { MInputSegmentedSwitch, MInputTextArea } from '@metaplay/meta-ui-next'
import { debounce, uniq } from 'lodash-es'
import { isValidPlayerId } from '../../coreUtils'

const props = withDefaults(defineProps<{
  /**
   * Optional: Whether or not the game supports targeting individual players.
   */
  isPlayerTargetingSupported?: boolean
  /**
   * The current targeting options.
   */
  value: TargetingOptions
}>(), {
  isPlayerTargetingSupported: true,
  value: () => {
    return {
      targetPlayers: [],
      targetCondition: null,
      valid: true,
    }
  },
})

const gameServerApi = useGameServerApi()

/**
 * Subscribe to player segment data.
 */
const {
  data: playerSegmentsData,
} = useSubscription(getPlayerSegmentsSubscriptionOptions())

/**
 * Runtime options for the game server.
 */
const {
  data: runtimeOptionsData,
  error: runtimeOptionsError
} = useSubscription(getRuntimeOptionsSubscriptionOptions())

/**
 * Type definition of segment option
 */
interface SegmentOption {
  displayName: string
  segmentId: string
  estimatedPlayerCount: string
}

/**
 * List of segment options that are to be displayed on the multi-select dropdown.
 */
const segmentOptions = ref<Array<MetaInputSelectOption<SegmentOption>>>()

/**
 * Server type definition of segment information displayed as an option.
 */
interface SegmentInfo {
  info: {
    displayName: string
    segmentId: string
  }
  sizeEstimate: number
}

/**
 * Derive segment option details from segment information supplied.
 * @param segment Segment whose information is to be displayed as an option.
 */
function makeSegmentOptionFromSegmentInfo (segment: SegmentInfo): MetaInputSelectOption<SegmentOption> {
  return {
    id: segment.info.segmentId,
    value: {
      displayName: segment.info.displayName,
      segmentId: segment.info.segmentId,
      estimatedPlayerCount: segment.sizeEstimate != null ? `~ ${abbreviateNumber(segment.sizeEstimate)} player${segment.sizeEstimate !== 1 ? 's' : ''}` : 'Estimate pending...',
    }
  }
}

/**
 * Derive segment options from supplied segment Ids.
 * @param segmentId Id of segment whose information is to be displayed as an option.
 */
function makeSegmentOptionFromSegmentId (segmentId: string): SegmentOption {
  const segment = playerSegmentsData.value.segments.find((segment: SegmentInfo) => segmentId === segment.info.segmentId)
  if (segment) {
    return makeSegmentOptionFromSegmentInfo(segment)?.value || null
  } else {
    // Segment is missing from config.
    // todo needs testing
    return {
      displayName: 'missing',
      segmentId: 'missing',
      estimatedPlayerCount: 'missing',
    }
  }
}

// Segment options are only loaded once to avoid breaking the select component's internal state... Not sure if there's a better way to do this.
const playerSegmentsUnwatch = watch(playerSegmentsData, (newVal: { $type: string, segments: Array<{ $type: string, sizeEstimate: number, info: any }>}) => {
  if (!newVal?.segments) return

  segmentOptions.value = newVal.segments.map((segment) => makeSegmentOptionFromSegmentInfo(segment))
  if (segmentOptions.value.length > 0) {
    // Unwatch the data when we get the first update. If that happens immediately then playerSegmentsUnwatch won't
    // exist yet, so we need to delay this for a frame.
    void nextTick(() => {
      playerSegmentsUnwatch()
    })
  }
}, { immediate: true })

/**
 * Selected segments and conditions used to target the audience.
 */
const chosenSegments = ref<SegmentOption[]>(
  (props.value.targetCondition?.requireAnySegment ??
  props.value.targetCondition?.requireAllSegments ??
  []).map((segment: string) => makeSegmentOptionFromSegmentId(segment)))
watch(chosenSegments, update)

/**
 * When true that enables targeting of a message to a specific audience.
 */
const enableTargeting = ref(props.value.targetPlayers.length > 0 || !!props.value.targetCondition)

/**
 * Rules that match players to at least one or all segment conditions.
 */
const segmentMatchingRule = ref<string>(props.value.targetCondition?.requireAllSegments ? 'all' : 'any')
watch(segmentMatchingRule, update)

const segmentMatchingOptions = [
  { label: 'All', value: 'all' },
  { label: 'Any', value: 'any' },
]

// MANUAL PLAYER ID's ---------------------------------------------------------

const playerBatchImportString = ref('')
const invalidPlayerIds = ref<any[]>([])

// Initialize the text field.
onMounted(() => {
  if (props.value.targetPlayers.length > 0) {
    playerBatchImportString.value = props.value.targetPlayers.join(', ')
    void validatePlayerListInput()
  }
})

/**
 * List of target player ids.
 */
const playerList = ref<string[]>(props.value.targetPlayers)

/**
 * Maximum number of individual players that can be targeted by a notification campaign.
 */
const maxTargetPlayerListSize = computed((): number => {
  const options = runtimeOptionsData.value?.options.find((option: any) => option.name === 'System')
  if (!options) return 0
  return options.values.maxTargetPlayersListSize
})

/**
 * Checks that there are no validation errors.
 */
const isPlayerInputValid = computed(() => {
  if (isPlayerListValidationLoading.value) return false
  if (props.isPlayerTargetingSupported && playerListValidationError.value) return false
  return true
})

const playerInputTextAreaVariant = computed(() => {
  if (playerBatchImportString.value) {
    if (isPlayerListValidationLoading.value) return 'loading'
    if (isPlayerInputValid.value) return 'success'
    else return 'danger'
  }
  return 'default'
})

/**
 * Utility to validate the input after debouncing.
 * @param input Incoming input.
 */
function onPlayerListInput (input: string) {
  playerBatchImportString.value = input
  debounce(() => {
    void validatePlayerListInput()
  }, 300)()
}

const playerListValidationError = ref<string>()
const isPlayerListValidationLoading = ref(false)

/**
 * Validates the input player list. Outputs a list of valid player ids and potential errors as side effects.
 */
async function validatePlayerListInput () {
  // Create list of player IDs and remove duplicates.
  const inputPlayerIdList = uniq(playerBatchImportString.value.trim().split(',').map((id) => id.trim()))

  // Remove the last array element if it's empty.
  if (inputPlayerIdList[inputPlayerIdList.length - 1] === '') {
    inputPlayerIdList.pop()
  }

  playerList.value = []
  playerListValidationError.value = undefined
  invalidPlayerIds.value = []

  // Validate the input locally first.
  if (inputPlayerIdList.length > maxTargetPlayerListSize.value) {
    playerListValidationError.value = `Too many players! You entered a list of ${inputPlayerIdList.length} players but the maximum limit is ${maxTargetPlayerListSize.value}. Consider using segments instead?`
    update()
    return
  }

  invalidPlayerIds.value = inputPlayerIdList.filter((id) => !isValidPlayerId(id))
  if (invalidPlayerIds.value.length > 0) {
    playerListValidationError.value = 'Not all listed IDs look like valid player IDs!'
    update()
    return
  }

  // Validate the input on the server.
  isPlayerListValidationLoading.value = true
  update() // Trigger an update here to clear the previous state and set the form as invalid due to the loading state.
  const response = (await gameServerApi.post('players/bulkValidate', { PlayerIds: inputPlayerIdList })).data
  isPlayerListValidationLoading.value = false
  const validPlayerIds = response.filter((x: any) => x.validId && x.playerData != null).map((x: any) => x.playerData.id) as string[]
  invalidPlayerIds.value = response.filter((x: any) => !x.validId || x.playerData == null).map((x: any) => x.playerIdQuery)
  if (invalidPlayerIds.value.length > 0) {
    playerListValidationError.value = 'Invalid player IDs in list!'
    update()
    return
  }

  // Save.
  playerList.value = validPlayerIds
  update()
}

// FORM INPUT --------------------------------------------------------------

const emits = defineEmits(['input'])

/**
 * Update the targeting list when a new segment is added.
 */
function update () {
  // Default to no targeting.
  const returnValue: TargetingOptions = {
    targetPlayers: [],
    targetCondition: null,
    valid: true,
  }

  if (enableTargeting.value) {
    // Add individual players.
    returnValue.targetPlayers = playerList.value

    // Add segments.
    if (chosenSegments.value.length > 0) {
      returnValue.targetCondition = {
        $type: 'Metaplay.Core.Player.PlayerSegmentBasicCondition'
      }

      const chosenSegmentIds = chosenSegments.value.map((x) => x.segmentId)
      if (segmentMatchingRule.value === 'all') {
        returnValue.targetCondition.requireAllSegments = chosenSegmentIds
      } else {
        returnValue.targetCondition.requireAnySegment = chosenSegmentIds
      }
    }

    if (!isPlayerInputValid.value) returnValue.valid = false
  }

  emits('input', returnValue)
}

// PLAYER COUNT -------------------------------------------------------------

// const databaseItemCounts = useSubscription(getDatabaseItemCountsSubscriptionOptions().data
// const totalPlayerCount = computed(() => databaseItemCounts.value?.totalItemCounts.Players)

// const totalEstimatedSelectedPlayers = computed(() => {
//   return estimateAudienceSize(playerSegmentsData.value?.segments, props.value)
// })
</script>
