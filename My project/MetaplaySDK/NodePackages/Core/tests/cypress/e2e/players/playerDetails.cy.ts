// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player Details Rendering', [], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}`)
  })

  it('Finds the overview card', function () {
    cy.get('[data-cy=player-overview-card]')
      .contains(this.testPlayer.id)
  })

  it('Finds the admin actions card', function () {
    cy.get('[data-cy=player-admin-actions-card]')
  })

  // Tab 0

  it('Finds the inbox card', function () {
    cy.get('[data-cy=player-inbox-card]')
  })

  it('Finds the broadcasts card', function () {
    cy.get('[data-cy=player-broadcasts-card]')
  })

  // Tab 1

  it('Navigates to tab 1', function () {
    cy.get('[data-cy=player-details-tab-1]')
      .click({ force: true })
  })

  it('Checks that the page query has updated', function () {
    cy.url()
      .should('include', 'tab=1')
  })

  it('Finds the player event log card', function () {
    cy.get('[data-cy=entity-event-log-card]')
  })

  it('Finds the audit log card', function () {
    cy.get('[data-cy=audit-log-card]')
  })

  it('Finds the devices & auths list card', function () {
    cy.get('[data-cy=player-login-methods-card]')
  })

  it('Finds the login history card', function () {
    cy.get('[data-cy=player-login-history-card]')
  })

  // Tab 2

  it('Navigates to tab 2', function () {
    cy.get('[data-cy=player-details-tab-2]')
      .click({ force: true })
  })

  it('Finds the purchase history card', function () {
    cy.get('[data-cy=player-purchase-history-card]')
  })

  it('Finds the subscriptions history card', function () {
    cy.get('[data-cy=player-subscriptions-history-card]')
  })

  // Tab 3

  it('Navigates to tab 3', function () {
    cy.get('[data-cy=player-details-tab-3]')
      .click({ force: true })
  })

  it('Finds the segments card', function () {
    cy.get('[data-cy=segments-card]')
  })

  it('Finds the in-game events card', function () {
    cy.contains('Events').get('[data-cy=activables-card]')
  })

  it('Finds the offers card', function () {
    cy.contains('Offers').get('[data-cy=activables-card]')
  })

  it('Finds the experiments card', function () {
    cy.get('[data-cy=player-experiments-card]')
  })

  // Tab 4

  it('Navigates to tab 4', function () {
    cy.get('[data-cy=player-details-tab-4]')
      .click({ force: true })
  })

  it('Finds the incident reports card', function () {
    cy.get('[data-cy=player-incident-history-card]')
  })
})
