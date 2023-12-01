<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!allBroadcastsData"
  :meta-api-error="allBroadcastsError"
  )
  template(#overview)
    meta-page-header-card
      template(#title) Create a New Broadcast
      template(#buttons)
        broadcast-form-button(
          data-cy='new-broadcast'
          button-text="Create New"
          button-icon="plus-square"
          @refresh="allBroadcastsRefresh"
          ).mr-2.mt-1

      p Broadcasts are scheduled in-game mail campaigns.
      div.small.text-muted Players who match the targeting criteria of an active broadcast will get an in-game mail generated into their inbox when they login. A player can receive every broadcast only once to prevent duplicate mails.

  template(#center-column)
    meta-list-card#broadcasts-list(
      data-cy='all-broadcasts'
      title="All broadcasts"
      :getItemKey="getBroadcastId"
      :itemList="decoratedBroadcasts"
      :searchFields="['params.id', 'params.name']"
      :filterSets="filterSets"
      :sortOptions="sortOptions"
      :defaultSortOption="1"
      :pageSize="20"
      icon="broadcast-tower"
    )
      template(#item-card="slot")
        MListItem
          span {{ slot.item.params.name }} #[span.font-size-normal.text-muted.small for {{ audienceSummary(slot.item.params) }}]

          template(#top-right)
            MBadge(:variant="slot.item.decoration.variant")
              template(#icon)
                fa-icon(:icon="slot.item.decoration.icon")
              | {{ slot.item.decoration.status }}

          template(#bottom-left)
            div(v-if="slot.item.decoration.status === 'Scheduled'") Start time: #[meta-time(:date="slot.item.params.startAt")]
            div(v-else) End time: #[meta-time(:date="slot.item.params.endAt")]

          template(#bottom-right)
            div Received by #[meta-plural-label(:value="slot.item.stats.receivedCount", label="player")]
            div: b-link( data-cy='view-broadcast' :to="getDetailPagePath(slot.item.params.id)") View broadcast

  template(#default)
    meta-raw-data(:kvPair="decoratedBroadcasts" name="decoratedBroadcasts")/

</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { MetaListSortOption, MetaListSortDirection, MetaListFilterSet, MetaListFilterOption, maybePlural } from '@metaplay/meta-ui'

import BroadcastFormButton from '../components/mails/BroadcastFormButton.vue'

import { useRoute } from 'vue-router'
import { MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getAllBroadcastsSubscriptionOptions } from '../subscription_options/broadcasts'
import { DateTime } from 'luxon'

const route = useRoute()
const detailsRoute = computed(() => route.path)

/**
 * Subscribe to broadcast data.
 * Unused variable broacastsRefresh, a TODO that was forgotten?
 */
const {
  data: allBroadcastsData,
  error: allBroadcastsError,
  refresh: allBroadcastsRefresh,
} = useSubscription<any[]>(getAllBroadcastsSubscriptionOptions())

/**
 * List of broadcasts to be displayed.
 */
const decoratedBroadcasts = computed(() => {
  if (!allBroadcastsData.value) return undefined
  const now = DateTime.now()
  return allBroadcastsData.value.map(broadcast => {
    const startAtDate = DateTime.fromISO(broadcast.params.startAt)
    const endAtDate = DateTime.fromISO(broadcast.params.endAt)
    if (endAtDate > now && startAtDate < now) {
      return {
        ...broadcast,
        decoration: {
          status: 'Active',
          variant: 'success',
          icon: 'broadcast-tower'
        }
      }
    } else if (endAtDate > now) {
      return {
        ...broadcast,
        decoration: {
          status: 'Scheduled',
          variant: 'primary',
          icon: 'calendar-alt'
        }
      }
    } else {
      return {
        ...broadcast,
        decoration: {
          status: 'Expired',
          variant: 'neutral',
          icon: 'times'
        }
      }
    }
  })
})

/**
 * List of sort options to be passed to the meta-list-card component.
 */
const sortOptions = [
  new MetaListSortOption('Time', 'params.startAt', MetaListSortDirection.Ascending),
  new MetaListSortOption('Time', 'params.startAt', MetaListSortDirection.Descending),
  new MetaListSortOption('Name', 'params.name', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'params.name', MetaListSortDirection.Descending),
  new MetaListSortOption('Received by', 'stats.receivedCount', MetaListSortDirection.Ascending),
  new MetaListSortOption('Received by', 'stats.receivedCount', MetaListSortDirection.Descending),
]

/**
 * List of filter sets to be passed to the meta-list-card component.
 */
const filterSets = [
  new MetaListFilterSet('status',
    [
      new MetaListFilterOption('Scheduled', (x: any) => x.decoration.status === 'Scheduled'),
      new MetaListFilterOption('Active', (x: any) => x.decoration.status === 'Active'),
      new MetaListFilterOption('Expired', (x: any) => x.decoration.status === 'Expired')
    ]
  )
]

/**
 * Retrieves the Id of the respective broadcast.
 * @param broadcast broadcast data.
 */
function getBroadcastId (broadcast: any) {
  return broadcast.params.id
}

/**
 * Get detail page path by joining it to the current path,
 * but take into account if there's already a trailing slash.
 * \todo Do a general fix with router and get rid of this.
 */
function getDetailPagePath (detailId: string) {
  const path = detailsRoute.value
  const maybeSlash = path.endsWith('/') ? '' : '/'
  return path + maybeSlash + detailId
}

/**
 * Summary estimation of number of players targeted in the campaign.
 */
function audienceSummary (broadcastParams: any) {
  if (!broadcastParams.isTargeted) {
    return 'everyone'
  } else if (broadcastParams.targetPlayers?.length && broadcastParams.targetCondition) {
    return `Segment based and ${maybePlural(broadcastParams.targetPlayers.length, 'player')}`
  } else if (broadcastParams.targetPlayers?.length) {
    return `${maybePlural(broadcastParams.targetPlayers.length, 'player')}`
  } else if (broadcastParams.targetCondition) {
    return 'Segment based'
  }
}
</script>
