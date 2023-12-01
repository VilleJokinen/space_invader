<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->
<!-- An component to display humanized language -->

<template lang="pug">
MBadge(v-if="variant === 'badge'") {{ getLanguageName(language, gameData) }}
span(v-else-if="variant === 'span'") {{ getLanguageName(language, gameData) }}
span(v-else).text-danger Invalid variant prop!
</template>

<script lang="ts" setup>
import { MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getGameDataSubscriptionOptions } from '../subscription_options/general'

import { getLanguageName } from '@metaplay/meta-ui'

const props = defineProps({
  /**
   * ISO language code of the language to show in the component
   */
  language: {
    type: String,
    required: true
  },
  /**
   * How to show the language name
   */
  variant: {
    type: String,
    validator: (prop: string) => { return ['span', 'badge'].includes(prop) },
    required: true
  }
})

const {
  data: gameData,
} = useSubscription(getGameDataSubscriptionOptions())
</script>
