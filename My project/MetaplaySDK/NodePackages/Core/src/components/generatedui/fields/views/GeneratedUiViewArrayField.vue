<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Collapsed list layout
meta-collapse(v-if="hasContent" :id="String(fieldInfo.fieldName)")
  //- Collapse header
  template(#header)
    div.text-truncate.mb-2
    fa-icon(icon="angle-right" size="sm").mr-2
    | {{ displayName }}

  //- Loading
  b-skeleton(v-if="!arrayField")
  //- Collapse content
  div(v-else)
    div(v-for="(val, idx) in value" :key="idx").mb-1
      generated-ui-view-dynamic-component(
        v-bind="$props"
        :fieldInfo="arrayField"
        :value="val"
      )
div(v-else).text-muted.font-italic.small No {{ displayName }}
</template>

<script lang="ts" setup>
import { onMounted, ref, computed } from 'vue'
import type { IGeneratedUiFieldInfo } from '../../generatedUiTypes'
import { generatedUiFieldBaseProps, useGeneratedUiFieldBase } from '../../generatedFieldBase'
import { GetTypeSchemaForTypeName } from '../../getGeneratedUiTypeSchema'
import GeneratedUiViewDynamicComponent from './GeneratedUiViewDynamicComponent.vue'

const props = defineProps(generatedUiFieldBaseProps)
const { displayName } = useGeneratedUiFieldBase(props)

const hasContent = computed(() => Array.isArray(props.value) && props.value.length > 0)

/**
 * FieldInfo constructed from the array type parameter.
 */
const arrayField = ref<IGeneratedUiFieldInfo>()

onMounted(async () => {
  // Get the schema for the type contained in the array and construct a type info from it.
  if (props.fieldInfo.fieldType === '[]' && Array.isArray(props.value)) {
    // TODO: Improve the prop typings so we don't need to use non-null assertions.
    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
    const typeSchema = await GetTypeSchemaForTypeName(props.fieldInfo.typeParams![0])
    arrayField.value = {
      fieldName: '',
      fieldType: typeSchema.typeName,
      typeKind: typeSchema.typeKind,
      isLocalized: typeSchema.isLocalized
    }
  }
})
</script>
