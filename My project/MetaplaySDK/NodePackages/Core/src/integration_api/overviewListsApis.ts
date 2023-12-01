// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { useCoreStore } from '../coreStore'
import { type LayoutRule } from './uiPlacementApis'

/**
 * All the available display types for an overview list item.
 */
enum OverviewListDisplayType {
  country = 'country',
  currency = 'currency',
  datetime = 'datetime',
  language = 'language',
  number = 'number',
  text = 'text'
}
/**
 * All the available display hints for an overview list item.
 */
enum OverviewListDisplayHint {
  date = 'date',
  highlightIfNonZero = 'highlightIfNonZero',
  monospacedText = 'monospacedText',
  timeAgo = 'timeago'
}
/**
 * Defines an item to be included in an overview list.
 * This is an internal class that is accessed using the static functions defined below.
 */
export class OverviewListItem {
  /**
   * Text label for this overview list item.
   * @example 'Multiplayer Rank'
   */
  displayName: string
  /**
   * A function that returns the value to display.
   * @example (playerModel: any) => playermodel.multiplayerRank
   */
  // Note: Return type 'any' added as a fix in R21.
  // TODO: Add better type support for child component types.
  // TODO: Requires refactoring MetaCountryCode and MetaLanguageLabel componenets.
  displayValue: (sourceObject: any) => any
  /**
   * Select how to format the `displayValue`.
   * @example OverviewListDisplayType.text
   */
  displayType: OverviewListDisplayType
  /**
   * Optional: Set a permission requirement for viewing this value.
   * By default, all values are visible however, you can set this property to hide the value if the user does not have the given permission.
   * @example 'api.players.set_wallet'
   */
  displayPermission?: string
  /**
   * Optional: display style hint to the child component selected by `displayType`.
   * @example OverviewListDisplayHint.monospacedText
   */
  displayHint?: OverviewListDisplayHint

  private constructor (displayName: string, displayValue: (sourceObject: any) => Date | number | string, displayType: OverviewListDisplayType, displayPermission?: string, displayHint?: OverviewListDisplayHint) {
    this.displayName = displayName
    this.displayValue = displayValue
    this.displayType = displayType
    this.displayPermission = displayPermission
    this.displayHint = displayHint
  }

  /**
   * Displays a number value with optional highlighting.
   * @param displayName Text label for this overview list item.
   * @param displayValue A function that returns the number to display.
   * @param displayPermission Optional: Set a permission requirement for viewing this value. The value will be hidden for users lacking the permission or always shown if set to undefined.
   * @param highlightIfNonZero Optional: Set to true to add a highlight color to non-zero numbers. For example, draw attention to the number of errors. Defaults to `false`.
   * @example
   * OverviewListItem.asNumber(
   *   'Highest Producer Level',
   *   (player) => {
   *     const producers = Object.entries(player.model.producers)
   *     const maxProducerLevel = producers.reduce((p, c: any) => Math.max(c[1].level, p), 0)
   *     return maxProducerLevel
   *   }
   * )
  */
  static asNumber (displayName: string, displayValue: (sourceObject: any) => number, displayPermission?: string, highlightIfNonZero: boolean = false) {
    return new OverviewListItem(
      displayName,
      displayValue,
      OverviewListDisplayType.number,
      displayPermission ?? undefined,
      highlightIfNonZero ? OverviewListDisplayHint.highlightIfNonZero : undefined,
    )
  }

  /**
   * Displays a date value as a human readable date using the `meta-time` component.
   * @param displayName Text label for this overview list item.
   * @param displayValue A function that returns the date to display.
   * @param displayPermission Optional: Set a permission requirement for viewing this value. The value will be hidden for users lacking the permission or always shown if set to undefined.
   * @example
   * OverviewListItem.asDate(
   *   'Joined',
   *   (player: any) => {
   *     return player.model.stats.createdAt
   *   }
   * )
  */
  static asDate (displayName: string, displayValue: (sourceObject: any) => Date, displayPermission?: string) {
    return new OverviewListItem(
      displayName,
      displayValue,
      OverviewListDisplayType.datetime,
      displayPermission ?? undefined,
      OverviewListDisplayHint.date,
    )
  }

  /**
   * Displays a date value as a time period relative to the current date using the `meta-time` component.
   * For example, 'A few minutes ago'.
   * @param displayName Text label for this overview list item.
   * @param displayValue A function that returns the date to display.
   * @param displayPermission Optional: Set a permission requirement for viewing this value. The value will be hidden for users lacking the permission or always shown if set to undefined.
   * @example
   * OverviewListItem.asTimeAgo(
   *   'Last Login',
   *   (player: any) => {
   *     return player.model.stats.lastLoginAt
   *   }
   * )
  */
  static asTimeAgo (displayName: string, displayValue: (sourceObject: any) => Date, displayPermission?: string) {
    return new OverviewListItem(
      displayName,
      displayValue,
      OverviewListDisplayType.datetime,
      displayPermission ?? undefined,
      OverviewListDisplayHint.timeAgo,
    )
  }

  /**
   * Displays a text value with optional monospace font.
   * For example, to display a string that includes both text and numbers.
   * @param displayName Text label for this overview list item.
   * @param displayValue A function that returns the text to display.
   * @param displayPermission Optional: Set a permission requirement for viewing this value. The value will be hidden for users lacking the permission or always shown if set to undefined.
   * @param monospacedText Optional: Set to true displays text using monospace font. Defaults to `false`.
   * @example
   * OverviewListItem.asString(
   *   'Player Name',
   *   (player: any) => {
   *     return player.model.playerName || 'n/a'
   *   }
   * )
   */
  static asString (displayName: string, displayValue: (sourceObject: any) => string, displayPermission?: string, monospacedText: boolean = false) {
    return new OverviewListItem(
      displayName,
      displayValue,
      OverviewListDisplayType.text,
      displayPermission ?? undefined,
      monospacedText ? OverviewListDisplayHint.monospacedText : undefined,
    )
  }

  /**
   * Displays an ISO country code ('FI' for Finland) in a human readable way.
   * @param displayName Text label for this overview list item.
   * @param displayValue A function that returns the text to display.
   * @param displayPermission Optional: Set a permission requirement for viewing this value.
   * The value will be hidden for users lacking the permission or always shown if set to undefined.
   * @example
   * OverviewListItem.asCountry(
   *   'Last location',
   *   (player: any) => {
   *     return player.model.lastKnownLocation ? player.model.lastKnownLocation.country.isoCode : 'Unknown'
   *   }
   * )
   */
  static asCountry (displayName: string, displayValue: (sourceObject: any) => string, displayPermission?: string) {
    return new OverviewListItem(
      displayName,
      displayValue,
      OverviewListDisplayType.country,
      displayPermission ?? undefined,
    )
  }

  /**
   * Displays an ISO language code in a human readable way.
   * For example 'en' is displayed as 'English'
   * @param displayName Text label for this overview list item.
   * @param displayValue A function that returns the text to display.
   * @param displayPermission Optional: Set a permission requirement for viewing this value. The value will be hidden for users lacking the permission or always shown if set to undefined.
   * @example
   * OverviewListItem.asLanguage(
   *   'Player Language',
   *   (player: any) => {
   *     return player.model.language
   *   }
   * )
   */
  static asLanguage (displayName: string, displayValue: (sourceObject: any) => string, displayPermission?: string) {
    return new OverviewListItem(
      displayName,
      displayValue,
      OverviewListDisplayType.language,
      displayPermission ?? undefined,
    )
  }

  /**
   * Displays a monetary value with a default currency symbol ($ USD).
   * For example '$45.34'
   * @param displayName Text label for this overview list item.
   * @param displayValue A function that returns a monetary value to display.
   * @param displayPermission Optional: Set a permission requirement for viewing this value. The value will be hidden for users lacking the permission or always shown if set to undefined.
   * @example
   * OverviewListItem.asCurrency(
   *   'Average Spend',
   *   (player: any) => {
   *     return player.model.totalIapSpend
   *   }
   * )
   */
  static asCurrency (displayName: string, displayValue: (sourceObject: any) => number, displayPermission?: string) {
    return new OverviewListItem(
      displayName,
      displayValue,
      OverviewListDisplayType.currency,
      displayPermission ?? undefined,
    )
  }
}

/**
 * Add game-specific content into the overview card of the player details page.
 * @param item Item to be inserted.
 * @param layoutRule Optional: how to position the new item in relation to other possible components in the same placement.
 * @param permission Optional: Set a permission requirement for viewing this value. The value will be hidden for users lacking the permission or always shown if set to undefined.
 * @example
 * initializationApi.addPlayerDetailsOverviewListItem(
 *   OverviewListItem.asNumber(
 *     'Highest Producer Level',
 *     (player) => {
 *       const producers = Object.entries(player.model.producers)
 *       const maxProducerLevel = producers.reduce((p, c: any) => Math.max(c[1].level, p), 0)
 *       return maxProducerLevel
 *     }),
 *    // Set the 'displayPermission' property to require permission to view this list-item.
 *    // The list-item will be hidden if a user does not have the required permission.
 *    'api.players.set_wallet',
 *   { position: 'after', targetId: 'Joined' }
 * )
 */
export function addPlayerDetailsOverviewListItem (item: OverviewListItem, layoutRule?: LayoutRule) {
  const coreStore = useCoreStore()
  addOverviewListItem(coreStore.overviewLists.player, item, layoutRule)
}

/**
 * Add game-specific content into the "reconnect player accounts" modal's account preview list.
 * @param item Item to be inserted.
 * @param layoutRule Optional: how to position the new item in relation to other possible components in the same placement.
 * @param displayPermission Optional: Set a permission requirement for viewing this value. The value will be hidden for users lacking the permission or always shown if set to undefined.
 * @example
 * initializationApi.addPlayerReconnectAccountPreviewListItem(
 *   OverviewListItem.asNumber(
 *     'Highest Producer Level',
 *     (player) => {
 *       const producers = Object.entries(player.model.producers)
 *       const maxProducerLevel = producers.reduce((p, c: any) => Math.max(c[1].level, p), 0)
 *       return maxProducerLevel
 *     }),
 *   { position: 'after', targetId: 'Joined' }
 * )
 */
export function addPlayerReconnectAccountPreviewListItem (item: OverviewListItem, layoutRule?: LayoutRule) {
  const coreStore = useCoreStore()
  addOverviewListItem(coreStore.overviewLists.playerReconnectPreview, item, layoutRule)
}

/**
 * Add game-specific content into the overview card of the guild details page.
 * @param item Item to be inserted.
 * @param layoutRule Optional: how to position the new item in relation to other possible components in the same placement.
 * @param displayPermission Optional: Set a permission requirement for viewing this value. The value will be hidden for users lacking the permission or always shown if set to undefined.
 * @example
 * initializationApi.addGuildDetailsOverviewListItem(
 *   OverviewListItem.asString(
 *     'New Phase',
 *     (guild) => {
 *       return guild.model.lifecyclePhase
 *     }),
 *    // Set the 'displayPermission' property to require permission to view this list-item.
 *    // The list-item will be hidden if a user does not have the required permission.
 *   'api.guilds.edit',
 *   { position: 'after', targetId: 'Members Online' }
 * )
 */
export function addGuildDetailsOverviewListItem (item: OverviewListItem, layoutRule?: LayoutRule) {
  const coreStore = useCoreStore()
  addOverviewListItem(coreStore.overviewLists.guild, item, layoutRule ?? { position: 'after' })
}

/**
 * Insert a new item into a given overview list.
 * @param overviewList List to insert the new item into.
 * @param item Item to be inserted.
 * @param layoutRule Optional: how to position the new item in relation to other possible components in the same placement.
 */
function addOverviewListItem (overviewList: OverviewListItem[], item: OverviewListItem, layoutRule?: LayoutRule) {
  layoutRule = layoutRule ?? { position: 'after' }

  if (layoutRule.position === 'before') {
    if (layoutRule.targetId === undefined) {
      overviewList.unshift(item)
    } else {
      const targetId = layoutRule.targetId
      const targetIndex = overviewList.findIndex((item) => item.displayName === targetId)
      if (targetIndex === -1) {
        throw new Error(`Could not find target id '${layoutRule.targetId}' for overview list item '${item.displayName}'.`)
      }
      overviewList.splice(targetIndex, 0, item)
    }
  } else if (layoutRule.position === 'after') {
    if (layoutRule.targetId === undefined) {
      overviewList.push(item)
    } else {
      const targetId = layoutRule.targetId
      const targetIndex = overviewList.findIndex((item) => item.displayName === targetId)
      if (targetIndex === -1) {
        throw new Error(`Could not find target id '${layoutRule.targetId}' for overview list item '${item.displayName}'.`)
      }
      overviewList.splice(targetIndex + 1, 0, item)
    }
  } else { // 'replace'
    if (layoutRule.targetId) {
      const targetId = layoutRule.targetId
      const targetIndex = overviewList.findIndex((item) => item.displayName === targetId)
      if (targetIndex === -1) {
        throw new Error(`Could not find target id '${targetId}' for overview list item '${item.displayName}'`)
      }
      overviewList[targetIndex] = item
    } else {
      throw new Error(`Could not place overview list item '${item.displayName}' because "replace" layoutRule requires a targetId.`)
    }
  }
}
