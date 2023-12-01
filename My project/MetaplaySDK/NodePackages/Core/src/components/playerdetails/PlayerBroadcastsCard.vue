<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  data-cy="player-broadcasts-card"
  title="Broadcast Log"
  icon="broadcast-tower"
  :itemList="broadcastListItems"
  :tooltip="`All broadcasts in the game.`"
  :searchFields="searchFields"
  :filterSets="filterSets"
  :sortOptions="sortOptions"
  :defaultSortOption="defaultSortOption"
  :emptyMessage="`${broadcastsHasPermission ? 'No broadcasts sent yet!' : 'You do not have permissions to view broadcasts.'}`",
)
  template(#item-card="slot")
    MCollapse(extraMListItemMargin)
      //- Row header
      template(#header)
        MListItem
          span.font-weight-bold(v-if="!slot.item.statusMuted") {{ slot.item.name }}
          span.text-muted.font-italic(v-else) {{ slot.item.name }}
          fa-icon(icon="paperclip" size="sm" v-if="Object.keys(slot.item.attachments).length > 0").ml-2.text-muted
          template(#top-right)
            MBadge(:tooltip="slot.item.statusTooltip" :variant="slot.item.statusBadgeVariant") {{ slot.item.statusText }}
          template(#bottom-right)
            b-link(:to="`/broadcasts/${slot.item.id}`") View broadcast
      //- Collapse content
      div.mt-2.mb-1
        meta-generated-content(:value="slot.item.contents")
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { MCollapse, MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getBroadcastsSubscriptionOptions } from '../../subscription_options/general'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'
import MetaGeneratedContent from '../generatedui/components/MetaGeneratedContent.vue'

const props = defineProps<{
  /**
   * Id of the player whose broadcast log to show.
   */
  playerId: string
}>()

/**
 * Subscribe to broadcast data.
 */
const {
  data: broadcastsData,
  hasPermission: broadcastsHasPermission,
} = useSubscription(getBroadcastsSubscriptionOptions())

/**
 * Subscribe to the target player's data.
 */
const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

/**
 * Search fields to be passed to the meta-list-card.
 */
const searchFields = ['name', 'statusText']

/**
 * Filter sets to be passed to the meta-list-card.
 */
const filterSets = [
  new MetaListFilterSet('status',
    [
      new MetaListFilterOption('Scheduled', (x: any) => x.statusText === 'Scheduled'),
      new MetaListFilterOption('Pending', (x: any) => x.statusText === 'Pending'),
      new MetaListFilterOption('Received', (x: any) => x.statusText === 'Received'),
      new MetaListFilterOption('Not eligible', (x: any) => x.statusText === 'Not eligible'),
      new MetaListFilterOption('Expired', (x: any) => x.statusText === 'Expired')
    ]
  ),
  new MetaListFilterSet('attachments',
    [
      new MetaListFilterOption('Has attachments', (x: any) => x.attachments !== null && x.attachments.length > 0),
      new MetaListFilterOption('No attachments', (x: any) => x.attachments === null || x.attachments.length === 0)
    ]
  )
]

/**
 * Sort options to be passed to meta-list-card.
 */
const sortOptions = [
  new MetaListSortOption('Start time', 'startAt', MetaListSortDirection.Ascending),
  new MetaListSortOption('Start time', 'startAt', MetaListSortDirection.Descending),
  new MetaListSortOption('Status', 'statusSortOrder', MetaListSortDirection.Ascending),
  new MetaListSortOption('Status', 'statusSortOrder', MetaListSortDirection.Descending),
]

/**
 * Sort option that is selected by default.
 */
const defaultSortOption = 1

/**
 * Broadcast items to be displayed in this card.
 */
const broadcastListItems = computed(() => {
  // Since we use playerData in generating the badges, we cannot create broadcast items until that data is loaded.
  if (!playerData.value) return undefined

  const items: any[] = []
  const broadcastData: any = broadcastsData.value || []
  broadcastData.forEach((broadcast: any) => {
    const broadcastId = broadcast.params.id
    const broadcastParams = broadcast.params

    let statusText, statusMuted, statusBadgeVariant, statusTooltip, statusSortOrder
    const receivedBroadcastIds = playerData.value.model.receivedBroadcastIds || []
    if (receivedBroadcastIds.includes(broadcastId)) {
      statusText = 'Received'
      statusMuted = false
      statusBadgeVariant = 'success'
      statusTooltip = `${playerData.value.model.playerName || 'The player'} has received this broadcast.`
      statusSortOrder = 2
    } else {
      const now = new Date()
      const startAtDate = new Date(broadcastParams.startAt)
      const endAtDate = new Date(broadcastParams.endAt)
      if (endAtDate > now && startAtDate < now) {
        if (isPlayerTargeted(playerData.value, broadcastParams)) {
          statusText = 'Pending'
          statusMuted = false
          statusBadgeVariant = 'neutral'
          statusTooltip = `${playerData.value.model.playerName || 'The player'} will receive this broadcast the next time they connect.`
          statusSortOrder = 1
        } else {
          statusText = 'Not eligible'
          statusMuted = true
          statusBadgeVariant = 'neutral'
          statusTooltip = `${playerData.value.model.playerName || 'The player'} is not eligible to receive this broadcast.`
          statusSortOrder = 3
        }
      } else if (endAtDate > now) {
        statusText = 'Scheduled'
        statusMuted = true
        statusBadgeVariant = 'neutral'
        statusTooltip = 'This broadcast has not yet started.'
        statusSortOrder = 0
      } else {
        statusText = 'Expired'
        statusMuted = true
        statusBadgeVariant = 'neutral'
        statusTooltip = `This broadcast has expired and ${playerData.value.model.playerName || 'the player'} did not receive it.`
        statusSortOrder = 4
      }
    }

    items.push({
      id: broadcastId,
      name: broadcastParams.name,
      contents: broadcastParams.contents,
      startAt: broadcastParams.startAt,
      attachments: broadcastParams.contents.contents?.consumableRewards ?? [],
      statusText,
      statusMuted,
      statusBadgeVariant,
      statusTooltip,
      statusSortOrder
    })
  })
  return items
})

/**
 * Checks whether the player being viewed is included in a broadcast target audience.
 * @param player Player whose data is currently displayed on the dashboard view.
 * @param broadcastParams Details of the broadcast message.
 */
function isPlayerTargeted (player: any, broadcastParams: any) {
  let isTargeted = false
  if (broadcastParams.isTargeted) {
    if (broadcastParams.targetPlayers.includes(player.id)) {
      isTargeted = true
    } else if (broadcastParams.targetCondition?.requireAllSegments) {
      if (broadcastParams.targetCondition.requireAllSegments.every((segment: any) => player.matchingSegments.includes(segment))) {
        isTargeted = true
      }
    } else if (broadcastParams.targetCondition?.requireAnySegment) {
      if (broadcastParams.targetCondition.requireAnySegment.some((segment: any) => player.matchingSegments?.includes(segment))) {
        isTargeted = true
      }
    }
  } else {
    isTargeted = true
  }
  return isTargeted
}

</script>
