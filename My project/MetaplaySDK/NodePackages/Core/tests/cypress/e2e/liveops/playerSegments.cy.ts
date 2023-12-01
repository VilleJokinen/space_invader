// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player Segments', ['segments'], function () {
  before(function () {
    cy.visit('/segments')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Checks that list page elements render', function () {
    cy.get('[data-cy=all-segments]')
  })

  it('Navigates into a segment', function () {
    cy.get('[data-cy=view-segment]')
      .first()
      .click({ force: true })
  })

  it('Checks that detail page elements render', function () {
    cy.get('[data-cy=segment-overview]')
    cy.get('[data-cy=segment-conditions]')
    cy.get('[data-cy=segment-references]')
  })
})
