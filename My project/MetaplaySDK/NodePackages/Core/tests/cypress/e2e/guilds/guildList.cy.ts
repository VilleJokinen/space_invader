// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Guild List', ['guilds'], function () {
  before(function () {
    cy.visit('/guilds')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Types something into the search box', function () {
    cy.get('.multiselect-search')
      .type('Guest')
      .should('have.value', 'Guest')
  })
})
