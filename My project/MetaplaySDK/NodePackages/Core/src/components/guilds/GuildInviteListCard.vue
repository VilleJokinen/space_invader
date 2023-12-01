<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  id="guild-member-list-card"
  title="Invites"
  icon="people-arrows"
  tooltip="A list of invites that are currently active for this guild."
  :itemList="invites"
  :searchFields="searchFields"
  :sortOptions="sortOptions"
  :filterSets="filterSets"
  emptyMessage="This guild has no active invites!"
)
  template(#item-card="slot")
    b-row(no-gutters align-h="between")
      span
        span {{ slot.item.inviteCode }}
      span.small: b-link(:to="`/players/${slot.item.playerId}`") View player
    b-row(no-gutters align-h="between")
      small Used: {{ slot.item.numTimesUsed }} #[span(v-if="slot.item.numMaxUsages > 0") /{{ slot.item.numMaxUsages }}] #[span.text-muted.ml-1.mr-1 |] Created: #[meta-time(:date="slot.item.createdAt")] #[span.text-muted.ml-1.mr-1 |] Expires: #[meta-time(:date="slot.item.expiresAt")]
</template>

<script lang="ts" setup>
import moment from 'moment'
import { computed } from 'vue'

import { MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'

import { getSingleGuildSubscriptionOptions } from '../../subscription_options/guilds'

const props = defineProps<{
  guildId: string
}>()

const {
  data: guildData,
} = useSubscription(getSingleGuildSubscriptionOptions(props.guildId))

const searchFields = [
  'inviteCode',
  'playerId',
]

const sortOptions = [
  MetaListSortOption.asUnsorted(),
  new MetaListSortOption('Player', 'playerId', MetaListSortDirection.Ascending),
  new MetaListSortOption('Player', 'playerId', MetaListSortDirection.Descending),
]

const invites = computed(() => {
  const list = []
  for (const member of Object.values(guildData.value.model.members) as any) {
    for (const invite of Object.values(member.invites) as any) {
      list.push({
        ...invite,
        playerId: member.id,
        expiresAt: moment(new Date(invite.createdAt)).add(moment.duration(invite.expiresAfter)), // TODO: refactor to Luxon
      })
    }
  }
  return list
})

const filterSets = [
  new MetaListFilterSet('usage',
    [
      new MetaListFilterOption('Unused', (x: any) => x.numTimesUsed === 0),
      new MetaListFilterOption('Part used', (x: any) => x.numTimesUsed > 0 && x.numTimesUsed < x.numMaxUsages),
      new MetaListFilterOption('Fully used', (x: any) => x.numTimesUsed === x.numMaxUsages)
    ]
  )
]
</script>
