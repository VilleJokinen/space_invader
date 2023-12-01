// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Game Config', [], function () {
  before(function () {
    cy.visit('/broadcasts')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Checks that list page elements renders', function () {
    cy.get('[data-cy=all-broadcasts]')
    cy.get('[data-cy=new-broadcast]')
  })
})
