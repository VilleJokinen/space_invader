<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  meta-list-card.mt-2(
    data-cy="player-nfts-card"
    title="NFTs"
    icon="cubes"
    :tooltip="`Shows NFTs owned by ${playerData.model.playerName || 'n/a'}.`"
    :itemList="playerData.nfts"
    :emptyMessage="`${playerData.model.playerName || 'n/a'} doesn't have any NFTs.`"
    permission="api.players.view"
  )
    template(#item-card="{ item }")
      MListItem(:avatarUrl="item.imageUrl")
        span(v-if="item.name") {{ item.name }}
        span(v-else).font-italic Unnamed {{ item.typeName }}

        template(#top-right) {{ item.collectionId }}/{{ item.tokenId }}

        template(#bottom-left)
          div(v-if="item.description") {{ item.description }}
          span.font-italic(v-else) No description.

        template(#bottom-right)
          meta-button(link :to="`/web3/nft/${item.collectionId}/${item.tokenId}`" data-cy="view-nft" permission="api.nft.view") View NFT

    template(#description) NFT ownerships update automatically but you can also #[meta-button(link @click="refreshOwnership" permission="api.nft.refresh_from_ledger" data-cy="nft-refresh-button") refresh now].
</template>

<script lang="ts" setup>
import { MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

const gameServerApi = useGameServerApi()

const props = defineProps<{
  playerId: string
}>()

const {
  data: playerData,
  refresh: playerRefresh,
} = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

async function refreshOwnership () {
  showSuccessToast('NFT ownership update triggered. It might take a few moments.')
  await gameServerApi.post(`players/${props.playerId}/refreshOwnedNfts`)
  showSuccessToast('NFT ownerships updated.')
  playerRefresh()
}
</script>
