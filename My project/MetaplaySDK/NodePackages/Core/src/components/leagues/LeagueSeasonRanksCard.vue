<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="singleLeagueSeasonData")
  meta-list-card(
    title="All Ranks"
    :itemList="rankDisplayData"
    :searchFields="['name', 'description']"
    :sortOptions="sortOptions"
    :pageSize="10"
    data-cy="league-season-ranks-card"
    )
    template(#item-card="{ item: rank }")
      MListItem
        | {{ rank.name }}

        template(#top-right)
          span.text-right #[meta-abbreviate-number(:value="rank.totalParticipants")] #[meta-plural-label(:value="rank.totalParticipants" label="participant" hideCount)]

        template(#bottom-left)
          div {{ rank.description }}
          div(v-if="rank.historicalData") #[meta-abbreviate-number(:value="rank.historicalData.numPromotions")] #[meta-plural-label(:value="rank.historicalData.numPromotions" label="promotion" hideCount)] | #[meta-abbreviate-number(:value="rank.historicalData.numDemotions")] #[meta-plural-label(:value="rank.historicalData.numDemotions" label="demotion" hideCount)] | #[meta-abbreviate-number(:value="rank.historicalData.numDropped")] dropped
          div(v-else) Statistics not available yet.

        template(#bottom-right)
          meta-action-modal-button.text-right(
            v-if="rank.totalDivisions > 0"
            :id="'view-division-modal' + rank.id"
            :action-button-text="maybePlural(rank.totalDivisions, 'division')"
            block
            link
            no-safety-lock
            modal-title="View Division"
            modal-size="md"
            ok-button-text="View Division"
            :onShow="() => { selectedRank = rank.id; selectedDivision = undefined }"
            :onOk="async () => { if (selectedRank !== undefined && selectedDivision !== undefined) await viewDivision(selectedRank, selectedDivision) }"
            :ok-button-disabled="!selectedDivisionValid"
            :disabled="divisionsInCurrentlySelectedRank === 0"
            )
            div Select a division to view from #[strong {{ rank.name }}] of #[strong {{ singleLeagueSeasonData.displayName }}]
              div.font-weight-bold.mt-2 Division Index
              meta-input-number(
                v-model="selectedDivision"
                placeholder="Enter division index..."
                :min="0"
                :max="divisionsInCurrentlySelectedRank - 1"
                integer
                )
              small(v-if="divisionsInCurrentlySelectedRank === 1").text-muted There is only one division available in this rank, with the index 0.
              small(v-else).text-muted There are a total of {{ divisionsInCurrentlySelectedRank }} divisions available, indexed from 0 to {{ divisionsInCurrentlySelectedRank - 1 }}.
          MTooltip(v-else content="There are no divisions available to view for this rank.") View division
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import { useGameServerApi } from '@metaplay/game-server-api'

import { MetaListSortDirection, MetaListSortOption, maybePlural } from '@metaplay/meta-ui'
import { MTooltip, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getSingleLeagueSeasonSubscriptionOptions } from '../../subscription_options/leagues'

const props = defineProps<{
  leagueId: string
  seasonId: number
}>()

const router = useRouter()

const gameServerApi = useGameServerApi()

// Data sources -------------------------------------------------------------------------------------------------------

/**
 * Fetch league data.
 */
const {
  data: singleLeagueSeasonData
} = useSubscription(getSingleLeagueSeasonSubscriptionOptions(props.leagueId, props.seasonId))

interface RankDisplayData {
  name: string
  description: string
  id: number
  totalParticipants: number
  totalDivisions: number
  // For seasons that have ended, we have extra data available.
  historicalData?: {
    numPromotions: number
    numDemotions: number
    numDropped: number
  }
}

/**
 * Converts raw league data into `RankDisplayData` for the selected league and season.
 */
const rankDisplayData = computed((): RankDisplayData[] => {
  const ranks = singleLeagueSeasonData.value.ranks
  return ranks.map((rank: any, index: number): RankDisplayData => {
    const displayData: RankDisplayData = {
      name: rank.rankName,
      description: rank.description,
      id: index,
      totalParticipants: rank.totalParticipants,
      totalDivisions: rank.numDivisions,
    }
    if (!singleLeagueSeasonData.value.isCurrent) {
      displayData.historicalData = {
        numPromotions: rank.numPromotions,
        numDemotions: rank.numDemotions,
        numDropped: rank.numDropped,
      }
    }
    return displayData
  })
})

/**
 * Sorting for the MetaListCard.
 */
const sortOptions = [
  new MetaListSortOption('Rank', 'id', MetaListSortDirection.Descending),
  new MetaListSortOption('Rank', 'id', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'name', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'name', MetaListSortDirection.Descending),
  new MetaListSortOption('Participants', 'totalParticipants', MetaListSortDirection.Ascending),
  new MetaListSortOption('Participants', 'totalParticipants', MetaListSortDirection.Descending)
]

// Modal control ------------------------------------------------------------------------------------------------------

/**
 * Currently selected rank form the list of ranks.
 */
const selectedRank = ref<number>()

/**
 * Helper function to return the number of available divisions in a rank.
 */
const divisionsInCurrentlySelectedRank = computed(() => {
  return rankDisplayData.value[selectedRank.value ?? 0].totalDivisions
})

/**
 * Currently selected division in the modal.
 */
const selectedDivision = ref<number>()

/**
 * Check if input is valid for view division button to activate
 */
const selectedDivisionValid = computed<boolean>(() => {
  return selectedDivision.value !== undefined
})

/**
 * User has selected a division on the modal and clicked on OK.
 */
const viewDivision = async (rank: number, division: number) => {
  const result = await gameServerApi.get(`/divisions/id/${props.leagueId}/${props.seasonId}/${rank}/${division}/`)
  const divisionEntityId = result.data
  const url = `/leagues/${props.leagueId}/${props.seasonId}/${divisionEntityId}`
  await router.push(url)
}

</script>
