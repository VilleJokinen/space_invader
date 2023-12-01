<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  meta-list-card(
    data-cy="player-inbox-card"
    title="Inbox"
    icon="inbox"
    :itemList="playerData?.model.mailInbox"
    :getItemKey="getItemKey"
    tooltip="Messages currently waiting in the player's inbox."
    :searchFields="searchFields"
    :filterSets="filterSets"
    :sortOptions="sortOptions"
    :defaultSortOption="defaultSortOption"
    emptyMessage="Inbox empty."
    )
    template(#item-card="{ item: mail }")
      MCollapse(extraMListItemMargin)
        template(#header)
          MListItem(data-cy="mail-item")
            //- Name and attachment icon
            | {{ mail.contents.description }} #[fa-icon(icon="paperclip" size="sm" v-if="Object.keys(mail.contents.consumableRewards).length > 0").ml-1.text-muted]
            template(#top-right)
              //- ID and delete button
              span(class="tw-mr-1 tw-text-sm tw-text-neutral-500") {{ mail.id }}
              meta-button(
                modal="confirm-mail-delete"
                variant="outline-danger"
                size="sm"
                permission="api.players.mail"
                subtle
                @click="mailToDelete = mail"
                class="tw-relative tw-bottom-0.5"
                )
                fa-icon(icon="trash-alt")
            template(#bottom-right)
              //- Time
              meta-time(:date="mail.sentAt" showAs="timeagoSentenceCase")

        //- Collapse content (mail contents)
        meta-generated-content(
          :value="mail.contents"
          :previewLocale="playerData.model.language"
        ).mt-2.mb-1

  //- Modal
  b-modal#confirm-mail-delete(v-if="playerData" data-cy="confirm-mail-delete" title="Delete Mail" size="md" @hidden="mailToDelete = null" @ok="deleteMail(mailToDelete)" centered no-close-on-backdrop)
    p #[MBadge {{ playerData.model.playerName || 'n/a' }}] has not claimed this mail yet. Are you sure that you want to delete the mail #[MBadge {{ mailToDeleteTitle }}]?

    meta-no-seatbelts

    template(v-slot:modal-footer="{ ok, cancel }")
      meta-button(variant="secondary" @click="cancel") Cancel
      meta-button(variant="danger" data-cy="confirm-delete-button" @click="ok" safety-lock).text-white Delete Mail

</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { MetaListFilterSet, MetaListFilterOption, MetaListSortDirection, MetaListSortOption, showSuccessToast } from '@metaplay/meta-ui'
import { MBadge, MCollapse, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

import MetaGeneratedContent from '../generatedui/components/MetaGeneratedContent.vue'

const props = defineProps<{
  /**
   * Id of the player whose inbox to show.
   */
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const {
  data: playerData,
  refresh: playerRefresh,
} = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

const searchFields = [
  'contents.description'
]

const filterSets = [
  new MetaListFilterSet('attachments',
    [
      new MetaListFilterOption('Has attachments', (x: any) => x.contents.consumableRewards.length > 0),
      new MetaListFilterOption('No attachments', (x: any) => x.contents.consumableRewards.length === 0)
    ]
  )
]

const sortOptions = [
  new MetaListSortOption('Time', 'sentAt', MetaListSortDirection.Ascending),
  new MetaListSortOption('Time', 'sentAt', MetaListSortDirection.Descending),
]

const defaultSortOption = 1
const mailToDelete = ref<any>(null)

const mailToDeleteTitle = computed(() => {
  return mailToDelete.value ? mailToDelete.value.contents.description as string : ''
})

function getItemKey (item: any) {
  return item.id
}

async function deleteMail (mail: any) {
  await gameServerApi.delete(`/players/${playerData.value.id}/deleteMail/${mail.id}`)
  const message = `${mail.contents.description}' deleted from ${playerData.value.model.playerName || 'n/a'}.`
  showSuccessToast(message)
  playerRefresh()
}
</script>
