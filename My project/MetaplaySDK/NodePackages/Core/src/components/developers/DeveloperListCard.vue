<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  data-cy="developer-players-list"
  title="All Developer Players"
  :itemList="developerPlayersData"
  :searchFields="['name', 'id']"
  :sortOptions="sortOptions"
  icon="user"
  emptyMessage="No developer players. You have not marked any players as developers."
  style="margin-bottom: 2rem;"
  )
  template(#item-card="{ item: developerPlayer }")
    MListItem
      span(v-if="developerPlayer.deserializedSuccessfully") {{ developerPlayer.name }}
      span(v-else).text-danger Player deserialization failed

      template(#top-right)
        span {{ developerPlayer.id }}

      template(#bottom-left)
        span(v-if="developerPlayer.deserializedSuccessfully") Last login on #[meta-time(:date="developerPlayer.lastLoginAt || ''" showAs="date")]
        span(v-else) Last login unknown

      template(#bottom-right)
        router-link(:to="`/players/${developerPlayer.id}`") View player
</template>

<script lang="ts" setup>
import { MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getDeveloperPlayersSubscriptionOptions } from '../../subscription_options/players'

/**
* Subset of the data that the game server returns for developer players as of R24.
*/
interface DeveloperPlayerInfo {
  id: string
  name: string
  lastLoginAt: string
}

const { data: developerPlayersData } = useSubscription<DeveloperPlayerInfo[]>(getDeveloperPlayersSubscriptionOptions())

const sortOptions = [
  MetaListSortOption.asUnsorted(),
  new MetaListSortOption('Name', 'name', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'name', MetaListSortDirection.Descending),
  new MetaListSortOption('Last login', 'lastLoginAt', MetaListSortDirection.Ascending),
  new MetaListSortOption('Last login', 'lastLoginAt', MetaListSortDirection.Descending),
]
</script>
