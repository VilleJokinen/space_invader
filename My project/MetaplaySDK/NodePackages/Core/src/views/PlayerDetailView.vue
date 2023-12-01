<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  class="position-relative"
  fluid
  :variant="singlePlayerData?.model.deletionStatus === 'Deleted' || singlePlayerData?.model.deletionStatus === 'ScheduledByAdmin' || singlePlayerData?.model.deletionStatus == 'ScheduledByUser' || singlePlayerData?.model.deletionStatus == 'ScheduledBySystem' ? 'danger' : 'default'"
  :alerts="alerts"
  :isLoading="!singlePlayerData || !dashboardOptionsData"
  :meta-api-error="singlePlayerError"
  )

  //- Error loading player
  template(#error-card-message)
    p Oh no, something went wrong while trying to access the player!

  template(#error-card-body)
    p.mt-3 You may use the following tools to find more information about this error:
    ul
      li Use #[b-link(:to="`/players/${id}/raw`") raw query debugger] to inspect data retrieval phases.
      li Use #[meta-button(link data-cy="model-size-link" permission="api.database.inspect_entity" :to="`/entities/${id}/dbinfo`") save file inspector] to inspect entity data saved on the database.
    div.mt-3.d-flex.justify-content-end
      player-action-force-reset(:playerId="id")
      player-action-export(:playerId="id" allow-debug-export no-block).ml-2

  //- Scheduled deletion alert (complex body -> using a slot)
  template(#alerts)
    meta-alert(
      v-if="singlePlayerData?.model.deletionStatus === 'ScheduledByAdmin' || singlePlayerData?.model.deletionStatus == 'ScheduledByUser' || singlePlayerData?.model.deletionStatus == 'ScheduledBySystem'"
      data-cy="player-deletion-alert"
      variant="danger"
      title="Scheduled for Deletion"
      )
        div
          MBadge(variant="danger") {{ playerName }}
          |  is scheduled to be deleted
          | #[span.font-weight-bold #[meta-time(:date="singlePlayerData.model.scheduledForDeletionAt" showAs="timeago")]]
          |  at #[meta-time(:date="singlePlayerData.model.scheduledForDeletionAt" showAs="time" disableTooltip)]
          |  on #[meta-time(:date="singlePlayerData.model.scheduledForDeletionAt" showAs="date" disableTooltip)].
          | Deletion was {{ singlePlayerData.model.deletionStatus === 'ScheduledByAdmin' ? "scheduled by an admin" : singlePlayerData.model.deletionStatus === 'ScheduledByUser' ? "requested in-game by the player" : "scheduled by an automated system" }}.
        div(v-if="new Date(singlePlayerData.model.scheduledForDeletionAt) < new Date()").mt-2.small.font-italic.text-muted
          | Note: Players are deleted with batch jobs that run every now and then. It is normal for players to get deleted up to a day after their due date!

  //- Overview card
  template(#overview)
    player-overview-card(:playerId="id")

  //- Admin actions card
  b-container.mb-5(v-if="singlePlayerData?.model.deletionStatus !== 'Deleted'")
    b-row(align-h="center")
      b-col(lg="12" xl="8")
        player-admin-actions-card(:playerId="id")

  //- Tabs
  div(v-if="singlePlayerData && dashboardOptionsData && singlePlayerData.model.deletionStatus !== 'Deleted'").mb-5
    //- Tabs
    div(
      class="tw-hidden sm:tw-flex tw-flex-row tw-justify-center tw-items-center tw-mb-10 tw-border-t tw-border-b tw-border-neutral-200 tw-bg-white tw-py-1 tw-sticky tw-z-10 tw-space-x-1"
      style="top: -1px;"
      )
        span(v-for="i in [0,1,2,3,4]" :key="i")
          button(
            v-if="!coreStore.isUiPlacementEmpty(getUiPlacementForTabByIndex(0))"
            :class="['tw-font-semibold tw-rounded tw-py-2 tw-px-4', { 'tw-bg-blue-500 tw-text-white': currentTabIndex === i, 'tw-text-blue-500 hover:tw-bg-neutral-300 active:tw-bg-neutral-400': currentTabIndex !== i }]"
            @click="tabClicked(i)"
            :data-cy="`player-details-tab-${i}`"
            ) {{ getDisplayNameForTabByIndex(i) }}
    div(
      class="sm:tw-hidden tw-px-4 tw-mb-10"
      )
      div(role="heading" class="tw-font-bold tw-mb-2") Current Tab
      meta-input-select(
        :value="String(currentTabIndex)"
        :options="getTabMultiselectOptions()"
        @input="tabClicked"
        no-clear
        no-deselect
        )

    //- Selected tab content
    b-container(style="min-height: 30rem;")
      div(v-if="currentTabIndex === 0")
        core-ui-placement(placementId="Players/Details/Tab0" :playerId="id")
      div(v-else-if="currentTabIndex === 1")
        core-ui-placement(placementId="Players/Details/Tab1" :playerId="id")
      div(v-else-if="currentTabIndex === 2")
        core-ui-placement(placementId="Players/Details/Tab2" :playerId="id")
      div(v-else-if="currentTabIndex === 3")
        core-ui-placement(placementId="Players/Details/Tab3" :playerId="id")

        //- TODO: this way of auto-discovering activables no longer feels kosher. Re-design?
        b-row
          b-col(v-for="(title, category) in activableCategories" :key="title" lg="6").mb-3
            offer-groups-card.h-100(
              v-if="category === 'OfferGroup'"
              hideDisabled
              hideConversion
              :playerId="id"
              :title="title"
              :emptyMessage="`${playerName} doesn't have any offers available.`"
              :defaultSortOption="3"
              hidePriority
              hideRevenue
              )
            meta-generic-activables-card.h-100(
              v-else
              hideDisabled
              hideConversion
              :playerId="id"
              :category="String(category)"
              :title="title"
              :emptyMessage="`There are no ${title} defined.`"
              )
      div(v-else-if="currentTabIndex === 4")
        core-ui-placement(placementId="Players/Details/Tab4" :playerId="id")

        //- Player pretty print
        meta-raw-data(:kvPair="singlePlayerData" name="singlePlayerData")
        meta-raw-data(:kvPair="dashboardOptionsData")
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import type { MetaPageContainerAlert } from '@metaplay/meta-ui'
import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import MetaGenericActivablesCard from '../components/activables/MetaGenericActivablesCard.vue'
import OfferGroupsCard from '../components/offers/OfferGroupsCard.vue'
import PlayerActionForceReset from '../components/playerdetails/adminactions/PlayerActionForceReset.vue'
import PlayerActionExport from '../components/playerdetails/adminactions/PlayerActionExport.vue'
import PlayerAdminActionsCard from '../components/playerdetails/PlayerAdminActionsCard.vue'
import PlayerOverviewCard from '../components/playerdetails/PlayerOverviewCard.vue'
import { getDashboardOptionsSubscriptionOptions, getStaticConfigSubscriptionOptions } from '../subscription_options/general'
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import { useCoreStore } from '../coreStore'
import useHeaderbar from '../useHeaderbar'
import { getSinglePlayerSubscriptionOptions } from '../subscription_options/players'

const props = defineProps<{
  /**
   * ID of the player to show.
   */
  id: string
}>()

const {
  data: singlePlayerData,
  error: singlePlayerError
} = useSubscription(getSinglePlayerSubscriptionOptions(props.id))
const playerName = computed((): string => singlePlayerData.value?.model.playerName || 'n/a')

const coreStore = useCoreStore()

// Update the headerbar title dynamically as data changes.
useHeaderbar().setDynamicTitle(playerName, (playerName) => `Manage ${playerName.value || 'Player'}`)

// Alerts ----------------------

const alerts = computed(() => {
  const allAlerts: MetaPageContainerAlert[] = []

  if (singlePlayerData.value?.model.deletionStatus === 'Deleted') {
    allAlerts.push({
      title: '☠️ Player Deleted',
      message: 'This player is no more. They have ceased to be and the account had been scrubbed of personal data.',
      variant: 'danger',
      dataCy: 'player-deleted-alert'
    })
  } else if (singlePlayerData.value?.model.attachedAuthMethods && Object.keys(singlePlayerData.value.model.attachedAuthMethods).length === 0) {
    allAlerts.push({
      title: 'Orphan Account',
      message: 'This account has no login methods attached. Nobody can currently connect to play as this player.',
      variant: 'warning',
      dataCy: 'player-orphaned-alert'
    })
  }

  if (singlePlayerData.value?.model.isBanned === true) {
    allAlerts.push({
      title: 'Banned',
      message: "This player is currently banned. That'll teach them!",
      variant: 'warning',
      dataCy: 'player-banned-alert'
    })
  }

  return allAlerts
})

// Tabs ------------------------

function getUiPlacementForTabByIndex (tabIndex: number) {
  if (tabIndex === 0) return 'Players/Details/Tab0'
  else if (tabIndex === 1) return 'Players/Details/Tab1'
  else if (tabIndex === 2) return 'Players/Details/Tab2'
  else if (tabIndex === 3) return 'Players/Details/Tab3'
  else if (tabIndex === 4) return 'Players/Details/Tab4'
  else throw new Error('Trying to find a placement for a tab that does not exist!')
}

// Fetch display names for tabs from dashboard options
const dashboardOptionsData = useSubscription(getDashboardOptionsSubscriptionOptions()).data
function getDisplayNameForTabByIndex (tabIndex: number) {
  if (!dashboardOptionsData.value) throw new Error('Trying to find a display name for a tab before dashboard options have loaded!')
  return dashboardOptionsData.value[`playerDetailsTab${tabIndex}DisplayName`]
}

// Sync tab navigation with query params
const route = useRoute()
const router = useRouter()
const currentTabIndex = ref(Number(route.query?.tab) || 0)
async function tabClicked (newTabIndex: number) {
  await router.replace({
    path: route.path,
    query: { tab: newTabIndex.toString() }
  })
  currentTabIndex.value = newTabIndex
}

function getTabMultiselectOptions () {
  return [{
    id: getDisplayNameForTabByIndex(0),
    value: '0'
  },
  {
    id: getDisplayNameForTabByIndex(1),
    value: '1'
  },
  {
    id: getDisplayNameForTabByIndex(2),
    value: '2'
  },
  {
    id: getDisplayNameForTabByIndex(3),
    value: '3'
  },
  {
    id: getDisplayNameForTabByIndex(4),
    value: '4'
  }]
}

// Activables etc. ---------------------
// TODO: migrate away from here

const {
  data: staticConfigData,
} = useSubscription(getStaticConfigSubscriptionOptions())

const activableCategories = computed(() => {
  const categories = Object.entries(staticConfigData.value?.serverReflection.activablesMetadata.categories || [])
  return Object.fromEntries(categories.map(x => {
    return [x[0], (x[1] as any).displayName]
  }))
})
</script>

<style>
.tabs-container {
  position: sticky;
  top: -1px;
  z-index: 4;
}

.tabs-container button {
  background-color: transparent;
  border: 0;
  font-weight: 300;
  border-radius: .3rem;
  padding: .5rem 1rem .5rem 1rem;
  margin-left: .1rem;
  margin-right: .1rem;
  color: var(--metaplay-blue);
}

.tabs-container button:hover {
  background-color: var(--metaplay-grey);
  color: rgb(27, 103, 162)
}

.tabs-container .active {
  background-color: var(--metaplay-blue);
  color: white;
}

.tabs-container .active:hover {
  background-color: var(--metaplay-blue);
  color: white;
}
</style>
