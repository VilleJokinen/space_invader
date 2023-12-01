<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  div.font-weight-bold {{ displayName }}
  div.text-muted.small(v-if="displayHint") {{ displayHint }}

  div.mt-1(:data-cy="dataCy + 'source-slot'")
    meta-input-select(
      :no-deselect="true"
      :no-clear="true"
      :value="selectedSourceOption"
      @input="onSourceSelectionChanged"
      :options="sourceOptions"
      placeholder="Select a source..."
      )

  div(v-if="isCustomSourceSelected").mt-2.border.p-2.rounded.bg-light
    generated-ui-form-abstract-field(
      :typeName="'Metaplay.Core.Config.GameConfigBuildSource'"
      :value="customSource"
      @input="onCustomSourceChanged"
      :field-info="fieldInfo"
      :field-schema="fieldSchema"
      )
</template>

<script lang="ts" setup>
import { ref, type ComputedRef, computed, watch } from 'vue'
import type { MetaInputSelectOption } from '@metaplay/meta-ui'
import { useSubscription } from '@metaplay/subscriptions'
import { getStaticConfigSubscriptionOptions } from '../../../../subscription_options/general'
import type { StaticConfig } from '../../../../subscription_options/generalTypes'
import { generatedUiFieldFormEmits, generatedUiFieldFormProps, useGeneratedUiFieldForm } from '../../generatedFieldBase'
import GeneratedUiFormAbstractField from './GeneratedUiFormAbstractField.vue'

const {
  data: staticConfig,
} = useSubscription<StaticConfig>(getStaticConfigSubscriptionOptions())

const props = defineProps({
  ...generatedUiFieldFormProps,
  value: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(generatedUiFieldFormEmits)

const {
  displayName,
  displayHint,
  update,
  dataCy,
} = useGeneratedUiFieldForm(props, emit)

function onSourceSelectionChanged (newSource: any) {
  if (newSource.displayName === customSourceName) {
    isCustomSourceSelected = true
    update(customSource.value)
  } else {
    isCustomSourceSelected = false
    update(newSource)
  }
}

function onCustomSourceChanged (newSource: any) {
  customSource.value = newSource
  if (isCustomSourceSelected) { update(newSource) }
}

function onSourceValidityChanged (valid: boolean) {
  // TODO: use frontend validation when possible
  // sourceValidationState.value = isValid
}

let isCustomSourceSelected = false
const customSourceName = 'Custom Source'
const customSourcePlaceholderForList = { displayName: customSourceName }
const customSource = ref<any>()

const predefinedSources = computed<Array<{ displayName: string }>>(() => staticConfig.value?.configBuildInfo.slotToAvailableSourcesMapping[props.fieldInfo.fieldName ?? ''] ?? [])
const availableSources = computed<Array<{ displayName: string }>>(() => predefinedSources.value.concat(customSourcePlaceholderForList))
const selectedSourceOption = computed(() => isCustomSourceSelected ? customSourcePlaceholderForList : { ...props.value, displayName: props.value?.name })
const sourceOptions: ComputedRef<Array<MetaInputSelectOption<any>>> = computed(() =>
  availableSources.value?.map(element => ({ id: element.displayName, value: element }))
)
const defaultValue = computed(() => predefinedSources.value?.[0])
const currentValue = computed(() => props.value)

watch(currentValue, (val, oldVal) => {
  if (!val) {
    setTimeout(() => update(defaultValue.value), 0)
  }
}, { immediate: true })

const customSourceValidationState = ref<boolean>()

</script>
