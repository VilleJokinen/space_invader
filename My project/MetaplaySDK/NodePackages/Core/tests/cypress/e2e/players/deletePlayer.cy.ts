// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player Deletion', [], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}`)
  })

  it('Opens the player deletion dialog', function () {
    cy.get('[data-cy=delete-player-button]')
      .click({ force: true })
    cy.get('[data-cy=delete-player-modal')
      .should('exist')
  })

  it('Toggles the player delete setting on', function () {
    cy.get('[data-cy=delete-player-modal]')
      .find('input[type="checkbox"]')
      .parent()
      .click()
  })

  it('Saves the delete settings', function () {
    cy.get('[data-cy=confirm-delete-player]')
      .clickMetaButton()
    cy.get('[data-cy=delete-player-modal')
      .should('not.exist')
  })

  it('Checks that an alert is visible', function () {
    cy.get('[data-cy=player-deletion-alert]')
  })

  it('Opens the player delete dialog again', function () {
    cy.get('[data-cy=delete-player-button]')
      .click({ force: true })
    cy.get('[data-cy=delete-player-modal')
      .should('exist')
  })

  it('Toggles the player delete settings off', function () {
    cy.get('[data-cy=delete-player-modal]')
      .find('input[type="checkbox"]')
      .parent()
      .click()
  })

  it('Saves the delete settings again', function () {
    cy.get('[data-cy=confirm-delete-player]')
      .clickMetaButton()
    cy.get('[data-cy=delete-player-modal')
      .should('not.exist')
  })

  it('Checks that the alert is no longer visible', function () {
    cy.get('[data-cy=player-deletion-alert]')
      .should('not.exist')
  })
})
