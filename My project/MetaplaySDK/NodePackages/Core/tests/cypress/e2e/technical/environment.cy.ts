// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Environment', [], function () {
  before(function () {
    cy.visit('/environment')
  })

  it('Checks that shard sets page element renders', function () {
    cy.get('[data-cy=shard-sets]')
  })

  it('Checks that database shards page element renders', function () {
    cy.get('[data-cy=database-shards]')
  })

  it('Checks that database items page element renders', function () {
    cy.get('[data-cy=database-items]')
      .contains('Players') // Check that at least one count is shown
  })

  it('Opens the model size inspector modal', function () {
    cy.get('[data-cy=inspect-entity-modal-button]')
      .click({ force: true })
  })

  it('Types in a player ID', function () {
    cy.get('[data-cy=player-id-input]')
      .type(this.testPlayer.id)
      .should('have.value', this.testPlayer.id)
  })

  it('Navigates to the player model size inspector page', function () {
    cy.get('[data-cy=inspect-entity-modal-ok]')
      .clickMetaButton()
  })

  it('Checks that elements render', function () {
    cy.get('[data-cy=entity-overview]')
    cy.get('[data-cy=entity-data]')
  })
})
