cy.maybeDescribe('Player Leagues', ['playerLeagues'], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}`)
  })

  it('Checks that player leagues card renders', function () {
    cy.get('[data-cy=player-leagues-card]')
  })
})
