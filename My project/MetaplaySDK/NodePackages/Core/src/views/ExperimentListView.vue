<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!allExperimentsData"
  :alerts="headerAlerts"
  )
  template(#overview)
    meta-page-header-card
      template(#title) View Experiments
      p Experiments allow you to test out variants of your game config on your players.
      p.small.text-muted Experiments deliver variations of your game configs to a subset of your player base. You can then use your analytics to discover which variant performs the best. Define experiments and their variants in your #[b-link(to="/gameConfigs") game configs] and then configure and administer them from here.

      div.font-weight-bold Performance Tip
      p.small.mb-0 Rolling out experiments introduces new combinations of your game configs that require memory on the game servers. You currently have #[meta-plural-label(:value="runningExperimentInfos.length" label='experiment')] running with a total of #[meta-plural-label(:value="allExperimentsData.combinations.currentCombinations" label="possible combination")]. Have a look at the #[b-link(href="https://docs.metaplay.io/feature-cookbooks/experiments-a-b-testing/working-with-experiments#technical-details-of-specialized-game-config-delivery") experiments documentation] to learn more.

  template(#center-column)
    meta-list-card(
      data-cy="all-experiments"
      title="All Experiments"
      :itemList="decoratedExperimentInfos"
      :searchFields="['experimentId', 'displayName', 'description']"
      :sortOptions="sortOptions"
      :filterSets="filterSets"
      :defaultSortOption="1"
      :pageSize="10"
      emptyMessage="No player experiments. Set them up in your game configs to start using the feature!"
    )
      template(#item-card="slot")
        MListItem
          span {{ slot.item.displayName }}
          template(#top-right)
            MBadge(:variant="slot.item.phaseInfo.titleVariant") {{ slot.item.phaseInfo.displayName }}
            span(v-if="slot.item.phaseStartedAt")  since #[meta-time(:date="slot.item.phaseStartedAt")]
          template(#bottom-left)
            span {{ slot.item.description }}
          template(#bottom-right)
            div(v-if="['Ongoing', 'Paused'].includes(slot.item.phase)").text-muted Total runtime: #[meta-duration(:duration="slot.item.totalTimeOngoing" showAs="humanizedSentenceCase")]
            div(v-else) Not started yet
            div.text-muted #[meta-abbreviate-number(:value="slot.item.totalPlayerCount")] #[meta-plural-label(:value="slot.item.totalPlayerCount" label="participant" hideCount)]
            b-link(:to="`/experiments/${slot.item.experimentId}`" data-cy="view-experiment") View experiment

  template(#default)
    meta-raw-data(:kvPair="experimentInfos" name="experimentInfos")
</template>

<script lang="ts" setup>
import { DateTime, Duration } from 'luxon'
import { computed } from 'vue'

import { MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption, metaJsonDurationToLuxonDuration } from '@metaplay/meta-ui'
import { MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getAllExperimentsSubscriptionOptions } from '../subscription_options/experiments'

// Get the data.
const {
  data: allExperimentsData,
  error: allExperimentsError
} = useSubscription(getAllExperimentsSubscriptionOptions())

// Massage the data.
const experimentInfos = computed(() => allExperimentsData.value?.experiments)
const decoratedExperimentInfos = computed(() => {
  return experimentInfos.value.map((experimentInfo: any) =>
    ({
      ...experimentInfo,
      phaseInfo: getExperimentPhaseInfoById(experimentInfo.experimentId),
      totalTimeOngoing: getTotalTimeOngoingById(experimentInfo.experimentId)
    })
  )
})
const runningExperimentInfos = computed(() => allExperimentsData.value?.experiments.filter((x: any) => x.phase === 'Ongoing'))

// Alerts.
const headerAlerts = computed(() => {
  const alerts: Array<{
    title: string
    message: string
    dataCy?: string
    variant?: 'secondary' | 'warning' | 'info' | 'danger'
  }> = []

  // A lot of combinations.
  if (allExperimentsData.value?.combinations.exceedsThreshold === true) {
    alerts.push({
      title: 'High number of combinations',
      variant: 'warning',
      message: `You currently have ${runningExperimentInfos.value.length} experiments running with a total of ${allExperimentsData.value.combinations.currentCombinations} possible combinations. This may cause a high memory use on your game servers. Consider pausing or concluding some experiments.`
    })
  }

  return alerts
})

// Utility functions.
function getExperimentPhaseInfoById (experimentId: string) {
  const phaseInfos = [
    {
      id: 'Testing',
      displayName: 'Testing',
      titleVariant: 'primary',
      sortOrder: 1
    },
    {
      id: 'Ongoing',
      displayName: 'Active',
      titleVariant: 'success',
      sortOrder: 2
    },
    {
      id: 'Paused',
      displayName: 'Paused',
      titleVariant: 'warning',
      sortOrder: 3
    },
    {
      id: 'Concluded',
      displayName: 'Concluded',
      titleVariant: 'neutral',
      sortOrder: 4
    }
  ]
  const phase = String(getExperimentInfoById(experimentId).phase)
  const phaseTitle = phaseInfos.find(phaseInfo => phaseInfo.id === phase)
  if (!phaseTitle) {
    throw new Error(`Unknown phase: ${phase}`)
  }
  return phaseTitle
}

function getExperimentInfoById (experimentId: string) {
  return experimentInfos.value.find((x: any) => x.experimentId === experimentId)
}

function getTotalTimeOngoingById (experimentId: string) {
  const info = getExperimentInfoById(experimentId)
  let duration: Duration | null = null
  if (info.ongoingDurationBeforeCurrentSpan !== null) {
    duration = metaJsonDurationToLuxonDuration(info.ongoingDurationBeforeCurrentSpan)
  }
  if (info.phase === 'Ongoing') {
    duration = duration ?? Duration.fromMillis(0)
    duration = duration.plus(DateTime.now().diff(DateTime.fromISO(info.phaseStartedAt)))
  }
  return duration
}

// Sorting.
const sortOptions = [
  MetaListSortOption.asUnsorted(),
  new MetaListSortOption('Phase', 'phaseInfo.sortOrder', MetaListSortDirection.Ascending),
  new MetaListSortOption('Phase', 'phaseInfo.sortOrder', MetaListSortDirection.Descending),
  new MetaListSortOption('Name', 'experimentId', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'experimentId', MetaListSortDirection.Descending),
  new MetaListSortOption('Total player count', 'totalPlayerCount', MetaListSortDirection.Ascending),
  new MetaListSortOption('Total player count', 'totalPlayerCount', MetaListSortDirection.Descending),
]

// Filtering based on the possible experiment phases.
const filterSets = [
  new MetaListFilterSet('phase',
    [
      new MetaListFilterOption('Testing', (x: any) => x.phaseInfo.id === 'Testing', true),
      new MetaListFilterOption('Active', (x: any) => x.phaseInfo.id === 'Ongoing', true),
      new MetaListFilterOption('Paused', (x: any) => x.phaseInfo.id === 'Paused', true),
      new MetaListFilterOption('Concluded', (x: any) => x.phaseInfo.id === 'Concluded'),
    ]
  ),
]
</script>
