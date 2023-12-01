<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!singleOfferGroupData"
  :meta-api-error="singleOfferGroupError"
  :variant="containsOffers ? undefined : 'warning'"
  )
  template(#overview)
    meta-page-header-card(data-cy="overview" :id="singleOfferGroupData.config.activableId")
      template(#title) {{ singleOfferGroupData.config.displayName }}
      template(#subtitle) {{ singleOfferGroupData.config.description }}

      b-alert(:show="!containsOffers" variant="warning").mt-3
        div.font-weight-bolder No Offers Found
        div This offer group contains no offers and thus will not be visible in the game! Did you forget to configure the offers in the game configs?

      div.font-weight-bold.mb-1 #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td Status
            b-td.text-right
              MBadge(v-if="isEnabled" variant="success") Enabled
              MBadge(v-else variant="danger") Disabled
          b-tr
            b-td Placement
            b-td.text-right {{ singleOfferGroupData.config.placement }}
          b-tr
            b-td Priority
            b-td.text-right: meta-ordinal-number(:number="singleOfferGroupData.config.priority")
          b-tr
            b-td Audience Size Estimate
            b-td.text-right #[meta-audience-size-estimate(:sizeEstimate="isTargeted ? singleOfferGroupData.audienceSizeEstimate : undefined")]

      div.font-weight-bold.mb-1 #[fa-icon(icon="chart-line")] Statistics
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td Activated By
              MBadge(tooltip="Number of players who have seen this group." shape="pill").ml-1 ?
            b-td.text-right #[meta-abbreviate-number(:value="singleOfferGroupData.statistics.numActivatedForFirstTime", unit="player")]
          b-tr
            b-td Consumed By
              MBadge(tooltip="Number of players who have bought something from this group." shape="pill").ml-1 ?
            b-td.text-right #[meta-abbreviate-number(:value="singleOfferGroupData.statistics.numConsumedForFirstTime", unit="player")]
          b-tr
            b-td Conversion
              MBadge(tooltip="The percentage of players who have bought something from this group after seeing it." shape="pill").ml-1 ?
            b-td.text-right(v-if="singleOfferGroupData.statistics.numActivatedForFirstTime > 0") {{ conversion }}%
            b-td.text-right.text-muted.font-italic(v-else) None
          b-tr
            b-td Total Consumes
              MBadge(tooltip="Total number of purchases from this group." shape="pill").ml-1 ?
            b-td.text-right #[meta-abbreviate-number(:value="singleOfferGroupData.statistics.numConsumed", unit="time")]
          b-tr
            b-td Total Revenue
            b-td.text-right ${{ singleOfferGroupData.revenue.toFixed(2) }}

      div.font-weight-bold.mb-1 #[fa-icon(icon="calendar-alt")] Scheduling
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td Activation Mode
            b-td.text-right
              MBadge(:variant="singleOfferGroupParams.schedule ? 'neutral' : 'primary'") {{ singleOfferGroupParams.schedule ? 'Scheduled' : 'Dynamic' }}
          // Lifetime overview
          b-tr(v-if="!singleOfferGroupParams.schedule")
            b-td Lifetime After Activation
            b-td.text-right
              span(v-if="lifetimeType === 'Forever'") Forever
              span(v-else-if="lifetimeType === 'Fixed'"): meta-duration(:duration="singleOfferGroupParams.lifetime.duration" showAs="humanizedSentenceCase")
              span(v-else).text-danger Unknown!
          b-tr(v-if="!singleOfferGroupParams.schedule")
            b-td Cooldown After Deactivation
            b-td.text-right
              span(v-if="cooldownType === 'Fixed'")
                span(v-if="durationToMilliseconds(singleOfferGroupParams.cooldown.duration) === 0").text-muted.font-italic None
                meta-duration(v-else :duration="singleOfferGroupParams.cooldown.duration" showAs="humanizedSentenceCase")
              span(v-else).text-danger Unknown!
          // Schedule overview
          b-tr(v-if="singleOfferGroupParams.schedule")
            b-td(colspan="2")
              b-row.my-3.mx-0
                b-col(md).pl-0
                  span Time Mode:
                    MBadge(:tooltip="singleOfferGroupParams.schedule.timeMode !== 'Utc' ? 'Using UTC time to preview the schedule.' : undefined").ml-1 {{ metaScheduleTimeModeDisplayString(singleOfferGroupParams.schedule.timeMode) }}
                b-col(md)
                  span Current Phase: #[meta-activable-phase-badge(:activable="singleOfferGroupData")]
                b-col(md="auto")
                  span(v-if="!isEnabled") Next Phase: #[meta-activable-phase-badge(:activable="singleOfferGroupData" phase="Disabled")]
                  span(v-else-if="nextPhase") Next Phase: #[meta-activable-phase-badge(:activable="singleOfferGroupData" :phase="nextPhase")]
                    div #[meta-time(:date="nextPhaseStartTime")]
                  span(v-else) No longer occurring
              // Start time label
              div.w-100
                div(:style="`margin-left: ${durationToMilliseconds(singleOfferGroupParams.schedule.preview) / totalDuration * 100}%; position: relative; left: -1px`").pb-3.pl-2.border-left.border-dark
                  div.small.font-weight-bold Start
                  div(v-if="displayedEnabledRange").small: meta-time(:date="displayedEnabledRange.start")
              // Schedule timeline
              b-progress(:max="totalDuration" height="3rem" :style="phase === 'Inactive' ? 'filter: contrast(50%) brightness(130%)' : ''").font-weight-bold
                b-progress-bar(
                  :value="durationToMilliseconds(singleOfferGroupParams.schedule.preview)"
                  variant="info"
                  :animated="phase === 'Preview'"
                ) Preview
                b-progress-bar(
                  :value="durationToMilliseconds(singleOfferGroupParams.schedule.duration) - durationToMilliseconds(singleOfferGroupParams.schedule.endingSoon)"
                  variant="success"
                  :animated="phase === 'Active'"
                ) Active
                b-progress-bar(
                  :value="durationToMilliseconds(singleOfferGroupParams.schedule.endingSoon)"
                  variant="warning"
                  :animated="phase === 'EndingSoon'"
                ) Ending soon
                b-progress-bar(
                  :value="durationToMilliseconds(singleOfferGroupParams.schedule.review)"
                  variant="info"
                  :animated="phase === 'Review'"
                ) Review
              // End time label
              div.w-100
                div(:style="`margin-right: ${durationToMilliseconds(singleOfferGroupParams.schedule.review) / totalDuration * 100}%; position: relative; right: -1px`").pt-3.pr-2.pb-1.border-right.border-dark.text-right
                  div.smallfont-weight-bold End
                  div(v-if="displayedEnabledRange").small: meta-time(:date="displayedEnabledRange.end")

  template(#default)
    b-row(no-gutters align-v="center").mt-3.mb-2
      h3 Contents

    b-row(align-h="center")
      b-col(lg="8").mb-3
        offer-groups-offers-card(
          :offerGroupId="routeParamToSingleValue(route.params.id)"
          emptyMessage="This offer group contains no offers and thus will not be visible in the game! Did you forget to configure the offers in the game configs?"
        )

    b-row(no-gutters align-v="center").mt-3.mb-2
      h3 Scheduling

    b-row(align-h="center" v-if="singleOfferGroupParams").mb-3
      b-col.mb-3
        b-card(data-cy="activable-configuration")
          b-card-title
            b-row(align-v="center" no-gutters)
              fa-icon.mr-2(icon="sliders-h")
              span Activable Configuration
          b-row
            b-col(md="6").mb-3
              div.rounded.border.px-3.py-2.h-100(:class="{ 'bg-light': !singleOfferGroupParams.lifetime || lifetimeType === 'ScheduleBased' }")
                b-row(align-v="center" no-gutters).mb-2
                  span.font-weight-bold Lifetime
                  MBadge.ml-1(v-if="!singleOfferGroupParams.lifetime || lifetimeType === 'ScheduleBased'") Off
                  MBadge.ml-1(v-else variant="success") On
                div(v-if="singleOfferGroupParams.lifetime").my-3
                  div(v-if="lifetimeType === 'ScheduleBased'").text-center.text-muted This activable's lifetime follows the schedule.
                  div(v-else-if="lifetimeType === 'Forever'").text-center.text-muted This activable exists forever.
                  div(v-else-if="lifetimeType === 'Fixed'").text-center This activable's lifetime is #[meta-duration(:duration="singleOfferGroupParams.lifetime.duration")].
                  div(v-else).text-muted Unknown lifetime type: {{ lifetimeType }}.
                div(v-else).text-center.text-muted No lifetime defined.

            b-col(md="6").mb-3
              div.rounded.border.px-3.py-2.h-100(:class="{ 'bg-light': !singleOfferGroupParams.cooldown || cooldownType === 'ScheduleBased' || durationToMilliseconds(singleOfferGroupParams.cooldown.duration) === 0 }")
                b-row(align-v="center" no-gutters).mb-2
                  span.font-weight-bold Cooldown
                  MBadge.ml-1(v-if="!singleOfferGroupParams.cooldown || cooldownType === 'ScheduleBased' || durationToMilliseconds(singleOfferGroupParams.cooldown.duration) === 0") Off
                  MBadge.ml-1(v-else variant="success") On
                div(v-if="singleOfferGroupParams.cooldown").my-3
                  div(v-if="cooldownType === 'ScheduleBased'").text-center.text-muted This activable's cooldown period follows the schedule.
                  div.text-center(v-else-if="cooldownType === 'Fixed'" :class="{ 'text-muted': durationToMilliseconds(singleOfferGroupParams.cooldown.duration) === 0 }") This activable's cooldown period is #[meta-duration(:duration="singleOfferGroupParams.cooldown.duration")].
                  div(v-else)
                    div.text-muted Unknown cooldown type: {{ cooldownType }}.
                    pre(style="font-size: .7rem") {{ singleOfferGroupParams.cooldown }}
                div(v-else).text-center.text-muted No cooldown defined.

            b-col(md="6").mb-3
              div.rounded.border.px-3.py-2.h-100(:class="{ 'bg-light': !singleOfferGroupParams.schedule}")
                b-row(align-v="center" no-gutters).mb-2
                  span.font-weight-bold Schedule
                  MBadge.ml-1(v-if="!singleOfferGroupParams.schedule") Off
                  MBadge.ml-1(v-else variant="success") On
                div(v-if="singleOfferGroupParams.schedule")
                  b-table-simple(small v-if="scheduleType === 'MetaRecurringCalendarSchedule'" style="font-size: .85rem")
                    b-tbody
                      b-tr
                        b-td Time mode
                        b-td.text-right(v-if="singleOfferGroupParams.schedule.timeMode !== 'Utc'") #[MBadge {{ singleOfferGroupParams.schedule.timeMode }}]
                        b-td.text-right(v-else) #[MBadge {{ metaScheduleTimeModeDisplayString(singleOfferGroupParams.schedule.timeMode) }}]
                      b-tr
                        b-td Start
                        b-td.text-right #[meta-time(:date="singleOfferGroupParams.schedule.start" showAs="timeagoSentenceCase")]
                      b-tr
                        b-td Preview
                        b-td.text-right
                          meta-duration(:duration="singleOfferGroupParams.schedule.preview" showAs="exactDuration")
                      b-tr
                        b-td Duration
                        b-td.text-right
                          meta-duration(:duration="singleOfferGroupParams.schedule.duration" showAs="exactDuration")
                      b-tr
                        b-td Ending Soon
                        b-td.text-right
                          meta-duration(:duration="singleOfferGroupParams.schedule.endingSoon" showAs="exactDuration")
                      b-tr
                        b-td Review
                        b-td.text-right
                          meta-duration(:duration="singleOfferGroupParams.schedule.review" showAs="exactDuration")
                      b-tr
                        b-td Repeats
                        b-td.text-right.text-muted.font-italic(v-if="singleOfferGroupParams.schedule.numRepeats === null") Unlimited
                        b-td.text-right(v-else) {{ singleOfferGroupParams.maxTotalConsumes }}
                      b-tr
                        b-td Recurrence
                        b-td.text-right
                          div(v-if="singleOfferGroupParams.schedule.recurrence === null") Never
                          meta-duration(v-else :duration="singleOfferGroupParams.schedule.recurrence" showAs="exactDuration")
                  div(v-else)
                    div.text-muted Unknown schedule type: {{ scheduleType }}.
                    pre(style="font-size: .7rem") {{ singleOfferGroupParams.schedule }}
                div(v-else).text-center.text-muted.my-auto No schedule defined.

            b-col(md="6").mb-3
              div.rounded.border.px-3.py-2.h-100
                div.mb-2.font-weight-bold Activations
                b-table-simple(small style="font-size: .85rem").m-0
                  b-tbody
                    b-tr
                      b-td Max Activations
                      b-td.text-right.text-muted.font-italic(v-if="singleOfferGroupParams.maxActivations === null") Unlimited
                      b-td.text-right(v-else) {{ singleOfferGroupParams.maxActivations }}
                    b-tr
                      b-td Max Total Consumes
                      b-td.text-right.text-muted.font-italic(v-if="singleOfferGroupParams.maxTotalConsumes === null") Unlimited
                      b-td.text-right(v-else) {{ singleOfferGroupParams.maxTotalConsumes }}
                    b-tr
                      b-td Max Consumed Per Activation
                      b-td.text-right.text-muted.font-italic(v-if="singleOfferGroupParams.maxConsumesPerActivation === null") Unlimited
                      b-td.text-right(v-else) {{ singleOfferGroupParams.maxConsumesPerActivation }}

    b-row(no-gutters align-v="center").mt-3.mb-2
      h3 Targeting

    b-row(align-h="center")
      b-col(md="6").mb-3
        segments-card(:segments="singleOfferGroupParams.segments" data-cy="segments" ownerTitle="This event")
      b-col(md="6").mb-3
        player-conditions-card(:playerConditions="singleOfferGroupParams.additionalConditions" data-cy="conditions")

    meta-raw-data(:kvPair="singleOfferGroupData" name="singleOfferGroupData")
    meta-raw-data(:kvPair="gameSpecificInfo" name="gameSpecificInfo")
</template>

<script lang="ts" setup>
import { BProgress, BProgressBar } from 'bootstrap-vue'
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import MetaActivablePhaseBadge from '../components/activables/MetaActivablePhaseBadge.vue'
import PlayerConditionsCard from '../components/global/PlayerConditionsCard.vue'
import SegmentsCard from '../components/global/SegmentsCard.vue'
import OfferGroupsOffersCard from '../components/offers/OfferGroupsOffersCard.vue'
import MetaAudienceSizeEstimate from '../components/MetaAudienceSizeEstimate.vue'

import { getStaticConfigSubscriptionOptions } from '../subscription_options/general'
import { getSingleOfferGroupSubscriptionOptions } from '../subscription_options/offers'
import { durationToMilliseconds, metaScheduleTimeModeDisplayString, roundTo, routeParamToSingleValue } from '../coreUtils'

const props = defineProps<{
  kindId: string
}>()

/**
 * Subscribe to the data.
 * NB: Root point of difference plain activable and offerGroup details page
 */

const route = useRoute()
const offerGroupId = routeParamToSingleValue(route.params.id)

// Offer group data ----------------------------------------------------------------------------------------------------

const {
  data: singleOfferGroupData,
  error: singleOfferGroupError
} = useSubscription(getSingleOfferGroupSubscriptionOptions(offerGroupId))

const {
  data: staticConfigData
} = useSubscription(getStaticConfigSubscriptionOptions())

/**
 * Parameters that define the behavior of the offerGroup.
 * @example Lifetime or cooldown durations.
 */
const singleOfferGroupParams = computed(() => {
  return singleOfferGroupData.value.config.activableParams
})

/**
 * Additional game-specific details about the offer groups
 */
const gameSpecificInfo = computed(() => {
  const gameSpecificMemberNames = activablesMetadata.value.kinds[props.kindId].gameSpecificConfigDataMembers
  return gameSpecificMemberNames.map((memberName: string) => {
    return {
      key: memberName,
      value: singleOfferGroupData.value.config[memberName]
    }
  })
})

/**
 *  Additional data about the offer groups.
 */
const activablesMetadata = computed(() => {
  return staticConfigData.value?.serverReflection.activablesMetadata
})

// Offer group schedule ----------------------------------------------------------------------------------------------------

/**
 * Specifies whether an offer group has a fixed or non-fixed lifetime.
 * i.e The duration an offer group is shown as 'active'.
 */
const lifetimeType = computed(() => {
  return singleOfferGroupParams.value.lifetime.$type.match(/Metaplay\.Core.Activables\.MetaActivableLifetimeSpec\+(.*)/)[1]
})

/**
 * Specifies the schedule based activation type that the offer group follows.
 * An offer group can be scheduled as a one-time offer or recurres after a set period of time.
 */
const scheduleType = computed(() => {
  return singleOfferGroupParams.value.schedule.$type.match(/Metaplay\.Core\.Schedule\.(.*)/)[1]
})

/**
 * Specifies whether an offer group has a fixed or non-fixed cooldown duration.
 * i.e The duration that must elapse after an offer Group has expired before it can be shown again.
 */
const cooldownType = computed(() => {
  return singleOfferGroupParams.value.cooldown.$type.match(/Metaplay\.Core\.Activables\.MetaActivableCooldownSpec\+(.*)/)[1]
})

/**
 * Estimated time it takes to complete all phases.
 */
const totalDuration = computed(() => {
  if (singleOfferGroupParams.value.schedule) {
    return durationToMilliseconds(singleOfferGroupParams.value.schedule.preview) + durationToMilliseconds(singleOfferGroupParams.value.schedule.duration) + durationToMilliseconds(singleOfferGroupParams.value.schedule.review)
  } else return 1
})

// Offer group phases ----------------------------------------------------------------------------------------------------

/**
 * Display name for the current phase.
 */
const phase = computed(() => {
  const scheduleStatus = singleOfferGroupData.value?.scheduleStatus
  return scheduleStatus ? scheduleStatus.currentPhase.phase : null
})

/**
 * Display name for the next phase.
 */
const nextPhase = computed(() => {
  const scheduleStatus = singleOfferGroupData.value?.scheduleStatus
  return scheduleStatus?.nextPhase ? scheduleStatus.nextPhase.phase : null
})

/**
 * Indicates when the next phase starts.
 */
const nextPhaseStartTime = computed(() => {
  const scheduleStatus = singleOfferGroupData.value.scheduleStatus
  return scheduleStatus?.nextPhase ? scheduleStatus.nextPhase.startTime : null
})

/**
 * Start and end time for the current offer group phase.
 */
const displayedEnabledRange = computed(() => {
  const scheduleStatus = singleOfferGroupData.value.scheduleStatus
  return scheduleStatus ? scheduleStatus.relevantEnabledRange : null
})

// Misc ---------------------------------------------------------------------------------------------------------------

/**
 * Number of players who have consumed the selected offer.
 */
const conversion = computed(() => {
  return roundTo(singleOfferGroupData.value.statistics.numConsumedForFirstTime / singleOfferGroupData.value.statistics.numActivatedForFirstTime * 100, 2)
})

/**
 * Check if the offer group contains multiple sub-offers.
 */
const containsOffers = computed(() => {
  return (singleOfferGroupData.value?.config.offers || []).length > 0
})

/**
 * Indicates whether an offer group is targeted to a specific audience or to the whole player base.
 */
const isTargeted = computed(() => {
  return singleOfferGroupParams.value.segments !== null && singleOfferGroupParams.value.segments.length !== 0
})

/**
 * Indicates if an offer group is actively running or not.
 */
const isEnabled = computed(() => {
  return singleOfferGroupParams.value.isEnabled
})

</script>

<style scoped>
pre {
  font-size: 0.7rem;
  margin-bottom: 0px;
}
</style>
