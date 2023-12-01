<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="playerData")
  meta-list-card(
    data-cy="player-login-methods-card"
    title="Login Methods"
    icon="key"
    :itemList="allAuths"
    tooltip="You can move these login methods to connect to another account via the admin actions."
    :searchFields="searchFields"
    :sortOptions="sortOptions"
    :defaultSortOption="defaultSortOption"
    :filterSets="filterSets"
    :emptyMessage="`${playerData.model.playerName || 'n/a'} has no credentials attached.`"
  )
    template(#item-card="{ item }")
      MCollapse(extraMListItemMargin)
        //- Row header
        template(#header)
          MListItem
            span(v-if="item.type === 'device'") #[fa-icon(icon="key")] Client token
            span(v-else) #[fa-icon(icon="user-tag")] {{ item.displayString }}

            template(#top-right)
              span.mr-1 Attached #[meta-time(:date="item.attachedAt")]
              meta-button(
                modal="confirm-auth-remove"
                variant="outline-danger"
                size="sm"
                permission="api.players.auth"
                @click="authToRemove = item"
                subtle
                class="tw-relative tw-bottom-0.5"
                )
                  fa-icon(icon="trash-alt")

            template(#bottom-left)
              div.text-break-word {{ item.id }}

        //- Collapse content
        div.font-weight-bold.mt-2 Known Devices
        MList(v-if="item.devices && item.devices.length > 0" showBorder class="tw-mt-2 tw-mb-3")
          MListItem(
            v-for="device in item.devices"
            :key="device.id"
            class="tw-px-3"
            condensed
            )
            span {{ device.deviceModel }}
            template(#top-right)
              span.text-muted {{ device.id }}
        MList(v-else showBorder class="tw-mt-2 tw-mb-3")
          MListItem(class="tw-px-3" condensed).text-muted.font-italic No devices recorded for this login method.

  //- Modal
  b-modal#confirm-auth-remove(title="Remove Authentication Method" size="md" @hidden="authToRemove = null" @ok="removeAuth" centered no-close-on-backdrop)
    p You are about to remove #[MBadge(v-if="authToRemove") {{ authToRemove.displayString }}] from #[MBadge {{ playerData.model.playerName }}]. They will not be able to login to their account using this method.

    p.font-italic.text-danger(v-if="allAuths.length === 1") Note: Removing this last auth method means the account will be orphaned!

    meta-no-seatbelts(:name="playerData.model.playerName || 'n/a'")

    template(#modal-footer="{ ok, cancel }")
      meta-button(variant="secondary" @click="cancel") Cancel
      meta-button(variant="danger" @click="ok" safety-lock).text-white Remove Auth
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { parseAuthMethods, MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption, showSuccessToast, showErrorToast } from '@metaplay/meta-ui'
import { MBadge, MCollapse, MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

const props = defineProps<{
  /**
   * Id of the player whose device list to show.
   */
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const {
  data: playerData,
  refresh: playerRefresh,
} = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))
const authToRemove = ref<any>(null)
const searchFields = [
  'displayString',
  'id'
]
const sortOptions = [
  new MetaListSortOption('Attached time ', 'attachedAt', MetaListSortDirection.Ascending),
  new MetaListSortOption('Attached time ', 'attachedAt', MetaListSortDirection.Descending),
]
const defaultSortOption = 1
const filterSets = [
  new MetaListFilterSet('type',
    [
      new MetaListFilterOption('Client token', (x: any) => x.type === 'device'),
      new MetaListFilterOption('Social auth', (x: any) => x.type === 'social')
    ]
  )
]

const allAuths = computed(() => {
  return parseAuthMethods(playerData.value.model.attachedAuthMethods, playerData.value.model.deviceHistory)
})

async function removeAuth () {
  const identifier = authToRemove.value.name
  try {
    await gameServerApi.delete(`/players/${playerData.value.id}/auths/${authToRemove.value.name}/${authToRemove.value.id}`)
    const message = `'${identifier}' deleted from ${playerData.value.model.playerName || 'n/a'}.`
    showSuccessToast(message)
    playerRefresh()
  } catch (error: any) {
    const message = `Failed to remove '${identifier}' from ${playerData.value.model.playerName || 'n/a'}. Reason: ${error.response.data.error.details}`
    const toastTitle = 'Backend Error'
    showErrorToast(message, toastTitle)
  }
}
</script>
