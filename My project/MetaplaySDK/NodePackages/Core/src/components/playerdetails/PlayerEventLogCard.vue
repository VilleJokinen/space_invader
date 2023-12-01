<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- TODO: loading state?
entity-event-log-card(
  v-if="playerData"
  entityKind="Player"
  :entityId="playerId"
  :entityResetTimestamp="playerData.model.stats.createdAt + '-' + playerData.model.stats.lastImportAt"
  )
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'

import EntityEventLogCard from '../entityeventlogs/EntityEventLogCard.vue'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

const props = defineProps<{
  playerId: string
}>()

const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))
</script>
