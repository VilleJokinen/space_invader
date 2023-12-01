<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :variant="errorVariant"
  no-bottom-padding
  :is-loading="loadState === 'loading'"
  :show-error-card="loadState === 'error'"
  fluid
  :alerts="alerts"
  )
  template(#overview)
    meta-page-header-card(v-if="localizationTableData?.info" data-cy="game-localization-overview" :id="localizationId")
      template(#title) {{ localizationTableData?.info.name || 'No name available' }}
      template(#subtitle) {{ localizationTableData?.info.description }}

      // TODO: Missing icon. Also compare to new gameconfig detail view. Should at least have the number of validation errors, like missing keys.
      div.font-weight-bold.mb-1 Overview
      b-table-simple(small responsive).mb-0
        b-tbody
          b-tr
            b-td Status
            b-td.text-right
              MBadge(v-if="localizationTableData?.info.isArchived" variant="neutral").mr-1 Archived
              MBadge(v-if="localizationTableData?.info.isActive" variant="success").mr-1 Active
              MBadge(v-else) Not active
          b-tr
            b-td Built At
            b-td.text-right #[meta-time(:date="localizationTableData?.info.archiveBuiltAt" showAs="timeagoSentenceCase")]
          b-tr
            b-td Built By
            b-td.text-right
              MBadge(v-if="localizationTableData?.info.source === 'disk'") Built-in with the server
              meta-username(v-else :username="localizationTableData?.info.source")
          b-tr
            b-td Uploaded At
            b-td.text-right #[meta-time(:date="localizationTableData?.info.persistedAt" showAs="timeagoSentenceCase")]

      template(#buttons)
        // TODO: Move achiving to its own button once it's done in game configs.
        meta-action-modal-button(
          id="edit-localization"
          permission="api.localization.edit"
          action-button-text="Edit"
          modal-title="Edit Localization Archive"
          @show="resetForm"
          ok-button-text="Update"
          :on-ok="sendUpdatedLocalizationDataToServer"
          )
            div.mb-2
              div.mb-1.font-weight-bold Name
              b-form-input(v-model="editModalConfig.name" :state="editModalConfig.name.length > 0 ? true : null" @submit.prevent="" placeholder="For example: 1.0.4 release candidate")

            div.mb-3
              div.mb-1.font-weight-bold Description
              b-form-textarea(v-model="editModalConfig.description" rows="3" no-resize :state="editModalConfig.description.length > 0 ? true : null" @submit.prevent="" placeholder="What is unique about this localization build that will help you find it later?")

            b-row(align-h="between" align-v="center" no-gutters).mb-1
              span.font-weight-bold Archived
              meta-input-checkbox(:disabled="!canArchive" v-model="editModalConfig.isArchived" name="Archive localization build" size="lg" showAs="switch")
            span.small.text-muted Archived localization builds are hidden from the localization list by default. Localization builds that are active or still building cannot be archived.

        meta-button.ml-1(variant="primary" :disabled="!canDiffToActive" :to="`diff?newRoot=${localizationId}`") Diff to Active

        meta-action-modal-button.ml-1(
          id="publish-localization"
          permission="api.localization.edit"
          action-button-text="Publish"
          modal-title="Publish Localization"
          ok-button-text="Publish Localization"
          :on-ok="publishLocalization"
          :action-button-disabled="!canPublish"
          )
            p Publishing #[MBadge(variant="neutral") {{ localizationTableData?.info.name || localizationTableData?.info.id }}] will make it the active localization, effective immediately.
            p.small.text-muted Players will download the new localization the next time they login. Currently live players will continue using the localization they started with until the end of their current play session.

  template(#default)
    b-container(fluid)
      core-ui-placement(placementId="Localization/Details" :localizationId="localizationId")

    b-container(fluid).pb-5
      //- Show localization contents or an alert.
      div(v-if="localizationTableData?.info.status === 'Building'")
        //- localization still building.
        b-row.justify-content-center
          b-col(lg="8")
            b-alert(show) This localization is still building. Check back soon!

      div(v-else-if="localizationTableData?.info.failureInfo")
        b-row.justify-content-center
          b-col
            b-alert(show) This localization failed to build correctly.

          b-col
            b-card.shadow-sm
              b-card-title
                b-row(no-gutters align-v="center")
                  fa-icon(icon="code").mr-2
                  span Stack Trace
              div.log.text-monospace.border.rounded.bg-light.w-100(style="max-height: 20rem")
                pre {{ localizationTableData?.info.failureInfo }}

      div(v-else-if="localizationTableData?.info.status === 'Succeeded'")
        localization-contents-card(:localizationId="localizationId")

      div(v-else)
        //- Localization was not built due to an error.
        b-row.justify-content-center
          b-col(lg="8")
            b-alert(variant="danger" show).text-center This localization failed to build correctly and has no configuration attached to it.

      meta-raw-data(:kvPair="rawSlicedLocalizationTableData" name="localizationData")

  template(#error-card-message)
    div.mb-3 Oh no, something went wrong when retrieving this localization from the game server. Are you looking in the right deployment?
    MErrorCallout(:error="localizationError")
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { MBadge, MErrorCallout } from '@metaplay/meta-ui-next'
import type { MetaPageContainerAlert } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'

import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import useHeaderbar from '../useHeaderbar'
import { routeParamToSingleValue } from '../coreUtils'
import { getSingleLocalizationTableSubscriptionOptions } from '../subscription_options/localization'
import type { LocalizationTable } from '../localizationServerTypes'
import LocalizationContentsCard from '../components/global/LocalizationContentsCard.vue'

const gameServerApi = useGameServerApi()
const route = useRoute()

// Load localization data ----------------------------------------------------------------------------------------------

/**
 * Id of localization that is to be displayed.
 */
const localizationId = routeParamToSingleValue(route.params.id)

const {
  data: localizationTableData,
  refresh: localizationRefresh,
  error: localizationError,
} = useSubscription<LocalizationTable>(getSingleLocalizationTableSubscriptionOptions(localizationId))

const rawSlicedLocalizationTableData = computed(() => {
  if (localizationTableData.value) {
    return {
      ...localizationTableData.value,
      table: localizationTableData.value.table.slice(0, 1000)
    }
  }
  return {}
})

// Update the headerbar title dynamically as data changes.
useHeaderbar().setDynamicTitle(localizationTableData, (locRef) => `View ${locRef.value?.info.name ?? 'Localization'}`)

/**
 * Indicates whether the page has completed loading or not.
 */
const loadState = computed(() => {
  if (localizationTableData.value) return 'loaded'
  else if (localizationError.value) return 'error'
  else return 'loading'
})

// Ui Alerts ----------------------------------------------------------------------------------------------------------

/**
 * Array of error messages to be displayed in the event something goes wrong.
 */
const alerts = computed(() => {
  const allAlerts: MetaPageContainerAlert[] = []

  if (localizationTableData.value?.info.status === 'Building') {
    allAlerts.push({
      title: 'Localization building...',
      message: 'This localization is still building and has no content to view for now.',
      variant: 'warning',
      dataCy: 'building-alert'
    })
  } else if (localizationTableData.value?.info.status === 'Failed') {
    allAlerts.push({
      title: 'Build Failed',
      message: 'This localization failed to build correctly and can not be activated. It is safe to archive.',
      variant: 'danger',
      dataCy: 'build-failed-alert'
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

// Modify and/or publish the localization ------------------------------------------------------------------------------

/**
 * Is the displayed localization is 'Ok' to be published?
 */
const canPublish = computed(() => {
  return !localizationTableData.value?.info.isActive &&
    localizationTableData.value?.info.status === 'Succeeded'
})

/**
 * Publish the displayed localization to the server.
 */
async function publishLocalization () {
  await gameServerApi.post('/localization/publish', { Id: localizationId })
  showSuccessToast('Localization published.')
  // Force reload the page as new localizations are now in play.
  // TODO: look into hot-loading the localization instead to solve this for all other dash users as well while making it less intrusive.
  window.location.reload()
}

/**
 * Information that is to be modified in the localization modal.
 */
interface LocalizationModalInfo {
  /**
   * Display name of the localization.
  */
  name: string
  /**
   * Optional description of what is unique about the localization build.
   */
  description: string
  /**
   * Indicates whether the localization has been archived.
  */
  isArchived: boolean
}

/**
 * localization data to be modified in the modal.
 */
const editModalConfig = ref<LocalizationModalInfo>({
  name: '',
  description: '',
  isArchived: false
})

/**
 * Reset edit modal.
 */
function resetForm () {
  editModalConfig.value = {
    name: localizationTableData.value?.info.name ?? '',
    description: localizationTableData.value?.info.description ?? '',
    isArchived: localizationTableData.value?.info.isArchived ?? false
  }
}

/**
 * Take localization build data from the modal and send it to the server.
 */
async function sendUpdatedLocalizationDataToServer () {
  const params = {
    name: editModalConfig.value.name,
    description: editModalConfig.value.description,
    isArchived: editModalConfig.value.isArchived
  }
  await gameServerApi.post(`/localization/${localizationId}`, params)
  showSuccessToast('Localization updated.')
  localizationRefresh()
}

/**
 * Can the displayed localization can be compared with the active localization?
 */
const canDiffToActive = computed(() => {
  return !localizationTableData.value?.info.isActive &&
    localizationTableData.value?.info.status === 'Succeeded'
})

/**
 * Can the displayed localization can be archived?
 */
const canArchive = computed(() => {
  return localizationTableData.value?.info.status !== 'Building'
})
</script>
