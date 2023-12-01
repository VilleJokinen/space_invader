<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!singleGuildData"
  :meta-api-error="singleGuildError"
  :alerts="alerts"
  )
  template(#error-card-message)
    p Oh no, something went wrong while trying to access the guild!
    p Use #[meta-button(link data-cy="model-size-link" permission="api.database.inspect_entity" :to="`/entities/${guildId}/dbinfo`") save file inspector] to inspect entity data saved on the database.

  template(#overview)
    guild-overview-card(:guildId="guildId")

  template(#default)
    b-row(align-h="center")
      b-col(lg="12" xl="8").mb-3
        guild-admin-actions-card(:guildId="guildId")

    b-row.mt-3
      b-col.mb-2
        h3 Game State

    core-ui-placement(placementId="Guilds/Details/GameState" :guildId="guildId")

    b-row.mt-3
      b-col.mb-2
        h3 Guild & Admin Logs

    core-ui-placement(placementId="Guilds/Details/GuildAdminLogs" :guildId="guildId")

    //- Guild pretty print
    meta-raw-data(:kvPair="singleGuildData", name="guild")/
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import type { MetaPageContainerAlert } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'
import { getSingleGuildSubscriptionOptions } from '../subscription_options/guilds'

import GuildAdminActionsCard from '../components/guilds/GuildAdminActionsCard.vue'
import GuildOverviewCard from '../components/guilds/GuildOverviewCard.vue'
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import { routeParamToSingleValue } from '../coreUtils'
import useHeaderbar from '../useHeaderbar'

const route = useRoute()
const guildId = computed(() => routeParamToSingleValue(route.params.id))

const {
  data: singleGuildData,
  error: singleGuildError
} = useSubscription(getSingleGuildSubscriptionOptions(guildId.value))

// Update the headerbar title dynamically as data changes.
useHeaderbar().setDynamicTitle(singleGuildData, (singleGuildData) => `Manage ${singleGuildData.value?.model.displayName || 'Guild'}`)

const alerts = computed(() => {
  const allAlerts: MetaPageContainerAlert[] = []

  if (singleGuildData.value?.model.lifecyclePhase === 'Closed') {
    allAlerts.push({
      title: '☠️ Guild Closed',
      message: 'This guild has no players and has been closed.',
      variant: 'danger',
      dataCy: 'guild-closed-alert'
    })
  } else if (singleGuildData.value?.model.lifecyclePhase === 'WaitingForSetup' || singleGuildData.value?.model.lifecyclePhase === 'WaitingForLeader') {
    allAlerts.push({
      title: 'Guild-in-Progress',
      message: 'All our actors are busy creating this guild and will be with you momentarily, please hold.',
      variant: 'warning',
      dataCy: 'guild-in-progress-alert'
    })
  }

  return allAlerts
})
</script>
