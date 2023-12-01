// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player Banning', [], function () {
  it('Bans and un-bans a player', function () {
    cy.visit(`/players/${this.testPlayer.id}`)

    // Open modal
    cy.get('[data-cy=action-ban-player-button]')
      .click({ force: true })
    cy.get('[data-cy=action-ban-player-modal')
      .should('exist')

    // Toggle ban
    cy.get('[data-cy=player-ban-toggle]')
      .parent()
      .click()

    // Ok the modal
    cy.get('[data-cy=action-ban-player-ok]')
      .clickMetaButton()
    cy.get('[data-cy=action-ban-player-modal')
      .should('not.exist')

    // Check that the player is banned
    cy.get('[data-cy=player-banned-alert]')
      .should('exist')

    // Open modal again
    cy.get('[data-cy=action-ban-player-button]')
      .click({ force: true })
    cy.get('[data-cy=action-ban-player-modal')
      .should('exist')

    // Toggle ban
    cy.get('[data-cy=player-ban-toggle]')
      .parent()
      .click()

    // Ok the modal
    cy.get('[data-cy=action-ban-player-ok]')
      .clickMetaButton()
    cy.get('[data-cy=action-ban-player-modal')
      .should('not.exist')

    // Check that the player is not banned
    cy.get('[data-cy=player-banned-alert]')
      .should('not.exist')
  })
})
