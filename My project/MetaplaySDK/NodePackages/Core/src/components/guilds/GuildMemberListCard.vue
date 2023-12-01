<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card#guild-member-list-card(
  title="Members"
  icon="users"
  :itemList="members"
  :searchFields="searchFields"
  :sortOptions="sortOptions"
  emptyMessage="This guild is empty!"
)
  template(#item-card="{ item: guildMember }")
    // TODO: replace this with a data driven, generic representation of guild member identifiers
    MListItem
      //- Online status
      span.mr-1
        fa-icon(v-if="guildMember.isOnline" size="xs" icon="circle").text-success
        fa-icon(v-else-if="hasRecentlyLoggedIn(guildMember.lastLoginAt)" size="xs" :icon="['far', 'circle']").text-success
        fa-icon(v-else size="xs" :icon="['far', 'circle']").text-dark
      //- Player name and rank
      span #[span.font-weight-bold.text-truncate {{ guildMember.displayName || 'n/a' }}] #[small: span.font-weight-bold(:class="guildMember.role === 'Leader' ? 'text-warning' : 'text-muted'") {{ guildRoleDisplayString(guildMember.role) }}]

      template(#top-right): b-link(:to="`/players/${guildMember.id}`") View player

      //- Game specific info. Feel free to replace with whatever is most relevant in your game!
      template(#bottom-left) Poked: {{ guildMember.numTimesPoked }} times #[span.text-muted.ml-1.mr-1 |] Vanity points: {{ guildMember.numVanityPoints }} #[span.text-muted.ml-1.mr-1 |] Vanity rank: {{ guildMember.numVanityRanksConsumed }}
</template>

<script lang="ts" setup>
import { cloneDeep } from 'lodash-es'
import moment from 'moment'
import { computed } from 'vue'

import { MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getSingleGuildSubscriptionOptions } from '../../subscription_options/guilds'
import { guildRoleDisplayString } from '../../coreUtils'

const props = defineProps<{
  guildId: string
}>()

const {
  data: guildData,
} = useSubscription(getSingleGuildSubscriptionOptions(props.guildId))

const searchFields = [
  'displayName',
  'id',
  'role'
]

const sortOptions = [
  MetaListSortOption.asUnsorted(),
  new MetaListSortOption('Role', 'role', MetaListSortDirection.Ascending),
  new MetaListSortOption('Role', 'role', MetaListSortDirection.Descending),
  new MetaListSortOption('Name', 'displayName', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'displayName', MetaListSortDirection.Descending),
]

// TODO: consider changing the API response to make this transformation unnecessary
const members = computed(() => {
  const list = []
  for (const key of Object.keys(guildData.value.model.members)) {
    const payload = cloneDeep(guildData.value.model.members[key])
    payload.id = key
    list.push(payload)
  }
  return list
})

function hasRecentlyLoggedIn (isoDateTime: string) {
  return moment().diff(moment(isoDateTime), 'hours') < 12
}
</script>
