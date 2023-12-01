// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * Alert type used by the MetaPageContainer component.
 * @example {
 *  title: 'Example Warning',
 *  message: 'Your mood has cooled down. Consider playing a trance anthem to get back into the zone.'
 * }
 */
export interface MetaPageContainerAlert {
  /**
   * Title of the alert.
   */
  title: string
  /**
   * Main body of the alert message.
   */
  message: string
  /**
   * Optional: The variant of the alert controls the styling. In MetaPageContainerAlert this also controls the styling
   * of the page background.
   */
  variant?: 'info' | 'secondary' | 'warning' | 'danger'
  /**
   * Optional: Add a `data-cy` element to the alert. Used in checking for the presence of elements in Cypress tests.
   */
  dataCy?: string
  /**
   * Optional: Key to use in the alert list v-for. If not given, the title is used as the key by default.
   */
  key?: string
}

/**
 * Base type definition of a selected option for the MetaInputSelect component.
 */
export interface MetaInputSelectOption<T> {
  id: string
  value: T
  disabled?: boolean
}

/**
 * Result data from the `MetaInputPlayerSelect` component.
 */
export interface PlayerSelectResult {
  $type: string
  id: string
  deserializedSuccessfully: boolean
  name: string
  level: number
  createdAt: string
  lastLoginAt: string
  deletionStatus: string // 'None' | 'Deleted' | ...
  isBanned: boolean
  totalIapSpend: number
  isDeveloper: boolean
  deserializationException: string | null
}

/**
 * Result data from the `MetaInputGuildSelect` component.
 */
export interface GuildSearchResult {
  $type: string
  entityId: string
  displayName: string
  createdAt: string
  lastLoginAt: string
  phase: string
  numMembers: number
  maxNumMembers: number
}
