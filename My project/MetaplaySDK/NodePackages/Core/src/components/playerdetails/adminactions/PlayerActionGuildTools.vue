<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="coreStore.hello.featureFlags.guilds && playerData")
  meta-action-modal-button(
    id="action-view-guild-recommendations"
    permission="api.players.guild_tools"
    action-button-text="Guild Tools"
    action-button-icon="chess-rook"
    block
    :modal-title="`Guild Tools for ${playerData.model.playerName || 'n/a'}`"
    modal-size="lg"
    @show="resetModal"
    only-close
    :on-ok="async () => {}"
    )
      b-row
        b-col(md="6")
          h6 Guild Recommendations
          p An example list of guilds that could be recommended for #[MBadge {{ playerData.model.playerName || 'n/a' }}].
          p.small.text-muted Note: This is not the exact list shown in the game at the moment, but rather an example of what recommendations the current game logic produces.

          div.mt-3.mb-2
            b-row(v-if="recommendations == null" align-h="center")
              b-spinner.mt-5.mb-5(label="Loading...")/
            //- Styling below to make this look like old b-list/group. Smaller font in everything compared to old.
            MList(v-else-if="recommendations && recommendations.length > 0" showBorder)
              MListItem(
                v-for="recommendation in recommendations"
                :key="recommendation.guildId"
                class="tw-px-5"
                striped
                )
                span {{ recommendation.displayName }}
                template(#top-right)
                  span #[router-link(:to="`/guilds/${recommendation.guildId}`") {{ recommendation.guildId }}]
                template(#bottom-left)
                  span {{ recommendation.numMembers }} / {{ recommendation.maxNumMembers }} members
            //- TODO R26 Add v-else for MList
            //- div(v-else class="tw-italic tw-text-red-500") No guilds available

            b-row(no-gutters align-h="end").mt-2
              b-button(variant="primary" size="sm" @click="refresh")
                fa-icon(icon="sync-alt").mr-2
                | Refresh

        b-col(md="6")
          h6 Guild Search
          p Perform a guild search as #[MBadge {{ playerData.model.playerName || 'n/a' }}] would see it.
          span.small.text-muted You can use this tool to preview how particular players are shown guild search results in the game.

          div.mt-3.mb-2
            b-form(@submit.prevent).mt-1
              //- TODO: Consider moving this to an automatic search without a button?
              meta-generated-form(
                typeName="Metaplay.Core.Guild.GuildSearchParamsBase"
                v-model="searchContents"
                @status="searchContentsValid = $event"
                )
            b-row(no-gutters).mt-2
              b-button(type="button" variant="primary" @click="search" :disabled="!searchContentsValid" size="sm").text-white.mb-2.ml-2 Search

            // TODO: error handling!
            b-row(no-gutters align-h="center" v-if="searchResults == null")
              b-spinner.mt-5.mb-5(label="Loading...")/
            p(v-if="searchResults !== null && searchResults.isError === true") Search failed
            div(v-if="searchResults !== null && searchResults.isError === false")
              // TODO: review layout
              p #[meta-plural-label(:value="searchResults.guildInfos.length" label="result")].
              // b-table(striped responsive :items="searchResults.guildInfos")

              MList(v-if="searchResults.guildInfos && searchResults.guildInfos.length > 0" showBorder)
                MListItem(
                  v-for="guildInfo in searchResults.guildInfos"
                  :key="guildInfo.guildId"
                  class="tw-px-5"
                  striped
                  )
                  span {{ guildInfo.displayName }}
                  template(#top-right)
                    span #[router-link(:to="`/guilds/${guildInfo.guildId}`") {{ guildInfo.guildId }}]
                  template(#bottom-left)
                    span {{ guildInfo.numMembers }} / {{ guildInfo.maxNumMembers }} members
</template>

<script lang="ts" setup>
import { ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { MBadge, MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { useCoreStore } from '../../../coreStore'
import { getSinglePlayerSubscriptionOptions } from '../../../subscription_options/players'

import MetaGeneratedForm from '../../generatedui/components/MetaGeneratedForm.vue'

const props = defineProps<{
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))
const coreStore = useCoreStore()
const recommendations = ref()
const searchContents = ref<any>({})
const searchContentsValid = ref(false)
const searchResults = ref<any>([])

async function resetModal () {
  searchContents.value = {}
  searchContentsValid.value = false
  searchResults.value = []
  await refresh()
}

async function search (event: any) {
  event.preventDefault()
  searchResults.value = null
  const res = (await gameServerApi.post(`/players/${playerData.value.id}/guildSearch`, searchContents.value))
  searchResults.value = res.data
}

async function refresh () {
  recommendations.value = null
  const res = (await gameServerApi.post(`/players/${playerData.value.id}/guildRecommendations`))
  recommendations.value = res.data
}
</script>
