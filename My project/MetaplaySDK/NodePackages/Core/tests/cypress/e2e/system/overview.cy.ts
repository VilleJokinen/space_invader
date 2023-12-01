// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Overview', [], function () {
  before(function () {
    cy.visit('/')
  })

  it('Clicks on the game icon and checks it finds overview in title', function () {
    cy.get('[data-cy=overview-link]')
      .click({ force: true })
    cy.get('[data-cy=header-bar-title]')
      .should('have.text', 'Overview')
  })

  it('Checks that cards render', function () {
    cy.get('[data-cy=overview-card]')
    cy.get('[data-cy=concurrents-card]')
    cy.get('[data-cy=player-actors-card]')
    cy.get('[data-cy=global-incident-history-card]')
    cy.get('[data-cy=global-incident-statistics-card]')
  })
})
