<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Loading animation
//- TODO: Could we change this to block rendering until all schemas etc. have been loaded -> no child component would need loading state handling?
div(v-if="!schema || !gameData || !staticConfigData")
  b-skeleton(width="85%")
  b-skeleton(width="55%")
  b-skeleton(width="70%")

//- Content
generated-ui-view-dynamic-component(
  v-else
  :fieldInfo="rootField"
  :value="value"
  :previewLocale="currentLocale"
  :fieldPath="''"
  )
//- TODO: some cool error box?
</template>

<script lang="ts" setup>
import { onMounted, ref, computed } from 'vue'

import { getGameDataSubscriptionOptions, getStaticConfigSubscriptionOptions } from '../../../subscription_options/general'
import { useSubscription } from '@metaplay/subscriptions'

import GeneratedUiViewDynamicComponent from '../fields/views/GeneratedUiViewDynamicComponent.vue'
import { EGeneratedUiTypeKind } from '../generatedUiTypes'
import type { IGeneratedUiFieldTypeSchema, IGeneratedUiFieldInfo } from '../generatedUiTypes'
import { PreloadAllSchemasForTypeName } from '../getGeneratedUiTypeSchema'

const props = defineProps({
  // TODO: If we could generate types from C# to TS, this could an enum or something?
  /**
   * The namespace qualified type name of the C# type we are about to visualize.
   * @example 'Metaplay.Core.InGameMail.MetaInGameMail'
   */
  typeName: {
    type: String,
    default: undefined
  },
  /**
   * The raw data of the object.
   */
  value: {
    type: null,
    required: false,
    default: undefined
  },
  /**
   * In case of localised content, this is the one locale to show as a preview. Should be set to the player's active locale to preview what they would see.
   * @example 'en'
   */
  previewLocale: {
    type: String,
    default: undefined
  }
})

const schema = ref<IGeneratedUiFieldTypeSchema>()
const {
  data: gameData,
} = useSubscription(getGameDataSubscriptionOptions())
const {
  data: staticConfigData,
} = useSubscription(getStaticConfigSubscriptionOptions())

const rootField = computed<IGeneratedUiFieldInfo>(() => ({
  fieldName: undefined,
  fieldType: schema.value?.typeName ?? '',
  typeKind: schema.value?.typeKind ?? EGeneratedUiTypeKind.Class,
  isLocalized: schema.value?.isLocalized,
}))

const currentLocale = computed(() => {
  if (schema.value?.isLocalized) return props.previewLocale ? props.previewLocale : staticConfigData.value.defaultLanguage
  else return undefined
})

// Preload all schemas for this type.
onMounted(async () => {
  if (props.typeName) {
    schema.value = await PreloadAllSchemasForTypeName(props.typeName)
  } else if (props.value && typeof (props.value) === 'object' && '$type' in props.value) {
    schema.value = await PreloadAllSchemasForTypeName(props.value.$type)
  } else {
    throw Error('No typeName or value with $type given')
  }
})
</script>
