<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A wrapper component for displaying customizable resources lists. -->

<template lang="pug">
div(v-if="playerData")
  span.font-weight-bold #[fa-icon(icon="coins")] Resources
  b-table-simple(small responsive).mt-1
    b-tbody
      b-tr(v-for="resource in coreStore.gameSpecific.playerResources" :key="resource.displayName")
        b-td {{ resource.displayName }}
        b-td.text-right
          span(v-if="!!resource.getAmount(playerData.model) || typeof resource.getAmount(playerData.model) === 'number'") {{ resource.getAmount(playerData.model) }}
          MBadge(v-else variant="danger") not found
</template>

<script lang="ts" setup>
import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { useCoreStore } from '../../coreStore'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

const props = defineProps<{
  /**
   * Id of the player whose resources are shown on the list.
   */
  playerId: string
}>()

const coreStore = useCoreStore()
const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))
</script>
