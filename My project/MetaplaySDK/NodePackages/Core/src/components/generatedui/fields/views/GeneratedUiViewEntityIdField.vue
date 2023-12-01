<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div.d-flex.justify-content-between
  div(v-if="hasFieldName") {{ displayName }}
  div(v-if="!value").text-muted undefined
  div(v-else) {{ value }}
</template>

<script lang="ts" setup>
import { generatedUiFieldBaseProps, useGeneratedUiFieldBase } from '../../generatedFieldBase'

const props = defineProps(generatedUiFieldBaseProps)
const { displayName, hasFieldName } = useGeneratedUiFieldBase(props)
</script>
