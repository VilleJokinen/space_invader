// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player Mail', [], function () {
  it('Sends a generic player email', function () {
    cy.visit(`/players/${this.testPlayer.id}`)

    // Finds the inbox card
    cy.get('[data-cy=player-inbox-card]')

    // Opens the send mail dialog
    cy.get('[data-cy=action-mail-button]')
      .click({ force: true })
    cy.get('[data-cy=action-mail-modal')
      .should('exist')

    // Fills in the mail fields
    cy.get('[data-cy=title-localizations-en-input]')
      .type(`Test Mail ${this.testToken}`)
    cy.get('[data-cy=body-localizations-en-input]')
      .type(`Lorem ipsum dolor sit ${this.testToken}.`)
    // TODO: Figure out how to add attachments
    // cy.contains('.multiselect__placeholder', 'Select')
    //  .click({ force: true })
    cy.get('[data-cy=action-mail-ok]')
      .clickMetaButton()
    cy.get('[data-cy=action-mail-modal')
      .should('not.exist')

    // Checks that mail is in the inbox
    cy.get('[data-cy=player-inbox-card]')
      .contains(`Test Mail ${this.testToken}`)

    // Opens the delete mail dialog
    cy.get('[data-cy=player-inbox-card]')
      .contains(`Test Mail ${this.testToken}`)
    cy.get('[data-cy=mail-item]')
      .find('button')
      .click({ force: true })
    cy.get('[data-cy=confirm-mail-delete')
      .should('exist')

    // Deletes the mail from the inbox
    cy.get('[data-cy=confirm-delete-button]')
      .clickMetaButton()
    cy.get('[data-cy=confirm-mail-delete')
      .should('not.exist')

    // Checks that the mail is no longer in the inbox
    cy.get('[data-cy=player-inbox-card]')
      .contains(`Test Mail ${this.testToken}`).should('not.exist')
  })
})
