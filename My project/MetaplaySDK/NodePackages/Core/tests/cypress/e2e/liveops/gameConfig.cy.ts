// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Game Config', [], function () {
  before(function () {
    cy.visit('/gameConfigs')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it.skip('Checks that list elements render', function () {
    cy.get('[data-cy=available-configs]')
  })

  it.skip('Contains at least one viewable config item that can be clicked on', function () {
    cy.get('[data-cy=view-config]')
      .first()
      .click({ force: true })
  })

  it.skip('Checks that detail page elements render', function () {
    cy.get('[data-cy=game-config-overview]')
    cy.get('[data-cy=audit-log-card]')
  })

  it.skip('Clicks on the first library item in the contents card', function () {
    cy.get('[data-cy=config-contents-card]')
      .find('[data-cy=library-title-row]')
      .first()
      .click()
  })
})
