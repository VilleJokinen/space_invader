// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player NFT Details', ['web3'], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}?tab=2`)
  })

  it('Checks that a card exists', function () {
    cy.get('[data-cy=player-nfts-card]')
  })

  it('Triggers NFT ownership refresh', function () {
    cy.get('[data-cy=nft-refresh-button]')
      .click({ force: true })
  })
})
