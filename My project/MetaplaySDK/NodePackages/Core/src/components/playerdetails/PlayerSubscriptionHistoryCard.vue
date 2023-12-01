<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="playerData")
  meta-list-card(
    data-cy="player-subscriptions-history-card"
    title="Subscription History"
    icon="money-check-alt"
    :itemList="subscriptionList"
    :sortOptions="sortOptions"
    emptyMessage="No subscriptions... yet!"
  ).mb-3
    //- Mapping slot.item to subscription for convenience
    template(#item-card="{ item: subscription }")
      MCollapse(extraMListItemMargin)
        //- Row header
        template(#header)
          MListItem
            | {{ subscription.productId }}
            span.text-muted.ml-1 ${{ subscription.spend.toFixed(2) }}

            template(#badge)
              MBadge(v-if="isActive(subscription.expirationTime)" variant="success") Active
              MBadge(v-else) Expired

            template(#top-right)
              meta-time(:date="subscription.startTime" showAs="date")

            template(#bottom-left)
              //- Expired due to re-use
              div(v-if="subscription.latestInstanceIsDisabledDueToReuse").text-danger The subscription was disabled because it was used on another player account.

            template(#bottom-right)
              //- Active
              div(v-if="isActive(subscription.expirationTime)")
                span(v-if="subscription.renewalStatus === 'ExpectedToRenew'") Renewing #[meta-time(:date="subscription.expirationTime")]
                span(v-else-if="subscription.renewalStatus === 'NotExpectedToRenew'") Set to expire in #[meta-time(:date="subscription.expirationTime")]
                span(v-else).text-warning Unhandled renewal status: {{ subscription.renewalStatus }}
              //- Expired naturally
              div(v-else) Expired #[meta-time(:date="subscription.expirationTime")]

        //- Collapse content
        div.font-weight-bold.mb-1 Overview
        MList(showBorder class="tw-my-2")
          MListItem(class="tw-px-5 !tw-py-2.5" condensed) Start time
            template(#top-right) #[meta-time(:date="subscription.startTime" showAs="datetime")]
          MListItem(class="tw-px-5 !tw-py-2.5" condensed) Expiration time
            template(#top-right) #[meta-time(:date="subscription.expirationTime" showAs="datetime")]
          MListItem(class="tw-px-5 !tw-py-2.5" condensed) Auto-renewal status
            template(#top-right)
              MBadge(v-if="subscription.renewalStatus === 'ExpectedToRenew'" variant="success") On
              MBadge(v-else-if="subscription.renewalStatus === 'NotExpectedToRenew'") Off
              span(v-else) {{ subscription.renewalStatus }}

        div.font-weight-bold.mb-1 Individual Subscriptions
        MList(v-if="subscription.subscriptionInstances && subscription.subscriptionInstances.length > 0" showBorder class="tw-my-2")
          MListItem(
            v-for="(instance, instanceId) in subscription.subscriptionInstances"
            :key="instanceId"
            class="tw-px-3"
            striped
            ) {{ instance.platform }}
            MTooltip.ml-1(:content="instance.spendExplanation") ${{ instance.spend.toFixed(2) }}
            template(#top-right)
              MBadge(v-if="isActive(instance.lastKnownState.expirationTime)" variant="success") Active
              MBadge(v-else) Expired
            template(#bottom-left)
              span {{ instanceId }}
              div.text-danger(v-if="instance.disabledDueToReuse")
                div The subscription was disabled because it was used on {{ maybePlural(instance.otherPlayers.length, 'other player account') }}:
                div(v-for="otherPlayerId in instance.otherPlayers") #[b-link( :to="`/players/${otherPlayerId}`") {{ otherPlayerId }} ]

            template(#bottom-right)
              div(v-if="instance.lastKnownState").text-right Start: #[meta-time(:date="instance.lastKnownState.startTime")]
              div(v-if="instance.lastKnownState").text-right Expiry: #[meta-time(:date="instance.lastKnownState.expirationTime")]
              div(v-if="instance.lastKnownState").text-right Periods: {{ instance.lastKnownState.numPeriods }}
        //- TODO R26 Add v-else for MList
        //- div(v-else)
        div.text-right.mb-2: meta-button(v-if="enableSubscriptionRemoving" size="sm" modal="remove-sub" @click="selectedSubscription = subscription" permission="api.players.remove_iap_subscription" variant="danger") Detach Subscription

  //- Detach modal
  b-modal#remove-sub(size="md" @ok="removeSubscription(selectedSubscription.productId)" @hidden="resetModal" centered no-close-on-backdrop)
    template(#modal-title) Detach Subscription

    p You are about to detach the #[MBadge {{ selectedSubscription.productId }}] subscription from #[MBadge {{ playerData.model.playerName }}]. This will also remove the related subscription IAP from the player's purchase history.
    div.small.text-muted Players can re-activate their subscriptions by reclaiming IAPs from within the game. Detaching a subscription does not refund the purchase and instead is primarily a development-only tool.

    b-row.justify-content-center
      meta-no-seatbelts(:name="playerData.model.playerName").mt-4/

    template(#modal-footer="{ ok, cancel }")
      meta-button(variant="secondary" @click="cancel") Cancel
      meta-button(variant="danger" @click="ok" safety-lock) Detach Subscription
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import { DateTime } from 'luxon'

import { useGameServerApi } from '@metaplay/game-server-api'
import { MetaListSortDirection, MetaListSortOption, maybePlural, showSuccessToast } from '@metaplay/meta-ui'
import { MTooltip, MBadge, MCollapse, MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

import { useCoreStore } from '../../coreStore'

const props = defineProps<{
  /**
   * Id of the player whose subscription history we want to show.
   */
  playerId: string
}>()

const gameServerApi = useGameServerApi()
const coreStore = useCoreStore()
const {
  data: playerData,
  refresh: playerRefresh,
} = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))
const sortOptions = [
  new MetaListSortOption('Start time', 'startTime', MetaListSortDirection.Ascending),
  new MetaListSortOption('Start time', 'startTime', MetaListSortDirection.Descending),
  new MetaListSortOption('Expiry time', 'expirationTime', MetaListSortDirection.Ascending),
  new MetaListSortOption('Expiry time', 'expirationTime', MetaListSortDirection.Descending),
]
const selectedSubscription = ref<any>({})

const enableSubscriptionRemoving = computed(() => {
  return coreStore.hello.enableRemoveIapSubscriptions || playerData.value.model.isDeveloper
})

const subscriptionList = computed(() => {
  // Create a lookup for past subscription purchases, to
  // enrich the subscription data with some purchase info.
  // Key is the originalTransactionId of the purchase.
  const subscriptionInstancePurchases: any = {}
  for (const purchaseEvent of playerData.value.model.inAppPurchaseHistory) {
    // Ignore purchases of non-subscription products.
    if (purchaseEvent.subscriptionQueryResult === null) {
      continue
    }

    const originalTransactionId = purchaseEvent.originalTransactionId
    subscriptionInstancePurchases[originalTransactionId] = purchaseEvent
  }

  // Create the subscriptions list.
  const subscriptions = []
  for (const productId in playerData.value.model.iapSubscriptions.subscriptions) {
    const subscription = playerData.value.model.iapSubscriptions.subscriptions[productId]

    let subscriptionSpend = 0

    // Enrich subscriptionInstances entries, and calculate total spend on subscription.
    const instances: any = {}
    for (const instanceId in subscription.subscriptionInstances) {
      const instance = subscription.subscriptionInstances[instanceId]
      const purchase = subscriptionInstancePurchases[instanceId]
      const platform = purchase?.platform // \note purchase should never be missing - the `?.` is just being defensive
      const referencePrice = purchase?.referencePrice ?? 0 // \note ditto

      let instanceSpend
      let instanceSpendExplanation
      if (instance.disabledDueToReuse) {
        instanceSpend = 0
        instanceSpendExplanation = 'Spend is omitted from this subscription, because it is being used on another player account.'
      } else if (instance.lastKnownState?.isAcquiredViaFamilySharing ?? false) {
        instanceSpend = 0
        instanceSpendExplanation = 'Spend is omitted from this subscription, because this player acquired it via Family Sharing instead of purchasing it themselves.'
      } else {
        const numPeriods = instance.lastKnownState?.numPeriods ?? 1
        instanceSpend = numPeriods * referencePrice
        instanceSpendExplanation = `${maybePlural(numPeriods, 'period')}, reference price $${referencePrice}`
      }

      subscriptionSpend += instanceSpend

      instances[instanceId] = {
        platform,
        spend: instanceSpend,
        spendExplanation: instanceSpendExplanation,
        referencePrice,
        otherPlayers: playerData.value.iapSubscriptionsExtra.instances[instanceId].otherPlayers,
        ...instance
      }
    }

    subscriptions.unshift({
      productId,
      spend: subscriptionSpend,
      ...subscription,

      // \note This must be after the `...subscription` part, because it
      //       overwrites the subscriptionInstances property of `subscription`.
      subscriptionInstances: instances
    })
  }
  return subscriptions
})

async function removeSubscription (productId: string) {
  await gameServerApi.post(`/players/${playerData.value.model.playerId}/removeIAPSubscription/${productId}`)
  showSuccessToast(`Subscription '${productId}' detached from this player.`)
  playerRefresh()
}

function resetModal () {
  selectedSubscription.value = {}
}

function isActive (expiryDateIsoString: string) {
  return DateTime.fromISO(playerData.value.model.currentTime) < DateTime.fromISO(expiryDateIsoString)
}
</script>
