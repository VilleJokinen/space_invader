<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A wrapper component for displaying a player's guild membership. -->

<template lang="pug">
div(v-if="coreStore.hello.featureFlags.guilds")
  span(v-if="playerData.guild !== null") #[router-link(:to="`/guilds/${playerData.guild.id}`") {{ playerData.guild.displayName }}] #[small.text-muted ({{ guildRoleDisplayString(playerData.guild.role) }})]
  span(v-else).text-muted Not in a guild
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'

import { useCoreStore } from '../../coreStore'
import { guildRoleDisplayString } from '../../coreUtils'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

const props = defineProps<{
  /**
   * Id of the player displayed on the overview card.
   */
  playerId: string
}>()

const coreStore = useCoreStore()
const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))
</script>
