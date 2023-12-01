<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(v-if="hasValue")
  meta-reward-badge(v-for="(reward, index) in value" :reward="reward" :key="index")
div(v-else).text-muted.font-italic.small No {{ displayName }}
</template>

<script lang="ts" setup>
import { generatedUiFieldBaseProps, useGeneratedUiFieldBase } from '../../generatedFieldBase'
import { computed } from 'vue'

const props = defineProps(generatedUiFieldBaseProps)
const { displayName } = useGeneratedUiFieldBase(props)

const hasValue = computed(() => {
  return props.value && Array.isArray(props.value) && props.value.length > 0
})
</script>
