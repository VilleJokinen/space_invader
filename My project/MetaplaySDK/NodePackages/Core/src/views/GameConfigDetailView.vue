<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :variant="errorVariant"
  no-bottom-padding
  :is-loading="loadState === 'loading'"
  :show-error-card="loadState === 'error'"
  fluid
  :alerts="alerts"
  permission="api.game_config.view"
  )
  template(#overview)
    meta-page-header-card(v-if="gameConfigWithoutContents" data-cy="game-config-overview" :id="gameConfigId")
      template(#title) {{ gameConfigWithoutContents.name || 'No name available' }}
      template(#subtitle) {{ gameConfigWithoutContents.description|| 'No description available' }}

      //- Overview.
      span.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td Status
            b-td.text-right
              MBadge(v-if="gameConfigWithoutContents.isArchived" variant="neutral").mr-1 Archived
              MBadge(v-if="gameConfigWithoutContents.isActive" variant="success").mr-1 Active
              MBadge(v-else) Not active
          b-tr
            b-td Built By
            b-td.text-right
              MBadge(v-if="gameConfigWithoutContents.source === 'disk'") Built-in with the server
              meta-username(v-else :username="gameConfigWithoutContents.source")
          b-tr
            b-td Experiments
            b-td(v-if="totalExperiments === undefined").text-right.text-muted.font-italic Loading...
            b-td(v-else-if="totalExperiments > 0").text-right {{ totalExperiments }}
            b-td(v-else).text-right.text-muted.font-italic None
          b-tr(:class="{ 'text-warning': gameConfigData?.buildReportSummary?.totalLogLevelCounts.Warning }")
            b-td Validation Warnings
            b-td(v-if="gameConfigData?.buildReportSummary?.totalLogLevelCounts.Warning").text-right {{ gameConfigData.buildReportSummary?.totalLogLevelCounts.Warning }}
            b-td(v-else).text-right.text-muted.font-italic None

      //- Overview.
      span.font-weight-bold #[fa-icon(icon="chart-bar")] Techical Details
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td Built At
            b-td.text-right #[meta-time(:date="gameConfigWithoutContents.archiveBuiltAt" showAs="timeagoSentenceCase")]
          b-tr
            b-td Uploaded At
            b-td.text-right #[meta-time(:date="gameConfigWithoutContents.persistedAt" showAs="timeagoSentenceCase")]
          b-tr
            b-td Full Config Archive Version
            b-td(v-if="!gameConfigData").text-right.text-muted.font-italic Loading...
            b-td(v-else).text-right
              div(v-if="gameConfigData?.fullConfigVersion").text-monospace.small {{ gameConfigData.fullConfigVersion }}
              div(v-else).text-muted.font-italic Not available
          b-tr
            b-td Client Facing Version
            b-td(v-if="!gameConfigData").text-right.text-muted.font-italic Loading...
            b-td(v-else).text-right
              div(v-if="gameConfigData?.cdnVersion").text-monospace.small {{ gameConfigData.cdnVersion }}
              MTooltip(v-else content="Only available for the currently active game config.").text-muted.font-italic Not available

      template(#buttons)
        // TODO: should this be conditionally disabled for old configs?
        meta-button(
          variant="primary"
          :to="`/gameConfigs/${gameConfigId}/logs`"
          data-cy="latest-season-button-link"
          ) View Build Log

        meta-action-modal-button.ml-1(
          id="edit-config"
          permission="api.game_config.edit"
          action-button-text="Edit"
          modal-title="Edit Game Config Archive"
          @show="resetForm"
          ok-button-text="Update"
          :on-ok="sendUpdatedConfigDataToServer"
          )
            div.mb-2
              div.mb-1.font-weight-bold Name
              b-form-input(v-model="editModalConfig.name" :state="editModalConfig.name.length > 0 ? true : null" @submit.prevent="" placeholder="For example: 1.0.4 release candidate")

            div.mb-3
              div.mb-1.font-weight-bold Description
              b-form-textarea(v-model="editModalConfig.description" rows="3" no-resize :state="editModalConfig.description.length > 0 ? true : null" @submit.prevent="" placeholder="What is unique about this config build that will help you find it later?")

            b-row(align-h="between" align-v="center" no-gutters).mb-1
              span.font-weight-bold Archived
              meta-input-checkbox(:disabled="!canArchive" v-model="editModalConfig.isArchived" name="Archive config build" size="lg" showAs="switch")
            span.small.text-muted Archived config builds are hidden from the game configs list by default. Config builds that are active or still building cannot be archived.

        meta-button.ml-1(
          variant="primary"
          :disabled="disallowDiffToActiveReason !== undefined"
          :tooltip="disallowDiffToActiveReason"
          :to="`diff?newRoot=${gameConfigId}`"
          ) Diff to Active

        meta-action-modal-button.ml-1(
          id="publish-config"
          permission="api.game_config.edit"
          action-button-text="Publish"
          modal-title="Publish Game Config"
          ok-button-text="Publish Config"
          :on-ok="publishConfig"
          :action-button-disabled="disallowPublishReason !== undefined"
          :action-button-tooltip="disallowPublishReason"
          )
            p Publishing #[MBadge(variant="neutral") {{ gameConfigWithoutContents.name }}] will make it the active game config, effective immediately.
            p.small.text-muted Players will download the new config the next time they login. Currently live players will continue using the config they started with until the end of their current play session.
            p.small.text-muted Other people using the LiveOps Dashboard at the moment may be disrupted by the game data changing while they work, so make sure to let them know you are publishing an update!

  template(#default)
    b-container(fluid)
      core-ui-placement(placementId="GameConfigs/Details" :gameConfigId="gameConfigId")

    b-container(fluid).pb-5
      b-row(no-gutters align-v="center").mt-3.mb-2
        h3 Configuration

      //- Show game config contents or an alert.
      div(v-if="gameConfigWithoutContents?.status === 'Building'")
        //- Game config still building.
        b-row.justify-content-center
          b-col(lg="8")
            b-alert(show) This game config is still building. Check back soon!

      div(v-else-if="gameConfigData?.configValidationError && gameConfigData?.contentsParseError")
        b-row.justify-content-center
          b-col
            b-alert(show) This game config could not be parsed with the current server version and it cannot be displayed.

          b-col
            b-card.shadow-sm
              b-card-title
                b-row(no-gutters align-v="center")
                  fa-icon(icon="code").mr-2
                  span Stack Trace
              div.log.text-monospace.border.rounded.bg-light.w-100(style="max-height: 20rem")
                pre {{ gameConfigData?.configValidationError || gameConfigWithoutContents?.failureInfo }}

      div(v-else-if="gameConfigData?.failureInfo")
        b-row.justify-content-center
          b-col
            b-alert(show) This game config failed to build correctly.

          b-col
            b-card.shadow-sm
              b-card-title
                b-row(no-gutters align-v="center")
                  fa-icon(icon="code").mr-2
                  span Stack Trace
              div.log.text-monospace.border.rounded.bg-light.w-100(style="max-height: 20rem")
                pre {{ gameConfigWithoutContents?.failureInfo }}

      div(v-else-if="gameConfigWithoutContents?.status === 'Succeeded'")
        config-contents-card(:gameConfigId="gameConfigId" show-experiment-selector)

      div(v-else)
        //- Game config was not built due to an error.
        b-row.justify-content-center
          b-col(lg="8")
            b-alert(variant="danger" show).text-center This game config failed to build correctly and has no configuration attached to it.

      meta-raw-data(:kvPair="gameConfigData" name="gameConfigData")
      meta-raw-data(:kvPair="gameConfigWithoutContents" name="gameConfigWithoutContents")

  template(#error-card-message)
    div.mb-3 Oh no, something went wrong when retrieving this game config from the game server. Are you looking in the right deployment?
    MErrorCallout(:error="gameConfigError")
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { MBadge, MErrorCallout, MTooltip } from '@metaplay/meta-ui-next'
import type { MetaPageContainerAlert } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'

import ConfigContentsCard from '../components/global/ConfigContentsCard.vue'
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import { getAllGameConfigsSubscriptionOptions, getSingleGameConfigCountsSubscriptionOptions } from '../subscription_options/gameConfigs'
import useHeaderbar from '../useHeaderbar'
import { routeParamToSingleValue } from '../coreUtils'
import type { GameConfigBuildMessage, LibraryCountGameConfigInfo, StaticGameConfigInfo } from '../gameConfigServerTypes'

const gameServerApi = useGameServerApi()
const route = useRoute()

// Load game config data ----------------------------------------------------------------------------------------------

/**
 *  There are two sources of information for this page:
 * 1 - We subscribe to all gameconfig data and pull out just the game config that we're interested in. This loads fast
 *     and allows us to show the overview card very quickly while..
 * 2 - ..we are also subscribed to the full data for the game config that we're interested in. This is much slower to
 *    load because it includes the archive contents. We only need this contents for the the experiments info on the
 *    overview card so we don't want to have to wait for it to be loaded.
 */

/**
 * Fetch all available game configs
 */
const {
  data: allGameConfigsData,
  refresh: allGameConfigsRefresh,
} = useSubscription<StaticGameConfigInfo[]>(getAllGameConfigsSubscriptionOptions())

/**
 * Id of game config that is to be displayed.
 */
const gameConfigId = routeParamToSingleValue(route.params.id)

/**
 * Fetch data for the specific game config that is to be displayed.
 */
const {
  data: gameConfigData,
  error: gameConfigError
} = useSubscription<LibraryCountGameConfigInfo>(getSingleGameConfigCountsSubscriptionOptions(gameConfigId))

/**
 * Game config data without the detailed content.
 */
const gameConfigWithoutContents = computed((): StaticGameConfigInfo | undefined => {
  if (allGameConfigsData.value) {
    return Object.values(allGameConfigsData.value).find((x) => x.id === gameConfigId)
  } else {
    return undefined
  }
})

// Update the headerbar title dynamically as data changes.
useHeaderbar().setDynamicTitle(gameConfigWithoutContents, (gameConfigRef) => `View ${gameConfigRef.value?.name ?? 'Config'}`)

/**
 * Indicates whether the page has completed loading or not.
 */
const loadState = computed(() => {
  if (gameConfigWithoutContents.value) return 'loaded'
  else if (gameConfigError.value) return 'error'
  else return 'loading'
})

/**
 * Experiment data.
 */
const totalExperiments = computed(() => {
  return gameConfigData.value?.contents?.serverLibraries.PlayerExperiments ?? 0
})

// UI Alerts ----------------------------------------------------------------------------------------------------------

/**
 * Array of error messages to be displayed in the event something goes wrong.
 */
const alerts = computed(() => {
  const allAlerts: MetaPageContainerAlert[] = []

  if (gameConfigWithoutContents.value?.status === 'Building') {
    allAlerts.push({
      title: 'Config building...',
      message: 'This game config is still building and has no content to view for now.',
      variant: 'warning',
      dataCy: 'building-alert'
    })
  } else if ((gameConfigData.value?.configValidationError ?? []).length > 0) {
    allAlerts.push({
      title: 'Parse Error',
      message: 'There was a problem parsing this game config with the current server version and it cannot be fully displayed.',
      variant: 'danger',
      dataCy: 'parse-error-alert'
    })
  } else if (gameConfigWithoutContents.value?.status === 'Failed') {
    allAlerts.push({
      title: 'Error',
      message: 'There was a problem accessing this game config and it can not be published. It is safe to archive.',
      variant: 'danger',
      dataCy: 'access-error-alert'
    })
  } else if (gameConfigData.value?.libraryParsingErrors && Object.keys(gameConfigData.value?.libraryParsingErrors).length > 0) {
    allAlerts.push({
      title: 'Library Errors',
      message: 'One or more libraries failed to parse.',
      variant: 'danger',
      dataCy: 'libraries-fail-to-parse-alert'
    })
  } else if (gameConfigData.value?.isPublishBlockedByErrors) {
    allAlerts.push({
      title: 'Build Cannot Be Published',
      message: 'This build cannot be published.',
      variant: 'danger',
      dataCy: 'cannot-publish-alert'
    })
  } else if (gameConfigData.value?.buildReportSummary?.totalLogLevelCounts.Warning) {
    allAlerts.push({
      title: `${gameConfigData.value.buildReportSummary.totalLogLevelCounts.Warning} Build Warnings`,
      message: `There were ${gameConfigData.value.buildReportSummary.totalLogLevelCounts.Warning} warnings when building this config. You can still publish it, but it may not work as expected. You can view the full build log for more information.`,
      variant: 'warning',
      dataCy: 'build-warnings-alert'
    })
  }
  return allAlerts
})

/**
 * Custom background color that indicates the type of alert message.
 */
const errorVariant = computed(() => {
  if (alerts.value.find((alert) => alert.variant === 'danger')) return 'danger'
  else if (alerts.value.find((alert) => alert.variant === 'warning')) return 'warning'
  else return undefined
})

// Modify and/or publish the game config ------------------------------------------------------------------------------

/**
 * Is the displayed game config is 'Ok' to be published?
 */
const canPublish = computed(() => {
  return !gameConfigWithoutContents.value?.isActive &&
    !gameConfigData.value?.isPublishBlockedByErrors
})

/**
 * Returns a reason why this config cannot be published, or undefined if it can.
 */
const disallowPublishReason = computed((): string | undefined => {
  if (gameConfigWithoutContents.value?.isActive) {
    return 'This game config is already active.'
  } else if (gameConfigData.value?.isPublishBlockedByErrors) {
    return 'Cannot publish a game config that contains errors.'
  } else {
    return undefined
  }
})

/**
 * Publish the displayed game config to the server.
 */
async function publishConfig () {
  await gameServerApi.post('/gameConfig/publish?parentMustMatchActive=false', { Id: gameConfigId })
  showSuccessToast('Game config published.')
  // Force reload the page as new configs are now in play.
  // TODO: look into hot-loading the configs instead to solve this for all other dash users as well while making it less intrusive.
  window.location.reload()
}

/**
 * Information that is to be modified in the game config modal.
 */
interface GameConfigModalInfo {
  /**
   * Display name of the game config.
  */
  name: string
  /**
   * Optional description of what is unique about the game config build.
   */
  description: string
  /**
   * Indicates whether the game config has been archived.
  */
  isArchived: boolean
}

/**
 * Game config data to be modified in the modal.
 */
const editModalConfig = ref<GameConfigModalInfo>({
  name: '',
  description: '',
  isArchived: false
})

/**
 * Reset edit modal.
 */
function resetForm () {
  editModalConfig.value = {
    name: gameConfigWithoutContents.value?.name ?? '',
    description: gameConfigWithoutContents.value?.description ?? '',
    isArchived: gameConfigWithoutContents.value?.isArchived ?? false
  }
}

/**
 * Take game config build data from the modal and send it to the server.
 */
async function sendUpdatedConfigDataToServer () {
  const params = {
    name: editModalConfig.value.name,
    description: editModalConfig.value.description,
    isArchived: editModalConfig.value.isArchived
  }
  await gameServerApi.post(`/gameConfig/${gameConfigId}`, params)
  showSuccessToast('Game config updated.')
  allGameConfigsRefresh()
}

/**
 * Returns a reason why this config cannot be diffed against the active config, or undefined if it can.
 */
const disallowDiffToActiveReason = computed((): string | undefined => {
  if (gameConfigWithoutContents.value?.isActive) {
    return 'Cannot diff this config against itself.'
  } else if (gameConfigWithoutContents.value?.status !== 'Succeeded') {
    return 'Cannot diff a config that is not in a valid state.'
  } else {
    return undefined
  }
})

/**
 * Can the displayed game config can be archived?
 */
const canArchive = computed(() => {
  return gameConfigWithoutContents.value?.status !== 'Building'
})
</script>
