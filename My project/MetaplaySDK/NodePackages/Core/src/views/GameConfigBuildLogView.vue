<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :isLoading="!gameConfigData"
  :alerts="pageAlerts"
  permission="api.game_config.view"
  )
  template(#overview)
    meta-page-header-card(:id="gameConfigId")
      template(#title) Build Log: {{ gameConfigData?.name || 'No name available' }}
      template(#subtitle) {{ gameConfigData?.description|| 'No description available' }}

      //- Overview.
      span.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td Build Status
            b-td.text-right
              div {{ gameConfigDataGuaranteed?.status }}
          b-tr
            b-td Built At
            b-td.text-right
              div(v-if="isFailedBuild") Build failed
              meta-time(v-else :date="gameConfigDataGuaranteed?.archiveBuiltAt" showAs="timeagoSentenceCase")
          b-tr
            b-td Built By
            b-td.text-right
              MBadge(v-if="gameConfigData?.source === 'disk'") Built-in with the server
              meta-username(v-else :username="gameConfigDataGuaranteed?.source")
          b-tr(:class="{ 'text-danger': gameConfigData?.buildReportSummary?.totalLogLevelCounts.Error }")
            b-td Logged Errors
            b-td(v-if="gameConfigData?.buildReportSummary?.totalLogLevelCounts.Error").text-right {{ gameConfigData.buildReportSummary?.totalLogLevelCounts.Error }}
            b-td(v-else).text-right.text-muted.font-italic None
          b-tr(:class="{ 'text-warning': gameConfigData?.buildReportSummary?.totalLogLevelCounts.Warning }")
            b-td Logged Warnings
            b-td(v-if="gameConfigData?.buildReportSummary?.totalLogLevelCounts.Warning").text-right {{ gameConfigData.buildReportSummary?.totalLogLevelCounts.Warning }}
            b-td(v-else).text-right.text-muted.font-italic None
          b-tr
            b-td Uploaded At
            b-td.text-right #[meta-time(:date="gameConfigDataGuaranteed?.persistedAt" showAs="timeagoSentenceCase")]
          b-tr
            b-td Associated Config
            b-td.text-right
              meta-button(
                v-if="!isFailedBuild"
                link
                :to="`/gameConfigs/${gameConfigId}`"
                data-cy="latest-season-button-link"
                ) View game config
              div(v-else).text-muted Not available

  template(#default)
    MErrorCallout(v-if="isFailedBuild" :error="buildErrorDetails").mb-3

    // TODO: Child components should subscribe to data themselves.
    core-ui-placement(placementId="GameConfigs/Logs" :buildReport="gameConfigDataGuaranteed.contents?.metaData.buildReport ?? undefined")

    meta-raw-data(:kvPair="gameConfigData")
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import type { MetaPageContainerAlert } from '@metaplay/meta-ui'
import { MBadge, MErrorCallout, DisplayError } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import { getSingleGameConfigCountsSubscriptionOptions } from '../subscription_options/gameConfigs'
import { routeParamToSingleValue } from '../coreUtils'
import type { LibraryCountGameConfigInfo } from '../gameConfigServerTypes'

const route = useRoute()

/**
 * Id of game config that is to be displayed.
 */
const gameConfigId = routeParamToSingleValue(route.params.id)

/**
 * Fetch data for the specific game config that is to be displayed.
 */
const {
  data: gameConfigData,
} = useSubscription<LibraryCountGameConfigInfo>(getSingleGameConfigCountsSubscriptionOptions(gameConfigId))

const gameConfigDataGuaranteed = computed(() => {
  if (!gameConfigData.value) {
    throw new Error('Accessing gameConfigData before it is loaded')
  }
  return gameConfigData.value
})

/**
 * True if the build failed.
 */
const isFailedBuild = computed(() => {
  return gameConfigData.value?.status === 'Failed'
})

/**
 * True if the build completed with warnings.
 */
const buildHasWarnings = computed(() => {
  const highestMessageLevel = gameConfigData.value?.contents?.metaData.buildReport?.highestMessageLevel
  return highestMessageLevel === 'Warning'
})

/**
 * Error details to display if the build failed, formatted to `MErrorCallout`.
 */
const buildErrorDetails = computed(() => {
  return new DisplayError(
    'Build Failed',
    'Fatal errors were encountered while trying to build this config.',
    undefined,
    [
      {
        title: 'Error Details',
        content: gameConfigData.value?.failureInfo ?? gameConfigData.value?.publishBlockingError ?? 'No error details available.',
      }
    ]
  )
})

/**
 * Alerts to display at the top of the page.
 */
const pageAlerts = computed(() => {
  const allAlerts: MetaPageContainerAlert[] = []

  if (isFailedBuild.value) {
    allAlerts.push({
      title: 'Build Failed',
      message: 'This config failed to build.',
      variant: 'danger',
      dataCy: 'failed-alert'
    })
  } else if (gameConfigData.value?.isPublishBlockedByErrors) {
    allAlerts.push({
      title: 'Build Cannot Be Published',
      message: 'The build that generated these logs cannot be published.',
      variant: 'danger',
      dataCy: 'cannot-publish-alert'
    })
  } else if (buildHasWarnings.value) {
    allAlerts.push({
      title: 'Build Warnings',
      message: `There were ${gameConfigData.value?.buildReportSummary.totalLogLevelCounts.Warning} warnings when building this config.
        You may not be able to publish the resulting config, or it may not work as expected.`,
      variant: 'warning',
      dataCy: 'warning-alert'
    })
  }

  if (gameConfigData.value?.buildReportSummary?.isBuildMessageTrimmed) {
    const availableMessages = Object.values(gameConfigData.value.buildReportSummary.buildLogLogLevelCounts).reduce((p, c) => p + c, 0)
    allAlerts.push({
      title: 'Too Many Build Log Messages',
      message: `This build generated too many build log messages to display. Only the most relevant ${availableMessages} messages are shown.`,
      variant: 'warning',
      dataCy: 'build-log-too-many-messages-alert'
    })
  }
  if (gameConfigData.value?.buildReportSummary?.isValidationMessagesTrimmed) {
    const availableMessages = Object.values(gameConfigData.value.buildReportSummary.validationResultsLogLevelCounts).reduce((p, c) => p + c, 0)
    allAlerts.push({
      title: 'Too Many Validation Log Messages',
      message: `This build generated too many validation log messages to display. Only the most relevant ${availableMessages} messages are shown.`,
      variant: 'warning',
      dataCy: 'validation-log-too-many-messages-alert'
    })
  }

  return allAlerts
})
</script>
