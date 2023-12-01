<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  title="All Seasons"
  :item-list="allSeasonsDisplayData"
  :searchFields="['name']"
  :sort-options="sortOptions"
  emptyMessage="No seasons are available to view.",
  :filterSets="filterSets"
  data-cy="league-seasons-list-card"
  )
  template(#item-card="{ item: season }")
    MListItem {{ season.name }}
      template(#badge v-if="season.currentSeason && singleLeagueData.enabled")
        MBadge(v-if="singleLeagueData.enabled" :variant="getPhaseVariant(singleLeagueData.currentOrNextSeasonSchedule?.currentPhase.phase)") {{ schedulePhaseDisplayString(singleLeagueData.currentOrNextSeasonSchedule?.currentPhase.phase) }}
        MBadge(v-else variant="danger") Disabled
      template(#top-right)
        MBadge(v-if="season.startedEarly && !season.endedEarly" variant="warning") Started Early
        MBadge(v-else-if="!season.startedEarly && season.endedEarly" variant="warning") Ended Early
        MBadge(v-else-if="season.startedEarly && season.endedEarly" variant="warning") Started & Ended Early
        MBadge(v-else variant="success") On Schedule
      template(#bottom-left)
        span(v-if="season.endTime.diffNow().toMillis() > 0 && singleLeagueData.schedule") {{ season.startTime > DateTime.now() ? 'Starting' : 'Started' }} #[meta-time(:date="season.startTime")] and will end in #[meta-duration(:duration="season.endTime.diffNow()" showAs='exactDuration' :hideMilliseconds="true")].
        span(v-else) Ended #[meta-time(:date="season.endTime" showAs="timeago")] after running for #[meta-duration(:duration="season.endTime.diff(season.startTime)" showAs='exactDuration' :hideMilliseconds="true")].
      template(#bottom-right)
        div.text-right {{ season.totalParticipants }} participants
        div.text-right: meta-button(link :to="`/leagues/${leagueId}/${season.id}`" permission="api.leagues.view") View season
</template>

<script lang="ts" setup>
import { DateTime } from 'luxon'
import { computed } from 'vue'

import { MetaListSortDirection, MetaListSortOption, MetaListFilterSet, MetaListFilterOption } from '@metaplay/meta-ui'
import { MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { schedulePhaseDisplayString } from '../../coreUtils'
import { getPhaseVariant } from '../../leagueUtils'
import { getSingleLeagueSubscriptionOptions } from '../../subscription_options/leagues'

const props = defineProps<{
  leagueId: string
}>()

const { data: singleLeagueData } = useSubscription(getSingleLeagueSubscriptionOptions(props.leagueId))

/**
 * Interface defining data structure for All Seasons MetaListCard's that takes common properties from current and historic seasons.
 */
interface SeasonDisplayData {
  name: string
  id: number
  endTime: DateTime
  startTime: DateTime
  totalParticipants: number
  startedEarly: boolean
  endedEarly: boolean
  onSchedule: boolean
  currentSeason?: boolean
}

/**
 * Transforms both past seasons and the current season(s) data into single formatted list for MetaListCard.
 */
const allSeasonsDisplayData = computed((): SeasonDisplayData[] => {
  const allSeasons = []
  if (singleLeagueData.value.state.historicSeasons) {
    // Historic seasons data.
    const mappedHistoricSeasonsData = singleLeagueData.value.state.historicSeasons.map((pastSeason: any): SeasonDisplayData => ({
      name: 'Season ' + pastSeason.seasonId,
      id: pastSeason.seasonId,
      endTime: DateTime.fromISO(pastSeason.endTime),
      startTime: DateTime.fromISO(pastSeason.startTime),
      totalParticipants: pastSeason.totalParticipants,
      startedEarly: pastSeason.startedEarly,
      endedEarly: pastSeason.endedEarly,
      onSchedule: !pastSeason.startedEarly && !pastSeason.endedEarly
    }))

    allSeasons.push(...mappedHistoricSeasonsData)
  }

  if (singleLeagueData.value.state.currentSeason) {
    // Current season data.
    const currentSeasonRawData = singleLeagueData.value.state.currentSeason
    const mappedCurrentSeasonData = {
      name: 'Season ' + currentSeasonRawData.seasonId,
      id: currentSeasonRawData.seasonId,
      endTime: DateTime.fromISO(currentSeasonRawData.endTime),
      startTime: DateTime.fromISO(currentSeasonRawData.startTime),
      totalParticipants: singleLeagueData.value.currentParticipantCount,
      startedEarly: currentSeasonRawData.startedEarly,
      endedEarly: currentSeasonRawData.endedEarly,
      onSchedule: !currentSeasonRawData.startedEarly && !currentSeasonRawData.endedEarly,
      currentSeason: true
    }
    allSeasons.push(mappedCurrentSeasonData)
  }
  return allSeasons
})

const sortOptions = [
  new MetaListSortOption('Start time', 'startTime', MetaListSortDirection.Descending),
  new MetaListSortOption('Start time', 'startTime', MetaListSortDirection.Ascending),
  new MetaListSortOption('Participants', 'totalParticipants', MetaListSortDirection.Descending),
  new MetaListSortOption('Participants', 'totalParticipants', MetaListSortDirection.Ascending),
]
const filterSets = [
  new MetaListFilterSet('startedEarly',
    [
      new MetaListFilterOption('Started early', (season: any) => season.startedEarly, false),
      new MetaListFilterOption('Ended early', (season: any) => season.endedEarly, false),
      new MetaListFilterOption('On schedule', (season: any) => season.onSchedule, false),
    ]),
]

</script>
