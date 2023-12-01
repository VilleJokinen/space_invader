<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  data-cy="available-localizations"
  title="Available Localizations"
  :itemList="allLocalizationData"
  :searchFields="['id', 'name', 'description']"
  :filterSets="filterSets"
  :sortOptions="sortOptions"
  :defaultSortOption="defaultSortOption"
  :pageSize="20"
  icon="language"
)
  template(#item-card="{item: localization}")
    // TODO: Align with game confgis and add info about warnings.
    MListItem
      fa-icon(
        v-if="localization.status === 'Error' || localization.status === 'Failed'"
        icon="times"
        class="tw-text-red-500 tw-mr-1.5"
        )
      span {{ localization.name || 'No name available' }}
      template(#badge)
        MBadge(v-if="localization.isActive" variant="success") Active
        MBadge(v-if="localization.isArchived" variant="neutral") Archived

      template(#top-right)
        //- TODO: align this with latest changes in gameconfig and double check naming?
        div(v-if="localization.status === 'Building'")
          b-progress(animated).font-weight-bold
            b-progress-bar(:value="100") Building...
        div(v-else)
          div Uploaded #[meta-time(:date="localization.persistedAt")]
      template(#bottom-left) {{ localization.description || 'No description available' }}
      template(#bottom-right)
        div #[meta-button(link :to="`localization/diff?newRoot=${localization.id}`" data-cy="diff-localization" :disabled="localization.isActive") Diff to active] / #[meta-button(link :to="getDetailPagePath(localization.id)" data-cy="view-localization") View localization]

meta-raw-data(:kvPair="allLocalizationDataUnfiltered" name="allLocalizationDataUnfiltered")
</template>

<script lang="ts" setup>
import { BProgress, BProgressBar } from 'bootstrap-vue'
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { getAllLocalizationsSubscriptionOptions } from '../../subscription_options/localization'
import { MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import type { LocalizationInfo } from '../../localizationServerTypes'

const {
  data: allLocalizationDataUnfiltered,
} = useSubscription<LocalizationInfo[]>(getAllLocalizationsSubscriptionOptions())

const route = useRoute()
const detailsRoute = computed(() => route.path)

/**
 * Filtered list containing only game configs that are not failed.
 */
const allLocalizationData = computed(() => {
  if (allLocalizationDataUnfiltered.value !== undefined) {
    return allLocalizationDataUnfiltered.value.filter((x) => x.status === 'Succeeded' || x.status === 'Building')
  } else {
    return undefined
  }
})

/**
 * Get detail page path by joining it to the current path,
 * but take into account if there's already a trailing slash.
 * \todo Do a general fix with router and get rid of this.
 */
function getDetailPagePath (detailId: string) {
  const path = detailsRoute.value
  const maybeSlash = path.endsWith('/') ? '' : '/'
  return path + maybeSlash + detailId
}

/**
 * Sorting options for localizations.
 */
const defaultSortOption = 1
const sortOptions = [
  new MetaListSortOption('Time', 'persistedAt', MetaListSortDirection.Ascending),
  new MetaListSortOption('Time', 'persistedAt', MetaListSortDirection.Descending),
  new MetaListSortOption('Name', 'name', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'name', MetaListSortDirection.Descending),
]

/**
 * Filtering options for localizations.
 */
const filterSets = [
  new MetaListFilterSet('status',
    [
      new MetaListFilterOption('Building', (x: any) => x.status === 'Building'),
      new MetaListFilterOption('Ok', (x: any) => x.status === 'Ok'),
    ]
  ),
  new MetaListFilterSet('archived',
    [
      new MetaListFilterOption('Archived', (x: any) => x.isArchived),
      new MetaListFilterOption('Not archived', (x: any) => !x.isArchived, true)
    ]
  )
]
</script>
