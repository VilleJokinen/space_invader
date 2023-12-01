<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="playerData")
  meta-action-modal-button(
    id="action-ban-player"
    permission="api.players.ban"
    :action-button-text="playerData.model.isBanned ? 'Un-Ban Player' : 'Ban Player'"
    variant="warning"
    block
    modal-title="Change Banned Status"
    @show="resetModal"
    ok-button-text="Save Settings"
    :okButtonDisabled="!isStatusChanged"
    :onOk="updateBannedStatus"
    )
      b-form
        div.d-flex.justify-content-between.mb-2
          span.font-weight-bold Player Banned
          meta-input-checkbox(v-model="isCurrentlyBanned" data-cy="player-ban-toggle" name="Player banned" size="lg" showAs="switch")
        span.small.text-muted Banning will disconnect the player and prevent them from logging into the game.
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../../subscription_options/players'

const props = defineProps<{
  /**
   * ID of the player to ban.
   */
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const {
  data: playerData,
  refresh: playerRefresh,
} = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))
const isCurrentlyBanned = ref(false)

const isStatusChanged = computed(() => {
  return isCurrentlyBanned.value !== playerData.value.model.isBanned
})

function resetModal () {
  isCurrentlyBanned.value = playerData.value.model.isBanned
}

async function updateBannedStatus () {
  const isBanned = isCurrentlyBanned.value // \note Copy, because this.isBanned might get modified before toast is shown
  await gameServerApi.post(`/players/${playerData.value.id}/ban`, { isBanned })
  showSuccessToast(`${playerData.value.model.playerName || 'n/a'} ${isBanned ? 'banned' : 'un-banned'}.`)
  playerRefresh()
}
</script>
