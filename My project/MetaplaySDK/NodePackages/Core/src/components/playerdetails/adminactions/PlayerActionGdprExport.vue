<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="playerData")
  //- Button to display the modal
  meta-button(
    data-cy="gdpr-export-button"
    variant="primary"
    block
    modal="action-gdpr-export"
    permission="api.players.gdpr_export"
  ).text-white
    fa-icon(icon="file-download").mr-2/
    | GDPR Export

  //- Modal
  b-modal#action-gdpr-export(title="GDPR Data Export" data-cy="action-gdpr-export" size="lg" @show="fetchData()" centered no-close-on-backdrop)
    b-row
      b-col(sm="6")
        h6 Personal Data of {{ playerData.model.playerName || 'n/a' }}
        p You can use this function to download personal data associated to player ID #[MBadge {{ playerData.id }}] stored in this deployment's database.
      b-col(sm="6")
        meta-alert(
          variant="secondary"
          title="Other Data Sources"
          ).small
            p Your company might have other personal data associated with this ID in third party tools like analytics. You'll need to export those separately.
            p The player ID might also show up in short lived system logs like automatic error reports. Those logs do not contain any personal infomation in addition to this ID and will be automatically deleted according to your retention policies.

    b-row(no-gutters)
      h6.mt-3 Export Preview
    b-row(no-gutters)
      pre(v-if="exportData" data-cy="export-payload" style="max-height: 10rem").border.rounded.bg-light.w-100.code-box {{ exportData }}

    template(#modal-footer="{ ok, cancel }")
      a(ref="gdprDownloadLink" :href="downloadUrl" :download="`${playerData.id}.json`" style="pointer-events: none")
      meta-button(variant="secondary" @click="cancel") Cancel
      meta-button(variant="primary" :disabled="!exportData" @click="gdprDownloadOk(ok)" safety-lock)
        fa-icon(icon="file-download").mr-2/
        | Download
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { useGameServerApi } from '@metaplay/game-server-api'
import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../../subscription_options/players'

const props = defineProps<{
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))
const downloadUrl = ref('')
const exportData = ref<string>()

async function fetchData () {
  exportData.value = (await gameServerApi.get(`/players/${playerData.value.id}/gdprExport`)).data
  downloadUrl.value = window.URL.createObjectURL(new Blob([JSON.stringify(exportData.value, null, 2)]))
}

const gdprDownloadLink = ref(null)
function gdprDownloadOk (okFunction: any) {
  if (gdprDownloadLink.value) {
    (gdprDownloadLink.value as HTMLLinkElement).click()
    okFunction()
  }
}
</script>
