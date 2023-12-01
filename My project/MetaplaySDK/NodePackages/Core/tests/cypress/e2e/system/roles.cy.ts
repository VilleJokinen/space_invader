// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Roles', [], function () {
  before(function () {
    cy.visit('/user')
  })

  it('Checks that elements render', function () {
    cy.get('[data-cy=user-overview]')
    cy.get('[data-cy=user-permissions-card]')
    cy.get('[data-cy=assume-role-card]')
  })

  it('Selects a checkbox', function () {
    cy.get('[data-cy=roles-to-assume]')
      .find('input[type="checkbox"]')
      .siblings()
      .contains('span', 'game-viewer')
      .parent()
      .click({ force: true })
  })

  it('No longer navigates to runtime options page', function () {
    cy.get('[data-cy=sidebar]')
      .contains('li', 'Runtime Options')
      .should('have.attr', 'disabled')
  })

  it('Unselects a checkbox', function () {
    cy.get('[data-cy=roles-to-assume]')
      .find('input[type="checkbox"]')
      .siblings()
      .contains('span', 'game-viewer')
      .parent()
      .click({ force: true })
  })

  it('Navigates to runtime options page again', function () {
    cy.get('[data-cy=sidebar]')
      .contains('li', 'Runtime Options')
      .should('not.have.attr', 'disabled')
  })
})
