<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!detailEntries"
)
  template(#overview)
    meta-page-header-card(data-cy="overview-card" :id="playerId")
      template(#title) Raw Player Details
      p.mb-2 There were a total of {{ errorCount }} errors encountered when trying to retrieve this player's details.
      p.small.text-muted.mb-0 This page allows you to inspect a player's details even when the server is unable to correctly deserialize the player's data.

  template(#default)
    b-card(v-for="entry in detailEntries" :key="entry.title" no-body).mb-3
      b-card-header(v-b-toggle:[entry.title] :data-cy="`detail-card-${entry.title}`").bg-card-gradient-meta-white.font-weight-bold
        fa-icon(icon="angle-right").mr-2.angle-icon
        span.mr-1 {{ entry.title }}
        MBadge(v-if="entry.data" variant="primary" shape="pill") Valid
        MBadge(v-else variant="danger" shape="pill") Not valid
      b-collapse(:id="entry.title").p-3
        div(v-if="entry.data")
          pre.small.mb-0 {{ entry.data }}
        div(v-else)
          b-alert(variant="danger" show).mb-0
            div {{ entry.error.title }}
            pre.small(v-if="entry.error.details").mt-2 {{ entry.error.details }}

    meta-raw-data(:kvPair="detailEntries", name="detailEntries")
</template>

<script lang="ts" setup>
import { computed, onMounted, ref } from 'vue'

import { MBadge, MCollapseCard } from '@metaplay/meta-ui-next'
import { useGameServerApi } from '@metaplay/game-server-api'

import { useRoute } from 'vue-router'
import { routeParamToSingleValue } from '../coreUtils'

const detailEntries = ref<any>()

const gameServerApi = useGameServerApi()
const route = useRoute()
const playerId = computed(() => routeParamToSingleValue(route.params.id))

onMounted(async () => {
  const response = await gameServerApi.get(`/players/${playerId.value}/raw`)
  detailEntries.value = Object.values(response.data)
})

const errorCount = computed(() => detailEntries.value.reduce((p: number, c: any) => p + (c.error ? 1 : 0), 0))
</script>
