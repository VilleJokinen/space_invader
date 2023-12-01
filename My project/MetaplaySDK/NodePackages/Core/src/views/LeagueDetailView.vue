<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!singleLeagueData"
  :meta-api-error="singleLeagueError"
  :alerts="alerts"
  )
  template(#error-card-message)
    p Oh no, something went wrong while trying to access the league!
  template(#overview)
    meta-page-header-card(
      :id="leagueId"
      data-cy="league-detail-overview-card"
      )
      template(#title) {{ singleLeagueData.details.leagueDisplayName }}
      p {{ singleLeagueData.details.leagueDescription }}

      b-alert.mt-2(
        :show="singleLeagueData.migrationProgress.isInProgress"
        variant="light"
        ).p-0
        div.mb-1 Season migration in progress.
        div.small.mb-2 Players will be able to join this season once the migrations are complete.
        MProgressBar(:value="singleLeagueData.migrationProgress.progressEstimate" :max="1")
        div(class="tw-italic tw-text-center").small.mt-2 {{ singleLeagueData.migrationProgress.phase }}...

      MErrorCallout(
        v-if="seasonMigrationError"
        :error="seasonMigrationError"
        )

      div(v-else)
        div.mb-1.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
        b-table-simple(small).mb-0
          b-tbody
            b-tr
              b-td League status
              b-td.text-right
                MBadge(v-if="singleLeagueData.enabled" variant="success") Enabled
                MBadge(v-else variant="danger") Disabled
            b-tr(v-if="singleLeagueData.enabled")
              b-td Total lifetime participants
              b-td.text-right {{ singleLeagueData.currentParticipantCount }}
            b-tr(v-if="singleLeagueData.state.currentSeason")
              b-td Latest season
              b-td.text-right
                meta-button(
                  link
                  :to="`/leagues/${leagueId}/${singleLeagueData.state.currentSeason.seasonId}`"
                  permission="api.leagues.view"
                  data-cy="latest-season-button-link"
                  ) Season {{ singleLeagueData.state.currentSeason.seasonId }}
            b-tr(v-if="singleLeagueData.enabled && singleLeagueData.currentOrNextSeasonSchedule")
              b-td Current phase
              b-td.text-right
                MBadge(v-if="singleLeagueData.enabled" :variant="getPhaseVariant(singleLeagueData.currentOrNextSeasonSchedule?.currentPhase.phase)") {{ schedulePhaseDisplayString(singleLeagueData.currentOrNextSeasonSchedule?.currentPhase.phase) }}
                MBadge(v-else variant="danger") Disabled
            b-tr(v-if="singleLeagueData.enabled")
              b-td(colspan="2")
                league-progress-card(v-if="singleLeagueData.enabled && singleLeagueData.schedule" :leagueId="leagueId")

                b-row(v-else)
                  b-td Schedule
                  b-td.text-right.text-warning No schedule defined for this league.

      template(#buttons)
        div(v-if="!seasonMigrationError")
          //- Force a season to end.
          meta-action-modal-button(
            v-if="seasonCurrentPhase === 'Active' || seasonCurrentPhase === 'EndingSoon' "
            id="force-end-season"
            action-button-text="End Season Now"
            modal-title="Force Season to End"
            variant="danger"
            permission="api.leagues.phase_debug"
            :on-ok="() => { return forceSeasonChange(true) }"
            )
              p You can force season {{ singleLeagueData?.state.currentSeason.seasonId }} to end immediately.
              p The next season will start automatically according to the configured schedule.

              p.small.text-muted Performance note: ending a season early will cause all of the division actors to be woken up. Triggering this in a live game with a large amount of divisions will have a performance impact.

              meta-no-seatbelts.mt-4(
                message="This action can't be undone. Are you sure the league participants are okay with this?"
                variant="danger"
                )

          //- Force season to start
          meta-action-modal-button(
            v-else-if="seasonCurrentPhase === 'Preview'"
            id="force-start-season"
            action-button-text="Start Season Now"
            :action-button-disabled="singleLeagueData.enabled === false"
            modal-title="Force Season To Start"
            variant="danger"
            permission="api.leagues.phase_debug"
            :on-ok="() => { return forceSeasonChange(false) }"
            :ok-button-disabled="!singleLeagueData.state.currentSeason.migrationComplete"
            )
              p You can force the season to go to #[MBadge(:variant="getPhaseVariant('Active')") Active] immediately.
              p The season will then progress automatically according to its configured schedule.

              b-alert.mt-4.text-center(
                :show="!singleLeagueData.state.currentSeason.migrationComplete"
                variant="danger"
                ) Season migrations are still ongoing. Please wait for them to finish before forcing this season to start.

          //- Force season to preview.
          meta-action-modal-button(
            v-else
            id="force-preview-season"
            action-button-text="Preview Next Season"
            :action-button-disabled="singleLeagueData.enabled === false"
            modal-title="Force Preview Next Season"
            variant="danger"
            permission="api.leagues.phase_debug"
            :on-ok="() => { return forceSeasonChange(false) }"
            )
              p You can force the next season to go to #[MBadge(:variant="getPhaseVariant('Preview')") Preview] immediately.
              p The season will progress automatically according to its configured schedule.

  template(#default)
    core-ui-placement(:placement-id="'Leagues/Details'" :leagueId="leagueId")

    meta-raw-data(:kvPair="singleLeagueData", name="singleLeagueData")
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { useRoute } from 'vue-router'

import { useGameServerApi } from '@metaplay/game-server-api'
import { DisplayError, MBadge, MErrorCallout, MProgressBar } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSingleLeagueSubscriptionOptions } from '../subscription_options/leagues'

import { routeParamToSingleValue, schedulePhaseDisplayString } from '../coreUtils'
import { showSuccessToast, showErrorToast } from '@metaplay/meta-ui/src/toasts'
import type { MetaPageContainerAlert } from '@metaplay/meta-ui'

import LeagueProgressCard from '../components/leagues/leaguedetails/LeagueProgressCard.vue'

import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'

import { getPhaseVariant } from '../leagueUtils'

const route = useRoute()
const leagueId = routeParamToSingleValue(route.params.leagueId)

const {
  data: singleLeagueData,
  error: singleLeagueError,
  refresh: singleLeagueRefresh
} = useSubscription(getSingleLeagueSubscriptionOptions(leagueId))

const gameServerApi = useGameServerApi()

/**
 * The current phase of the latest season.
 */
const seasonCurrentPhase = computed(() => singleLeagueData.value?.currentOrNextSeasonSchedule?.currentPhase.phase)

/**
 * Season migration error.
 */
const seasonMigrationError = computed(() => {
  if (singleLeagueData.value?.migrationProgress.error) {
    return new DisplayError(
      'Season migration error',
      'Something went wrong during the season migration process. Contact your game team for assistance.',
      '',
      [
        {
          title: 'Internal Error',
          content: singleLeagueData.value?.migrationProgress.error
        }
      ]
    )
  } return undefined
})

/**
 * Array of alert messages to be displayed at the top of this page.
 */
const alerts = computed(() => {
  const allAlerts: MetaPageContainerAlert[] = []
  if (singleLeagueData.value?.enabled === false) {
    allAlerts.push({
      title: 'Disabled League',
      message: 'You are currently viewing a disabled league and there are no active seasons. Set up the leagues feature in your runtime options.',
      variant: 'danger',
    })
  }
  if (singleLeagueData.value?.state.currentSeason?.endedEarly === true) {
    allAlerts.push({
      title: 'Latest Season Ended Early',
      message: 'The latest season was forced to end before its configured schedule.',
      variant: 'warning',
    })
  }
  if (singleLeagueData.value?.state.currentSeason?.startedEarly === true) {
    allAlerts.push({
      title: 'Current Season Started Early',
      message: 'The current season was forced to start before its configured schedule.',
      variant: 'warning',
    })
  }
  return allAlerts
})

/**
 * Async function to force a season to advance to next phase.
 */
async function forceSeasonChange (endSeason: boolean) {
  const response = await gameServerApi.post(`/leagues/${leagueId}/advance`, { isSeasonEnd: endSeason })
  if (response.data.success) {
    showSuccessToast(endSeason ? 'Current season successfully ended early!' : 'Current season successfully advanced!')
  } else {
    showErrorToast(response.data.errorMessage)
  }
  singleLeagueRefresh()
}

</script>
