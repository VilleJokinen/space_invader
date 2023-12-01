// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Client Compatibility', [], function () {
  before(function () {
    cy.visit('/system')
  })

  it('Opens the client compatibility dialog', function () {
    cy.get('[data-cy=client-settings-button]')
      .click({ force: true })
  })

  it('Toggles the settings', function () {
    cy.get('[data-cy=logic-redirect-modal]')
      .contains('Client Redirect')
      .siblings()
      .find('input[type="checkbox"]')
      .parent()
      .click()

    cy.get('[data-cy=logic-redirect-modal]')
      .contains('Host')
      .parent()
      .find('input[type="text"]')
      .clear()
      .type(`host${this.testToken}`)

    cy.get('[data-cy=logic-redirect-modal]')
      .contains('CDN URL')
      .parent()
      .find('input[type="text"]')
      .clear()
      .type(`cdn${this.testToken}`)
  })

  it('Saves the settings', function () {
    cy.get('[data-cy=save-client-settings]')
      .clickMetaButton()
  })

  it('Checks that the redirect is on', function () {
    cy.get('[data-cy=system-redirect-card]')
      .contains('New Version Redirect')
      .siblings()
      .contains('span', 'On')

    cy.get('[data-cy=system-redirect-card]')
      .contains('Host')
      .siblings()
      .contains(`host${this.testToken}`)

    cy.get('[data-cy=system-redirect-card]')
      .contains('CDN URL')
      .siblings()
      .contains(`cdn${this.testToken}`)
  })

  it('Opens the client compatibility dialog again', function () {
    cy.get('[data-cy=client-settings-button]')
      .click({ force: true })
  })

  it('Toggles the settings again', function () {
    cy.get('[data-cy=logic-redirect-modal]')
      .contains('Client Redirect')
      .siblings()
      .find('input[type="checkbox"]')
      .parent()
      .click()
  })

  it('Saves the settings again', function () {
    cy.get('[data-cy=save-client-settings]')
      .clickMetaButton()
  })
})
