// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { describe, it } from 'vitest'

import {
  toSentenceCase
} from '../../src/utils'

describe('toSentenceCase', () => {
  it('Transforms single word correctly', async ({ expect }) => {
    const input = 'word'
    const expected = 'Word'
    expect(toSentenceCase(input)).to.equal(expected)
  })

  it('Transforms multiple words correctly', async ({ expect }) => {
    const input = 'someWords'
    const expected = 'Some Words'
    expect(toSentenceCase(input)).to.equal(expected)
  })

  it('Transforms correctly if first letter is uppercase', async ({ expect }) => {
    const input = 'SomeWords'
    const expected = 'Some Words'
    expect(toSentenceCase(input)).to.equal(expected)
  })
})
