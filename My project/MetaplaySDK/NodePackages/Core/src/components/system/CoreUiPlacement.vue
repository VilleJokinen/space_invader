<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- Display all components that are registered to a given UiPlacement. -->

<template lang="pug">
b-row(
  align-h="center"
  :class="{ 'small-margin-between' : smallBottomMargin }"
)
  b-col(
    v-for="placementInfo in filteredUiComponents"
    :key="placementInfo.uniqueId"
    :lg="placementInfo?.width === 'full' || alwaysFullWidth ? 12 : 6"
    :class="{ 'mb-3': !smallBottomMargin }"
    )
      //- Use $attrs and $listeners to forward all props and listeners to components. This may change in Vue3.
      //- Also pass in any props that were defined with the placement.
      component(
        :is="placementInfo.vueComponent"
        v-bind="Object.assign({}, $attrs, placementInfo.props)"
        )
</template>

<script lang="ts" setup>
import type { UiPlacement } from '../../integration_api/uiPlacementApis'
import { useCoreStore } from '../../coreStore'
import { useGameServerApiStore } from '@metaplay/game-server-api'
import { computed } from 'vue'

const props = defineProps<{
  /**
   * Name of the UI placement to be displayed.
   */
  placementId: UiPlacement
  alwaysFullWidth?: boolean
  smallBottomMargin?: boolean
}>()

const coreStore = useCoreStore()
const gameServerApiStore = useGameServerApiStore()

/**
 * List of components that are registered to the given placement.
 * By default all components are visible to all users however,
 * components that have the 'displayPermission' property,
 * will only be visible to users with the required permission.
 */
const filteredUiComponents = computed(() => {
  return coreStore.uiComponents[props.placementId]?.filter((placementInfo) => {
    return gameServerApiStore.doesHavePermission(placementInfo.displayPermission)
  })
})

</script>

<style scoped>

@media (min-width: 576px) {
 .small-margin-between div+div {
  margin-top: 4px;
}
}

</style>
