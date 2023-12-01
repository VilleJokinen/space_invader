<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- An element for use in the game config select component to show details about a given game config. -->

<template lang="pug">
div(style="font-size: 0.95rem;")
  div.font-weight-bold {{ localizationFromId?.name ?? 'No name available' }}
  div.small.font-mono {{ localizationId }}
  div.small(v-if="localizationFromId") Built #[meta-time(:date="localizationFromId?.persistedAt")]
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { useSubscription } from '@metaplay/subscriptions'

import { getAllLocalizationsSubscriptionOptions } from '../../subscription_options/localization'
import type { LocalizationInfo } from '../../localizationServerTypes'

const props = defineProps<{
  /**
   * Id of the game config to show in the card.
   */
  localizationId: string
}>()

const {
  data: allGameConfigsData,
} = useSubscription<LocalizationInfo[]>(getAllLocalizationsSubscriptionOptions())

const localizationFromId = computed((): LocalizationInfo | undefined => {
  return (allGameConfigsData.value ?? []).find((config) => config.id === props.localizationId)
})
</script>
