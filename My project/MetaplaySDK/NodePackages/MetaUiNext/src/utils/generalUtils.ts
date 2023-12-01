// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

let uniqueKeyIndex = 0
/**
 * Adds a running number as the suffix of a given key.
 * @param baseKey The original key to transform.
 * @returns A unique string.
 */
export function makeIntoUniqueKey (baseKey: string | number) {
  const uniquePart = (uniqueKeyIndex++).toString(36).padStart(4, '0')
  if (baseKey) {
    return `${baseKey}_${uniquePart}`
  } else {
    return uniquePart
  }
}
