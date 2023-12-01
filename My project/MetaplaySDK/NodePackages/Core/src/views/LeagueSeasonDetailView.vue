<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!singleLeagueSeasonData"
  :meta-api-error="pageError"
  :alerts="alerts"
)
  template(#error-card-message)
    p Oh no, something went wrong while trying to access the season!

  template(#overview)
    meta-page-header-card(data-cy="league-season-detail-overview-card")
      template(#title) {{ singleLeagueSeasonData.displayName }}

      template(#subtitle)
        div(v-if="singleLeagueData") #[router-link(:to="`/leagues/${leagueId}`") {{ singleLeagueData.details.leagueDisplayName }}] / {{ singleLeagueSeasonData.displayName }}
        p.mt-2 {{ singleLeagueSeasonData.description }}

      div.mb-1.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small)
        b-tbody
          b-tr(v-if="!singleLeagueSeasonData.isCurrent")
            b-td Start
            b-td.text-right: meta-time(:date="singleLeagueSeasonData.startTime" showAs="datetime")

          b-tr(v-if="!singleLeagueSeasonData.isCurrent")
            b-td End
            b-td.text-right: meta-time(:date="singleLeagueSeasonData.endTime" showAs="datetime")

          b-tr(v-if="!singleLeagueSeasonData.isCurrent")
            b-td Duration
            //- TODO: showAs="top-two" is not the ideal format but best we have now, pennina suggested an exact format for meta-duration.
            b-td.text-right: meta-duration(:duration="DateTime.fromISO(singleLeagueSeasonData.endTime).diff(DateTime.fromISO(singleLeagueSeasonData.startTime))" showAs="top-two")
          b-tr
            b-td Total participants
            b-td.text-right {{ singleLeagueSeasonData.totalParticipantCount }}
          b-tr
            b-td First-time participants
            b-td.text-right {{ singleLeagueSeasonData.newParticipantCount }}
          b-tr(v-if="!singleLeagueSeasonData.isCurrent")
            b-td Dropped at season's end
            b-td.text-right {{ singleLeagueSeasonData.numDropped }}
          b-tr(v-if="singleLeagueSeasonData.isCurrent")
            b-td(colspan="2")
              league-progress-card(v-if="singleLeagueData.enabled" :leagueId="leagueId")

  template(#center-column)
    core-ui-placement(placementId="Leagues/Season/Details" :leagueId="leagueId" :seasonId="seasonId")

    //- Active divisions list
    div(v-if="uiStore.showDeveloperUi")
      meta-list-card(
        title="Recently Active Divisions"
        :item-list="activeDivisionsData"
        empty-message="No recently active divisions in this season."
        description="This card is only visible in the developer UI mode."
        )
        template(#item-card="{ item: singleDivisionData }")
          MListItem
            span {{ singleDivisionData.displayName }}
            template(#bottom-left)
              div Created #[meta-time(:date="singleDivisionData.createdAt")].
            template(#top-right)
              span {{ singleDivisionData.participants }} participants
            template(#bottom-right)
              div.text-right: meta-button(link :to="`/leagues/${leagueId}/${seasonId}/${singleDivisionData.entityId}`" permission="api.leagues.view") View division

  meta-raw-data(:kvPair="singleLeagueSeasonData", name="singleLeagueSeasonData")
  meta-raw-data(:kvPair="activeDivisionsData", name="activeDivisionsData")
</template>

<script lang="ts" setup>
import { DateTime } from 'luxon'
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { type MetaPageContainerAlert, useUiStore } from '@metaplay/meta-ui'
import { MListItem, DisplayError } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import LeagueProgressCard from '../components/leagues/leaguedetails/LeagueProgressCard.vue'
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import { getSingleLeagueSubscriptionOptions, getSingleLeagueSeasonSubscriptionOptions, getActiveDivisionsForLeagueSeasonSubscriptionOptions } from '../subscription_options/leagues'
import { routeParamToSingleValue, isNullOrUndefined } from '../coreUtils'

const route = useRoute()

const uiStore = useUiStore()

/**
 * Id of the league whose data is displayed on this page.
 */
const leagueId = routeParamToSingleValue(route.params.leagueId)

/**
 * Id of the season whose data is displayed on this page.
 */
const seasonId = Number(routeParamToSingleValue(route.params.seasonId))

/**
 * Record an error if we tried to ask for a season number that isn't an integer.
 */
const seasonIdError: DisplayError | undefined = isFinite(seasonId)
  ? undefined
  : new DisplayError('Invalid season ID', `Season ID must be a number, not '${route.params.seasonId}'.`)

/**
 * Subscribe to the league data displayed on this page.
 */
const {
  data: singleLeagueData,
} = useSubscription(getSingleLeagueSubscriptionOptions(leagueId))

/**
 * Subscribe to the season data displayed on this page. Note that we don't bother to make the request if the season ID is
 * invalid because we know it will fail.
 */
const {
  data: singleLeagueSeasonData,
  error: singleLeagueSeasonError
} = useSubscription(() => { return seasonIdError ? undefined : getSingleLeagueSeasonSubscriptionOptions(leagueId, seasonId) })

/**
 * Subscribe to the currently active divisions data displayed on this page. Note that we don't bother to make the
 * request if the season ID is invalid because we know it will fail.
 */
const {
  data: activeDivisionsData
} = useSubscription(() => { return seasonIdError ? undefined : getActiveDivisionsForLeagueSeasonSubscriptionOptions(leagueId, seasonId) })

/**
 * If any kind of error is found then we need to show it on the page.
 */
const pageError = computed((): Error | DisplayError | undefined => seasonIdError ?? singleLeagueSeasonError.value)

/**
 * Array of messages to be displayed at the top of the page.
 */
const alerts = computed(() => {
  const allAlerts: MetaPageContainerAlert[] = []
  if (isNullOrUndefined(singleLeagueData.value) && isNullOrUndefined(singleLeagueSeasonData.value)) {
    if (singleLeagueSeasonData.value.isCurrent === false) {
      allAlerts.push({
        title: 'Past Season',
        message: pastSeasonAlertMessage.value,
        variant: 'secondary'
      })
    }
    if (singleLeagueSeasonData.value.isCurrent === true && singleLeagueData.value.state.currentSeason.endedEarly === true) {
      allAlerts.push({
        title: 'Season Ended Early',
        message: 'This season ended before its configured schedule.',
        variant: 'warning',
      })
    }
    if (singleLeagueSeasonData.value.isCurrent === true && singleLeagueData.value.state.currentSeason.startedEarly === true) {
      allAlerts.push({
        title: 'Season Started Early',
        message: 'This season started before its configured schedule.',
        variant: 'warning',
      })
    }
  }
  return allAlerts
})

/**
 * Message displayed at the top of the page when viewing a past season.
 */
const pastSeasonAlertMessage = computed(() => {
  if (singleLeagueSeasonData.value?.startedEarly === true && singleLeagueSeasonData.value?.endedEarly === false) {
    return 'You are currently viewing a season that has already ended. This season started before its configured schedule.'
  } else if (singleLeagueSeasonData.value?.startedEarly === false && singleLeagueSeasonData.value?.endedEarly === true) {
    return 'You are currently viewing a season that has already ended. This season ended before its configured schedule.'
  } else if (singleLeagueSeasonData.value?.startedEarly === true && singleLeagueSeasonData.value?.endedEarly === true) {
    return 'You are currently viewing a season that has already ended. This season started and ended before its configured schedule.'
  } else {
    return 'You are currently viewing a season that has already ended.'
  }
})
</script>
