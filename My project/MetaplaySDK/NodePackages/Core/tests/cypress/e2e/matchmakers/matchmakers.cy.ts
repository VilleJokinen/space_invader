// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Matchmaker List', ['asyncMatchmaker'], function () {
  before(function () {
    cy.visit('/matchmakers')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Checks that elements render', function () {
    cy.get('[data-cy=matchmakers-overview-card]')
    cy.get('[data-cy=async-matchmakers-list-card]')
    cy.get('[data-cy=realtime-matchmakers-list-card]')
  })

  it('Navigates into a matchmaker', function () {
    cy.get('[data-cy=view-matchmaker]')
      .first()
      .click({ force: true })
  })

  it('Checks that detail page elements render', function () {
    cy.get('[data-cy=matchmaker-overview-card]')
    cy.get('[data-cy=matchmaker-bucket-chart]')
    cy.get('[data-cy=matchmaker-buckets-list-card]')
    cy.get('[data-cy=matchmaker-top-players-list-card]')
    cy.get('[data-cy=audit-log-card]')
  })

  it('Opens the simulation modal', function () {
    cy.get('[data-cy=simulate-matchmaking-button]')
      .click({ force: true })
  })

  it('Types in a new MMR', function () {
    cy.get('[data-cy=attackmmr-input]')
      .clear()
      .type('9001')
      .should('have.value', '9001')
  })

  it('Clicks simulate', function () {
    cy.get('[data-cy=simulate-matchmaking-ok-button]')
      .click({ force: true })
  })

  it('Closes the modal', function () {
    cy.get('[data-cy=simulate-matchmaking-close]')
      .click({ force: true })
  })

  it.skip('Opens the rebalancing modal', function () {
    cy.get('[data-cy=rebalance-matchmaker-button]')
      .click({ force: true })
  })

  it.skip('Triggers rebalancing', function () {
    cy.get('[data-cy=rebalance-matchmaker-ok]')
      .clickMetaButton()
  })

  it('Opens the reset modal', function () {
    cy.get('[data-cy=reset-matchmaker-button]')
      .click({ force: true })
  })

  it('Triggers reset', function () {
    cy.get('[data-cy=reset-matchmaker-ok]')
      .clickMetaButton()
  })
})
