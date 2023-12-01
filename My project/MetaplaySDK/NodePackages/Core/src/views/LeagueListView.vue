<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!allLeaguesData"
  :meta-api-error="allLeaguesError"
  )
  template(#alerts)
    meta-alert(
      v-if="allLeaguesData.length === 0"
      title='No Leagues Configured'
      variant="secondary"
      ) No leagues have been configured for this deployment. Set them up in your #[b-link(to="/runtimeOptions") runtime options].

  template(#overview)
    meta-page-header-card(data-cy="league-list-overview-card")
      template(#title) Leagues
      p Leagues are a season-based leaderboard system for competitive multiplayer.
      p.small.text-muted You can have multiple unique leagues in the same game, each with a different configuration.

  template(#center-column)
    core-ui-placement(:placement-id="'Leagues/List'" always-full-width)

  template(#default)
    meta-raw-data(:kvPair="allLeaguesData", name="allLeaguesData")
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'
import { getAllLeaguesSubscriptionOptions } from '../subscription_options/leagues'

import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'

/**
 * Subscribe to all leagues data.
 */
const {
  data: allLeaguesData,
  error: allLeaguesError
} = useSubscription(getAllLeaguesSubscriptionOptions())

</script>
