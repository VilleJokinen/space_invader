<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="playerData")
  meta-action-modal-button(
    id="action-set-developer"
    permission="api.players.set_developer"
    :action-button-text="playerData.model.isDeveloper ? 'Remove Dev Status' : 'Mark as Developer'"
    variant="warning"
    block
    modal-title="Manage Developer Status"
    @show="resetModal"
    ok-button-text="Save Settings"
    :okButtonDisabled="!isStatusChanged"
    :onOk="updateDeveloperStatus"
    )
      b-form
        div.d-flex.justify-content-between.mb-2
          span.font-weight-bold Developer Status
          MInputSwitch(
            :model-value="isDeveloper"
            @update:model-value="isDeveloper = $event"
            name="Developer status"
            data-cy="developer-status-toggle"
            )

        div.small.text-muted
          div Developer players have special powers. For instance, developers can:
          ul(style="padding-inline-start: .5rem").mt-1
            li - Login during maintenance.
            li - Execute development-only actions in production.
            li - Allow validating iOS sandbox in-app purchases.
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../../subscription_options/players'
import { MInputSwitch } from '@metaplay/meta-ui-next'

const props = defineProps<{
  /**
   * ID of the player to set as developer.
   */
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const {
  data: playerData,
  refresh: playerRefresh,
} = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))
const isDeveloper = ref(false)

const isStatusChanged = computed(() => {
  return isDeveloper.value !== playerData.value.model.isDeveloper
})

function resetModal () {
  isDeveloper.value = playerData.value.model.isDeveloper
}

async function updateDeveloperStatus () {
  const response = await gameServerApi.post(`/players/${playerData.value.id}/developerStatus?newStatus=${isDeveloper.value}`)
  showSuccessToast(`${playerData.value.model.playerName || 'n/a'} ${response.data.isDeveloper ? 'set as developer' : 'no longer set as developer'}.`)
  playerRefresh()
}
</script>
