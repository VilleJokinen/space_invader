<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  //- Card.
  b-card(title="Re-Delete Players")
    p Batch re-delete players that have been marked for deletion, but the deletion was un-done during a backup restore.

    div.text-right
      meta-button(
        variant="primary"
        modal="redelete-players-modal"
        permission="api.system.player_redelete"
      ) Open Re-Delete Menu

  //- Modal.
  b-modal#redelete-players-modal(title="Re-Delete Players" size="lg" @ok="redeletePlayers" @show="resetModal()" centered no-close-on-backdrop)
    b-row
      b-col(sm="6")
        p You can use this tool to upload player deletion logs and recover the deletion status of players in case something may have been lost during backup recovery.
        p.small.text-muted This feature is a practical way to respect GDPR and the right for your players to be forgotten even during backup recovery scenarios!

        span.font-weight-bold Log File
        b-form-file(
          class="text-nowrap text-truncate mt-1 mb-2"
          :value="file"
          @input="file = $event; fetchRedeletionList()"
          :state="Boolean(file)"
          placeholder="Choose a file or drop it here..."
          drop-placeholder="Drop file here..."
          )

        MInputDateTime(
          label="Re-Delete Cutoff Time"
          :model-value="cutoffTime"
          @update:model-value="onDateUpdated"
          max-date-time="now"
          )

        b-alert(v-if="error" show variant="warning") {{ error }}

      b-col(sm="6")
        h6.mb-3 Select Players
        MList(v-if="players && players.length > 0" showBorder)
          MListItem(v-for="(player, key) in players" :key="key").tw-px-3
            span {{ player.playerName || 'n/a' }}
            template(#top-right)
              span {{ player.playerId }}
            template(#bottom-left)
              div.small.text-muted.text-break-word Deleted #[meta-time(:date="player.scheduledDeletionTime")] by {{ player.deletionSource }}
              span
                span Marked for re-deletion:
                meta-input-checkbox.ml-1.pt-2(v-model="player.redelete" name="redelete" inline)
              MBadge.mt-2(v-if="player.redelete" variant="danger") To Be Deleted
              MBadge.mt-2(v-else) Skip

        div(v-else-if="players && players.length == 0")
          b-alert(variant="secondary" show) Based on the selected log file and cutoff time, there are no players who are eligible for re-deletion.

        div(v-else)
          b-alert(variant="secondary" show) Choose a valid log file to preview players for re-deletion.

        b-form(:class="players ? '' : 'text-muted'")
          h6.mt-3 Confirm
          div.d-flex.justify-content-between.mb-2
            span I know what I am doing
            meta-input-checkbox(v-model="confirmRedeletion" name="Confirm redeletion" size="lg" showAs="switch" :disabled="!players || players.length == 0")

    b-row(align-h="center" no-gutters)
      meta-no-seatbelts.mt-3/

    template(#modal-footer="{ ok, cancel }")
      meta-button(variant="secondary" @click="cancel") Cancel
      meta-button(variant="primary" @click="ok" :disabled="!isFormValid" safety-lock) Re-Delete Players

  // TODO: Show results.
  b-modal#redelete-players-results-modal(title="Import Success" size="md" ok-only centered no-close-on-backdrop)
    b-row(v-if="result != null")
      b-col
        b-list-group
          div(v-for="(entityType, entityKey) in result.entities")
            b-list-group-item(v-for="(entity, id) in entityType" :key="id")
              b-row(no-gutters align-h="between" align-v="center")
                span.font-weight-bold(v-if="String(entityKey) === 'player'") #[fa-icon(icon="user")] #[router-link(:to="`/players/${id}`") {{ id }}]
                span.font-weight-bold(v-else) {{ id }}

                MBadge(v-if="entity.success" variant="success") Imported
                MBadge(v-else variant="danger") Skipped
              b-row(no-gutters)
                small(:class="entity.success ? 'text-muted' : 'text-danger'") {{ entity.text }}
</template>

<script lang="ts" setup>
import { BFormFile } from 'bootstrap-vue'
import { DateTime, Duration } from 'luxon'
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { MList, MListItem, MInputDateTime, MBadge } from '@metaplay/meta-ui-next'

const gameServerApi = useGameServerApi()
const players = ref<any>(null)
const cutoffTime = ref<DateTime>(DateTime.now())
const result = ref<any>(null)
const error = ref<string>()
const confirmRedeletion = ref(false)
const file = ref<any>(null)

const isFormValid = computed(() => {
  return players.value !== null && confirmRedeletion.value
})
const defaultCutoffTime = computed((): DateTime => {
  const offset = Duration.fromDurationLike({ days: 60 })
  return DateTime.now().minus(offset)
})

function resetModal () {
  players.value = null
  result.value = null
  confirmRedeletion.value = false
  cutoffTime.value = defaultCutoffTime.value
  file.value = null
}
async function redeletePlayers () {
  // Build the request.
  const formData = new FormData()
  formData.append('file', file.value)
  formData.append('cutoffTime', cutoffTime.value.toISO() as string)
  players.value.filter((p: any) => p.redelete).forEach((p: any) => {
    formData.append('playerIds', p.playerId)
  })
  // Send.
  await gameServerApi.post(
    'redeletePlayers/execute',
    formData,
    {
      headers: { 'Content-Type': 'multipart/form-data' }
    }
  )
  const message = 'Player re-deletion started.'
  showSuccessToast(message)
}

/**
 * Utility function to prevent undefined inputs.
 */
async function onDateUpdated (value?: DateTime) {
  if (!value) return
  cutoffTime.value = value

  // Handle empty state.
  if (file.value !== null) {
    await fetchRedeletionList()
  }
}

async function fetchRedeletionList () {
  // Reset the results.
  players.value = null
  confirmRedeletion.value = false
  error.value = undefined

  // Build the request.
  const formData = new FormData()
  formData.append('file', file.value)
  formData.append('cutoffTime', cutoffTime.value.toISO() as string)

  // Send.
  try {
    const res = (await gameServerApi.post(
      'redeletePlayers/list',
      formData,
      {
        headers: { 'Content-Type': 'multipart/form-data' }
      })).data

    // Process results.
    players.value = res.playerInfos.map((p: any) => ({ ...p, redelete: true }))
  } catch (e: any) {
    error.value = e.response.data.error
  }
}
</script>
