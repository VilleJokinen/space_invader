// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { computed, getCurrentInstance, onMounted, type PropType } from 'vue'
import { pascalToDisplayName } from './generatedUiUtils'
import type {
  IGeneratedUiFieldBaseProps,
  IGeneratedUiFieldFormProps,
  IGeneratedUiFieldInfo,
  IGeneratedUiFieldTypeSchema,
  IGeneratedUiFormAbtractTypeFilter,
  IGeneratedUiServerValidationResult
} from './generatedUiTypes'

// TODO: Remove this and use interface to define props when switch to Vue 3.3
const generatedUiFieldBaseProps = {
  value: {
    type: null, // Can be anything 😨
    required: true,
    default: undefined
  },
  fieldInfo: {
    type: Object as PropType<IGeneratedUiFieldInfo>, // Info about this field, such as title, type
    required: true,
    default: () => ({})
  },
  fieldSchema: {
    type: Object as PropType<IGeneratedUiFieldTypeSchema>, // Possibly null if current field is an array or a dictionary.
    default: () => ({})
  },
  // Which locale should be shown in case of localized content with multiple options.
  // TODO: fetch the default from configs or make this mandatory?
  previewLocale: {
    type: String,
    default: undefined
  },
}

const generatedUiFieldFormEmits = [
  'input'
]

// TODO: Remove this and use interface to define props when switch to Vue 3.3
const generatedUiFieldFormProps = {
  ...generatedUiFieldBaseProps,
  // Which locales are selected for editing.
  editLocales: {
    type: Array as PropType<string[]>,
    default: undefined
  },
  // Unique identifier for this field
  fieldPath: {
    type: String,
    default: ''
  },
  // Validation errors, if any
  serverValidationResults: {
    type: Array as PropType<IGeneratedUiServerValidationResult[]>,
    default: undefined
  },
  abstractTypeFilter: {
    type: Function as PropType<IGeneratedUiFormAbtractTypeFilter>,
    default: undefined
  }
}

export { generatedUiFieldBaseProps, generatedUiFieldFormProps, generatedUiFieldFormEmits }

export const useGeneratedUiFieldBase = function (anyprops: any) {
  const props = anyprops as IGeneratedUiFieldBaseProps
  // ----- Visualization -----

  // TODO: missing names?
  const displayName = computed(() => {
    if (props.fieldInfo?.displayProps?.displayName) return props.fieldInfo.displayProps.displayName
    else if (props.fieldInfo.fieldName) return pascalToDisplayName(props.fieldInfo.fieldName)
    else return undefined
  })

  const displayHint = computed(() => {
    return props.fieldInfo.displayProps?.displayHint ?? undefined
  })

  /**
   * True if this field has a fieldName that is not empty.
   */
  const hasFieldName = computed(() => {
    return props.fieldInfo.fieldName && props.fieldInfo.fieldName.length > 0
  })

  return {
    displayName,
    displayHint,
    hasFieldName
  }
}

export const useGeneratedUiFieldForm = function (anyprops: any, emit: (event: string, ...args: any[]) => void) {
  const { displayName, displayHint, hasFieldName } = useGeneratedUiFieldBase(anyprops)

  const props = anyprops as IGeneratedUiFieldFormProps

  const formInputPlaceholder = computed(() => {
    return props.fieldInfo.displayProps?.placeholder ?? `Enter ${displayName.value}...`
  })

  // ----- Events -----

  const vm = getCurrentInstance() as any

  function update (newValue: any): void {
    emit('input', newValue)
  }

  // ----- Validation -----

  const isValid = computed((): boolean | undefined => {
    if (!props.serverValidationResults) {
      return undefined
    }
    return !props.serverValidationResults.some((x: IGeneratedUiServerValidationResult) => (x.path === props.fieldPath))
  })

  const validationError = computed((): string | null => {
    return getServerValidationError()
  })

  /**
   * Checks if the server has returned any validation errors for this field.
   * @returns Null if there are no errors or a string with the validation error reason.
   */
  function getServerValidationError (): string | null {
    if (!props.serverValidationResults) {
      return null
    }
    const result = props.serverValidationResults.find((x: IGeneratedUiServerValidationResult) => (x.path === props.fieldPath))
    return result ? result.reason : null
  }

  // ----- Testing -----

  // Generate unique labels for Cypress tests
  const dataCy = computed(() => {
    let fieldPath = props.fieldPath ?? ''
    fieldPath = fieldPath.replace(/\//g, '-').toLowerCase()

    return fieldPath
  })

  function useDefault (emptyValue: any, defaultValue: any) {
    onMounted(() => {
      if (props.value === emptyValue) {
        setTimeout(() => {
          update(defaultValue)
        }, 0)
      }
    })
  }

  return {
    displayName,
    displayHint,
    hasFieldName,
    formInputPlaceholder,
    dataCy,
    update,
    isValid,
    validationError,
    getServerValidationError,
    useDefault,
  }
}
