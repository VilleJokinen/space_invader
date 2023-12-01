// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import { EGeneratedUiTypeKind, type IGeneratedUiFieldInfo, type IGeneratedUiFieldTypeSchema } from './generatedUiTypes'
import { GetTypeSchemaForTypeName } from './getGeneratedUiTypeSchema'

/**
 * This camelCase is a javascirpt version of Newtonsoft.Json.Utilities.StringUtils.ToCamelCase method,
 * different from lodash.camelCase which behaves differently in some cases.
 * @param str The string to transform.
 * @returns The camelCased result string.
 */
export function camelCase (str: string | undefined): string {
  const isUpper = (c: string): boolean => (c.toUpperCase() === c && c.toLowerCase() !== c)
  const setCharAt = (str: string, index: number, chr: string) => (str.substring(0, index) + chr + str.substring(index + 1))

  if (!str || str.length === 0) {
    return ''
  }
  if (!isUpper(str.charAt(0))) {
    return str
  }

  for (let i = 0; i < str.length; i++) {
    const char = str.charAt(i)
    if (i === 1 && !isUpper(char)) {
      break
    }

    if (i + 1 >= str.length) {
      break
    }

    const nextChar = str.charAt(i + 1)

    if (i > 0 && !isUpper(nextChar)) {
      if (nextChar === ' ') {
        str = setCharAt(str, i, char.toLowerCase())
      }
      break
    }

    str = setCharAt(str, i, char.toLowerCase())
  }
  return str
}

/**
 * // Change a pascal case string into a human-readable string.
 * @param str The string to transform.
 * @returns The transformed result string.
 */
export function pascalToDisplayName (str: string): string {
  if (typeof (str) !== 'string') {
    return ''
  }
  let i, j

  str = str.replace(/([A-Z])+/g, ' $1').replace(/^./, function (str) { return str.toUpperCase() }).trim()

  // Certain minor words should be left lowercase unless
  // they are the first or last words in the string
  const lowers = ['A', 'An', 'The', 'And', 'But', 'Or', 'For', 'Nor', 'As', 'At',
    'By', 'For', 'From', 'In', 'Into', 'Near', 'Of', 'On', 'Onto', 'To', 'With']
  for (i = 0, j = lowers.length; i < j; i++) {
    str = str.replace(new RegExp('\\s' + lowers[i] + '\\s', 'g'),
      function (txt) {
        return txt.toLowerCase()
      })
  }

  // Certain words such as initialisms or acronyms should be left uppercase
  const uppers = ['Id', 'Tv']
  for (i = 0, j = uppers.length; i < j; i++) {
    str = str.replace(new RegExp('\\b' + uppers[i] + '\\b', 'g'),
      uppers[i].toUpperCase())
  }
  return str
}

/**
 * Finds all languages inside an object from localized fields.
 * @param obj Object to find languages from.
 */
export function findLanguages (obj: any, gameData: any): string[] {
  if (typeof (obj) !== 'object' || obj === null || !gameData) {
    return []
  }
  if (obj.localizations) {
    return [...Object.keys(obj.localizations).filter(x => x in gameData.gameConfig.Languages)]
  }
  const returnSet = new Set<string>()
  for (const key in obj) {
    for (const lang of findLanguages(obj[key], gameData)) {
      returnSet.add(lang)
    }
  }
  return [...returnSet]
}

/**
 * Join fieldPath with fieldName from fieldInfo
 */
export function concatFieldPath (path: string, fieldInfo: IGeneratedUiFieldInfo): string {
  return (path.length === 0 ? fieldInfo.fieldName : (path + '/' + fieldInfo.fieldName)) ?? ''
}

/**
 * Strip any non MetaMember fields from an object
 */
export async function stripNonMetaFields (obj: any, schema: IGeneratedUiFieldTypeSchema): Promise<any> {
  if (schema.typeKind === EGeneratedUiTypeKind.Abstract) {
    const abstractType = obj?.$type

    if (abstractType) {
      const abstractSchema = await GetTypeSchemaForTypeName(abstractType)
      return await stripNonMetaFields(obj, abstractSchema)
    }
  }
  if (!obj || typeof (obj) !== 'object' || !schema.fields) {
    return obj
  }

  const newObject: any = {}
  newObject.$type = obj.$type

  for (const field of schema.fields) {
    const camelField = camelCase(field.fieldName)
    const fieldValue = obj[camelField]

    if (field.typeKind === EGeneratedUiTypeKind.Class || field.typeKind === EGeneratedUiTypeKind.Abstract || field.typeKind === EGeneratedUiTypeKind.Localized) {
      const fieldSchema = await GetTypeSchemaForTypeName(field.fieldType)
      newObject[camelField] = await stripNonMetaFields(fieldValue, fieldSchema)
    } else if (field.typeKind === EGeneratedUiTypeKind.ValueCollection) {
      if (!field.typeParams) throw new Error('ValueCollection must have typeParams')
      const collectionSchema = await GetTypeSchemaForTypeName(field.typeParams[0])
      if (fieldValue && (collectionSchema.typeKind === EGeneratedUiTypeKind.Class || collectionSchema.typeKind === EGeneratedUiTypeKind.Abstract)) {
        newObject[camelField] = await Promise.all(fieldValue.map(async (o: any) => await stripNonMetaFields(o, collectionSchema)))
      } else {
        newObject[camelField] = fieldValue
      }
    } else if (field.typeKind === EGeneratedUiTypeKind.KeyValueCollection) {
      if (!field.typeParams) throw new Error('ValueCollection must have typeParams')
      const collectionSchema = await GetTypeSchemaForTypeName(field.typeParams[1])
      if (fieldValue && (collectionSchema.typeKind === EGeneratedUiTypeKind.Class || collectionSchema.typeKind === EGeneratedUiTypeKind.Abstract)) {
        newObject[camelField] = Object.fromEntries(await Promise.all(Object.entries(fieldValue).map(async ([k, v]) => ([k, await stripNonMetaFields(v, collectionSchema)]))))
      } else {
        newObject[camelField] = fieldValue
      }
    } else {
      newObject[camelField] = fieldValue
    }
  }

  return newObject
}
