<template lang="pug">
meta-list-card(
  title="Validation Log"
  description="Messages generated during validation of the game config. Validation issues may label the game config as failed."
  icon="clipboard-list"
  :itemList="validationMessages"
  :searchFields="searchFields"
  :sortOptions="sortOptions"
  :filterSets="filterSets"
  emptyMessage="No log entries."
  ).mb-3.list-group-stripes
  template(#item-card="{ item }")
    MCollapse(extraMListItemMargin)
      //- Row header
      template(#header)
        //- Title can be too long and truncate when width is too narrow.
        MListItem {{ item.message }}
          template(#top-right)
            MBadge(:variant="messageLevelBadgeVariant(item.messageLevel)") {{ item.messageLevel }}
          template(#bottom-left)
            div {{ item.sheetName }} / {{ item.configKey }} / {{ item.columnHint }}
            div #[meta-plural-label(:value="item.variants.length" label="variant")] affected
          template(#bottom-right)
            meta-button(v-if="isUrl(item.url)" link @click="openMessageSourceUrl(item)") View source #[fa-icon(icon="external-link-alt")]

      //- Collapse content
      div.mb-3
        //- Source information.
        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Library
          span {{ item.sheetName }}

        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Config Key
          span {{ item.configKey }}

        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Column
          span {{ item.columnHint }}

        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Source
          span(v-if="item.sourcePath").small
            span.text-monospace {{ item.sourceMember }}
            span.text-monospace :...{{ item.sourcePath.slice(-10) }}
            span.text-monospace @{{ item.sourceLineNumber }}
            meta-clipboard-copy(:contents="`${item.sourceMember}:${item.sourcePath}@${item.sourceLineNumber}`")
          span(v-else).text-muted.font-italic None

        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span URL
          span(v-if="item.url").small
            span.text-monospace ...{{ item.url.slice(-30) }}
            meta-clipboard-copy(:contents="item.url")
          span(v-else).text-muted.font-italic None

        //- List of affected variants.
        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Variants affected
          span(v-if="item.variants.length > 0")
            div(v-for="(value, key) in item.variants" :key="key") {{ value }}
          span(v-else).text-muted.font-italic None

        //- Game specific additional data dumped as key/value pairs.
        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Additional data
          span(v-if="item.additionData")
            span(v-for="(value, key) in item.additionData" :key="key") {{ key }}:{{ value }}
          span(v-else).text-muted.font-italic None
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import type { GameConfigLogLevel, GameConfigBuildReport, GameConfigValidationMessage } from '../../gameConfigServerTypes'

import { MetaListFilterOption, MetaListFilterSet, MetaListSortOption, MetaListSortDirection } from '@metaplay/meta-ui'
import { MBadge, MCollapse, type Variant, MListItem } from '@metaplay/meta-ui-next'

const props = defineProps<{
  /**
   * Build report to show.
   */
  buildReport?: GameConfigBuildReport
}>()

/**
 * Extract validation messages from the build report.
 */
const validationMessages = computed(() => {
  return props.buildReport?.validationMessages ?? []
})

/**
 * Card search fields.
 */
const searchFields = [
  'message',
  'sheetName',
  'configKey',
  'columnHint',
]

/**
 * Card sort options.
 * */
const sortOptions = [
  new MetaListSortOption('Message', 'message', MetaListSortDirection.Ascending),
  new MetaListSortOption('Message', 'message', MetaListSortDirection.Descending),
  new MetaListSortOption('Warning Level', 'messageLevel', MetaListSortDirection.Ascending),
  new MetaListSortOption('Warning Level', 'messageLevel', MetaListSortDirection.Descending),
  new MetaListSortOption('Library', 'sheetName', MetaListSortDirection.Ascending),
  new MetaListSortOption('Library', 'sheetName', MetaListSortDirection.Descending),
  new MetaListSortOption('Config Key', 'configKey', MetaListSortDirection.Ascending),
  new MetaListSortOption('Config Key', 'configKey', MetaListSortDirection.Descending),
  new MetaListSortOption('Column', 'columnHint', MetaListSortDirection.Ascending),
  new MetaListSortOption('Column', 'columnHint', MetaListSortDirection.Descending),
  MetaListSortOption.asUnsorted(),
]

/**
 * Card filters.
 */
const filterSets = computed(() => {
  return [
    new MetaListFilterSet('messageLevel',
      [
        new MetaListFilterOption('Verbose', (x: any) => x.messageLevel === 'Verbose'),
        new MetaListFilterOption('Debug', (x: any) => x.messageLevel === 'Debug'),
        new MetaListFilterOption('Information', (x: any) => x.messageLevel === 'Information'),
        new MetaListFilterOption('Warning', (x: any) => x.messageLevel === 'Warning'),
        new MetaListFilterOption('Error', (x: any) => x.messageLevel === 'Error'),
      ]
    ),
  ]
})

/**
 * Calculate variant (ie: color) for badges based on message level.
 * @param messageLevel Message level of warning.
 */
function messageLevelBadgeVariant (messageLevel: GameConfigLogLevel): Variant {
  const mappings: {[messageLevel: string]: Variant} = {
    Verbose: 'neutral',
    Debug: 'neutral',
    Information: 'primary',
    Warning: 'warning',
    Error: 'danger',
  }
  return mappings[messageLevel] ?? 'danger'
}

/**
 * Crudely determine if the given string is a valid URL or not. Used to decide if a warning's URL should be a clickable
 * link or not.
 * @param url URL to check.
 */
function isUrl (url: string | undefined | null) {
  return url && (url.startsWith('http://') || url.startsWith('https://'))
}

/**
 * Open the view to a messages URL in another window.
 * @param message Message to open URL for.
 */
function openMessageSourceUrl (message: GameConfigValidationMessage) {
  if (message.url) {
    window.open(message.url, '_blank')
  }
}
</script>
