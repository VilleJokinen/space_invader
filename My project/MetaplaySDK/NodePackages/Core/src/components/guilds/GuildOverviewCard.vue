<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Player card (anything important enough to be visible at first glance should go here)
div#guild-overview-card(v-if="guildData")
  meta-page-header-card(:id="guildData.model.guildId")
    template(#title) {{ guildData.model.displayName || '☠️ Closed' }}
    template(#subtitle) {{ guildData.model.description }}
    template(#caption)
      | Save file size:
      |
      meta-button(link data-cy="model-size-link" permission="api.database.inspect_entity" :to="`/entities/${guildData.model.guildId}/dbinfo`")
        meta-abbreviate-number(:value="guildData.persistedSize" unit="byte")

    overview-list(listTitle="Overview" icon="chart-bar" :sourceObject="guildData" :items="coreStore.overviewLists.guild")

b-card.shadow-sm(v-else title="Guild Overview")
  b-alert(show variant="danger") Could not render this component because data is missing!
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'

import { getSingleGuildSubscriptionOptions } from '../../subscription_options/guilds'
import { useCoreStore } from '../../coreStore'
import OverviewList from '../global/OverviewList.vue'

const coreStore = useCoreStore()

const props = defineProps<{
  guildId: string
}>()

const {
  data: guildData,
} = useSubscription(getSingleGuildSubscriptionOptions(props.guildId))
</script>
