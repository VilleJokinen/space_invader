<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!allMatchmakersData"
  :meta-api-error="allMatchmakersError"
)
  template(#overview)
    meta-page-header-card(data-cy="matchmakers-overview-card")
      template(#title) Matchmaking
      p Matchmakers help players find the best match for themselves for multiplayer game modes.
      p.small.text-muted You can have multiple unique matchmakers in the same game, each with different settings.

  template(#default)
    core-ui-placement(:placement-id="'Matchmakers/List'")

    meta-raw-data(:kvPair="allMatchmakersData", name="allMatchmakersData")
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'
import { getAllMatchmakersSubscriptionOptions } from '../subscription_options/matchmaking'

import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'

/**
 * Subscribe to all matchmakers data and error.
 */
const {
  data: allMatchmakersData,
  error: allMatchmakersError
} = useSubscription(getAllMatchmakersSubscriptionOptions())

</script>
