<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card.h-100(
  data-cy="matchmaker-top-players-list-card"
  title="Top Players"
  :itemList="topPlayersData"
  :sortOptions="topPlayersSortOptions"
  :searchFields="['name', 'model.playerId', 'model.defenseMmr']"
  ).h-100
  template(#item-card="{ item: player }")
    MListItem
      | {{ player.name }}
      template(#top-right) {{ player.model.playerId }}
      template(#bottom-left) {{ player.summary }}
      template(#bottom-right): meta-button(link permission="api.players.view" :to="`/players/${player.model.playerId}`") View player
</template>

<script lang="ts" setup>
import { MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getTopPlayersOfSingleMatchmakerSubscriptionOptions } from '../../subscription_options/matchmaking'

const props = defineProps({
  /**
   * Matchmaker to display.
   */
  matchmakerId: {
    type: String,
    required: true
  }
})

/**
 * Subscribe to top players data of a single matchmaker based on matchmakerId.
 */
const { data: topPlayersData } = useSubscription(getTopPlayersOfSingleMatchmakerSubscriptionOptions(props.matchmakerId))

const topPlayersSortOptions = [
  new MetaListSortOption('MMR', 'model.defenseMmr', MetaListSortDirection.Descending),
  new MetaListSortOption('MMR', 'model.defenseMmr', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'name', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'name', MetaListSortDirection.Descending),
]

</script>
