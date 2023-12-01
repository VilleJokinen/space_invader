<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="playerData")
  meta-action-modal-button(
    id="action-edit-name"
    action-button-text="Edit Name"
    variant="warning"
    modal-title="Edit Player Name"
    :onOk="updateName"
    @show="resetModal"
    :ok-button-disabled="!isValid"
    permission="api.players.edit_name"
    block
  )
    span.font-weight-bold Current Name
    p.mt-1 {{ playerData.model.playerName || 'n/a' }}

    span.font-weight-bold New Name
    b-form-input(v-model="newName" data-cy="name-input" :state="isValid" placeholder="DarkAngel87" @update="validate")
    span.small.text-muted Same rules are applied to name validation as changing it in-game.
</template>

<script lang="ts" setup>
import { debounce } from 'lodash-es'
import { ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../../subscription_options/players'

const props = defineProps<{
  /**
   * ID of the player to rename.
   */
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const {
  data: playerData,
  refresh: playerRefresh,
} = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))
const newName = ref('')
const isValid = ref<boolean | null>(false)

function resetModal () {
  newName.value = ''
  isValid.value = false
}

const validateDebounce = debounce(async function () {
  isValid.value = (await gameServerApi.post(`/players/${playerData.value.id}/validateName`, { NewName: newName.value })).data.nameWasValid
}, 300)

async function validate () {
  isValid.value = null
  if (newName.value === '') {
    isValid.value = false
  } else {
    await validateDebounce()
  }
}
async function updateName () {
  try {
    await gameServerApi.post(`/players/${playerData.value.id}/changeName`, { NewName: newName.value })
    const message = `Player '${playerData.value.id}' is now '${newName.value}'.`
    showSuccessToast(message)
  } finally {
    playerRefresh()
  }
}
</script>
