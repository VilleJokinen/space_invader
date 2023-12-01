// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('GDPR Export', [], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}`)
  })

  it('Opens the GDPR export dialog', function () {
    cy.get('[data-cy=gdpr-export-button]')
      .click({ force: true })
  })

  it('Checks that the preview has some data', function () {
    cy.get('[data-cy=export-payload]')
      .contains('inAppPurchaseHistory')
  })

  it('Closes the modal', function () {
    cy.get('#action-gdpr-export')
      .type('{esc}')
  })
})
