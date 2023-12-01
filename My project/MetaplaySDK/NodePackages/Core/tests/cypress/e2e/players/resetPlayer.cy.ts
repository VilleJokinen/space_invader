// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player Reset', [], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}`)
  })

  it('Opens the player reset dialog', function () {
    cy.get('[data-cy=action-reset-player-state-button]')
      .click({ force: true })
  })

  it('Resets the player', function () {
    cy.get('[data-cy=action-reset-player-state-ok]')
      .clickMetaButton()
  })
})
