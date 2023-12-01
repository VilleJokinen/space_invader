<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A wrapper component for displaying customizable overview lists. -->

<template lang="pug">
b-list-group(flush)
  span.font-weight-bold.border-bottom.pb-1 #[fa-icon(:icon="icon")] {{ listTitle }}
  b-list-group-item.py-1.px-0(v-for="item in filteredItems" :key="item.displayName")
    span.w-100(v-if="item.displayType=== 'country'")
      span {{ item.displayName }}
      meta-country-code.float-right(:isoCode="item.displayValue(sourceObject)" :class="item.displayValue(sourceObject) === 'Unknown' ? 'text-muted' : '' " showName)

    span.w-100(v-else-if="item.displayType=== 'currency'")
      span {{ item.displayName }}
      span.float-right ${{ item.displayValue(sourceObject).toFixed(2) }}

    span.w-100(v-else-if="item.displayType === 'datetime'")
      span {{ item.displayName }}
      meta-time.float-right(:date="item.displayValue(sourceObject)" :showAs="item.displayHint === 'date' ? item.displayHint : undefined")

    span.w-100(v-else-if="item.displayType === 'language'")
      span {{ item.displayName }}
      meta-language-label.mt-1.float-right(:language="item.displayValue(sourceObject)" variant="badge").small

    span.w-100(v-else-if="item.displayType === 'number'")
      span(:class="item.displayHint === 'highlightIfNonZero' && item.displayValue(sourceObject) !== 0 ? 'text-danger' : ''") {{ item.displayName }}
      span.float-right(:class="item.displayHint === 'highlightIfNonZero' && item.displayValue(sourceObject) !== 0 ? 'text-danger' : ''") {{ item.displayValue(sourceObject) }}

    span.w-100(v-else-if="item.displayType === 'text'")
      span {{ item.displayName }}
      span.float-right(:class="item.displayHint === 'monospacedText' ? 'text-monospace' : item.displayValue(sourceObject) === 'Unknown' ? 'text-muted' : '' ") {{ item.displayValue(sourceObject) }}
      //span.float-right(:class="classHint(item)") {{ item.displayValue(sourceObject) }}

    span.w-100(v-else)
      span.text-danger Unknown displayType {{ item.displayType }}

</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { OverviewListItem } from '../../integration_api/overviewListsApis'
import MetaLanguageLabel from '../MetaLanguageLabel.vue'
import { useGameServerApiStore } from '@metaplay/game-server-api'

const props = withDefaults(defineProps<{
  /**
   * Title for this overview list.
   */
  listTitle: string
  /**
   * Optional font awesome icon.
   */
  icon?: string
  /**
   * The Overview list items to be rendered.
  */
  items: OverviewListItem[]
  /**
   * Base object that contains the data i.e the guild or player object.
   */
  sourceObject: object
}>(), {
  icon: 'bar-chart',
})

const gameServerApiStore = useGameServerApiStore()

/**
 * List of items to be displayed on the overview card.
 * By default all items are visible to all users however,
 * items that have the 'displayPermission' property,
 * will only be visible to users with the required permission.
 */
const filteredItems = computed(() => {
  return props.items.filter(item => gameServerApiStore.doesHavePermission(item.displayPermission))
})

</script>
