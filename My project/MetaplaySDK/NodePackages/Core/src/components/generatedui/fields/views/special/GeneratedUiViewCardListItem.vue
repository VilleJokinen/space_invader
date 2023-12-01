<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-collapse(:id="fieldSchema.typeName + '-' + fieldInfo.fieldName" v-if="fieldSchema && (fieldSchema.typeKind === 'Class' || fieldSchema.typeKind === 'Abstract')")
  //- Row header
  template(v-slot:header)
    div(v-if="resolvedTitle").text-truncate
      fa-icon(icon="angle-right" size="sm").mr-2
      span.font-weight-bold {{ resolvedTitle }}

  //- Collapse content
  div.mt-2.mb-1
  generated-ui-view-dynamic-component(
    v-bind="$props"
    :fieldInfo="fieldInfo"
    :value="value"
  )
generated-ui-view-dynamic-component(
  v-else
  v-bind="$props"
  :fieldInfo="fieldInfo"
  :value="value"
)
</template>

<script lang="ts" setup>
import { GetTypeSchemaForTypeName } from '../../../getGeneratedUiTypeSchema'
import { ref, onMounted, watch } from 'vue'
import GeneratedUiViewDynamicComponent from '../GeneratedUiViewDynamicComponent.vue'
import { generatedUiFieldBaseProps } from '../../../generatedFieldBase'
import { camelCase } from '../../../generatedUiUtils'

const props = defineProps({
  ...generatedUiFieldBaseProps,
  index: {
    type: Number,
    default: 0
  }
})

// --- Finding the title magics! ---

const resolvedTitle = ref<any>(null)

async function tryFindTitle (typeName: string, value: any) {
  const schema = await GetTypeSchemaForTypeName(typeName)
  if (schema.typeKind === 'Abstract' && '$type' in value) {
    // TODO: Improve prop typings to avoid this non-null assertion.
  // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
    const derivedType = schema.derived!.find((d: any) => d.jsonType === value.$type)!.typeName
    const derivedTitle = await tryFindTitle(derivedType, value)

    if (!derivedTitle) {
      return derivedType.split('.').at(-1)
    }
  } else if (schema.typeKind === 'Class') {
    // TODO: Improve prop typings to avoid this non-null assertion.
    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
    for (const field of schema.fields!) {
      if (field.fieldType === '[]' || field.fieldType === '{}') {
        continue
      }
      const fieldValue = camelCase(field.fieldName) in value ? value[camelCase(field.fieldName)] : undefined

      if (fieldValue) {
        const childTitle: any = await tryFindTitle(field.fieldType, fieldValue)
        if (childTitle) {
          return childTitle
        }
      }
    }
    return undefined
  } else if (schema.typeKind === 'Localized') {
    return 'Localized' // \todo find localized title
  } else if (schema.typeName === 'System.String') {
    return value
  }
  return undefined
}

async function findTitle (value: any) {
  await tryFindTitle(props.fieldInfo.fieldType, value).then(title => {
    resolvedTitle.value = title || String(props.index)
  })
}

watch(() => props.value, async (newValue) => {
  await findTitle(newValue)
})

onMounted(async () => {
  await findTitle(props.value)
})
</script>
