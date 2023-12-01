// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview Contains definitions for classes that are used to config sorting and filtering in `MetaListCard`s.
 * There are two concepts here:
 * Sorting defines in what order items are listed. You will typically defined multiple sort options.
 * Filtering is used to optionally show/hide items according to whether they pass the filter criteria or not. Filters
 * are stored in related groups as filter "sets".
 */

/**
 * Defines the direction of sorting in a `MetaListSortOption`.
 */
export enum MetaListSortDirection {
  // eslint-disable-next-line no-unused-vars
  Unsorted = 0,
  // eslint-disable-next-line no-unused-vars
  Ascending = 1,
  // eslint-disable-next-line no-unused-vars
  Descending = -1,
}

/**
 * Defines a sort option for a MetaListCard.
 */
export class MetaListSortOption {
  /**
   * The name of the sort option as shown in the UI.
   */
  displayName: string

  /**
   * The data field to sort by.
   */
  dataFieldName: string | null

  /**
   * The direction to sort in.
   */
  direction: MetaListSortDirection

  /**
   * Create a new custom sorting option into a MetaListCard component.
   * @param displayName The name of the sort option visible in the UI.
   * @param dataFieldName The data field to sort by.
   * @param direction The direction to sort in.
   */
  constructor (displayName: string, dataFieldName: string | null, direction: MetaListSortDirection) {
    this.displayName = displayName
    this.dataFieldName = dataFieldName
    this.direction = direction
  }

  /**
   * Create a new 'Unsorted' option into a MetaListCard component.
   * @returns A pre-configured 'Unsorted' option.
   */
  static asUnsorted () {
    return new MetaListSortOption('Unsorted', null, MetaListSortDirection.Unsorted)
  }
}

/**
 * Defines a filter option for a MetaListCard. A filter option is used by the MetaListCard UI to allow the user to
 * show or hide items that pass the filter. For example, one could be created for players to filter in/out those who
 * have completed some game milestone, or spent money in the game. Multiple related `MetaListFilterOption`s are
 * collected together into one `MetaListFilterSet`.
 */
export class MetaListFilterOption {
  /**
   * The name of the filter option visible in the UI.
   */
  displayName: string

  /**
   * The filter function to use on list items. This function is passed the object to test and should return true if
   * that object "passes" the filter test.
   */
  filterFn: (item: object) => boolean

  /**
   * Should the filter be on by default?
   */
  initiallyActive: boolean

  /**
   * Create a new custom filter option for a MetaListCard component.
   * @param displayName The name of the filter option visible in the UI.
   * @param filterFn The filter function to use on list items.
   * @param initiallyActive Should the filter be on by default?
   */
  constructor (displayName: string, filterFn: (item: object) => boolean, initiallyActive = false) {
    this.displayName = displayName
    this.filterFn = filterFn
    this.initiallyActive = initiallyActive
  }
}

/**
 * A set of related `MetaListFilterOption`s that are made available to the user inside a MetaListCard. Options are
 * grouped together into Sets for UI readability reasons: related filters are shown next to each other.
 */
export class MetaListFilterSet {
  /**
   * Used internally to uniquely identify this set. The key must be unique among all sets that are passed to a
   * `MetaListCard`.
   */
  key: string

  /**
   * List of `MetaListFilterOption`s` that belong to this set.
   */
  filterOptions: MetaListFilterOption[]

  /**
   * Create a new set of filter options for a MetaListCard component.
   * @param key Used internally to uniquely identify this set.
   * @param filterOptions List of `MetaListFilterOption`s` that belong to this set.
   */
  constructor (key: string, filterOptions: MetaListFilterOption[]) {
    this.key = key
    this.filterOptions = filterOptions
  }

  /**
   * Automatically create a new set of filters based on some values in an set of items. Pass a list of items (this is
   * almost always the same as the list that you pass to the MetaListCard itself) along with a function that can
   * extract a filter string from those items.
   * This helper is a quick and easy way to generate a filter set from the unique values of a particular field in a
   * list of items.
   * @param items List of items to search.
   * @param key Unique key for the created filter set.
   * @param extractKeyFn Function that takes an item and extracts a value.
   * @returns
   */
  static asDynamicFilterSet (items: object[], key: string, extractKeyFn: (item: object) => string) {
    if (items) {
      const uniqueKeysSet = new Set(items.map(item => extractKeyFn(item)))
      const uniqueKeysArray = [...uniqueKeysSet.keys()].sort()
      const options = uniqueKeysArray.map(
        (uniqueKey) => new MetaListFilterOption(uniqueKey, (item) => extractKeyFn(item) === uniqueKey)
      )
      return new MetaListFilterSet(key, options)
    } else {
      return new MetaListFilterSet(key, [])
    }
  }
}
