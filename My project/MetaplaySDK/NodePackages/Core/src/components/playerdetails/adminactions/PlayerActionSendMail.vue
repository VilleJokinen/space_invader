<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="playerData")
  meta-action-modal-button(
    id="action-mail"
    permission="api.players.mail"
    action-button-icon="paper-plane"
    action-button-text="Send Mail"
    block
    modal-title="Send In-Game Mail"
    @show="resetModal"
    ok-button-icon="paper-plane"
    ok-button-text="Send Mail"
    :okButtonDisabled="!isValid"
    :onOk="sendMail"
    )
    template(#modal-subtitle)
      span.small.mall.text-no-transform.text-muted.ml-2 Player's language:&#32;
        meta-language-label(:language="playerData.model.language" variant="badge")

    b-form(@submit.prevent)
      meta-generated-form(
        :typeName="'Metaplay.Core.InGameMail.MetaInGameMail'"
        v-model="mail"
        :forcedLocalization="playerData.model.language"
        :page="'PlayerActionSendMail'"
        @status="isValid = $event"
        )
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../../subscription_options/players'

import MetaGeneratedForm from '../../generatedui/components/MetaGeneratedForm.vue'
import MetaLanguageLabel from '../../MetaLanguageLabel.vue'

const props = defineProps<{
  /**
   * ID of the player to send the mail to.
   */
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const {
  data: playerData,
  refresh: playerRefresh,
} = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

const mail = ref<any>({})
const isValid = ref(false)

function resetModal () {
  mail.value = {}
  isValid.value = false
}

async function sendMail () {
  await gameServerApi.post(`/players/${playerData.value.id}/sendMail`, mail.value)
  showSuccessToast(`In-game mail sent to ${playerData.value.model.playerName || 'n/a'}.`)
  playerRefresh()
}
</script>
