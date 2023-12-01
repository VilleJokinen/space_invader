<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!collectionsData"
  :meta-api-error="generalNftInfoError"
)
  template(#overview)
    meta-page-header-card(data-cy="web3-overview-card")
      template(#title) Web3 Ledgers
      template(#subtitle) These are the currently configured blockchain ledgers in this environment.
      p See the Web3 section in #[b-link(to="/runtimeOptions") runtime options] for more details.

      //- Styling below to make this look like old b-list/group.
      MList(v-if="ledgers && ledgers.length > 0" class="tw-border tw-border-neutral-300 tw-rounded-md")
        MListItem(
          v-for="ledger in ledgers"
          :key="ledger.displayName"
          class="tw-px-3"
          data-cy="ledger-list"
          )
          | {{ ledger.displayName }}
          template(#top-right) {{ ledger.networkName }}
      //- TODO R26 Add v-else for MList
      //- div(v-else)

  template(#center-column)
    meta-list-card(
      data-cy="nft-collections-list-card"
      :itemList="collectionsData"
      :searchFields="searchFields"
      title="NFT Collections"
      emptyMessage="No NFT configured Collections"
      )
      template(#item-card="{ item }")
        MListItem(:avatarUrl="item.ledgerInfo?.iconUrl")
          | {{ item.ledgerInfo?.name ?? 'Name unknown' }}
          template(#top-right) {{ item.ledgerName }}
          template(#bottom-left) {{ item.ledgerInfo?.description ?? 'Description unknown' }}
          template(#bottom-right): meta-button(link :to="`/web3/nft/${item.collectionId}`" data-cy="view-nft-collection") View NFT Collection

  template(#default)
    meta-raw-data(:kvPair="collectionsData", name="collections")
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getGeneralNftInfoSubscriptionOptions } from '../subscription_options/web3'

const {
  data: generalNftInfoData,
  error: generalNftInfoError
} = useSubscription(getGeneralNftInfoSubscriptionOptions())

const ledgers = computed(() => generalNftInfoData.value?.ledgers)
const collectionsData = computed(() => generalNftInfoData.value?.collections)

const searchFields = ['collectionId', 'contractAddress']
</script>
