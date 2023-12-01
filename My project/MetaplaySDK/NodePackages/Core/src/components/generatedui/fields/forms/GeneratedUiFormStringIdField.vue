<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div
  div.mb-1.font-weight-bold {{ displayName }}
    MTooltip.ml-2(v-if="displayHint" :content="displayHint" noUnderline): MBadge(shape="pill") ?
  div
    meta-input-select(
      v-if="fieldSchema.configLibrary && fieldSchema.configLibrary.length > 0"
      :value="value"
      @input="updateValue"
      :options="possibleValues"
      :data-cy="dataCy + '-input'"
      :class="isValid ? 'border-success' : ''"
      no-clear
      no-deselect
      )
    b-input(
      v-else
      :value="value"
      @input="update"
      :type="'text'"
      :placeholder="formInputPlaceholder"
      :state="isValid"
      :data-cy="dataCy + '-input'"
      )
  b-form-invalid-feedback(:state="isValid") {{ validationError }}
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { getGameDataSubscriptionOptions } from '../../../../subscription_options/general'
import { MTooltip, MBadge } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { generatedUiFieldFormEmits, generatedUiFieldFormProps, useGeneratedUiFieldForm } from '../../generatedFieldBase'
import { useCoreStore } from '../../../../coreStore'

const props = defineProps({
  ...generatedUiFieldFormProps,
  value: {
    type: String,
    default: ''
  },
})

const emit = defineEmits(generatedUiFieldFormEmits)

const {
  displayName,
  displayHint,
  isValid,
  validationError,
  update,
  dataCy,
  formInputPlaceholder,
  useDefault
} = useGeneratedUiFieldForm(props, emit)

const {
  data: gameData,
} = useSubscription(getGameDataSubscriptionOptions())
const coreStore = useCoreStore()

function updateValue (value: any) {
  update(value)
}

const possibleValues = computed(() => {
  // TODO: Improve the prop typings so we don't need to use non-null assertions.
  // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
  const libraryKey = props.fieldSchema.configLibrary!
  if (gameData.value.gameConfig[libraryKey]) {
    return Object.keys(gameData.value.gameConfig[libraryKey]).map((key) => {
      // Look up if there is a prettier display name for this string id.
      const id = coreStore.stringIdDecorators[props.fieldInfo.fieldType] ? coreStore.stringIdDecorators[props.fieldInfo.fieldType](key) : key
      return {
        id,
        value: key,
      }
    })
  } else {
    return []
  }
})

useDefault('', possibleValues.value.find(() => true)?.value) // Use first value if available, or undefined

</script>
