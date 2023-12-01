// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { describe, it } from 'vitest'

describe('trivial example', () => {
  it('passes', async ({ expect }) => {
    expect(123).to.not.equal(456)
  })
})
