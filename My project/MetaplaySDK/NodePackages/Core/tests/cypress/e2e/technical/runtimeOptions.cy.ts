// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Runtime Options', [], function () {
  before(function () {
    cy.visit('/runtimeOptions')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Finds one of the expected sets of options', function () {
    cy.get('[data-cy=all-options]')
      .contains('Admin Api')
  })
})
