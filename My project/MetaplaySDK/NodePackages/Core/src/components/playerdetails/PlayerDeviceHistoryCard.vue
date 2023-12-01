<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  v-if="playerData"
  data-cy="player-device-history-card"
  title="Device History"
  icon="tablet-alt"
  :itemList="allDevices"
  :searchFields="searchFields"
  :sortOptions="sortOptions"
  :filterSets="filterSets"
  :defaultSortOption="1"
  :emptyMessage="`${playerData.model.playerName || 'n/a'} has no device history.`"
)
  template(#item-card="{ item }")
    MCollapse(extraMListItemMargin)
      //- Row header
      template(#header)
        MListItem
          //- By default we should not add anything after span below to fill the top left position, now it takes away the default bold styling which is inconsistent.
          span #[fa-icon(:icon="item.clientPlatform === 'iOS' || item.clientPlatform === 'Android' ? 'tablet-alt' : 'desktop'")] {{ item.deviceModel }}

          template(#top-right) Last login #[meta-time(:date="item.lastLoginAt")]

          template(#bottom-left)
            div(v-if="item.incompleteHistory").text-warning Note: This is an old device and we don't have a full login history for it.
            div.text-break-word {{ item.id }}

          template(#bottom-right)
            div.text-right Created #[meta-time(:date="item.firstSeenAt")]
            div.text-right Total logins: {{ item.numLogins }}

      //- Collapse content
      div.font-weight-bold Login Methods History
      MList(v-if="item.loginMethods && item.loginMethods.length > 0" showBorder class="tw-mt-2 tw-mb-3")
          MListItem(
            v-for="login in item.loginMethods"
            :key="login?.id"
            class="tw-px-3"
            condensed
            )
            span(v-if="login") {{ login.platform === 'DeviceId' ? 'Client token' : login.platform }}
            span(v-else)
              div.text-muted.font-italic No login history.
            template(#top-right)
              span(v-if="login").small.text-muted {{ login.id }}
      MList(v-else showBorder class="tw-mt-2 tw-mb-3")
          MListItem(class="tw-px-3" condensed).text-muted.font-italic No login methods recorded for this device.
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { MetaListFilterSet, MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MList, MListItem, MCollapse } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

const props = defineProps<{
  /**
   * Id of the player whose device list to show.
   */
  playerId: string
}>()

const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

const allDevices = computed(() => {
  if (!playerData.value.model.deviceHistory) return []
  return Object.keys(playerData.value.model.deviceHistory).map(x => Object.assign({ id: x }, playerData.value.model.deviceHistory[x]))
})

const searchFields = [
  'deviceModel',
  'id',
]
const sortOptions = [
  new MetaListSortOption('Last login ', 'lastLoginAt', MetaListSortDirection.Ascending),
  new MetaListSortOption('Last login ', 'lastLoginAt', MetaListSortDirection.Descending),
]

const filterSets = computed(() => {
  return [
    MetaListFilterSet.asDynamicFilterSet(allDevices.value, 'Platform', (x: any) => x.clientPlatform)
  ]
})
</script>
