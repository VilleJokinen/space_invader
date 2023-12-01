// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player Matchmaker', ['asyncMatchmaker'], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}`)
  })

  it('Opens matchmaker panel', function () {
    cy.get('[data-cy=matchmaker-list-entry]')
      .first()
      .click({ force: true })
  })

  it.skip('Opens a matchmaker simulation modal', function () {
    cy.get('[data-cy=simulate-matchmaker-button]')
      .click({ force: true })
  })

  it.skip('Checks that there is no error message', function () {
    cy.get('[data-cy=meta-api-error]')
      .should('not.exist')
  })

  it.skip('Closes the simulation modal', function () {
    cy.get('[data-cy=simulate-matchmaking-close-button]')
      .clickMetaButton()
  })

  it('Join matchmaker if not already', function () {
    cy.get('body').then((body) => {
      if (body.find('[data-cy=enter-matchmaker-button]').length !== 0) {
        cy.get('[data-cy=enter-matchmaker-button]').click({ force: true })
        cy.get('[data-cy=enter-matchmaker-confirm-button]').clickMetaButton()
      }
    })
  })

  it('Opens a matchmaker exit modal', function () {
    cy.get('[data-cy=exit-matchmaker-button]')
      .click({ force: true })
    cy.get('[data-cy=exit-matchmaker-modal')
      .should('exist')
  })

  it('Exits a matchmaker', function () {
    cy.get('[data-cy=exit-matchmaker-confirm-button]')
      .clickMetaButton()
    cy.get('[data-cy=exit-matchmaker-modal')
      .should('not.exist')
  })

  it('Opens a matchmaker join modal', function () {
    cy.get('[data-cy=enter-matchmaker-button]')
      .click({ force: true })
    cy.get('[data-cy=enter-matchmaker-modal')
      .should('exist')
  })

  it('Enters a matchmaker', function () {
    cy.get('[data-cy=enter-matchmaker-confirm-button]')
      .clickMetaButton()
    cy.get('[data-cy=enter-matchmaker-modal')
      .should('not.exist')
  })
})
