<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div.d-flex.justify-content-between
  div(v-if="hasFieldName") {{ displayName }}
  div(v-if="prettyValue === undefined || prettyValue === null").text-muted undefined
  div(v-else) {{ prettyValue }}
</template>

<script setup lang="ts">
import { generatedUiFieldBaseProps, useGeneratedUiFieldBase } from '../../generatedFieldBase'
import { useCoreStore } from '../../../../coreStore'

import { getGameDataSubscriptionOptions } from '../../../../subscription_options/general'
import { useSubscription } from '@metaplay/subscriptions'
import { computed } from 'vue'

const {
  data: gameData,
} = useSubscription(getGameDataSubscriptionOptions())
const coreStore = useCoreStore()

const props = defineProps(generatedUiFieldBaseProps)
const { displayName, hasFieldName } = useGeneratedUiFieldBase(props)

const prettyValue = computed(() => {
  if (props.value === undefined || props.value === null) {
    return undefined
  }
  return coreStore.stringIdDecorators[props.fieldInfo.fieldType] ? coreStore.stringIdDecorators[props.fieldInfo.fieldType](props.value) : props.value
})

</script>
