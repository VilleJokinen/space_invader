<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- TODO: could we remove the need for this?
div(v-if="!contentsComponent")
  b-skeleton(width="85%")
  b-skeleton(width="55%")
  b-skeleton(width="70%")
//- Dynamic component
asyncComponent(
  v-else
  v-bind="$props"
  :value="value !== null ? value : undefined"
  :fieldInfo="fieldInfo"
  :fieldSchema="typeSchema"
)
//- TODO: error handling visuals
</template>

<script lang="ts" setup>
import { defineAsyncComponent } from 'vue'
import { generatedUiFieldBaseProps } from '../../generatedFieldBase'
import { useGeneratedUiDynamicViewComponentPicker } from '../../generatedUiComponentPicker'

const props = defineProps(generatedUiFieldBaseProps)
const { contentsComponent, contentsComponentLoader, typeSchema } = useGeneratedUiDynamicViewComponentPicker(props)

const asyncComponent = defineAsyncComponent({
  loader: contentsComponentLoader
})

</script>
