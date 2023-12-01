<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  title="Additional Conditions"
  icon="cog"
  :itemList="decoratedPlayerConditions"
  emptyMessage="No additional conditions defined."
)
  template(#item-card="slot")
    b-row(v-if="slot.item.conditionType === 'MetaOfferPrecursorCondition'" align-h="between" no-gutters)
      span #[meta-duration(:duration="slot.item.delay")] after offer #[b-link(:to="`/offerGroups/offer/${slot.item.offerId}`") {{ slot.item.offerId }}] {{ slot.item.purchased == true ? 'was' : 'was not' }} purchased.
    b-row(v-else no-gutters)
      div.text-muted Unknown condition type: {{ slot.item.conditionType }}.
      pre(style="font-size: .7rem") {{ slot.item }}
</template>

<script lang="ts" setup>
import { computed } from 'vue'

const props = defineProps<{
  /**
   * Optional: Array of additional criteria for targeting players.
   */
  playerConditions?: any[]
}>()

/**
 * Decorate the conditions with their type, extracted from the class name.
 */
const decoratedPlayerConditions = computed(() => {
  if (props.playerConditions) {
    return props.playerConditions.map((x: any) => {
      return {
        ...x,
        conditionType: x.$type.split('.').pop().split(',')[0]
      }
    })
  } else {
    return []
  }
})
</script>
