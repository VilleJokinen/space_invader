// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import type { LibraryConfigItem } from './gameConfigServerTypes'

export enum LibraryFlattenedItemClickAction {
  Expand = 'Expand',
  Collapse = 'Collapse',
}

/**
 * Description of a `LibraryConfigItem` in a simplified way that the UI knows how to render.
 */
export interface LibraryFlattenedItem {
  /**
   * Absolute path to this item. Hierarchical path parts are separated by the `:` character.
   */
  path: string
  /**
   * How many times to indent this item when rendering. This is just an efficient shortcut to a value derived from
   * the path.
   */
  indentation: number
  /**
   * Title of the item.
   */
  title: string
  /**
   * Optional subtitle of the item.
   */
  subtitle?: string
  /**
   * C# type.
   */
  type: string
  /**
   * Sparse array of values.
   */
  values?: { [key: string]: string }
  /**
   * True if the item has any differences.
   */
  differences?: boolean
  /**
   * Optional action that should be carried out if this item is clicked on.
   */
  clickAction?: LibraryFlattenedItemClickAction
}

/**
 * Convert a list of config items from the server into a flattened render list for the UI. The hierarchical server
 * list is flattened into an array whose sub branches are optionally expanded according to `expandedPaths`.
 * @param libraryConfigItems Hierarchical list of config items for this library in server format.
 * @param expandedPaths List of paths that should be expanded during the flattening process.
 * @returns Flattened render list.
 */
export function flattenLibraryConfigItems (libraryConfigItems: LibraryConfigItem[], expandedPaths: string[]): LibraryFlattenedItem[] {
  // The final output list from this function is stored here.
  const flattenedItems: LibraryFlattenedItem[] = []

  // Add a list of config items.
  function expandItems (libraryConfigItems: LibraryConfigItem[], basePath: string, indentationLevel: number) {
    libraryConfigItems.forEach(libraryConfigItem => {
      // Gather information about this item.
      const path = `${basePath}${libraryConfigItem.title}`
      const hasChildren = (libraryConfigItem.children && libraryConfigItem.children.length > 0)
      const isExpanded = hasChildren && expandedPaths.includes(path)
      const newItem: LibraryFlattenedItem = {
        path,
        indentation: indentationLevel,
        title: libraryConfigItem.title,
        subtitle: libraryConfigItem.subtitle ?? undefined,
        type: libraryConfigItem.type,
        values: libraryConfigItem.values,
        differences: libraryConfigItem.differences ?? undefined,
        clickAction: isExpanded ? LibraryFlattenedItemClickAction.Collapse : (hasChildren ? LibraryFlattenedItemClickAction.Expand : undefined),
      }

      // Store it in the output list.
      flattenedItems.push(newItem)

      // If this item is expanded then recurse into it.
      // Note: Using an expressive if to avoid a TS error.
      if (isExpanded && libraryConfigItem.children) {
        expandItems(libraryConfigItem.children, path + ':', indentationLevel + 1)
      }
    })
  }

  // Start the process by recursing into the root level object.
  expandItems(libraryConfigItems, '', 0)

  // Done.
  return flattenedItems
}
