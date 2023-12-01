// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

export enum EGeneratedUiTypeKind {
  Class = 'Class',
  Enum = 'Enum',
  StringId = 'StringId',
  DynamicEnum = 'DynamicEnum',
  Primitive = 'Primitive',
  Localized = 'Localized',
  Abstract = 'Abstract',
  ValueCollection = 'ValueCollection',
  KeyValueCollection = 'KeyValueCollection',
  ConfigLibraryItem = 'ConfigLibraryItem'
}

export interface IGeneratedUiFieldInfo {
  fieldName?: string
  fieldType: string // Metaplay.Core.InGameMail.MetaInGameMail
  typeKind: EGeneratedUiTypeKind
  typeParams?: string[]
  isLocalized?: boolean
  /**
   * The default value for this field.
   */
  default?: any
  validationRules?: Array<{
    /**
     * What type of validation rule is this?
     */
    type: string
    /**
     * Props for the validation rule.
     */
    props: any
  }>
  displayProps?: {
    displayName: string
    displayHint?: string
    placeholder?: string
  }
  /**
   * Additional context to use while filtering for components.
   */
  context?: [
    { key: string, value: any }
  ]
  /**
   * Properties injected by field decorators.
   */
  [decoratorKey: string]: any
}

export interface IGeneratedUiFieldSchemaDerivedTypeInfo {
  typeName: string
  jsonType: string
  isDeprecated: boolean
}

export interface IGeneratedUiFieldTypeSchema {
  typeName: string
  jsonType: string
  typeKind: EGeneratedUiTypeKind
  isLocalized?: boolean
  isGeneric?: boolean
  /**
   * A list of types deriving from this type. Available if typeKind is Abstract.
   */
  derived?: IGeneratedUiFieldSchemaDerivedTypeInfo[]
  /**
   * The fields of this class. Available if typeKind is Class or Localized
   */
  fields?: IGeneratedUiFieldInfo[]
  /**
   * A list of possible values for an enum. Available if typeKind is Enum or DynamicEnum.
   */
  possibleValues?: string[]
  /**
   * The config library associated with this field.
   * Available if typekind is StringId or ConfigLibraryItem.
   */
  configLibrary?: string
}

export interface IGeneratedUiFieldType {
  typeName: string
  typeKind: EGeneratedUiTypeKind
  typeParams?: string[]
  isLocalized?: Boolean
  schema?: IGeneratedUiFieldTypeSchema
}

export interface IGeneratedUiServerValidationResult {
  path: string
  reason: string
}

export interface IGeneratedUiFieldBaseProps {
  /**
   * Current value of the field. Can be anything.
   */
  value?: any
  fieldInfo: IGeneratedUiFieldInfo
  gameData: {}
  staticConfig: {}
  fieldSchema?: IGeneratedUiFieldTypeSchema
  // Which locale is being edited/shown currently.
  previewLocale?: string
}

export interface IGeneratedUiFieldFormProps extends IGeneratedUiFieldBaseProps {
  fieldPath?: string
  serverValidationResults?: IGeneratedUiServerValidationResult[]
  // Which locales are selected for editing.
  editLocales?: string[]
}

export interface IGeneratedUiFilterProps {
  /**
   * The current page or section.
   */
  page?: string
  /**
   * Server-injected properties.
   */
  serverContext?: {
    [key: string]: any
  }
}

/**
 * A rule for deciding if this vue component should be used to render a type of data within generated UI.
 */
export interface IGeneratedUiComponentRule {
  /**
   * A function that evaluates if this component is the right fit.
   */
  filterFunction: (props: IGeneratedUiFilterProps, type: IGeneratedUiFieldType) => boolean
  /**
   * The Vue component to use to render the data.
   * @example () => import('./ProducerIdFormField.vue')
   */
  vueComponent: any // Component types are a bit broken as of Vue 2.7
}

/**
 * A filter parameter that can be passed in to a generated form to filter specific abstract types.
 */
/**
   * A function that either returns a filter function if it exists for a given abstract type
   *  or undefined if no filter is provided.
   */
export type IGeneratedUiFormAbtractTypeFilter = (abstractType: string) => /**
     * A function that returns whether a type should be included in the list of selectable types for
     * an abstract form field.
     */
((concreteType: IGeneratedUiFieldSchemaDerivedTypeInfo) => boolean) | undefined
