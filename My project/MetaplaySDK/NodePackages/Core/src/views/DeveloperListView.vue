<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!developerPlayersData"
  permission="api.players.view"
  :meta-api-error="developerPlayersError"
  )
  template(#overview)
    meta-page-header-card(data-cy="matchmakers-overview-card")
      template(#title) Developer Players
      p Players who have a special developer status
      p.small.text-muted These players can be online during maintenance breaks and make it easier for you to test development-only features in production environments.

      b-alert.mt-5(show variant="secondary" v-if="developerPlayersData?.length === 0")
        p.font-weight-bolder.mt-2.mb-0 No developer players
        p You can find out how to mark a player as a developer from #[b-link(href="https://docs.metaplay.io/feature-cookbooks/working-with-developer-players#mark-a-player-as-a-developer" target="_blank" rel="noopener noreferrer") our docs]!
  //- MetaListCard for Developer Players
  template(#center-column)
    core-ui-placement(:placement-id="'Developers/List'" always-full-width)

  template(#default)
    meta-raw-data(:kvPair="developerPlayersData" name="activePlayers")
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'
import { getDeveloperPlayersSubscriptionOptions } from '../subscription_options/players'
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'

interface DeveloperPlayerInfo {
  id: string
  name: string
  lastLoginAt: string
}

/**
 * Subscribe to developer players data.
 */
const {
  data: developerPlayersData,
  error: developerPlayersError
} = useSubscription<DeveloperPlayerInfo[]>(getDeveloperPlayersSubscriptionOptions())

</script>
