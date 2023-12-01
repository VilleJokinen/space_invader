<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  meta-action-modal-button(
    id="action-force-reset-player-state"
    permission="api.players.reset_player"
    action-button-text="Force Reset Player"
    variant="danger"
    modal-title="Reset Player State"
    ok-button-text="Force Reset Player"
    :onOk="resetPlayerState"
    )
      p Force resetting the player will re-initialize the entire player state, including important things like purchases and connected devices.
      p The authentication records are preserved, so it will be possible for the player to re-attach to the player after reset.
</template>

<script lang="ts" setup>
import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../../subscription_options/players'

const props = defineProps<{
  /**
   * ID of the player to set as developer.
   */
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const {
  refresh: playerRefresh,
} = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))
async function resetPlayerState () {
  await gameServerApi.post(`/players/${props.playerId}/forceResetState`)
  showSuccessToast('Player state force reset to defaults.')
  playerRefresh()
}
</script>
