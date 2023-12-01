// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// Definitions of various data types that go to make up the server responses. ---------------------

export type GameDataBuildStatus = 'Building' | 'Succeeded' | 'Failed'

/**
 * Base class for game configs data that the server returns.
 */
interface GameConfigInfoBase {
  id: string
  name: string
  description: string
  status: GameDataBuildStatus
  isActive: boolean
  isArchived: boolean
  fullConfigVersion: string
  cdnVersion: string

  persistedAt: string
  lastModifiedAt: string
  archiveBuiltAt: string
  source: string

  blockingGameConfigMessageCount: number
  failureInfo?: string
  contentsParseError?: string
}

export interface BuildReportSummary {
  buildLogLogLevelCounts: {[key: string]: number}
  isBuildMessageTrimmed: boolean
  validationResultsLogLevelCounts: {[key: string]: number}
  isValidationMessagesTrimmed: boolean
  totalLogLevelCounts: {[key: string]: number}
}

export type GameConfigLogLevel = 'NotSet' | 'Verbose' | 'Debug' | 'Information' | 'Warning' | 'Error'

export interface GameConfigBuildMessage {
  sourceInfo: string | null
  sourceLocation: string | null
  locationUrl: string | null
  itemId: string | null
  variantId: string | null
  level: GameConfigLogLevel
  message: string
  exception: string | null
  callerFileName: string
  callerMemberName: string
  callerLineNumber: number
}

export interface GameConfigValidationMessage {
  sheetName: string
  configKey: string
  message: string
  columnHint: string
  variants: string[]
  url: string
  sourcePath: string
  sourceMember: string
  sourceLineNumber: number
  messageLevel: GameConfigLogLevel
  count: string
  additionalData: {[key: string]: any}
}

export interface GameConfigBuildReport {
  highestMessageLevel: GameConfigLogLevel
  buildMessages: GameConfigBuildMessage[]
  validationMessages: GameConfigValidationMessage[]
}

export interface GameConfigMetaData {
  buildParams: { [key: string]: any } // GameConfigBuildParameters - game specific
  buildSourceMetadata: { [key: string]: any } // GameConfigBuildParameters - game specific
  buildDescription: string
  buildReport: GameConfigBuildReport | null

  parentConfigId: string
  parentConfigHash: string
}

export interface ExperimentData {
  id: string
  displayName: string
  patchedLibraries: string[]
  variants: string[]
}

interface GameConfigEntryImportError {
  exceptionType: string
  message: string
  fullException: string
}

/**
 * Describes an item in a library.
 */
export interface LibraryConfigItem {
  /**
   * C# type.
   */
  type: string
  /**
   * Title to display for this item. Usually the member's name.
   */
  title: string
  /**
   * Optional subtitle that contains extra information about the item.
   */
  subtitle?: string
  /**
   * Optional sparse list of values for this item. The list contains entries for each variant of the selected
   * experiment. If there are no changes in any of the variants then this list will just include a single entry under
   * the key `Baseline`. This field is optional because some items (such as arrays) don't themselves have any values.
   */
  values?: { [variant: string]: string}
  /**
   * True if the item has any differences.
   */
  differences?: boolean
  /**
   * Optional list of child items, ie: for arrays, dictionaries and objects.
   */
  children?: LibraryConfigItem[]
}

// These are the responses from the various server endpoints. -------------------------------------

/**
 * Game config data as returned by the `/gameConfig`.
 */
export interface StaticGameConfigInfo {
  id: string
  name: string
  description: string
  status: GameDataBuildStatus
  isActive: boolean
  isArchived: boolean

  persistedAt: string
  archiveBuiltAt: string
  source: string

  blockingGameConfigMessageCount: number
  failureInfo?: string

  bestEffortIsPublishBlockedByErrors: boolean
  publishBlockingError: string
  buildReportSummary: BuildReportSummary
}

/**
 * Game config data as returned by the `/gameConfig/{configIdStr}` endpoint.
 */
export interface StaticGameConfigContents extends GameConfigInfoBase {
  contents: {
    metaData: GameConfigMetaData
    sharedConfig: { [library: string]: number }
    serverConfig: { [library: string]: number }
  }
}

/**
 * Game config data as returned by the `/gameConfig/{configIdStr}/count` endpoint.
 */
export interface LibraryCountGameConfigInfo extends GameConfigInfoBase {
  contents: {
    metaData: GameConfigMetaData
    sharedLibraries: { [library: string]: number }
    serverLibraries: { [library: string]: number }
  }
  experiments: ExperimentData
  configValidationError: string | null
  libraryParsingErrors?: { [library: string]: GameConfigEntryImportError }
  isPublishBlockedByErrors: boolean
  publishBlockingError: string
  buildReportSummary: BuildReportSummary
}

/**
 * Game config details as returned by the `/gameConfig/{configIdStr}/details` endpoint.
 */
export interface GameConfigLibraryDetails {
  config: {[id: string]: LibraryConfigItem}
  libraryParsingErrors?: { [library: string]: GameConfigEntryImportError }
}
