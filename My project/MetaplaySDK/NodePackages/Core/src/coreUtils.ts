// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { DateTime, Duration } from 'luxon'
import moment from 'moment'
import type { LocationQuery } from 'vue-router'

/**
 * Transforms a string into kebab-case. For example someComponentName -> some-component-name.
 * @param str Original string to transform.
 * @returns Transformed output string.
 */
export function toKebabCase (str: string): string | undefined {
  return str
    .match(/[A-Z]{2,}(?=[A-Z][a-z]+[0-9]*|\b)|[A-Z]?[a-z]+[0-9]*|[A-Z]|[0-9]+/g)
    ?.map(x => x.toLowerCase())
    .join('-')
}

/**
 * Checks whether a string looks like a valid Metaplay SDK's entity ID.
 * @param entityId The string to check.
 * @returns A boolean result.
 */
export function isValidEntityId (entityId: string): boolean {
  const EntityIdValueLength = 10
  const EntityIdMaxValidValue = 'ZH0toCzB90' // (1 << 58) -1

  // There must be two parts separated by a colon.
  const parts = entityId.split(':')
  if (parts.length !== 2) return false

  const value = parts[1]

  // Value must be of specific lenth.
  if (value.length !== EntityIdValueLength) return false

  // Characters in value must all be in the allowed list.
  const EntityIdValidCharacters = '023456789ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz' // 1, I, and l omitted to avoid confusion
  for (let i = 0; i < value.length; i++) {
    if (!EntityIdValidCharacters.includes(value[i])) {
      return false
    }
  }

  // Must not exceed maximum value: '(1 << 58) - 1' as string.
  if (stringCompareOrdinal(value, EntityIdMaxValidValue) >= 1) {
    return false
  }

  return true
}

/**
 * Checks whether a string looks like a valid Metaplay SDK's entity ID of a specific type.
 * @param entityKind The expected entity type. For example: Player
 * @param entityId The string to check.
 * @returns A boolean result.
 */
function isValidEntityKindId (entityKind: string, entityId: string): boolean {
  // Must be a valid entity ID.
  if (!isValidEntityId(entityId)) return false

  // Kind must match provided one.
  if (entityId.split(':')[0] !== entityKind) return false

  return true
}

/**
 * Convenience utility to check whether a string looks like a valid Metaplay SDK's player ID.
 * @param id The string to check.
 */
export function isValidPlayerId (id: string) {
  return isValidEntityKindId('Player', id)
}

/**
 * Compares two strings and returns their relative order.
 * @param a String one.
 * @param b String two.
 * @returns An integer about the relative order (-1, 0 or 1).
 */
function stringCompareOrdinal (a: string, b: string): number {
  const minLen = Math.min(a.length, b.length)
  for (let ndx = 0; ndx < minLen; ndx++) {
    const ac = a.charCodeAt(ndx)
    const bc = b.charCodeAt(ndx)
    if (ac < bc) {
      return -1
    } else if (ac > bc) {
      return +1
    }
  }

  if (a.length < b.length) {
    return -1
  } else if (a.length > b.length) {
    return +1
  }
  return 0
}

/**
 * Rounds a number to the selected amount of decimals.
 * @param n The number to round.
 * @param digits Number of decimals. Defaults to 0.
 * @returns The rounded number.
 */
export function roundTo (n: number, digits: number = 0) {
  let negative = false

  if (n < 0) {
    negative = true
    n = n * -1
  }
  const multiplicator = Math.pow(10, digits)
  n = parseFloat((n * multiplicator).toFixed(11))
  n = Number((Math.round(n) / multiplicator).toFixed(digits))
  if (negative) {
    n = Number((n * -1).toFixed(digits))
  }
  return n
}

/**
 * Does a lazy and inaccurate estimation of an audience's size based on current player segments.
 * @param playerSegments All current player segments.
 * @param audience The audience selection to check.
 * @returns A guesstimate of what the audience size might be.
 */
export function estimateAudienceSize (playerSegments: any, audience: any) {
  let count = 0
  if (audience.targetCondition) {
    if (!playerSegments || audience.targetCondition.requireAllSegments) {
      return null
    }
    for (const segment of playerSegments.filter((s: any) => audience.targetCondition.requireAnySegment.includes(s.info.segmentId))) {
      if (segment.sizeEstimate == null) {
        return null
      }
      count += segment.sizeEstimate
    }
  }
  count += audience.targetPlayers?.length ?? 0
  return count
}

/**
 * Times how long a function or piece of code takes to execute.
 * Usage eg: timeFunction(() => { myfunc(args) }, "myFunc")
 * @param func Function to time.
 * @param functionName Name of the function.
 * @returns The execution time in milliseconds.
 */
export function timeFunction (func: Function, functionName: string, log: boolean = false) {
  const startTime = performance.now()
  const result = func()
  const endTime = performance.now()
  // eslint-disable-next-line
  if (log) console.log(`${functionName} took ${(endTime - startTime).toFixed()}ms to execute`)
  return result
}

/**
 * Generate a hash code for a string.
 * https://stackoverflow.com/a/34842797
 * @param str Input string.
 * @returns The generated hash code.
 */
export function hashCode (str: string) {
  return str.split('').reduce((a, b) => (((a << 5) - a) + b.charCodeAt(0)) | 0, 0)
}

/**
 * Helper function to extract multiple values from the page's query string. An array is always returned. If the key
 * does not exist then an empty array is returned.
 * @param query Query string as returned from the route.
 * @param key Name of the key to search for.
 * @returns Array of string values.
 */
export function extractMultipleValuesFromQueryString (query: LocationQuery, key: string): string[] {
  const rawValue = query[key]
  if (typeof rawValue === 'string') {
    return [rawValue]
  } else if (Array.isArray(rawValue)) {
    return rawValue as string[]
  } else {
    return []
  }
}

/**
 * Helper function to extract a value from the page's query string. If there are multiple instances of the key in the
 * query string then we just return the first one.
 * @param query Query string as returned from the route.
 * @param key Name of the key to search for.
 * @returns Query string value as a string or undefined if the key does not exist.
 */
export function extractSingleValueFromQueryStringOrUndefined (query: LocationQuery, key: string): string | undefined {
  return extractMultipleValuesFromQueryString(query, key)[0] || undefined
}

/**
 * Helper function to extract a value from the page's query string. If there are multiple instances of the key in the
 * query string then we just return the first one.
 * @param query Query string as returned from the route.
 * @param key Name of the key to search for.
 * @param defaultValue Default string value to return if the key does not exist.
 * @returns Query string value or defaultValue as a string if the key does not exist.
 */
export function extractSingleValueFromQueryStringOrDefault (query: LocationQuery, key: string, defaultValue: string): string {
  return extractSingleValueFromQueryStringOrUndefined(query, key) ?? defaultValue
}

/**
 * Helper for using async/await with `setTimeout()`.
 * @param ms Milliseconds to wait until resolving the promise.
 * @example await sleep(2_000)
 */
export async function sleep (ms: number): Promise<void> {
  return await new Promise(resolve => setTimeout(resolve, ms))
}

/**
 * Helper to convert a route parameter to a single value. If the parameter is an array then we just return the first value.
 * @param param The route param to convert.
 * @example routeParamToSingleValue(route.params.someParameter)
 */
export function routeParamToSingleValue (param: string | string[]): string {
  if (Array.isArray(param)) {
    console.warn('routeParamToSingleValue() was called with an array. Returning the first value.')
    return param[0]
  } else {
    return param
  }
}

/**
 * Helper function to parse ASP.NET style time spans.
 * @param duration ASP.Net duration.
 * @returns The duration as a Luxon Duration object.
 */
export function parseASPNetDurationToLuxon (duration: string): Duration {
  // Luxon cannot handle ASP.NET durations and there is no trivial way to parse them. Moment _does_ so, for now,
  // I'm just gonna use Moment to handle this.
  return Duration.fromMillis(moment.duration(duration).asMilliseconds())
}

/**
 * Helper function to convert a notification phase to a pretty string.
 * @param phase The notification phase.
 * @returns The display string for the phase.
 */
export function notificationPhaseDisplayString (phase: String): String {
  return phase === 'DidNotRun' ? 'Did Not Run' : phase
}

/**
 * Helper function to convert a MetaScheduleTimeMode to a pretty string.
 * @param timeMode The MetaScheduleTimeMode.
 * @returns The display string for the timeMode.
 */
export function metaScheduleTimeModeDisplayString (timeMode: string): string {
  const lookups: { [index: string]: string } = {
    Utc: 'UTC'
  }
  return lookups[timeMode] || timeMode
}

/**
 * Helper function to convert a schedule phase to a pretty string.
 * @param phase The schedule phase.
 * @returns The display string for the phase.
 */
export function schedulePhaseDisplayString (phase: string): string {
  const lookups: { [index: string]: string } = {
    EndingSoon: 'Ending Soon'
  }
  return lookups[phase] || phase
}

/**
 * Helper function to convert a guild role to a pretty string.
 * TODO: These should be configured somewhere else in the game server and fetched here.
 * @param phase The guild role.
 * @returns The display string for the guild role.
 */
export function guildRoleDisplayString (roleId: string): string {
  const lookups: { [index: string]: string } = {
    LowTier: 'Low Tier',
    MiddleTier: 'Middle Tier',
  }
  return lookups[roleId] || roleId
}

/**
 * Helper function to convert an ISO duration string to the equivalent duration in milliseconds.
 * @param isoDuration The ISO duration string to be converted.
 * @example durationToMilliseconds('PT1S')
 */
export function durationToMilliseconds (isoDuration: string) {
  return Duration.fromISO(isoDuration).toMillis()
}

/**
 * Test whether a given time is 0 (ie: UNIX epoch time)
 * @param time Time as an ISO string or a DateTime object.
 * @returns True if time is equal to epoch time.
 */
export function isEpochTime (time: string | DateTime) {
  let timeObject: DateTime
  if (time instanceof DateTime) timeObject = time
  else if (typeof time === 'string') timeObject = DateTime.fromISO(time)
  else throw new Error('Invalid time')
  if (!timeObject.isValid) throw new Error('Invalid time')
  return timeObject.toMillis() === 0
}

/**
 * Test whether a given value is null or undefined.
 * @param value The value to be checked.
 * @returns True if value is not null or undefined.
 */
export function isNullOrUndefined (value: any): boolean {
  if (value === null || value === undefined) {
    return false
  } else return true
}
