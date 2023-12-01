// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Copy Player ID', [], function () {
  it('Copies a player ID to the clipboard', function () {
    cy.visit(`/players/${this.testPlayer.id}`)

    // Copy to clipboard
    cy.get('[data-cy=copy-to-clipboard]')
      .click({ force: true })

    // Check that the clipboard contains the player ID
    cy.window().then((win) => {
      win.navigator.clipboard.readText().then((text) => {
        expect(text).to.eq(this.testPlayer.id)
      }).catch((err) => {
        throw err
      })
    })
  })
})
