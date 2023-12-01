<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Normal schedule
div(v-if="singleLeagueData && singleLeagueData.state.currentSeason && singleLeagueData.currentOrNextSeasonSchedule" )
  b-row.my-3
    b-col(md)
      span Time mode: #[MBadge.ml-1 {{ metaScheduleTimeModeDisplayString(singleLeagueData.schedule.timeMode) }}]
    b-col(md="auto")
      span Next phase:
        span(v-if="singleLeagueData.enabled").ml-1
          MBadge(:variant="getPhaseVariant(singleLeagueData.currentOrNextSeasonSchedule.nextPhase.phase)").mr-1 {{ schedulePhaseDisplayString(singleLeagueData.currentOrNextSeasonSchedule.nextPhase.phase) }}
          meta-time(:date="singleLeagueData.currentOrNextSeasonSchedule.nextPhase.startTime")
        span(v-else).ml-1
          MBadge(variant="danger") Disabled

  //- Start time label
  div.w-100
    div(:style="`margin-left: ${Duration.fromISO(singleLeagueData.schedule.preview).toMillis() / totalDurationInMilliseconds * 100}%; position: relative; left: -1px`").pb-3.pl-2.border-left.border-dark
      div.small.font-weight-bold Start
      div.small: meta-time(:date="singleLeagueData.currentOrNextSeasonSchedule.start")
  //- Schedule timeline
  b-progress(:max="totalDurationInMilliseconds" height="3rem" :style="singleLeagueData.currentOrNextSeasonSchedule.currentPhase.phase === 'Inactive' ? 'filter: contrast(50%) brightness(130%)' : ''").font-weight-bold
    b-progress-bar(
      :value="Duration.fromISO(singleLeagueData.schedule.preview).toMillis()"
      variant="primary"
      :animated="singleLeagueData.enabled && singleLeagueData.currentOrNextSeasonSchedule.currentPhase.phase === 'Preview'"
    ) Preview
    b-progress-bar(
      :value="Duration.fromISO(singleLeagueData.schedule.duration).toMillis() - Duration.fromISO(singleLeagueData.schedule.endingSoon).toMillis()"
      variant="success"
      :animated="singleLeagueData.enabled && singleLeagueData.currentOrNextSeasonSchedule.currentPhase.phase === 'Active'"
    ) Active
    b-progress-bar(
      :value="Duration.fromISO(singleLeagueData.schedule.endingSoon).toMillis()"
      variant="warning"
      :animated="singleLeagueData.enabled && singleLeagueData.currentOrNextSeasonSchedule.currentPhase.phase === 'EndingSoon'"
    ) Ending Soon
  //- End time label
  div.w-100
    div(:style="`position: relative; right: -1px`").pt-3.pr-2.pb-1.border-right.border-dark.text-right
      div.small.font-weight-bold End
      div.small: meta-time(:date="singleLeagueData.currentOrNextSeasonSchedule.end")
</template>

<script lang="ts" setup>
import { Duration } from 'luxon'
import { computed } from 'vue'

import { BProgress, BProgressBar } from 'bootstrap-vue'

import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSingleLeagueSubscriptionOptions } from '../../../subscription_options/leagues'

import { getPhaseVariant } from '../../../leagueUtils'
import { metaScheduleTimeModeDisplayString, schedulePhaseDisplayString } from '../../../coreUtils'

const props = defineProps<{
  leagueId: string
}>()

const { data: singleLeagueData } = useSubscription(getSingleLeagueSubscriptionOptions(props.leagueId))

const totalDurationInMilliseconds = computed(() => {
  return Duration.fromISO(singleLeagueData.value.schedule.preview).toMillis() +
    Duration.fromISO(singleLeagueData.value.schedule.duration).toMillis()
})

</script>
