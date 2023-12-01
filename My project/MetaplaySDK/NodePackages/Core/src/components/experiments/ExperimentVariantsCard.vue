<template lang="pug">
meta-list-card.h-100(
  data-cy="variants"
  title="Variants"
  icon="table"
  tooltip="Variants configured for this experiment."
  :itemList="variants"
  :sortOptions="sortOptions"
  emptyMessage="No variants for this experiment!"
)
  template(#item-card="slot")
    MListItem
      span {{ slot.item.id }}
      MBadge.ml-1(v-if="slot.item.isConfigMissing"
        tooltip="This variant has been removed from the current game configs and can no longer be used."
        variant="danger"
        ) Missing
      MBadge.ml-1(v-if="slot.item.weight === 0"
        tooltip="This variant has a weight of 0% and no players can enter it."
        variant="warning"
        ) No weight
      template(#top-right)
        div.text-muted.small(v-if="slot.item.analyticsId") Analytics ID: {{ slot.item.analyticsId }}
        span.small(v-else) #[span.text-muted Analytics ID:] #[span.text-danger None]

      template(#bottom-left)
        div
          meta-abbreviate-number(:value="slot.item.playerCount", unit="player")
          |  currently in this group.

      template(#bottom-right)
        MTooltip(:content="`Approximately ${percentageWeightAsPlayerCount(slot.item.weight)} players`") {{ slot.item.weight }}% of total rollout
        div(v-if="slot.item.isConfigMissing || !experiment").text-danger Config diff missing
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MTooltip, MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getSingleExperimentSubscriptionOptions } from '../../subscription_options/experiments'
import { getDatabaseItemCountsSubscriptionOptions, getGameDataSubscriptionOptions } from '../../subscription_options/general'

const props = defineProps<{
  experimentId: string
}>()

const {
  data: experimentInfoData,
} = useSubscription(getSingleExperimentSubscriptionOptions(props.experimentId))

const {
  data: gameData,
} = useSubscription(getGameDataSubscriptionOptions())

const experiment = computed(() => gameData.value?.serverGameConfig.PlayerExperiments[props.experimentId])

interface Variant {
  id: string
  weight: number
  diffParams: any
  playerCount: number
  isConfigMissing: boolean
  analyticsId: string
}

const variants = computed((): Variant[] => {
  const variants: Variant[] = [{
    id: 'Control',
    weight: experimentInfoData.value.state.controlWeight,
    diffParams: null,
    playerCount: playerCountForGroup('null'),
    isConfigMissing: false,
    analyticsId: experiment.value?.controlVariantAnalyticsId
  }]

  Object.entries(experimentInfoData.value.state.variants).forEach(([id, variant]) => {
    variants.push({
      id,
      weight: (variant as any).weight,
      diffParams: `newPatch=${props.experimentId}:${id}`,
      playerCount: playerCountForGroup(id),
      isConfigMissing: (variant as any).isConfigMissing,
      analyticsId: experiment.value?.variants[id]?.analyticsId
    })
  })

  // Normalize weights
  const totalWeight = Object.values(variants).reduce((sum, variant) => sum + variant.weight, 0)
  Object.values(variants).forEach(variant => {
    variant.weight = Math.round(variant.weight / totalWeight * 100)
  })

  return variants
})

const {
  data: databaseItemCountsData
} = useSubscription(getDatabaseItemCountsSubscriptionOptions())
const totalPlayerCount = computed(() => databaseItemCountsData.value?.totalItemCounts.Players || 0)
function percentageWeightAsPlayerCount (percentageWeight: number) {
  if (totalPlayerCount.value > 0) {
    const audiencePlayerCount = experimentInfoData.value.state.rolloutRatioPermille / 1000 * totalPlayerCount.value
    const playerCount = percentageWeight / 100 * audiencePlayerCount
    return Math.floor(playerCount)
  } else {
    return 0
  }
}

function playerCountForGroup (group: string | null) {
  if (group) {
    return experimentInfoData.value.stats.variants[group]?.numPlayersInVariant ?? 0
  } else {
    return 0
  }
}

const sortOptions = [
  MetaListSortOption.asUnsorted(),
  new MetaListSortOption('ID', 'id', MetaListSortDirection.Ascending),
  new MetaListSortOption('ID', 'id', MetaListSortDirection.Descending),
  new MetaListSortOption('Weight', 'weight', MetaListSortDirection.Ascending),
  new MetaListSortOption('Weight', 'weight', MetaListSortDirection.Descending),
]
</script>
