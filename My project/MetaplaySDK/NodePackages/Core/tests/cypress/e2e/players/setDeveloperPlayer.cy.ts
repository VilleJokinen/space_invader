// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player Export', [], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}`)
  })

  it('Sets a player as developer and verifies they got added to the developers page list', function () {
    // Open modal.
    cy.get('[data-cy=action-set-developer-button]')
      .click({ force: true })
    cy.get('[data-cy=action-set-developer-modal')
      .should('exist')

    // Toggle developer.
    cy.get('[data-cy=developer-status-toggle]')
      .click()

    // Ok the modal.
    cy.get('[data-cy=action-set-developer-ok]')
      .clickMetaButton()
    cy.get('[data-cy=action-set-developer-modal')
      .should('not.exist')

    // Check that the player is developer.
    cy.get('[data-cy=player-is-developer-icon]')

    // Navigate to the developers page.
    cy.visit('/developers')

    // Check that the player is in the list.
    cy.get('[data-cy=developer-players-list]')
      .contains(this.testPlayer.id)

    // Navigate back to the player page.
    cy.visit(`/players/${this.testPlayer.id}`)

    // Open modal again.
    cy.get('[data-cy=action-set-developer-button]')
      .click({ force: true })
    cy.get('[data-cy=action-set-developer-modal')
      .should('exist')

    // Toggle developer again.
    cy.get('[data-cy=developer-status-toggle]')
      .click()

    // Ok the modal.
    cy.get('[data-cy=action-set-developer-ok]')
      .clickMetaButton()
    cy.get('[data-cy=action-set-developer-modal')
      .should('not.exist')

    // Check that the player is not developer.
    cy.get('[data-cy=player-is-developer-icon]')
      .should('not.exist')
  })
})
