<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="playerData || allowDebugExport")
  //- Button to display the modal
  meta-button(
    variant="danger"
    data-cy="copy-player-button"
    :block="!noBlock"
    modal="action-player-copy"
    permission="api.entity_archive.export"
  ) Export Player

  //- Modal
  b-modal#action-player-copy(title="Export Player" size="md" @show="fetchExportData" centered no-close-on-backdrop)
    b-row
      b-col
        b-row(no-gutters)
          p This is the persisted player data for #[MBadge {{ playerData?.model.playerName || 'n/a' }}] as an #[MBadge entity archive]. You can use it for raw debugging as well as copying players between deployments.
          p.small.text-muted Please note that you are taking a copy of a player's personally identifiable information (PII) and you should only do this if you have a legitimate reason or the player's consent.

        b-row(no-gutters align-v="baseline")
          h6 Serialized Player Data
          meta-clipboard-copy(:contents="exportArchive" :disabled="exportSize >= sizeLimit" data-cy="copy-player-to-clipboard").ml-1

        b-row(no-gutters).mt-2
          div(v-if="exportSize < sizeLimit").w-100
            div.entity-archive.text-monospace.border.rounded.bg-light.w-100(style="max-height: 30rem; overflow-x: hidden")
              span(data-cy="export-payload") {{ exportArchive }}
            div.text-right.text-muted.w-100.small #[meta-abbreviate-number(:value="exportSize")]b
          div(v-else)
            b-alert(show variant="secondary")
              p Export preview and copying disabled because of its large size of #[meta-abbreviate-number(:value="exportSize")]b! You can still download the data 👍
              p.small.text-muted Some OS & browser combinations are known to have performance issues parsing large blobs of data. Let us know if you are one of them and we'll figure it out!

    template(#modal-footer="{ ok, cancel }")
      meta-button(data-cy="copy-player-close-button" @click="cancel") Close
      a(:href="downloadUrl" :download="exportFileName" :disabled="!exportArchive")
        meta-button(variant="danger" downloadsafety-lock)
          fa-icon(icon="file-download").mr-2/
          | Download
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { roughSizeOfObject } from '@metaplay/meta-ui'
import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../../subscription_options/players'

const props = defineProps<{
  /**
   * Id of the target player whose data is to be exported.
   */
  playerId: string
  /**
   * Allow player exporting even when the player can't be loaded by the server, due to
   * deserialization or migration issues. Should only be used for debugging scenarios.
   * Defaults to false.
   */
  allowDebugExport?: boolean
  /**
   * Optional: Disable the default button style. Should refactor this away in the next iteration.
   */
  noBlock?: boolean
}>()

const gameServerApi = useGameServerApi()

/**
 * Subscribe to the target player's data.
 */
const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

/**
 * The maximum file size that can be exported at a given time.
 */
const sizeLimit = 300000

/**
 * URL used to download the exported JSON file.
 */
const downloadUrl = ref('')

/**
 * Archive or record containing the player data that is to be exported.
 */
const exportArchive = ref('')

/**
 * The name of the JSON file that is to be exported.
 */
const exportFileName = `${props.playerId.replace(':', '_')}.export.json`

/**
 * Estimated size of the JSON file that is to be exported.
 */
const exportSize = computed(() => {
  return roughSizeOfObject(exportArchive.value)
})

/**
 * Custom entity data type.
 */
interface EntityInfo { player: string[], guild?: string[] }

/**
 * Retrieve the player data that is to be exported.
 */
async function fetchExportData () {
  const entities: EntityInfo = {
    player: [
      props.playerId
    ]
  }

  if (playerData.value?.guild) {
    entities.guild = [playerData.value.guild.id]
  }

  const payload = { entities, allowExportOnFailure: props.allowDebugExport }
  exportArchive.value = JSON.stringify((await gameServerApi.post('/entityArchive/export', payload)).data)
  downloadUrl.value = window.URL.createObjectURL(new Blob([exportArchive.value]))
}
</script>

<style scoped>
.entity-archive {
  font-size: 8pt;
  padding: 0.5rem;
  overflow-wrap: break-word;
  word-break: break-all;
  overflow: scroll;
}

.entity-archive pre {
  overflow: visible;
  margin: 0;
}
</style>
