<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="playerData")
  //- Button to display the modal
  meta-button(
    variant="danger"
    data-cy="delete-player-button"
    block
    modal="action-delete-player"
    permission="api.players.edit_scheduled_deletion"
  ) {{ !isCurrentlyScheduledForDeletion ? 'Delete Player' : 'Cancel Deletion' }}

  //- Modal
  b-modal#action-delete-player(title="Schedule Player for Deletion" data-cy="delete-player-modal" size="md" @ok="updateDeletionSchedule" @show="resetModal()" @hidden="resetModal()" centered no-close-on-backdrop)
    b-form
      div.d-flex.justify-content-between.mb-2
        span.font-weight-bold Scheduled for deletion
        meta-input-checkbox(v-model="scheduleForDeletion" name="Player banned" size="lg" showAs="switch")

      MInputDateTime(
        :model-value="scheduledDateTime"
        @update:model-value="onScheduleDateTimeChange"
        min-date-time="now"
        :disabled="!scheduleForDeletion"
        )

      span.small.text-muted
        p(v-if="!isCurrentlyScheduledForDeletion && !scheduleForDeletion")
          | This player is not currently scheduled for deletion.
        p(v-else-if="!isCurrentlyScheduledForDeletion && scheduleForDeletion && scheduledDateTime")
          | This player will be deleted #[span.font-weight-bold #[meta-time(:date="scheduledDateTime" showAs="timeago")]].
        p(v-else-if="isCurrentlyScheduledForDeletion && !scheduleForDeletion && scheduledDateTime")
          | This player will no longer be deleted.
        p(v-else-if="scheduledDateTime")
          | This player is currently scheduled for deletion #[span.font-weight-bold #[meta-time(:date="scheduledDateTime" showAs="timeago")]].
          | {{ deletionStatusText }}

      b-alert(show variant="secondary" v-if="!isCurrentlyBanned && !isCurrentlyScheduledForDeletion")
        p Scheduling a player for deletion does not prevent the player from playing the game. The player can still connect and play the game until the deletion has completed. If you wish to stop the player from connecting you should also ban them.
      b-alert(show variant="secondary" v-if="isCurrentlyBanned && isCurrentlyScheduledForDeletion")
        p The player is currently banned and will not be able to play the game, even if you cancel the scheduled deletion. To allow the player to play the game you must also un-ban them.

    //- Add default delay to runtime configs -> pass to dashboard (7 days)
    template(#modal-footer="{ ok, cancel }")
      meta-button(variant="secondary" @click="cancel") Cancel
      meta-button(v-if="scheduleForDeletion && !isScheduledDateTimeInTheFuture" variant="danger" :disabled="!isFormChanged" data-cy="confirm-delete-player" @click="ok" safety-lock).text-white Delete Immediately
      meta-button(v-else-if="scheduleForDeletion" variant="danger" :disabled="!isFormChanged" data-cy="confirm-delete-player" @click="ok" safety-lock).text-white Schedule for Deletion
      meta-button(v-else variant="danger" :disabled="!isFormChanged" data-cy="confirm-delete-player" @click="ok" safety-lock).text-white Unschedule for Deletion
</template>

<script lang="ts" setup>
import { DateTime } from 'luxon'
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'
import { parseASPNetDurationToLuxon } from '../../../coreUtils'

import { useCoreStore } from '../../../coreStore'
import { getSinglePlayerSubscriptionOptions } from '../../../subscription_options/players'

import { MInputDateTime } from '@metaplay/meta-ui-next'

const props = defineProps<{
  /**
   * Id of the player to target the reset action at.
   **/
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const coreStore = useCoreStore()

/**
 * Subscribe to player data used to render this component.
 */
const {
  data: playerData,
  refresh: playerRefresh,
} = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

/**
 * Specifies the date and time when the target player will be deleted.
 */
const scheduledDateTime = ref<DateTime>(DateTime.now().plus({ days: 7 }))

/**
 * When true player is to be deleted at the target date and time.
 */
const scheduleForDeletion = ref(false)

/**
 * Utility function to prevent undefined inputs.
 */
function onScheduleDateTimeChange (value?: DateTime) {
  if (!value) return
  scheduledDateTime.value = value
}

/**
 * When true the target player is currently scheduled for deletion.
 */
const isCurrentlyScheduledForDeletion = computed(() => {
  return playerData.value.model.deletionStatus !== 'None'
})

/**
 * Checks whether the player is currently banned.
 */
const isCurrentlyBanned = computed(() => {
  return playerData.value.model.isBanned
})

/**
 * Checks whether the deletion status or scheduled deletion date/time of a target player has been changed.
 */
const isFormChanged = computed(() => {
  if (scheduleForDeletion.value !== isCurrentlyScheduledForDeletion.value) {
    // Toggle was changed.
    return true
  } else if (scheduleForDeletion.value && !scheduledDateTime.value.equals(DateTime.fromISO(playerData.value.model.scheduledForDeletionAt))) {
    // Was already scheduled, but the date was changed.
    return true
  } else {
    return false
  }
})

/**
 * Indicates whether the target player will be deleted immediately or at a future date/time.
 */
const isScheduledDateTimeInTheFuture = computed(() => {
  if (!scheduledDateTime.value) return false
  else return scheduledDateTime.value.diff(DateTime.now()).toMillis() >= 0
})

/**
 * Human readable description of how the player's deletion was scheduled.
 */
const deletionStatusText = computed(() => {
  switch (playerData.value.model.deletionStatus) {
    case 'ScheduledByAdmin':
      return 'The deletion was scheduled by a dashboard user.'
    case 'ScheduledByUser':
      return 'The deletion was requested in-game by the player.'
    case 'ScheduledBySystem':
      return 'The deletion was scheduled by an automated system.'
    case 'None':
    case 'Deleted':
    default:
      return 'Unexpected deletion status.'
  }
})

/**
 * Reset state of the modal.
 */
function resetModal () {
  scheduleForDeletion.value = isCurrentlyScheduledForDeletion.value
  if (scheduleForDeletion.value) {
    // User has specified an exact time.
    scheduledDateTime.value = DateTime.fromISO(playerData.value.model.scheduledForDeletionAt)
  } else {
    // Use default date of current time + delay.
    const delay = parseASPNetDurationToLuxon(coreStore.hello.playerDeletionDefaultDelay)
    const delayedDateTime = DateTime.now().plus(delay)
    scheduledDateTime.value = delayedDateTime
  }
}

/**
 * Update the date and time when the target player is to be deleted.
 */
async function updateDeletionSchedule () {
  const message = `${playerData.value.model.playerName || 'n/a'} ${scheduleForDeletion.value ? 'scheduled for deletion' : 'is no longer scheduled for deletion'}.`
  if (scheduleForDeletion.value) {
    await gameServerApi.put(`/players/${playerData.value.id}/scheduledDeletion`, { scheduledForDeleteAt: scheduledDateTime.value })
  } else {
    await gameServerApi.delete(`/players/${playerData.value.id}/scheduledDeletion`)
  }
  showSuccessToast(message)
  playerRefresh()
}
</script>
