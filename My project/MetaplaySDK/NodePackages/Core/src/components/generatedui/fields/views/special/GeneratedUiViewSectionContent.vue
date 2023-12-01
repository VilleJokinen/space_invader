<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
b-row(align-h="center")
  b-col(v-if="primitiveCard" md="6").mb-3
    b-card.h-100.shadow-sm(:title="primitiveCard.title")
      //- Below not tested in dashboard locally.
      MList(v-if="primitiveCard.fields && primitiveCard.fields.length > 0")
        MListItem(v-for="primfield in primitiveCard.fields" :key="primfield.field.fieldName")
          generated-ui-view-dynamic-component(
            v-bind="$props"
            :fieldInfo="primfield.field"
            :value="primfield.value"
          )
  b-col(v-if="localizedCard" md="6").mb-3
    meta-list-card.h-100(
      title="Localizations"
      icon="language"
      :itemList="localizedCard.languages"
      )
      template(#item-card="{ item: localization}")
        MCollapse
          template(#header)
            span.font-weight-bold {{ localization.displayName }}
          template(#default)
            MList(v-if="localizedCard.fields && localizedCard.fields.length > 0" showBorder class="tw-my-3")
              div(v-for="locfield in localizedCard.fields" :key="locfield.field.fieldName" class="tw-px-5 tw-py-2")
                //- TODO: Below is not working properly. Look into styling later.
                generated-ui-view-dynamic-component(
                    v-bind="$props"
                    :fieldInfo="locfield.field"
                    :value="locfield.value"
                    :previewLocale="localization.lang"
                  )
  b-col(v-for="othfield in otherFields" md="6" :key="othfield.field.fieldName").mb-3
    meta-generated-card(
      v-if="othfield.arrayType"
      :typeName="othfield.arrayType"
      :value="othfield.value"
      :title="othfield.field.fieldName || 'Untitled'"
      list
    )
    meta-generated-card(
      v-else-if="othfield.dictionaryType"
      :typeName="othfield.dictionaryType"
      :value="othfield.value"
      :title="othfield.field.fieldName || 'Untitled'"
      dictionary
    )
    meta-generated-card(
      v-else
      :typeName="othfield.field.fieldType"
      :value="othfield.value"
      :title="othfield.field.fieldName || 'Untitled'"
    )
</template>

<script lang="ts" setup>
import { isEqual } from 'lodash-es'
import { computed, onMounted, ref, watch } from 'vue'

import { getGameDataSubscriptionOptions } from '../../../../../subscription_options/general'
import { MCollapse, MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getLanguageName } from '@metaplay/meta-ui'
import { generatedUiFieldBaseProps } from '../../../generatedFieldBase'
import { EGeneratedUiTypeKind } from '../../../generatedUiTypes'
import type { IGeneratedUiFieldInfo, IGeneratedUiFieldTypeSchema } from '../../../generatedUiTypes'
import { GetTypeSchemaForTypeName } from '../../../getGeneratedUiTypeSchema'
import { camelCase, findLanguages } from '../../../generatedUiUtils'
import GeneratedUiViewDynamicComponent from '../GeneratedUiViewDynamicComponent.vue'

import MetaGeneratedCard from '../../../components/MetaGeneratedCard.vue'

export interface ISectionField {
  field: IGeneratedUiFieldInfo
  value: any
  arrayType?: string
  dictionaryType?: string
}

const props = defineProps(generatedUiFieldBaseProps)

// --- Schema resolving for Abstract types ---
const resolvedSchema = ref<IGeneratedUiFieldTypeSchema>()

onMounted(async () => {
  if (props.fieldInfo.typeKind === EGeneratedUiTypeKind.Abstract) {
    resolvedSchema.value = props.value.$type ? await GetTypeSchemaForTypeName(props.value.$type) : undefined
  } else {
    resolvedSchema.value = props.fieldSchema
  }
})

// --- Field filtering ---

// TODO: use different prop notViewable
const viewableFilter = (x: IGeneratedUiFieldInfo) => !x.notEditable

const primitiveFilter = (x: IGeneratedUiFieldInfo) =>
  x.typeKind === EGeneratedUiTypeKind.Primitive ||
  x.typeKind === EGeneratedUiTypeKind.StringId ||
  x.typeKind === EGeneratedUiTypeKind.ConfigLibraryItem ||
  x.typeKind === EGeneratedUiTypeKind.Enum ||
  x.typeKind === EGeneratedUiTypeKind.DynamicEnum ||
  x.fieldType === 'Metaplay.Core.MetaTime'

const localizedFilter = (x: IGeneratedUiFieldInfo) => x.typeKind === EGeneratedUiTypeKind.Localized

const primitiveFields = ref<ISectionField[]>([])
const localizedFields = ref<ISectionField[]>([])
const otherFields = ref<ISectionField[]>([])

// Find all fields of the class recursively and sort them based on their type.
watch(() => ([resolvedSchema.value, props.value]), async ([newResolvedSchema, newValue], [oldSchema, oldValue]) => {
  if (!newResolvedSchema) {
    return
  }
  if ((primitiveFields.value.length > 0 ||
    localizedFields.value.length > 0 ||
    otherFields.value.length > 0) &&
    isEqual(newValue, oldValue)) {
    return
  }
  const schemasTraversed: string[] = []
  const traverseQueue: Array<{
    schema: IGeneratedUiFieldTypeSchema
    value: any
    namePrefix?: string
  }> = [{ schema: newResolvedSchema as IGeneratedUiFieldTypeSchema, value: newValue }]

  primitiveFields.value = []
  localizedFields.value = []
  otherFields.value = []

  while (traverseQueue.length > 0) {
    // TODO: Improve prop typings to avoid this non-null assertion.
    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
    const { schema: currentSchema, value: currentValue, namePrefix } = traverseQueue.pop()!

    // Avoid recursive reference looping
    if (schemasTraversed.find((x) => currentSchema.typeName === x)) {
      continue
    }

    schemasTraversed.push(currentSchema.typeName)

    // TODO: Improve prop typings to avoid this non-null assertion.
    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
    for (const field of currentSchema.fields!) {
      if (!viewableFilter(field)) {
        continue
      }
      const fieldValue = currentValue ? currentValue[camelCase(field.fieldName)] : undefined

      const constructedField: ISectionField = {
        field: {
          ...field,
          fieldName: (namePrefix ?? '') + field.fieldName
        },
        value: fieldValue
      }

      if (field.typeKind === EGeneratedUiTypeKind.ValueCollection) {
        // TODO: Improve prop typings to avoid this non-null assertion.
        // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
        constructedField.arrayType = field.typeParams![0]
      } else if (field.typeKind === EGeneratedUiTypeKind.KeyValueCollection) {
        // TODO: Improve prop typings to avoid this non-null assertion.
        // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
        constructedField.dictionaryType = field.typeParams![1]
      }

      // Deep traverse into classes and abstracts
      if (field.typeKind === EGeneratedUiTypeKind.Class) {
        const fieldSchema = (await GetTypeSchemaForTypeName(field.fieldType))
        traverseQueue.push({
          schema: fieldSchema,
          value: fieldValue,
          // TODO: Improve prop typings to avoid this non-null assertion.
          // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
          namePrefix: currentSchema.fields!.length > 1 ? field.fieldName + ' ' : namePrefix // Add name prefix if current schema has more than one field
        })
      } else if (field.typeKind === EGeneratedUiTypeKind.Abstract && fieldValue && '$type' in fieldValue) {
        const fieldType = fieldValue.$type
        const fieldSchema = (await GetTypeSchemaForTypeName(fieldType))
        traverseQueue.push({
          schema: fieldSchema,
          value: fieldValue,
          // TODO: Improve prop typings to avoid this non-null assertion.
          // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
          namePrefix: currentSchema.fields!.length > 1 ? field.fieldName + ' ' : namePrefix // Add name prefix if current schema has more than one field
        })
      } else if (primitiveFilter(field)) {
        primitiveFields.value.push(constructedField)
      } else if (localizedFilter(field)) {
        localizedFields.value.push(constructedField)
      } else {
        otherFields.value.push(constructedField)
      }
    }
  }
}, {
  deep: true,
})

// --- Section members ---

const primitiveCard = computed(() => {
  if (primitiveFields.value.length === 0) {
    return null
  }
  return {
    title: 'General',
    fields: primitiveFields.value
  }
})

const {
  data: gameData,
} = useSubscription(getGameDataSubscriptionOptions())

const localizedCard = computed(() => {
  if (localizedFields.value.length === 0) {
    return null
  }

  return {
    title: 'Localizations',
    languages: findLanguages(props.value, gameData.value).map(x => ({
      displayName: getLanguageName(x, gameData.value),
      lang: x
    })),
    fields: localizedFields.value
  }
})
</script>
