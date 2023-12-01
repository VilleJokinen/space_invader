<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- TODO: This is a bit old code now and doesn't have a discreet loading state. Clean up when refactoring to MetaUiNext.
div(data-cy="system-redirect-card" v-if="backendStatusData")
  b-card
    b-card-title
      span.mt-2 📱 Client Compatibility Settings
    p Prevent incompatible clients from connecting and redirect clients compatible with a newer #[MBadge LogicVersion] to another backend deployment.

    p.mb-2
      span.font-weight-bold Active #[MBadge(variant="primary") LogicVersion]: {{ backendStatusData.clientCompatibilitySettings.activeLogicVersion }}
      p.small.text-muted Clients supporting only #[MBadge LogicVersion] {{ backendStatusData.clientCompatibilitySettings.activeLogicVersion - 1 }} or older will not be able to connect to this deployment.

    p.mb-2
      span.font-weight-bold New Version Redirect:
      MBadge.ml-2(variant="success" v-if="backendStatusData.clientCompatibilitySettings.redirectEnabled") On
      MBadge.ml-2(v-else) Off

    p(v-if="backendStatusData.clientCompatibilitySettings.redirectEnabled").small.text-muted Clients supporting #[MBadge LogicVersion] {{ backendStatusData.clientCompatibilitySettings.activeLogicVersion + 1 }} or newer will be redirected.
    p(v-else).small.text-muted Clients compatible with #[MBadge LogicVersion] {{ backendStatusData.clientCompatibilitySettings.activeLogicVersion + 1 }} or newer will not be able to connect to this deployment. Please note that clients older than {{ backendStatusData.clientCompatibilitySettings.activeLogicVersion + 1 }} that are still compatible with {{ backendStatusData.clientCompatibilitySettings.activeLogicVersion + 1 }} are also included in this rule!

    div(v-if="backendStatusData.clientCompatibilitySettings.redirectEnabled")
      span.font-weight-bold Redirect Settings
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td Host
            b-td {{ backendStatusData.clientCompatibilitySettings.redirectServerEndpoint.serverHost }}
          b-tr
            b-td Port
            b-td {{ backendStatusData.clientCompatibilitySettings.redirectServerEndpoint.serverPort }}
          b-tr
            b-td TLS enabled
            b-td {{ backendStatusData.clientCompatibilitySettings.redirectServerEndpoint.enableTls }}
          b-tr
            b-td CDN URL
            b-td {{ backendStatusData.clientCompatibilitySettings.redirectServerEndpoint.cdnBaseUrl }}

    div.text-right
      meta-button(
        variant="primary"
        data-cy="client-settings-button"
        modal="logic-redirect-modal"
        :disabled="!staticConfig?.supportedLogicVersions"
        permission="api.system.edit_logicversioning"
      ).mt-2 Edit Settings

  b-modal#logic-redirect-modal(v-if="staticConfig.supportedLogicVersions" data-cy="logic-redirect-modal" size="md" title="Update Client Compatibility Settings" @ok="setClientCompatibilitySettings()" @show="resetModal()" @hidden="resetModal()" centered no-close-on-backdrop)
    b-form
      span.font-weight-bold Active #[MBadge(variant="primary") LogicVersion]
        //small.text-muted (supported versions: {{ supportedLogicVersions.minVersion }} - {{ supportedLogicVersions.maxVersion }}):
      b-row.mb-4
        b-col
          b-form-group.mt-1.mb-1
            b-form-radio-group(v-model="activeLogicVersion" :options="logicVersionOptions" button-variant="primary")
          p.m-0.small.text-muted Only clients compatible with the active version will be allowed to connect.

          b-alert.mt-2(:show="activeLogicVersion && activeLogicVersion < backendStatusData.clientCompatibilitySettings.activeLogicVersion" variant="warning")
            h6(style="color: inherit") ⚠️ Tread carefully, brave knight
            p.m-0 Rolling back the active #[MBadge LogicVersion] can have very bad unintended consequences and should ideally never be done. Please make sure you know what you are doing before saving this action!
      div.d-flex.justify-content-between.mb-2
        span.font-weight-bold New Client Redirect Enabled
        meta-input-checkbox(v-model="redirectEnabled" name="redirectEnabled" size="lg" showAs="switch")

      span.font-weight-bold Redirect Endpoint
      b-form-group.mb-2
        label(for="redirectHost") Host
        b-form-input(v-model="redirectHost" :disabled="!redirectEnabled" :state="redirectEnabled ? !!redirectHost : null" required type="text")
      b-form-group.mb-2
        label(for="redirectPort") Port
        b-form-input(v-model="redirectPort" :disabled="!redirectEnabled" :state="redirectEnabled ? !!redirectPort : null" required type="number" @blur="redirectPort = (redirectPort && parseInt(redirectPort) > 0) ? parseInt(redirectPort) : 0")
      b-form-group.mb-3
        label(for="redirectCdnUrl") CDN URL
        b-form-input(v-model="redirectCdnUrl" :disabled="!redirectEnabled" :state="redirectEnabled ? !!redirectCdnUrl : null" required type="text")
      div.d-flex.justify-content-between.mb-2
        span TLS Enabled
        meta-input-checkbox(v-model="redirectTls" :disabled="!redirectEnabled" name="redirectTls" size="lg" showAs="switch")

    template(#modal-footer="{ ok, cancel }")
      meta-button(variant="secondary" @click="cancel") Cancel
      meta-button(variant="primary" data-cy="save-client-settings" :disabled="!isLogicVersionFormValid" @click="ok" safety-lock) Save Settings
//- TODO: This is a placeholder for until we migrate to tailwind with unified styling and extract this logic to own component.
div(v-else-if="backendStatusError")
  b-card
    b-card-title
      span.mt-2 📱 Client Compatibility Settings
    p.text-danger Failed to load the current client compatibility status.
    //- Note R25: this error wasn't actually drop-in compatible with the error callout as was missing a title and cause a null ref. Fixed by adding a fallback title inside the callout component.
    MErrorCallout(:error="backendStatusError")
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { MBadge, MErrorCallout } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getBackendStatusSubscriptionOptions, getStaticConfigSubscriptionOptions } from '../../subscription_options/general'

const gameServerApi = useGameServerApi()

const {
  data: backendStatusData,
  refresh: backendStatusTriggerRefresh,
  error: backendStatusError
} = useSubscription(getBackendStatusSubscriptionOptions())
const {
  data: staticConfig,
} = useSubscription(getStaticConfigSubscriptionOptions())

const activeLogicVersion = ref<any>(null)
const redirectHost = ref<any>(null)
const redirectPort = ref<any>(null)
const redirectTls = ref(false)
const redirectCdnUrl = ref<any>(null)
const redirectEnabled = ref(false)

const logicVersionOptions = computed(() => {
  const versions = []
  for (let i = staticConfig.value.supportedLogicVersions.minVersion; i <= staticConfig.value.supportedLogicVersions.maxVersion; i++) {
    versions.push({
      text: i === backendStatusData.value.clientCompatibilitySettings.activeLogicVersion ? `${i} (current)` : i,
      value: i
    })
  }
  return versions
})

const isLogicVersionFormValid = computed(() => {
  return (!redirectEnabled.value || (!!redirectHost.value && !!redirectPort.value && !!redirectCdnUrl.value))
})

async function setClientCompatibilitySettings () {
  const payload = {
    activeLogicVersion: activeLogicVersion.value,
    redirectEnabled: redirectEnabled.value,
    redirectServerEndpoint: {
      serverHost: redirectHost.value,
      serverPort: redirectPort.value,
      enableTls: redirectTls.value,
      cdnBaseUrl: redirectCdnUrl.value,
    }
  }
  await gameServerApi.post('/clientCompatibilitySettings', payload)
  showSuccessToast('Client compatibility settings updated.')
  backendStatusTriggerRefresh()
}

function resetModal () {
  const settings = backendStatusData.value.clientCompatibilitySettings
  const redirectEndpoint = settings.redirectServerEndpoint
  activeLogicVersion.value = settings.activeLogicVersion
  redirectEnabled.value = settings.redirectEnabled
  redirectHost.value = redirectEndpoint ? settings.redirectServerEndpoint.serverHost : ''
  redirectPort.value = redirectEndpoint ? settings.redirectServerEndpoint.serverPort : 9339
  redirectTls.value = redirectEndpoint ? settings.redirectServerEndpoint.enableTls : true
  redirectCdnUrl.value = redirectEndpoint ? settings.redirectServerEndpoint.cdnBaseUrl : ''
}
</script>
