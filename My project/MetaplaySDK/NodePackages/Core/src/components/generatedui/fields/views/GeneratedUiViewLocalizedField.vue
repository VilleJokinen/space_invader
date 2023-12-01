<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
MListItem(class="!tw-py-0" condensed) {{ displayName }}
  template(#top-right v-if="value.localizations")
    meta-language-label(:language="previewLocale && previewLocale in value.localizations ? previewLocale : staticConfigData.defaultLanguage" variant="span")
  template(#bottom-left)
    generated-ui-view-dynamic-component(
      v-if="localizedField && value.localizations"
      v-bind="$props"
      :fieldInfo="localizedField"
      :value="previewLocale && previewLocale in value.localizations ? value.localizations[previewLocale] : value.localizations[staticConfigData.defaultLanguage]"
    )
    generated-ui-view-dynamic-component(
      v-else-if="localizedField && fieldSchema.typeName === 'Metaplay.Core.Localization.LocalizedString' && value.localizationKey"
      v-bind="$props"
      :fieldInfo="localizedField"
      :value="`[${value.localizationKey}]`"
    )
    MBadge(v-else) null
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'

import { MBadge, MListItem } from '@metaplay/meta-ui-next'
import { getStaticConfigSubscriptionOptions } from '../../../../subscription_options/general'
import { useSubscription } from '@metaplay/subscriptions'

import GeneratedUiViewDynamicComponent from './GeneratedUiViewDynamicComponent.vue'
import MetaLanguageLabel from '../../../MetaLanguageLabel.vue'

import { generatedUiFieldBaseProps, useGeneratedUiFieldBase } from '../../generatedFieldBase'
import type { IGeneratedUiFieldInfo } from '../../generatedUiTypes'

const props = defineProps(generatedUiFieldBaseProps)

const { displayName } = useGeneratedUiFieldBase(props)

const localizedType = ref('')
const localizedField = ref<IGeneratedUiFieldInfo>()
const {
  data: staticConfigData,
} = useSubscription(getStaticConfigSubscriptionOptions())

onMounted(() => {
  const lField = props.fieldSchema?.fields?.find((field: any) => field.fieldName === 'Localizations')
  localizedType.value = lField?.typeParams?.[1] ?? ''

  localizedField.value = {
    ...props.fieldInfo,
    fieldName: undefined,
    fieldType: localizedType.value
  }
})
</script>
