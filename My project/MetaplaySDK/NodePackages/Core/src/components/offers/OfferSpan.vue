<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
span(v-if="displayStrings")
  MBadge.ml-1(v-for="displayString in displayStrings" :key="displayString") {{ displayString }}
span(v-else)
  slot
    //- pre {{ displayStrings }}
    span.text-danger Error: undefined offer type '{{ offer.$type }}'
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { useCoreStore } from '../../coreStore'
import { rewardWithMetaData } from '@metaplay/meta-ui'
import { MBadge } from '@metaplay/meta-ui-next'

const props = defineProps({
  /**
   * The offer to visualise.
   */
  offer: {
    type: Object,
    required: true
  }
})

const coreStore = useCoreStore()

const displayStrings = computed(() => {
  // If using the SDK-provided ResolvedPurchaseMetaRewards class, that is handled explicitly here.
  if (props.offer.$type === 'Metaplay.Core.InAppPurchase.ResolvedPurchaseMetaRewards') {
    return props.offer.rewards.map((reward: any) => {
      return rewardWithMetaData(reward).getDisplayValue(reward)
    })
  }

  // Otherwise, find the game-specific display function for the offer and return the result.
  return coreStore.gameSpecific.iapContents.find(iap => iap.$type === props.offer.$type)?.getDisplayContent(props.offer) ?? null
})
</script>
