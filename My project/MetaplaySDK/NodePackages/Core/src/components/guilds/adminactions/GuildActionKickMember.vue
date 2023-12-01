<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-action-modal-button(
  id="action-kick-member"
  variant="warning"
  :disabled="!hasMembers"
  action-button-text="Kick a Member"
  modal-title="Kick a Player From the Guild"
  :onOk="kickMember"
  @show="resetModal"
  :ok-button-disabled="!chosenPlayer"
  :ok-button-text="playerOptions?.length > 1 ? 'Kick Player' : 'Kick and Close Guild'"
  permission="api.guilds.kick_member"
  block
  )
  div(v-if="playerOptions")
    p(v-if="playerOptions.length >= 2"): i Note: Kicking all {{ playerOptions.length }} members from #[MBadge {{ guildData.model.displayName }}] will also automatically close it.
    p(v-else).text-danger: i Note: #[MBadge(variant="danger") {{ lastPlayerName }}] is the last player in #[MBadge {{ guildData.model.displayName }}] and kicking them will also close the guild.

    div.mb-2
      span.font-weight-bold Select a Guild Member
    meta-input-select(
      :value="chosenPlayer"
      @input="chosenPlayer = $event"
      :options="playerOptions"
      placeholder="Select a player..."
      :searchFields="['playerId', 'displayName']"
      )
      template(#option="{ option }")
        MListItem(class="!tw-py-0")
          span(class="tw-text-sm") {{ option?.displayName }}
          template(#top-right) {{ option?.playerId }}

    meta-no-seatbelts.mt-3(v-if="chosenPlayer" :name="chosenPlayer.displayName")/
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getSingleGuildSubscriptionOptions } from '../../../subscription_options/guilds'
import type { MetaInputSelectOption } from '@metaplay/meta-ui'

const props = defineProps<{
  guildId: string
}>()

const gameServerApi = useGameServerApi()

/**
 * Subscribe to guild data.
 */
const {
  data: guildData,
  refresh: guildTriggerRefresh,
} = useSubscription(getSingleGuildSubscriptionOptions(props.guildId))
const hasMembers = computed(() => {
  return Object.keys(guildData.value.model.members).length !== 0
})

interface PlayerInfo {
  playerId: string
  displayName: string
}

/**
 * List of players to be displayed as options on the multiselect dropdown.
 */
const playerOptions = ref<Array<MetaInputSelectOption<PlayerInfo>>>([])

/**
 * The selected player.
 */
const chosenPlayer = ref<PlayerInfo>()

/**
 * Reset the modal.
 */
function resetModal () {
  chosenPlayer.value = undefined
  // Re-populate player options list on modal show as it might change based on subscriptions refresh, but live-refreshing messes up the select component.
  const newPlayerOptions: Array<MetaInputSelectOption<PlayerInfo>> = []
  for (const playerId in guildData.value.model.members) {
    const displayName = guildData.value.model.members[playerId].displayName
    newPlayerOptions.push({
      id: playerId,
      value: {
        playerId,
        displayName,
      }
    })
  }
  playerOptions.value = newPlayerOptions
}

/**
 * Last player name to be shown when only one memeber is left in the guild.
 */
const lastPlayerName = computed(() => playerOptions.value[0]?.value.displayName)

/**
 * Kick the chosen member out and update the game server.
 */
async function kickMember () {
  await gameServerApi.post(`/guilds/${props.guildId}/kickMember`, { playerId: chosenPlayer.value?.playerId })
  showSuccessToast(`${chosenPlayer.value?.displayName} kicked from ${guildData.value.model.displayName}.`)
  guildTriggerRefresh()
}
</script>
