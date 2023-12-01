import type { GameDataBuildStatus } from './gameConfigServerTypes'
/**
 * Describes an item in a library.
 */
export interface LocalizationTableItem {
  /**
   * C# type.
   */
  type: string
  /**
   * Title to display for this item. Usually the member's name.
   */
  title: string
  /**
   * Object containing key-value pairs for different language translations.
   * TODO: instead of a string, this probably should be a type that can more efficiently tell us if the value is missing. Now we do a lot of string content checks in downstream code.
   */
  values: {[key: string]: string}
}

export interface LocalizationTable {
  info: LocalizationInfo
  table: LocalizationTableItem[]
  languageIds: string[]
}

export interface LocalizationContent {
  info: LocalizationInfo
  locs: {
    [languageId: string]: {
      languageId: string
      contentHash: string
      translations: {[key: string]: string}
    }
  }
}

export interface LocalizationInfo {
  id: string
  name: string
  description: string
  status: GameDataBuildStatus
  isActive: boolean
  isArchived: boolean

  persistedAt: string
  lastModifiedAt: string
  archiveBuiltAt: string
  source: string

  failureInfo?: string
}
