// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { describe, it } from 'vitest'

import { DateTime } from 'luxon'

import {
  isEpochTime,
  isValidEntityId,
  isValidPlayerId,
  hashCode,
  extractMultipleValuesFromQueryString,
  extractSingleValueFromQueryStringOrUndefined,
  extractSingleValueFromQueryStringOrDefault,
  durationToMilliseconds,
  isNullOrUndefined,
} from '../../src/coreUtils'

describe('isValidEntityId', () => {
  it('should allow valid id', async ({ expect }) => {
    expect(isValidEntityId('EntityX:0000000000')).to.equal(true)
  })
  it('should not allow too long id', async ({ expect }) => {
    expect(isValidEntityId('EntityX:0000000000x')).to.equal(false)
  })
  it('should not allow too short id', async ({ expect }) => {
    expect(isValidEntityId('EntityX:000000000')).to.equal(false)
  })
  it('should not allow invalid characters', async ({ expect }) => {
    expect(isValidEntityId('EntityX:000000000I')).to.equal(false)
    expect(isValidEntityId('EntityX:0000000001')).to.equal(false)
    expect(isValidEntityId('EntityX:000000000l')).to.equal(false)
  })
  it('should not allow invalid id value', async ({ expect }) => {
    expect(isValidEntityId('EntityX:ZZZZZZZZZZ')).to.equal(false)
  })
})

describe('isValidPlayerId', () => {
  it('should allow valid player id', async ({ expect }) => {
    expect(isValidPlayerId('Player:0000000000')).to.equal(true)
  })
  it('should not allow valid guild id', async ({ expect }) => {
    expect(isValidPlayerId('Guild:0000000000')).to.equal(false)
  })
})

describe('hashCode', () => {
  it('returns the same values for the same inputs', async ({ expect }) => {
    const pairs = [
      ['one', 'one'],
      ['1', '1'],
      ['input1', 'input1'],
    ]
    expect(pairs.every(([x, y]) => hashCode(x) === hashCode(y))).to.equal(true)
  })
  it('returns different values for different inputs', async ({ expect }) => {
    const pairs = [
      ['one', 'two'],
      ['1', '2'],
      ['input1', 'input2'],
    ]
    expect(pairs.every(([x, y]) => hashCode(x) !== hashCode(y))).to.equal(true)
  })
})

describe('extractMultipleValuesFromQueryString', () => {
  it('returns empty array when no values present', async ({ expect }) => {
    expect(extractMultipleValuesFromQueryString({}, 'key')).to.eql([])
  })
  it('returns empty array when no values does not exist', async ({ expect }) => {
    expect(extractMultipleValuesFromQueryString({ notKey: 'x' }, 'key')).to.eql([])
  })
  it('returns single value array value exists once', async ({ expect }) => {
    expect(extractMultipleValuesFromQueryString({ key: 'once' }, 'key')).to.eql(['once'])
  })
  it('returns multiple value array value exists more than once', async ({ expect }) => {
    expect(extractMultipleValuesFromQueryString({ key: ['once', 'twice'] }, 'key')).to.eql(['once', 'twice'])
  })
})

describe('extractSingleValueFromQueryStringOrUndefined', () => {
  it('returns undefined when value not present', async ({ expect }) => {
    expect(extractSingleValueFromQueryStringOrUndefined({}, 'key')).to.equal(undefined)
  })
  it('returns value when when value present once', async ({ expect }) => {
    expect(extractSingleValueFromQueryStringOrUndefined({ key: 'once' }, 'key')).to.equal('once')
  })
  it('returns first value when when value present more than once', async ({ expect }) => {
    expect(extractSingleValueFromQueryStringOrUndefined({ key: ['once', 'twice'] }, 'key')).to.equal('once')
  })
})

describe('extractSingleValueFromQueryStringOrDefault', () => {
  it('returns default when value not present', async ({ expect }) => {
    expect(extractSingleValueFromQueryStringOrDefault({}, 'key', 'default')).to.equal('default')
  })
  it('returns value when when value present once', async ({ expect }) => {
    expect(extractSingleValueFromQueryStringOrDefault({ key: 'once' }, 'key', 'default')).to.equal('once')
  })
  it('returns first value when when value present more than once', async ({ expect }) => {
    expect(extractSingleValueFromQueryStringOrDefault({ key: ['once', 'twice'] }, 'key', 'default')).to.equal('once')
  })
})

describe('durationToMilliseconds', () => {
  it('should return the correct number of milliseconds for a given ISO duration', ({ expect }) => {
    const isoDuration = 'PT1M' // 1 Minute
    const expected = 60000 // 1 Minute = 60000 milliseconds
    expect(durationToMilliseconds(isoDuration)).to.equal(expected)
  })
})

describe('isEpochTime', () => {
  it('works with ISO time strings', async ({ expect }) => {
    expect(isEpochTime('1970-01-01T00:00:00Z')).to.deep.equal(true)
    expect(isEpochTime('2023-01-01T00:00:00Z')).to.deep.equal(false)
  })
  it('works with DateTime objects', async ({ expect }) => {
    expect(isEpochTime(DateTime.fromISO('1970-01-01T00:00:00Z'))).to.deep.equal(true)
    expect(isEpochTime(DateTime.fromISO('2023-01-01T00:00:00Z'))).to.deep.equal(false)
  })
  it('throws on bad data', async ({ expect }) => {
    expect(() => isEpochTime('bad time')).to.throw()
    expect(() => isEpochTime({} as any)).to.throw()
    expect(() => isEpochTime(undefined as any)).to.throw()
    expect(() => isEpochTime(12345 as any)).to.throw()
  })
})

describe('isNullOrUndefined', () => {
  it('should return false for null', ({ expect }) => {
    const result = isNullOrUndefined(null)
    expect(isNullOrUndefined(null)).to.equal(false)
  })

  it('should return false for undefined', ({ expect }) => {
    const result = isNullOrUndefined(undefined)
    expect(result).to.equal(false)
  })

  it('should return true for a non-null/undefined value', ({ expect }) => {
    const result = isNullOrUndefined('Hello')
    expect(result).to.equal(true)
  })

  it('should return true for 0', ({ expect }) => {
    const result = isNullOrUndefined(0)
    expect(result).to.equal(true)
  })
})
