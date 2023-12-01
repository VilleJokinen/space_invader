<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
MCollapse
  template(#header) {{ displayName }}
  template(#default)
    MList(
      v-if="valueField && value && Object.entries(value).length > 0"
      class="tw-my-3"
      showBorder
      )
      MListItem(
        v-for="([key, val], idx) in Object.entries(value)"
        :key="idx"
        class="tw-px-5"
        condensed
        )
        span {{key}}
        template(#top-right)
          generated-ui-view-dynamic-component(
            v-bind="$props"
            :fieldInfo="valueField"
            :value="val"
            )
    div(v-else) No entries
</template>

<script lang="ts" setup>
import { generatedUiFieldBaseProps, useGeneratedUiFieldBase } from '../../generatedFieldBase'
import { onMounted, ref } from 'vue'
import { GetTypeSchemaForTypeName } from '../../getGeneratedUiTypeSchema'
import GeneratedUiViewDynamicComponent from './GeneratedUiViewDynamicComponent.vue'
import { MCollapse, MList, MListItem } from '@metaplay/meta-ui-next'

const props = defineProps(generatedUiFieldBaseProps)
const { displayName } = useGeneratedUiFieldBase(props)

const valueType = ref('')
const valueField = ref<any>(null)

onMounted(async () => {
  if (props.fieldInfo?.typeParams) {
    valueType.value = props.fieldInfo.typeParams[1]
    const typeSchema = await GetTypeSchemaForTypeName(props.fieldInfo.typeParams[1])
    valueField.value = {
      fieldName: '',
      fieldType: props.fieldInfo.typeParams[1],
      typeKind: typeSchema.typeKind
    }
  }
})
</script>
