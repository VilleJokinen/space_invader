// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import countryEmoji from 'country-emoji'
import { Duration } from 'luxon'

/**
 * Abbreviates an arbitrary number into a short display string. For example 123321 -> 123k.
 * @param x The number to abbreviate.
 * @param precision The amount of numbers to display after abbreviation.
 * @returns A string with the abbreviated result.
 */
export function abbreviateNumber (x: number, precision: number = 3): string | undefined {
  if (x >= 1000000000.0) {
    return (x / 1000000000.0).toPrecision(precision) + 'B'
  } else if (x >= 1000000.0) {
    return (x / 1000000.0).toPrecision(precision) + 'M'
  } else if (x >= 1000.0) {
    return (x / 1000.0).toPrecision(precision) + 'k'
  } else {
    if (x || x === 0) return x.toString()
    else return undefined
  }
}

/**
 * Transforms a camelCase string string to sentence case.
 * @param string String to transform
 * @returns Transformed string
 * @example
 * toSentenceCase('thisIsAString') => 'This Is A String'
 */
export function toSentenceCase (string: string): string {
  // RegEx replacement to precede all upper-case chars with a space unless they are the first character of the string.
  const result = string.replace(/([A-Z])/g, ' $1').trim()
  return result.charAt(0).toUpperCase() + result.slice(1)
}

/**
 * Transforms an objects key-value pairs into printable fields.
 * @param obj Object to transform
 * @returns Array of string values
 * @example
 * getObjectPrintableFields({serverHost : 'localhost'}) => [{key: "serverHost", name: "Server Host", value: "localhost"}]
 */
export function getObjectPrintableFields (obj: { [key: string]: any }): Array<{ key: string, name: string, value: string }> {
  const fields = []
  for (const key in obj) {
    if (key === '$type') {
      continue
    }
    const name = toSentenceCase(String(key))
    const value = obj[key]
    fields.push({ key, name, value })
  }
  return fields
}

/**
 * Transforms an arbitrary string into English Title Case.
 * @param str The string to transform.
 * @returns The transformed result string.
 */
export function toTitleCase (str: string): string {
  let i, j
  str = str.replace(/([^\W_]+[^\s-]*) */g, function (txt) {
    return txt.charAt(0).toUpperCase() + txt.substring(1).toLowerCase()
  })

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
 * Returns the display name of a given country based on it's ISO country code.
 * @param isoCode The source ISO country code.
 * @returns Display name of the country.
 */
export function isoCodeToCountryName (isoCode: string): string {
  return countryEmoji.name(isoCode) ?? isoCode
}

/**
 * Returns the flag emoji of a given country based on it's ISO country code.
 * @param isoCode The source ISO country code.
 * @returns Flag emoji of the country.
 */
export function isoCodeToCountryFlag (isoCode: string): string {
  return countryEmoji.flag(isoCode) ?? ''
}

/**
 * Humanize a dashboard username. For example: no_id -> No User ID.
 * @param username The username to humanize.
 * @returns Humanized result.
 */
export function humanizeUsername (username: string): string {
  if (username === 'auth_not_enabled') return 'Anonymous'
  else if (username === 'no_id') return 'No User ID'
  else return username
}

/**
 * Generates a humanized string that pluralizes a word if needed. For example: 1 apple or 2 apples.
 * @param count The number of units. For example: 2
 * @param unit The unit to be pluralized if needed. For example: apple.
 * @param showCount Wether or not the count should be included in the beginning of the string. For example: apples or 2 apples. Defaults to true.
 * @returns The generated string.
 */
export function maybePlural (count: number, unit: string, showCount: boolean = true): string {
  let result = ''
  if (showCount) {
    result = `${count} `
  }
  return result + `${unit}${count !== 1 ? 's' : ''}`
}

/**
 * Internal utility function for gathering device history entries that have a specific login method
 * associated to them.
 * @param authMethodId The authentication key of a login method.
 * @param devices Device history from player model.
 * @returns Array of (deviceId, deviceModel) pairs that match the query.
 */
function findDevicesForAuthMethod (authMethodId: string, devices: any): Array<{
  id: string
  deviceModel: string
}> {
  return Object.entries(devices ?? {})
    .filter(([k, v]: [string, any]) => v.loginMethods.some((login: any) => login?.id === authMethodId))
    .map(([k, v]: [string, any]) => { return { id: k, deviceModel: v.deviceModel as string } })
}

/**
 * Get display string for a ClientToken login entry based on the devices that is has been used with.
 * @param devices List of devices associated to the login.
 * @returns Display string.
 */
function getClientTokenDisplayString (devices: any[]): string {
  if (devices.length === 0) return 'ClientToken'
  else if (devices.length === 1) return devices[0].deviceModel
  else return 'ClientToken (multiple devices)'
}

/**
 * Transforms a list of raw auth records into more ergonomic objects for easier UI work.
 * @param auths An array of authentication objects.
 * @returns An array of objects in our dashboard preferred format.
 */
// eslint-disable-next-line @typescript-eslint/explicit-function-return-type -- TODO: Make this return type explicit
export function parseAuthMethods (auths: Array<{ name: string, id: string, attachedAt: string }>, devices: any) {
  const res = []
  const authStrings = Object.keys(auths)
  for (const a of authStrings) {
    const properties = a.split('/')
    const associatedDevices = findDevicesForAuthMethod(properties[1], devices)
    const isClientToken = properties[0] === 'DeviceId'
    res.push(Object.assign({
      name: properties[0],
      id: properties[1],
      type: isClientToken ? 'device' : 'social',
      displayString: isClientToken ? getClientTokenDisplayString(associatedDevices) : properties[0],
      devices: associatedDevices
    }, auths[a as any]))
  }
  return res
}

/**
 * Adds a suffix to a string if it doesn't already have it.
 * @param str The string to transform.
 * @param suffix The suffix string to add if needed.
 * @returns A string with the provided suffix.
 */
export function ensureEndsWith (str: string, suffix: string): string {
  if (str.endsWith(suffix)) {
    return str
  } else {
    return str + suffix
  }
}

/**
 * Returns a language name when given an ISO language code
 * @param languageId ISO language code
 * @param gameData Reference to gameData
 * @returns Language name or languageId if language not found in the gameData or gameData isn't loaded
 */
export function getLanguageName (languageId: string, gameData: any): string {
  const languageInfo = gameData?.gameConfig.Languages[languageId]
  return languageInfo?.displayName || languageId
}

export function metaJsonDurationToLuxonDuration (metaJsonDuration: string): Duration {
  const split1 = metaJsonDuration.split('.')
  const days = parseInt(split1[0], 10)
  // const ticks = parseInt(split1[2], 10) <- ignoring ticks for now
  const split2 = split1[1].split(':')
  const hours = parseInt(split2[0], 10)
  const minutes = parseInt(split2[1], 10)
  const seconds = parseInt(split2[2], 10)
  return (Duration.fromObject({ days, hours, minutes, seconds }))
}

let uniqueKeyIndex = 0
/**
 * Adds a running number as the suffix of a given key.
 * @param baseKey The original key to transform.
 * @returns A unique string.
 */
export function makeIntoUniqueKey (baseKey: string | number): string {
  const uniquePart = (uniqueKeyIndex++).toString(36).padStart(4, '0')
  if (baseKey) {
    return `${baseKey}_${uniquePart}`
  } else {
    return uniquePart
  }
}

/**
 * Returns an approximate size of an arbitrary JS object. Not super scientific, but good enough for a ballpark estimate.
 * @param object The object to evaluate.
 * @returns An integer with the estimated size in bytes.
 */
export function roughSizeOfObject (object: any): number {
  const objectList: any[] = []
  const stack = [object]
  let bytes = 0

  while (stack.length) {
    const value: any = stack.pop()

    if (typeof value === 'boolean') {
      bytes += 4
    } else if (typeof value === 'string') {
      bytes += value.length * 2
    } else if (typeof value === 'number') {
      bytes += 8
    } else if (typeof value === 'object' && !objectList.includes(value)) {
      objectList.push(value)

      for (const i in value) {
        stack.push(value[i])
      }
    }
  }
  return bytes
}

export function roundToDigits (value: number, numDigits: number): string {
  return value.toFixed(numDigits || 0)
}

/** Returns brief explanation of an experiment phase.
 * @param phaseName The id of the experiment phase.
 * @returns Human readable explanation of the phase.
 */
export function experimentPhaseDetails (phaseName: string): string {
  const experimentPhaseDetails: { [key: string]: string } = {
    Testing: 'The experiment is not yet active, it is only active to testers.',
    Active: 'The experiment is active for all players.',
    Paused: 'The experiment is temporarily suspended; it is only active for testers.',
    Concluded: 'The experiment is no longer active for all players.',
  }

  return experimentPhaseDetails[phaseName]
}

/**
 * Resolve using function or named path in object, eg:
 * resolve({item:{body:123}}), (elem) => elem.item.body) returns 123
 * resolve({item:{body:123}}), 'item.body') returns 123
 * @param obj Object to resolve from.
 * @param path The function or named path to resolve.
 * @returns The resolved function or path from the original object.
 */
export function resolve (obj: any, path: string | Function): any {
  if (typeof path === 'function') {
    return path(obj)
  } else {
    return path.split('.').reduce((prev, curr) => {
      return prev ? prev[curr] : undefined
    }, obj || self)
  }
}
