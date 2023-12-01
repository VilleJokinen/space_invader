<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  data-cy="activables-card"
  :title="title"
  :icon="categoryIcon"
  :itemList="decoratedActivables"
  :getItemKey="getItemKey"
  :pageSize="longList ? 50 : undefined"
  :searchFields="searchFields"
  :filterSets="filterSets || defaultFilterSets"
  :sortOptions="sortOptions"
  :defaultSortOption="defaultSortOption"
  :emptyMessage="emptyMessage"
)
  template(#item-card="slot")
    meta-collapse(:id="noCollapse ? '' : slot.item.activable.config.activableId")
      //- Row header
      template(#header)
        MListItem
          span {{ slot.item.activable.config.displayName }}
          template(#badge)
            span(v-if="slot.item.activable.config.displayShortInfo").small.text-muted.ml-1 ({{ slot.item.activable.config.displayShortInfo }})
          template(#top-right)
            MBadge(v-if="playerId && slot.item.activable.debugState"
              :tooltip="slot.item.debugPhaseTooltip"
              variant="warning"
              ) {{ getPhaseDisplayString(slot.item.activable) }}
            meta-activable-phase-badge(v-else :activable="slot.item.activable" :player="playerData" :typeName="`${categoryDisplayName.toLocaleLowerCase()}`")
          template(#bottom-left)
            div {{ slot.item.activable.config.description }}
          template(#bottom-right)
            div(v-if="slot.item.activable.debugState").text-warning Debug override!
            div(v-else-if="slot.item.nextPhase") {{ getPhaseDisplayString(slot.item.activable, slot.item.nextPhase) }} #[meta-time(:date="slot.item.nextPhaseStartTime")]
            slot(name="additionalTexts" v-bind:item="slot.item" v-bind:index="`${slot.item.activable.activableId}`")

            div(v-if="!props.hideConversion")
              MTooltip(:content="slot.item.conversionTooltip") {{ slot.item.conversion.toFixed() }}% conversion
            b-link(v-if="slot.item.linkUrl" :to="slot.item.linkUrl" data-cy="view-activable") View {{ `${slot.item.linkText.toLocaleLowerCase()}` }}

      MList(v-if="!noCollapse" showBorder)
        slot(name="collapseContents" v-bind:item="slot.item" v-bind:index="`${slot.item.activable.activableId}`")

      //- Debug controls
      div.border.rounded-sm.mt-2.pb-3.bg-light(v-if="playerId && enableDevelopmentFeatures").px-3.py-2.mb-3
        div.font-weight-bold.mb-1 #[fa-icon(icon="wrench")] Debug Controls
        div.small.text-muted.mb-2 You can force this player into different activation states to test the feature easier. Just remember to clear the debug state afterwards!
        b-form.d-sm-flex.justify-content-between.align-content-center
          label.sr-only(for="inline-form-debug-controls") Debug Phase
          b-form-select#inline-form-debug-controls.mb-3(size="sm" v-model="selectedDebugPhase[activableKey(slot.item)]" :options="debugPhaseOptions")
          div(:style="slot.item.activable.debugState ? 'min-width: 16rem' : 'min-width: 7rem'").text-right
            meta-button.mr-2(variant="warning" size="sm" @click="forceSelectedPhase(slot.item)" :disabled="!hasValidSelectedDebugPhase(slot.item)" permission="api.players.force_activable_phase") Set Phase
            meta-button(v-if="slot.item.activable.debugState" variant="primary" size="sm" @click="clearDebugPhase(slot.item)" permission="api.players.force_activable_phase") Clear Debug Phase
      div.mt-1.w-100.text-center.text-muted.font-italic(v-else) Debug controls disabled in current environment.

</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { MetaListFilterOption, MetaListFilterSet, MetaListSortOption, abbreviateNumber, showSuccessToast } from '@metaplay/meta-ui'
import { MTooltip, MBadge, MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import * as Activables from '../../activables'
import { useCoreStore } from '../../coreStore'
import { getIsActivableForcePhaseEnabledSubscriptionOptions } from '../../subscription_options/activables'
import { getStaticConfigSubscriptionOptions } from '../../subscription_options/general'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'
import MetaActivablePhaseBadge from './MetaActivablePhaseBadge.vue'

const props = withDefaults(defineProps<{
  activables?: any
  /**
   * Optional: If true hide disabled activable data.
   */
  hideDisabled?: boolean
  /**
   * Optional: If true hide activable conversion statistics.
   */
  hideConversion?: boolean
  /**
   * Optional: Id of the player whose data we are interested in.
   */
  playerId?: string
  /**
   * Group name assigned to similar activables.
   * @example 'In-game events'
   */
  category: string
  /**
   * Optional: If true show the 50 list items on one page.
   * Defaults 8.
   */
  longList?: boolean
  /**
   * Custom message to be shown when there are no activables available to display.
   */
  emptyMessage: string
  /**
   * Title to be shown on the card.
   */
  title: string
  /**
   * Optional: If true renders a non-collapsible list card.
   */
  noCollapse?: boolean
  /**
   * Optional: String to be added at the beginning of the URL.
   * @example "/offerGroups"
   */
  linkUrlPrefix?: string
  /**
   * Optional: Array of list item property names that can be searched.
   */
  searchFields?: string[]
  /**
   * Optional: List of filter sets that can be applied.
   */
  filterSets?: MetaListFilterSet[]
  /**
   * Optional: List of sort types that can be applied.
   */
  sortOptions?: MetaListSortOption[]
  /**
   * Optional: Index of sort option to choose by default.
   */
  defaultSortOption?: number
}>(), {
  playerId: undefined,
  linkUrlPrefix: undefined,
  activables: undefined,
  searchFields: undefined,
  filterSets: undefined,
  sortOptions: undefined,
  defaultSortOption: 1
})

const gameServerApi = useGameServerApi()
const coreStore = useCoreStore()
const emits = defineEmits(['refreshActivables'])

/**
 * Subscribe to the static config data.
 */
const {
  data: staticConfigData
} = useSubscription(getStaticConfigSubscriptionOptions())

/**
 * Subscription check if 'force phase debug controls' is enabled for the activable.
 */
const {
  data: activablesForcePhaseEnabled
} = useSubscription(getIsActivableForcePhaseEnabledSubscriptionOptions())

/**
 * Subscribe to player data when a playerId is provided. We watch the id and resubscribe as it changes.
 */
const {
  data: playerData,
} = useSubscription(() => props.playerId ? getSinglePlayerSubscriptionOptions(props.playerId) : undefined)

// Activable data -----------------------------------------------------------------------------------------------------

interface activableInfo {
  activable: any
  kindId: string
  linkUrl: string
  linkText: string
  nextPhase: string | undefined
  nextPhaseStartTime: string | undefined
  phaseSortOrder: number
  phaseDisplayString: string
  conversion: number
  conversionTooltip: string | number
  debugPhaseTooltip: string | undefined
}
/**
 * Activable data to be displayed in this card.
 */
const decoratedActivables = computed(() => {
  if (props.activables && activablesMetadata) {
    const categoryData: any = activablesMetadata.value.categories[props.category]
    const activableInfos: activableInfo[] = []
    for (const activableKind of categoryData.kinds) {
      const kindInfo = activablesMetadata.value.kinds[activableKind]
      activableInfos.push(...Object.values(props.activables?.[activableKind].activables).map((activable: any) => {
        const scheduleStatus = activable.scheduleStatus
        const statistics = activable.statistics
        return {
          activable,
          kindId: activableKind,
          linkUrl: (props.linkUrlPrefix ?? `/activables/${kindInfo.category}`) + `/${activableKind}/${activable.config.activableId}`,
          linkText: categoryData.shortSingularDisplayName,
          nextPhase: scheduleStatus?.nextPhase ? scheduleStatus.nextPhase.phase : undefined,
          nextPhaseStartTime: scheduleStatus?.nextPhase ? scheduleStatus.nextPhase.startTime : undefined,
          phaseSortOrder: Activables.phaseSortOrder(activable, null, playerData.value),
          phaseDisplayString: Activables.phaseDisplayString(activable, null, playerData.value),
          conversion: props.hideConversion ? 0 : conversion(statistics.numConsumedForFirstTime, statistics.numActivatedForFirstTime),
          conversionTooltip: props.hideConversion ? 0 : conversionTooltip(statistics.numConsumedForFirstTime, statistics.numActivatedForFirstTime),
          debugPhaseTooltip: debugPhaseTooltip(activable)
        }
      }))
    }
    return activableInfos.filter((x: any) => !props.hideDisabled || x.activable.config.activableParams.isEnabled === true)
  } else return undefined
})

/**
 * Additional data about the activable.
 */
const activablesMetadata = computed(() => {
  return staticConfigData?.value.serverReflection.activablesMetadata
})

// Debug controls --------------------------------------------------------------------------------------------------

/**
 * When true the debug controls are visible on the dashboard.
 */
const enableDevelopmentFeatures = computed(() => {
  return activablesForcePhaseEnabled.value?.forcePlayerActivablePhaseEnabled || false
})

/**
 * The phase to forcibly activate.
 */
const selectedDebugPhase: any = ref({})

/**
 * List of all debug phases displayed as dropdown options.
 */
const debugPhaseOptions = computed(() => {
  const phases = [
    'Preview',
    'Active',
    'EndingSoon',
    'Review',
    'Inactive'
  ]

  const options = phases.map(phase => { return { value: phase, text: Activables.phaseToDisplayString(phase) } })
  // \todo Figure out a better way to have a default option than having the string 'undefined' here.
  //       Can we populate the initial selectedDebugPhase for all the activables?
  options.unshift({ value: 'undefined', text: 'Select a phase' })
  return options
})

/**
 * Text to show the phase badge when an activiable is in a forced debug state.
 */
function getPhaseDisplayString (activable: any, phase = null) {
  return Activables.phaseDisplayString(activable, phase, playerData.value)
}

/**
 * Tooltip text to show the phase when an activiable is in a forced debug state.
 */
function debugPhaseTooltip (activable: any) {
  if (!activable.debugState) {
    return undefined
  } else {
    return `${activable.config.activableId} is in debug-forced phase '${activable.debugState.phase}'`
  }
}

/**
 * Helper function that retrieves an activible unique key.
 */
function activableKey (decoratedActivable: any) {
  return decoratedActivable.kindId + '/' + decoratedActivable.activable.config.activableId
}

/**
 * Helper function that checks that the activable has a valid debug phase.
 */
function hasValidSelectedDebugPhase (decoratedActivable: any) {
  const selected = selectedDebugPhase.value[activableKey(decoratedActivable)]
  return selected && selected !== 'undefined' // \todo Funky 'undefined'. See debugPhaseOptions.
}

/**
 * Async function that forcibly activates an activable phase and ignores the configured schedules and/or other conditions.
 */
async function forceSelectedPhase (decoratedActivable: any) {
  const phase = selectedDebugPhase.value[activableKey(decoratedActivable)]
  if (!phase || phase === 'undefined') { // \todo Funky 'undefined'. See debugPhaseOptions.
    return
  }
  const kindId = decoratedActivable.kindId
  const activableId = decoratedActivable.activable.config.activableId
  const displayName = decoratedActivable.activable.config.displayName

  const phaseStr = phase === null ? 'none' : phase
  await gameServerApi.post(`/activables/${props.playerId}/forcePhase/${kindId}/${activableId}/${phaseStr}`)

  const message = `'${displayName}' debug-forced to phase '${phase}'.`
  showSuccessToast(message)
  emits('refreshActivables')
}

/**
 * Async function that removes a forced phase to return the Activable to its normal configured behavior.
 */
async function clearDebugPhase (decoratedActivable: any) {
  const kindId = decoratedActivable.kindId
  const activableId = decoratedActivable.activable.config.activableId
  const displayName = decoratedActivable.activable.config.displayName

  await gameServerApi.post(`/activables/${props.playerId}/forcePhase/${kindId}/${activableId}/none`)
  const message = `'${displayName}' reset to its original phase.`
  showSuccessToast(message)
  emits('refreshActivables')
}

// Filtering and Sorting ----------------------------------------------------------------------------------------------

/**
 * Filtering options passed to the MetaListCard component.
 */
const defaultFilterSets = computed(() => {
  let allPhaseDisplayStrings = Activables.allPhaseDisplayStrings(!playerData.value)
  if (props.hideDisabled) {
    allPhaseDisplayStrings = allPhaseDisplayStrings.filter(x => x !== 'Disabled')
  }
  return [
    new MetaListFilterSet('status', allPhaseDisplayStrings.map(phaseDisplayString => {
      return new MetaListFilterOption(phaseDisplayString, (x: any) => {
        return x.phaseDisplayString === phaseDisplayString
      })
    }))
  ]
})

// Misc ui ---------------------------------------------------------------------------------------------------------------

/**
 * The activable category icon to be displayed.
 */
const categoryIcon = computed(() => {
  return coreStore.gameSpecific.activableCustomization[props.category]?.icon || 'calendar-alt'
})

/**
 * The activable category name to be displayed.
 */
const categoryDisplayName = computed(() => {
  return activablesMetadata.value?.categories[props.category].shortSingularDisplayName
})

/**
 * Function that returns the conversion rate of the displayed activable.
 */
function conversion (consumed: any, activated: any) {
  if (activated === 0) {
    return 0
  } else {
    return consumed / activated * 100
  }
}

/**
 * Returns a tooltip for the conversion rate.
 */
function conversionTooltip (consumed: any, activated: any): string {
  if (activated === 0) {
    return 'Not activated by any players yet.'
  } else {
    return `Activated by ${abbreviateNumber(activated)} players and consumed by ${abbreviateNumber(consumed)}.`
  }
}

/**
 * Retrieves the activable Id.
 */
function getItemKey (item: any) {
  return item.activable.config.activableId
}

</script>
