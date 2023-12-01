// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { EGeneratedUiTypeKind, type IGeneratedUiComponentRule, type IGeneratedUiFieldType, type IGeneratedUiFilterProps } from './generatedUiTypes'

const numberTypes = ['System.Int32', 'System.Int64', 'System.UInt32', 'System.UInt64',
  'System.Single', 'System.Double', 'System.Decimal',
  'System.Byte', 'System.SByte', 'System.Int16', 'System.UInt16',
  'Metaplay.Core.Math.MetaUInt128', 'Metaplay.Core.Math.F32', 'Metaplay.Core.Math.F64']

function filterTypeName (typeName: string, vueComponent: any): IGeneratedUiComponentRule {
  return {
    filterFunction: (_props: IGeneratedUiFilterProps, type: IGeneratedUiFieldType) => {
      return type.typeName === typeName
    },
    vueComponent
  }
}

function filterTypeKind (typeKind: EGeneratedUiTypeKind, vueComponent: any): IGeneratedUiComponentRule {
  return {
    filterFunction: (_props: IGeneratedUiFilterProps, type: IGeneratedUiFieldType) => {
      return type.typeKind === typeKind
    },
    vueComponent
  }
}

// Remember to use import('') style imports here.
const DefaultGeneratedUiViewComponents: IGeneratedUiComponentRule[] = [
  {
    filterFunction: (_props, type) => {
      return (type.typeKind === EGeneratedUiTypeKind.ValueCollection &&
        !!type.typeParams &&
        type.typeParams[0] === 'Metaplay.Core.Rewards.MetaPlayerRewardBase')
    },
    vueComponent: import('./fields/views/GeneratedUiViewMetaRewardListField.vue')
  },
  filterTypeName('[]', import('./fields/views/GeneratedUiViewArrayField.vue')),
  filterTypeName('{}', import('./fields/views/GeneratedUiViewDictionaryField.vue')),
  filterTypeName('Metaplay.Core.EntityId', import('./fields/views/GeneratedUiViewEntityIdField.vue')),
  filterTypeName('System.Boolean', import('./fields/views/GeneratedUiViewBooleanField.vue')),
  filterTypeName('Metaplay.Core.MetaTime', import('./fields/views/GeneratedUiViewMetaTimeField.vue')),
  filterTypeKind(EGeneratedUiTypeKind.StringId, import('./fields/views/GeneratedUiViewStringIdField.vue')),
  filterTypeKind(EGeneratedUiTypeKind.ConfigLibraryItem, import('./fields/views/GeneratedUiViewStringIdField.vue')),
  {
    filterFunction: (_props, type) => {
      return (
        type.typeKind === EGeneratedUiTypeKind.Primitive ||
        type.typeKind === EGeneratedUiTypeKind.Enum ||
        type.typeKind === EGeneratedUiTypeKind.DynamicEnum)
    },
    vueComponent: import('./fields/views/GeneratedUiViewPrimitiveField.vue')
  },
  filterTypeKind(EGeneratedUiTypeKind.Localized, import('./fields/views/GeneratedUiViewLocalizedField.vue')),
  {
    filterFunction: (_props, type) => {
      return type.typeKind === EGeneratedUiTypeKind.Class || type.typeKind === EGeneratedUiTypeKind.Abstract
    },
    vueComponent: import('./fields/views/GeneratedUiViewContainerField.vue')
  }
]

const DefaultGeneratedUiFormComponents: IGeneratedUiComponentRule[] = [
  {
    filterFunction: (_props, type) => {
      return type.typeKind === 'Primitive' && numberTypes.includes(type.typeName)
    },
    vueComponent: import('./fields/forms/GeneratedUiFormNumberField.vue')
  },
  {
    filterFunction: (_props, type) => {
      return type.typeKind === EGeneratedUiTypeKind.Enum || type.typeKind === EGeneratedUiTypeKind.DynamicEnum
    },
    vueComponent: import('./fields/forms/GeneratedUiFormEnumField.vue')
  },
  filterTypeName('System.String', import('./fields/forms/GeneratedUiFormTextField.vue')),
  filterTypeName('System.Boolean', import('./fields/forms/GeneratedUiFormBooleanField.vue')),
  filterTypeName('Metaplay.Core.MetaTime', import('./fields/forms/GeneratedUiFormMetaTimeField.vue')),
  filterTypeName('Metaplay.Core.Config.GameConfigBuildSource', import('./fields/forms/GeneratedUiFormGameConfigBuildSourceField.vue')),
  filterTypeKind(EGeneratedUiTypeKind.ValueCollection, import('./fields/forms/GeneratedUiFormArrayField.vue')),
  filterTypeKind(EGeneratedUiTypeKind.KeyValueCollection, import('./fields/forms/GeneratedUiFormDictionaryField.vue')),
  filterTypeKind(EGeneratedUiTypeKind.Localized, import('./fields/forms/GeneratedUiFormLocalizedField.vue')),
  filterTypeKind(EGeneratedUiTypeKind.StringId, import('./fields/forms/GeneratedUiFormStringIdField.vue')),
  filterTypeKind(EGeneratedUiTypeKind.ConfigLibraryItem, import('./fields/forms/GeneratedUiFormStringIdField.vue')),
  {
    filterFunction: (_props, type) => {
      return type.typeKind === EGeneratedUiTypeKind.Abstract
    },
    vueComponent: import('./fields/forms/GeneratedUiFormAbstractField.vue')
  },
  {
    filterFunction: (_props, type) => {
      return type.typeKind === EGeneratedUiTypeKind.Class
    },
    vueComponent: import('./fields/forms/GeneratedUiFormContainerField.vue')
  }
]

export { DefaultGeneratedUiViewComponents, DefaultGeneratedUiFormComponents }
