<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- An element for use in the game config select component to show details about a given game config. -->

<template lang="pug">
div(style="font-size: 0.95rem;")
  div.font-weight-bold {{ gameConfigFromId?.name ?? 'No name available' }}
  div.small.font-mono {{ gameConfigId }}
  div.small(v-if="gameConfigFromId") Built #[meta-time(:date="gameConfigFromId?.persistedAt")]
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { useSubscription } from '@metaplay/subscriptions'

import { getAllGameConfigsSubscriptionOptions } from '../../subscription_options/gameConfigs'
import type { StaticGameConfigInfo } from '../../gameConfigServerTypes'

const props = defineProps<{
  /**
   * Id of the game config to show in the card.
   */
  gameConfigId: string
}>()

const {
  data: allGameConfigsData,
} = useSubscription<StaticGameConfigInfo[]>(getAllGameConfigsSubscriptionOptions())

const gameConfigFromId = computed((): StaticGameConfigInfo | undefined => {
  return (allGameConfigsData.value ?? []).find((config) => config.id === props.gameConfigId)
})
</script>
