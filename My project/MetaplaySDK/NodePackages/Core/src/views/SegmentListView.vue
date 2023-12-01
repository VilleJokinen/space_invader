<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!playerSegmentsData"
  :meta-api-error="playerSegmentsError"
)
  template(#overview)
    meta-page-header-card(title="View Player Segments")
      p Player segments are dynamic sets of players based on their game state.
      p.small.text-muted Players can enter and leave segments as their play the game and belong to multiple segments at once. You can define your own properties to segment players with and then create the actual segments in your #[b-link(to="/gameConfigs") game configs].

  template(#center-column)
    meta-list-card(
      data-cy="all-segments"
      title="All Segments"
      :itemList="playerSegmentsData.segments"
      :searchFields="['info.displayName', 'info.description']"
      :sortOptions="sortOptions"
      :pageSize="20"
      emptyMessage="No player segments. Set them up in your game configs to start using the feature!"
      )
      template(#item-card="{ item: segment }")
        MListItem
          span {{ segment.info.displayName }}
          template(#top-right): meta-audience-size-estimate(:sizeEstimate="segment.sizeEstimate" small)
          template(#bottom-left) {{ segment.info.description }}
          template(#bottom-right): b-link(:to="`/segments/${segment.info.segmentId}`" data-cy="view-segment") View segment

  template(#default)
    meta-raw-data(:kvPair="playerSegmentsData.segments" name="segments")/
</template>

<script lang="ts" setup>
import { MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import MetaAudienceSizeEstimate from '../components/MetaAudienceSizeEstimate.vue'

import { getPlayerSegmentsSubscriptionOptions } from '../subscription_options/general'

const {
  data: playerSegmentsData,
  error: playerSegmentsError
} = useSubscription(getPlayerSegmentsSubscriptionOptions())

const sortOptions = [
  MetaListSortOption.asUnsorted(),
  new MetaListSortOption('Name', 'info.displayName', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'info.displayName', MetaListSortDirection.Descending),
  new MetaListSortOption('Audience Size Estimate', 'sizeEstimate', MetaListSortDirection.Ascending),
  new MetaListSortOption('Audience Size Estimate', 'sizeEstimate', MetaListSortDirection.Descending),
]
</script>
