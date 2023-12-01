// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Leagues', ['playerLeagues'], function () {
  before(function () {
    cy.visit('/leagues')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Checks that elements render', function () {
    cy.get('[data-cy=league-list-overview-card]')
    cy.get('[data-cy=league-list-card]')
  })

  it('Navigates into a league', function () {
    cy.get('[data-cy=view-league]')
      .first()
      .click({ force: true })
  })

  it('Checks that league detail page elements render', function () {
    cy.get('[data-cy=league-detail-overview-card]')
    cy.get('[data-cy=league-seasons-list-card]')
    cy.get('[data-cy=league-schedule-card]')
    cy.get('[data-cy=audit-log-card]')
  })

  it('Navigates into latest season', function () {
    cy.get('[data-cy=latest-season-button-link]')
      .click({ force: true })
  })

  it('Checks that league season detail page elements render', function () {
    cy.get('[data-cy=league-season-detail-overview-card]')
    cy.get('[data-cy=league-season-ranks-card]')
  })
})
