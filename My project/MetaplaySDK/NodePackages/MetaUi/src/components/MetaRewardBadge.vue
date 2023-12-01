<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->
<!-- An opinionated component to display humanized MetaReward types. -->

<template lang="pug">
//- TODO: This 'rewardWithMetaData' is not actually needed -> refactor to just look up the reward entry to make this simpler
MBadge.mr-1 {{ rewardDisplayValue() }}
</template>

<script lang="ts" setup>
import { rewardWithMetaData } from '../rewardUtils'
import type { PropType } from 'vue'
import { MBadge } from '@metaplay/meta-ui-next'

const props = defineProps({
  reward: {
    type: Object as PropType<{ $type: string, amount: number }>,
    default: null
  }
})

const rewardDisplayValue = () => {
  if (props.reward) {
    return rewardWithMetaData(props.reward).getDisplayValue(props.reward)
  } else {
    return 'null'
  }
}
</script>
