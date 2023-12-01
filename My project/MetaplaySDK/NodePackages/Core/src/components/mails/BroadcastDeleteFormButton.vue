<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Delete modal
meta-action-modal-button(
  id="action-delete-broadcast"
  permission="api.broadcasts.edit"
  variant="danger"
  modal-title="Delete broadcast"
  modal-size="md"
  ok-button-text="Delete"
  action-button-text="Delete"
  :on-ok="deleteBroadcast"
  )
  div Deleting this broadcast will prevent any more players from receiving it. However, it will not be removed from the inbox of those who have already received it.
  meta-no-seatbelts.mt-4

</template>

<script lang="ts" setup>
import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'

import { useRoute, useRouter } from 'vue-router'

const props = defineProps<{
  /** Id of the broadcast to delete */
  id: string
}>()

const gameServerApi = useGameServerApi()
const route = useRoute()
const router = useRouter()

const emits = defineEmits(['refresh'])

/**
 * Deletes the displayed broadcast and navigates back to the broadcast list view.
 */
async function deleteBroadcast () {
  await gameServerApi.delete(`/broadcasts/${props.id}`)
  const message = `Broadcast with id ${props.id} deleted.`
  showSuccessToast(message)

  // Close modal
  if (route.path.includes(`${props.id}`)) {
    // Navigate back to broadcast list page
    await router.push('/broadcasts')
  }
  emits('refresh')
}

</script>
