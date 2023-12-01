<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  :title="title"
  icon="user"
  :emptyMessage="emptyMessage"
  :searchFields="['name', 'id']"
  :itemList="playersWithInfo"
  )
  template(#item-card="{ item: player }")
    MListItem
      span.font-weight-bold {{ player.name }}
      template(#top-right) Joined #[meta-time(:date="player.createdAt || ''" showAs="date")]
      template(#bottom-left) Level {{ player.level }}
      template(#bottom-right): router-link(:to="`/players/${player.id}`") View player
</template>

<script lang="ts" setup>
import { isEqual } from 'lodash-es'
import { onMounted, ref, watch } from 'vue'
import { useGameServerApi } from '@metaplay/game-server-api'
import { MListItem } from '@metaplay/meta-ui-next'

const props = withDefaults(defineProps<{
  /**
   * The list of target players' IDs.
   */
  playerIds: string[]
  /**
   * Optional: The title of this card. Defaults to "Players".
   */
  title?: string
  /**
   * Optional: The message to be displayed when the list is empty.
   */
  emptyMessage?: string
}>(), {
  title: 'Players',
  emptyMessage: 'No players.'
})

/**
 * The list of target players' data to be displayed on this card.
 */
const playersWithInfo = ref<any[]>()

/**
 * Watch `playerIds` prop and fetch target player(s) data from the game server when it changes.
 */
watch(() => props.playerIds, async (newValue, oldValue) => {
  if (!isEqual(newValue, oldValue)) {
    await fetchPlayerInfos()
  }
},
{ deep: true })

const gameServerApi = useGameServerApi()

/**
 * Fetches target player(s) data from the game server.
 */
async function fetchPlayerInfos () {
  const response = await gameServerApi.post('players/bulkValidate', { PlayerIds: props.playerIds })
  playersWithInfo.value = response.data
    .filter((player: any) => player.validId)
    .map((player: any) => {
      return {
        id: player.playerData.id,
        name: player.playerData.name,
        level: player.playerData.level,
        createdAt: player.playerData.createdAt
      }
    })
}

onMounted(async () => await fetchPlayerInfos())
</script>
