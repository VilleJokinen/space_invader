// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player Export', [], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}`)
  })

  it('Opens the player export dialog', function () {
    cy.get('[data-cy=copy-player-button]')
      .click({ force: true })
  })

  it('Checks that the text field has some data', function () {
    cy.get('[data-cy=export-payload]')
      .contains(this.testPlayer.id)
  })

  it('Clicks the copy button to get the right payload into the clipboard', function () {
    cy.get('[data-cy=copy-player-to-clipboard]')
      .click()

    cy.window().then(win =>
      // eslint-disable-next-line
      new Cypress.Promise((resolve, reject) => win.navigator.clipboard.readText().then(resolve).catch(reject)).then(
        text => {
          expect(text).to.contain(this.testPlayer.id)
        }
      )
    )
  })

  it('Closes the modal', function () {
    cy.get('#action-player-copy')
      .type('{esc}')
  })
})
