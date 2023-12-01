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
  v-bind="props"
  :value="value !== null ? value : undefined"
  @input="update"
  :fieldInfo="fieldInfo"
  :fieldSchema="typeSchema"
)
//- TODO: error handling visuals
</template>

<script lang="ts" setup>
import { defineAsyncComponent } from 'vue'
import { generatedUiFieldFormProps, generatedUiFieldFormEmits, useGeneratedUiFieldForm } from '../../generatedFieldBase'
import { useGeneratedUiDynamicFormComponentPicker } from '../../generatedUiComponentPicker'

const props = defineProps(generatedUiFieldFormProps)
const emit = defineEmits(generatedUiFieldFormEmits)

const { contentsComponentLoader, contentsComponent, typeSchema } = useGeneratedUiDynamicFormComponentPicker(props)
const { update } = useGeneratedUiFieldForm(props, emit)

const asyncComponent = defineAsyncComponent({
  loader: contentsComponentLoader
})

</script>
