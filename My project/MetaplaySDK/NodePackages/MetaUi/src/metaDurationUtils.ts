import moment from 'moment'
import { maybePlural } from './utils'

/**
 * Constructs a compact string representation of the exact time duration from an ISO8601 duration string.
 *
 * The time units (years, months, days, hours, minutes, and seconds) are represented by their first letter (e.g., '1y 1m' for 1 year 1 month).
 * When only one time unit is present in the ISO string, the returned string will be in full format (e.g., '1 year' instead of '1y').
 *
 * @param isoString An ISO8601 duration string to be converted.
 * @param hideMilliseconds Whether to exclude milliseconds from the output string.
 * @returns A compact string representing the time duration.
 */
export function constructExactTimeCompactString (isoString: string, hideMilliseconds: boolean): string {
  const duration = moment.duration(isoString)
  if (duration.asSeconds() === 0) return '0 seconds'

  let timeUnits: string[] = []
  timeUnits = appendTimeUnit(timeUnits, duration.years(), 'y', false)
  timeUnits = appendTimeUnit(timeUnits, duration.months(), 'm', false)
  timeUnits = appendTimeUnit(timeUnits, duration.days(), 'd', false)
  timeUnits = appendTimeUnit(timeUnits, duration.hours(), 'h', false)
  timeUnits = appendTimeUnit(timeUnits, duration.minutes(), 'min', false)
  timeUnits = appendTimeUnit(timeUnits, duration.seconds(), 's', false)
  if (!hideMilliseconds) {
    timeUnits = appendTimeUnit(timeUnits, duration.milliseconds(), 'ms', false)
  }

  if (timeUnits.length === 1) {
    // If only one time unit is present, return the full format of this unit.
    return constructSingleTimeUnitString(timeUnits[0])
  } else {
    // If multiple time units are present, join them with spaces.
    return timeUnits.join(' ')
  }
}

/**
 * Constructs a verbose string representation of the exact time duration from an ISO8601 duration string.
 *
 * The time units (years, months, days, hours, minutes, and seconds) are fully spelled out and correctly pluralized (e.g., '1 year and 1 month' or '2 years and 3 months').
 * The returned string will be in a verbose format, using 'and' between the last two units when multiple units are present, and appending '0 {smallerUnit}' when only one unit is present (e.g., '1 minute' becomes '1 minute 0 seconds').
 *
 * @param isoString An ISO8601 duration string to be converted.
 * @param hideMilliseconds Whether to exclude milliseconds from the output string.
 * @returns A verbose string representing the time duration.
 */
export function constructExactTimeVerboseString (isoString: string, hideMilliseconds: boolean): string {
  const duration = moment.duration(isoString)
  if (duration.asSeconds() === 0) return '0 seconds'

  let timeUnits: string[] = []
  timeUnits = appendTimeUnit(timeUnits, duration.years(), 'year', true)
  timeUnits = appendTimeUnit(timeUnits, duration.months(), 'month', true)
  timeUnits = appendTimeUnit(timeUnits, duration.days(), 'day', true)
  timeUnits = appendTimeUnit(timeUnits, duration.hours(), 'hour', true)
  timeUnits = appendTimeUnit(timeUnits, duration.minutes(), 'minute', true)
  timeUnits = appendTimeUnit(timeUnits, duration.seconds(), 'second', true)
  if (!hideMilliseconds) {
    timeUnits = appendTimeUnit(timeUnits, duration.milliseconds(), 'millisecond', true)
  }

  if (timeUnits.length === 1) {
    const timeUnitForTooltip = timeUnits[0]
    const smallerUnits = getSmallerUnit(timeUnitForTooltip)
    // If only one time unit is present, append '0 {smallerUnit}' to it for verbose representation.
    return `${timeUnitForTooltip} 0 ${smallerUnits}`
  } else if (timeUnits.length > 1) {
    // Combine time units if there are multiple, with 'and' before the last one.
    return timeUnits.slice(0, -1).join(' ') + ' and ' + timeUnits.slice(-1)
  } else {
    return timeUnits.join('')
  }
}

/**
 * Used in constructExactTimeCompactString and constructExactTimeVerboseString to create the final string by appending time units to an array.
 *
 * @param timeUnits An array of time units (e.g., '1h', '30min').
 * @param value The value of the new time unit.
 * @param unit The new time unit to be appended (e.g., 'h', 'min').
 * @param pluralize Pluralizing the unit if the value is zero or more than one when set to true.
 * @returns The updated array of time units.
 */
function appendTimeUnit (timeUnits: string[], value: number, unit: string, pluralize: boolean): string[] {
  if (value !== 0) {
    const valueUnit = pluralize ? maybePlural(value, unit) : `${value}${unit}`
    return [...timeUnits, valueUnit]
  } else {
    return timeUnits
  }
}

/**
 * Converts a single time unit string from its short form to its long form, including the number of units.
 *
 * @param timeUnit A string representing a time unit in a short form (e.g., '1h', '30min').
 * @returns The time unit string in its long form (e.g., '1 hour', '30 minutes'). If the numeric part is greater than 1, the unit is pluralized.
 */
function constructSingleTimeUnitString (timeUnit: string) {
  const unitsOfTime: { [key: string]: string } = {
    y: 'year',
    m: 'month',
    d: 'day',
    h: 'hour',
    min: 'minute',
    s: 'second',
    ms: 'millisecond',
  }

  const numericMatch = timeUnit.match(/\d+/)
  if (!numericMatch) throw new Error(`Invalid time unit string: ${timeUnit}`)
  const unitMatch = timeUnit.match(/[a-zA-Z]+/)
  if (!unitMatch) throw new Error(`Invalid time unit string: ${timeUnit}`)
  const numericPart = numericMatch[0]
  const unitPart = unitMatch[0]
  const prefix = `${numericPart} ${unitsOfTime[unitPart]}`
  const suffix = numericPart > '1' ? 's' : ''
  return `${prefix}${suffix}`
}

/**
 * Determines the next smaller time unit for a given unit string.
 *
 * @param unitString A string representing a time unit in its long form (e.g., '1 year', '30 minutes').
 * @returns The next smaller unit for the given time unit in a pluralized form (e.g., 'months' for 'year', 'seconds' for 'minute').
 */
function getSmallerUnit (unitString: string) {
  const unit = unitString.replace(/\d+ /, '') // Remove the leading numeric part

  const smallerUnit: { [key: string]: string } = {
    year: 'months',
    years: 'months',
    month: 'days',
    months: 'days',
    day: 'hours',
    days: 'hours',
    hour: 'minutes',
    hours: 'minutes',
    minute: 'seconds',
    minutes: 'seconds',
    second: 'milliseconds',
    seconds: 'milliseconds',
  }
  return smallerUnit[unit]
}
