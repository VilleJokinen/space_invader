// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { GetTypeSchemaForTypeName } from './getGeneratedUiTypeSchema'
import { onMounted, ref, defineAsyncComponent } from 'vue'
import type {
  IGeneratedUiFieldBaseProps,
  IGeneratedUiFieldInfo,
  IGeneratedUiFieldType,
  IGeneratedUiFieldTypeSchema,
  IGeneratedUiComponentRule,
  IGeneratedUiFilterProps,
} from './generatedUiTypes'

import GeneratedUiMissingComponentPlaceholder from './fields/GeneratedUiMissingComponentPlaceholder.vue'
import { useCoreStore } from '../../coreStore'

async function selectComponentForField (fieldInfo: IGeneratedUiFieldInfo, componentRules: IGeneratedUiComponentRule[]): Promise<[any, IGeneratedUiFieldTypeSchema?]> {
  function getDynamicComponent (props: IGeneratedUiFilterProps, type: IGeneratedUiFieldType, componentRules: IGeneratedUiComponentRule[]) {
    for (const { filterFunction, vueComponent } of componentRules) {
      if (filterFunction(props, type)) {
        return vueComponent
      }
    }
    return null
  }

  const filterProps: IGeneratedUiFilterProps = {
    serverContext: fieldInfo.context
      ? Object.fromEntries(fieldInfo.context.map((c) => ([c.key, c.value])))
      : {}
  }

  let returnSchema: IGeneratedUiFieldTypeSchema | undefined
  let returnComponent: any

  if (fieldInfo.fieldType === '[]') {
    returnComponent =
      getDynamicComponent(
        filterProps,
        {
          typeName: fieldInfo.fieldType,
          typeKind: fieldInfo.typeKind,
          isLocalized: fieldInfo.isLocalized,
          typeParams: fieldInfo.typeParams
        },
        componentRules
      ) ?? GeneratedUiMissingComponentPlaceholder
  } else if (fieldInfo.fieldType === '{}') {
    returnComponent =
      getDynamicComponent(
        filterProps,
        {
          typeName: fieldInfo.fieldType,
          typeKind: fieldInfo.typeKind,
          isLocalized: fieldInfo.isLocalized,
          typeParams: fieldInfo.typeParams
        },
        componentRules
      ) ?? GeneratedUiMissingComponentPlaceholder
    // TODO: look into making this check redundant?
  } else if (fieldInfo.fieldType && fieldInfo.fieldType !== '') {
    returnSchema = await GetTypeSchemaForTypeName(fieldInfo.fieldType)
    returnComponent =
    getDynamicComponent(
      filterProps,
      {
        typeName: fieldInfo.fieldType,
        typeKind: returnSchema.typeKind,
        typeParams: fieldInfo.typeParams,
        isLocalized: returnSchema.isLocalized,
        schema: returnSchema
      },
      componentRules
    ) ?? GeneratedUiMissingComponentPlaceholder
  } else throw new Error(`Trying to resolve a component for a type of '${fieldInfo.fieldType}' in '${fieldInfo.fieldName}'`)

  return [returnComponent, returnSchema]
}

export function useGeneratedUiDynamicViewComponentPicker (anyprops: any) {
  const props = anyprops as IGeneratedUiFieldBaseProps

  const coreStore = useCoreStore()

  const contentsComponent = ref<any>(undefined)
  const typeSchema = ref<IGeneratedUiFieldTypeSchema>()

  const contentsComponentLoader = async () => {
    const [component, schema] = await selectComponentForField(props.fieldInfo, coreStore.generatedUiViewComponents)
    return component
  }

  onMounted(() => {
    selectComponentForField(props.fieldInfo, coreStore.generatedUiViewComponents).then(
      ([component, schema]) => {
        contentsComponent.value = component
        typeSchema.value = schema
      }
    ).catch((err) => {
      console.error(err)
    })
  })
  return {
    contentsComponentLoader,
    contentsComponent,
    typeSchema
  }
}

export function useGeneratedUiDynamicFormComponentPicker (anyprops: any) {
  const props = anyprops as IGeneratedUiFieldBaseProps

  const coreStore = useCoreStore()

  const contentsComponent = ref<any>(undefined)
  const typeSchema = ref<IGeneratedUiFieldTypeSchema>()

  const contentsComponentLoader = async () => {
    const [component, schema] = await selectComponentForField(props.fieldInfo, coreStore.generatedUiFormComponents)
    return component
  }

  onMounted(() => {
    selectComponentForField(props.fieldInfo, coreStore.generatedUiFormComponents).then(
      ([component, schema]) => {
        contentsComponent.value = component
        typeSchema.value = schema
      }
    ).catch((err) => {
      console.error(err)
    })
  })
  return {
    contentsComponentLoader,
    contentsComponent,
    typeSchema
  }
}
