// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Offers Groups', ['offerGroups'], function () {
  before(function () {
    cy.visit('/offerGroups')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Checks that list page elements render', function () {
    cy.get('[data-cy=custom-time]')
  })

  it.skip('TODO: Checks that custom time tool works', function () {
    // TODO
  })

  it('Navigates into an offer group', function () {
    cy.get('[data-cy=view-activable]')
      .first()
      .click({ force: true })
  })

  it('Checks that detail page elements render', function () {
    cy.get('[data-cy=overview]')
    cy.get('[data-cy=individual-offers]')
    cy.get('[data-cy=activable-configuration]')
    cy.get('[data-cy=segments]')
    cy.get('[data-cy=conditions]')
  })

  it('Navigates into an offer', function () {
    cy.get('[data-cy=view-offer]')
      .first()
      .click({ force: true })
  })

  it('Checks that detail page elements render', function () {
    cy.get('[data-cy=overview]')
    cy.get('[data-cy=offers]')
    cy.get('[data-cy=references]')
    cy.get('[data-cy=segments]')
    cy.get('[data-cy=conditions]')
  })
})
