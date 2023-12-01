// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Raw Player', [], function () {
  it('Check rendering of valid player', function () {
    cy.visit(`/players/${this.testPlayer.id}/raw`)
    cy.get('[data-cy=overview-card]')
    cy.get('[data-cy="detail-card-Player Metadata"]')
      .contains(/^Valid$/)
    cy.get('[data-cy="detail-card-Persisted Player"]')
      .contains(/^Valid$/)
    cy.get('[data-cy="detail-card-Player State Response"]')
      .contains(/^Valid$/)
    cy.get('[data-cy="detail-card-Specialized Config Metadata"]')
      .contains(/^Valid$/)
    cy.get('[data-cy="detail-card-Player Model"]')
      .contains(/^Valid$/)
  })

  it('Check rendering of missing player', function () {
    cy.visit('/players/Player:9999999999/raw')
    cy.get('[data-cy=overview-card]')
    cy.get('[data-cy="detail-card-Player Metadata"]')
      .contains(/^Valid$/)
    cy.get('[data-cy="detail-card-Persisted Player"]')
      .contains(/^Not valid$/)
    cy.get('[data-cy="detail-card-Player State Response"]')
      .contains(/^Not valid$/)
    cy.get('[data-cy="detail-card-Specialized Config Metadata"]')
      .contains(/^Not valid$/)
    cy.get('[data-cy="detail-card-Player Model"]')
      .contains(/^Not valid$/)
  })

  it('Check rendering of invalid player', function () {
    cy.visit('/players/Player:invalid/raw')
    cy.get('[data-cy=overview-card]')
    cy.get('[data-cy="detail-card-Player Metadata"]')
      .contains(/^Not valid$/)
    cy.get('[data-cy="detail-card-Persisted Player"]')
      .contains(/^Not valid$/)
    cy.get('[data-cy="detail-card-Player State Response"]')
      .contains(/^Not valid$/)
    cy.get('[data-cy="detail-card-Specialized Config Metadata"]')
      .contains(/^Not valid$/)
    cy.get('[data-cy="detail-card-Player Model"]')
      .contains(/^Not valid$/)
  })
})
