// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('In-Game Events', ['events'], function () {
  before(function () {
    cy.visit('/activables/Event')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Checks that list page elements render', function () {
    cy.get('[data-cy=all]')
    cy.get('[data-cy=custom-time]')
  })

  it.skip('TODO: Checks that custom time tool works', function () {
    // TODO
  })

  it('Navigates into an event', function () {
    cy.get('[data-cy=view-activable]')
      .first()
      .click({ force: true })
  })

  it('Checks that detail page elements render', function () {
    cy.get('[data-cy=overview]')
    cy.get('[data-cy=game-configuration]')
    cy.get('[data-cy=activable-configuration]')
    cy.get('[data-cy=segments]')
    cy.get('[data-cy=conditions]')
  })
})
