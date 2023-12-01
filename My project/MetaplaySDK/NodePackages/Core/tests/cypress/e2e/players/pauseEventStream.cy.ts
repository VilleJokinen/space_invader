// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Pause event stream', [], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}?tab=1`)
  })

  it('Event arrives when stream not paused', function () {
    // Get initial count of events in the card.
    cy.get('[data-cy=entity-event-log-card]')
      .find('[data-cy=badge-text]')
      .invoke('text')
      .then(text => {
        const initialEventCount = parseInt(text)

        // Create an event - change the player's name.
        cy.get('[data-cy=action-edit-name-button]')
          .click({ force: true })
        cy.get('[data-cy=name-input]')
          .type('changeWhileNotPaused')
        cy.get('[data-cy=action-edit-name-ok]')
          .clickMetaButton()
        cy.get('[data-cy=action-edit-name-modal')
          .should('not.exist')

        // Check that the number of events has increased by one.
        cy.get('[data-cy=entity-event-log-card]')
          .get('[data-cy=badge-text]')
          .contains(initialEventCount + 1)

        // Navigation fix. Click on the card to bring it into view. Should be using scrollIntoView() here but it does not.
        cy.get('[data-cy=entity-event-log-card]')
          .should('exist')
        cy.get('[data-cy=entity-event-log-card]')
          .scrollIntoView()

        // Check that the event is listed.
        cy.get('[data-cy=entity-event-log-card]')
          .should('contain', 'to changeWhileNotPaused by')
      })
  })

  it('Pause event stream', function () {
    cy.get('[data-cy=entity-event-log-card]')
      .find('[data-cy=play-pause-button]')
      .contains('Pause updates')
      .click({ force: true })
      .contains('Resume updates')
  })

  it('Event does not arrive when stream is paused', function () {
    // Get initial count of events in the card.
    cy.get('[data-cy=entity-event-log-card]')
      .find('[data-cy=badge-text]')
      .invoke('text')
      .then(text => {
        const initialEventCount = parseInt(text)

        // Create an event - change the player's name.
        cy.get('[data-cy=action-edit-name-button]')
          .click({ force: true })
        cy.get('[data-cy=name-input]')
          .type('changeWhilePaused')
        cy.get('[data-cy=action-edit-name-ok]')
          .clickMetaButton()
        cy.get('[data-cy=action-edit-name-modal')
          .should('not.exist')

        // Check that the number of events has increased by one.
        cy.get('[data-cy=entity-event-log-card]')
          .get('[data-cy=badge-text]')
          .contains(initialEventCount + 1)

        // Navigation fix. Click on the card to bring it into view. Should be using scrollIntoView() here but it does not.
        cy.get('[data-cy=entity-event-log-card]')
          .should('exist')
        cy.get('[data-cy=entity-event-log-card]')
          .scrollIntoView()

        // Check that only the first event is listed.
        cy.get('[data-cy=entity-event-log-card]')
          .should('not.contain', 'to changeWhilePaused by')
      })
  })

  it('Unpause event stream', function () {
    cy.get('[data-cy=entity-event-log-card]')
      .find('[data-cy=play-pause-button]')
      .contains('Resume updates')
      .click({ force: true })
      .contains('Pause updates')
  })

  it('Event appears after stream is un-paused', function () {
    // Check that both events are listed.
    cy.get('[data-cy=entity-event-log-card]')
      .should('contain', 'to changeWhilePaused by')
  })
})
