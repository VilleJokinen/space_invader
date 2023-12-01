// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player model size inspector', [], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}`)
  })

  it('Opens the model size inspector', function () {
    cy.get('[data-cy=model-size-link]')
      .click({ force: true })
  })

  it('Checks that elements render', function () {
    cy.get('[data-cy=entity-overview]')
    cy.get('[data-cy=entity-data]')
  })
})
