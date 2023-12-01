<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  //- Button to display the modal
  meta-button(permission="api.guilds.edit_details" classes="w-100" variant="primary" block modal="action-edit-guild-name-and-description") Edit Name and Description

  //- Modal
  b-modal#action-edit-guild-name-and-description(title="Edit Guild Name and Description" size="md" @ok="change" @show="resetModal" @hidden="resetModal" centered no-close-on-backdrop)
    b-row(no-gutters).mb-3
      span.font-weight-bold Display Name
      b-form-input.mt-1(v-model="newDisplayName" :state="isValidDisplayName" @update="validate")
      span.small.text-muted Same rules are applied to name validation as changing it in-game.

    b-row(no-gutters).mb-2
      span.font-weight-bold Description
      b-form-input.mt-1(v-model="newDescription" :state="isValidDescription" @update="validate")

    template(#modal-footer="{ ok, cancel }")
      meta-button(variant="secondary" @click="cancel") Cancel
      meta-button(variant="primary" :disabled="!isValid" @click="ok" safety-lock).text-white Update Fields
</template>

<script lang="ts" setup>
import { debounce } from 'lodash-es'
import { ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'

import { getSingleGuildSubscriptionOptions } from '../../../subscription_options/guilds'

const props = defineProps<{
  guildId: string
}>()

const {
  data: guildData,
  refresh: guildTriggerRefresh,
} = useSubscription(getSingleGuildSubscriptionOptions(props.guildId))

const gameServerApi = useGameServerApi()
const newDisplayName = ref('')
const newDescription = ref('')
const isValidDisplayName = ref<boolean | null>(null)
const isValidDescription = ref<boolean | null>(null)
const isValid = ref(false)

function resetModal () {
  newDisplayName.value = guildData.value.model.displayName
  newDescription.value = guildData.value.model.description
  isValidDisplayName.value = null
  isValidDescription.value = null
  isValid.value = false
}

const validateDebounce = debounce(async () => {
  const isDisplayNameChanged = newDisplayName.value !== guildData.value.model.displayName
  const isDescriptionChanged = newDescription.value !== guildData.value.model.description
  if (isDisplayNameChanged || isDescriptionChanged) {
    const res = (await gameServerApi.post(`/guilds/${guildData.value.id}/validateDetails`, { NewDisplayName: newDisplayName.value, NewDescription: newDescription.value })).data
    isValidDisplayName.value = res.displayNameWasValid
    isValidDescription.value = res.descriptionWasValid
  }
  isValid.value = (isDisplayNameChanged || isDescriptionChanged) && Boolean(isValidDisplayName.value) && Boolean(isValidDescription.value)
}, 300)

async function validate () {
  isValidDisplayName.value = null
  isValidDescription.value = null
  isValid.value = false
  await validateDebounce()
}

async function change () {
  try {
    await gameServerApi.post(`/guilds/${guildData.value.id}/changeDetails`, { NewDisplayName: newDisplayName.value, NewDescription: newDescription.value })
    const message = `Guild renamed to '${newDisplayName.value}'.`
    showSuccessToast(message)
  } finally {
    guildTriggerRefresh()
  }
}
</script>
