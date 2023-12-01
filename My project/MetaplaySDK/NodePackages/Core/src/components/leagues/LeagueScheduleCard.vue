<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
b-card(data-cy="league-schedule-card")
  b-card-title
    b-row(align-v="center" no-gutters)
      fa-icon.mr-2(icon="sliders-h")
      | Schedule

  div.mb-1.font-weight-bold Configuration
  b-table-simple(small style="font-size: .85rem" v-if="singleLeagueData.schedule")
    b-tbody
      b-tr(v-if="!singleLeagueData.enabled")
        b-td Schedule status
        b-td.text-right #[MBadge(variant="danger") Inactive]
      b-tr
        b-td Time mode
        b-td.text-right #[MBadge {{ metaScheduleTimeModeDisplayString(singleLeagueData.schedule.timeMode) }}]
      b-tr
        b-td First season start
        b-td.text-right #[meta-time(:date="singleLeagueData.schedule.start" showAs="datetime")]
      b-tr
        b-td Preview time
        b-td.text-right #[meta-duration(:duration="singleLeagueData.schedule.preview" showAs="exactDuration")] before start
      b-tr
        b-td Season duration
        b-td.text-right: meta-duration(:duration="singleLeagueData.schedule.duration" showAs="exactDuration")
      b-tr
        b-td Ending soon time
        b-td.text-right #[meta-duration(:duration="singleLeagueData.schedule.endingSoon" showAs="exactDuration")] before end
      b-tr
        // TODO: Is this always the same as the season duration + preview? If so, remove?
        b-td Recurrence
        b-td.text-right
          meta-duration(v-if="!!singleLeagueData.schedule.recurrence" :duration="singleLeagueData.schedule.recurrence" showAs="exactDuration")
          div(v-else) Never
  div(v-else).text-warning No schedule defined!
</template>

<script lang="ts" setup>
import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSingleLeagueSubscriptionOptions } from '../../subscription_options/leagues'

import { metaScheduleTimeModeDisplayString } from '../../coreUtils'

const props = defineProps<{
  leagueId: string
}>()

const { data: singleLeagueData } = useSubscription(getSingleLeagueSubscriptionOptions(props.leagueId))
</script>
