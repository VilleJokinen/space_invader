<template lang="pug">
meta-list-card(
  title="Build Log"
  description="Messages generated during the config build process. Errors will cause the build to fail."
  icon="clipboard-list"
  :itemList="buildMessages"
  :searchFields="searchFields"
  :sortOptions="sortOptions"
  :filterSets="filterSets"
  emptyMessage="No log entries."
  ).mb-3.list-group-stripes
  template(#item-card="{ item }")
    //- pre {{ item }}
    MCollapse(extraMListItemMargin)
      //- Row header
      template(#header)
        //- Title can be too long and truncate when width is too narrow.
        MListItem {{ item.message }}
          template(#top-right)
            MBadge(:variant="messageLevelBadgeVariant(item.level)") {{ item.level }}
          template(#bottom-right)
            meta-button(v-if="isUrl(item.url)" link @click="openMessageSourceUrl(item)") View source #[fa-icon(icon="external-link-alt")]

      //- Collapse content
      div.mb-3
        //- Source information.
        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Source Info
          span(v-if="item.sourceInfo").small
            span.text-monospace {{ item.sourceInfo.slice(0, 30) }}...
            meta-clipboard-copy(:contents="`${item.sourceInfo}`")
          span(v-else).text-muted.font-italic None

        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Source Location
          span(v-if="item.sourceLocation").small
            span.text-monospace {{ item.sourceLocation.slice(0, 30) }}...
            meta-clipboard-copy(:contents="`${item.sourceLocation}`")
          span(v-else).text-muted.font-italic None

        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Location URL
          span(v-if="item.locationUrl").small
            span.text-monospace ...{{ item.locationUrl.slice(-30) }}
            meta-clipboard-copy(:contents="`${item.locationUrl}`")
          span(v-else).text-muted.font-italic None

        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Item ID
          span(v-if="item.itemId")
            span {{ item.itemId }}
          span(v-else).text-muted.font-italic None

        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Variant ID
          span(v-if="item.variantId")
            span {{ item.variantId }}
          span(v-else).text-muted.font-italic None

        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Exception
          span(v-if="item.exception")
            span {{ item.exception }}
          span(v-else).text-muted.font-italic None

        b-list-group-item.py-1: b-row(no-gutters align-h="between").small
          span Caller
          span(v-if="item.callerFileName").small
            span.text-monospace {{ item.callerMemberName }}
            span.text-monospace :...{{ item.callerFileName.slice(-10) }}
            span.text-monospace @{{ item.callerLineNumber }}
            meta-clipboard-copy(:contents="`${item.callerMemberName}:${item.callerFileName}@${item.callerLineNumber}`")
          span(v-else).text-muted.font-italic None
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import type { GameConfigLogLevel, GameConfigBuildMessage, GameConfigBuildReport } from '../../gameConfigServerTypes'

import { MetaListFilterOption, MetaListFilterSet, MetaListSortOption, MetaListSortDirection } from '@metaplay/meta-ui'
import { MBadge, MCollapse, type Variant, MListItem } from '@metaplay/meta-ui-next'

const props = defineProps<{
  /**
   * Build report to show.
   */
  buildReport?: GameConfigBuildReport
}>()

/**
 * Extract build messages from the build report.
 */
const buildMessages = computed(() => {
  return props.buildReport?.buildMessages ?? []
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
  new MetaListSortOption('Warning Level', 'level', MetaListSortDirection.Ascending),
  new MetaListSortOption('Warning Level', 'level', MetaListSortDirection.Descending),
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
    new MetaListFilterSet('level',
      [
        new MetaListFilterOption('Verbose', (x: any) => x.level === 'Verbose'),
        new MetaListFilterOption('Debug', (x: any) => x.level === 'Debug'),
        new MetaListFilterOption('Information', (x: any) => x.level === 'Information'),
        new MetaListFilterOption('Warning', (x: any) => x.level === 'Warning'),
        new MetaListFilterOption('Error', (x: any) => x.level === 'Error'),
      ]
    ),
  ]
})

/**
 * Calculate variant (ie: color) for badges based on message level.
 * @param level Message level of warning.
 */
function messageLevelBadgeVariant (level: GameConfigLogLevel): Variant {
  const mappings: {[level: string]: Variant} = {
    Verbose: 'neutral',
    Debug: 'neutral',
    Information: 'primary',
    Warning: 'warning',
    Error: 'danger',
  }
  return mappings[level] ?? 'danger'
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
function openMessageSourceUrl (message: GameConfigBuildMessage) {
  if (message.locationUrl) {
    window.open(message.locationUrl, '_blank')
  }
}
</script>
