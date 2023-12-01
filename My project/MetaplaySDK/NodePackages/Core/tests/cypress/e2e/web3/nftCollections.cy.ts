// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('NFT Collections List', ['web3'], function () {
  before(function () {
    cy.visit('/web3')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Checks that elements render', function () {
    cy.get('[data-cy=web3-overview-card]')
    cy.get('[data-cy=nft-collections-list-card]')
  })

  it('Navigates into a collection', function () {
    cy.get('[data-cy=view-nft-collection]')
      .first()
      .click({ force: true })
  })

  it('Checks that collection detail page elements render', function () {
    cy.get('[data-cy=nft-collection-overview-card]')
    cy.get('[data-cy=nft-collection-nft-list]')
    cy.get('[data-cy=nft-collection-uninitialized-nfts-card]')
    cy.get('[data-cy=nft-collection-audit-log-card]')
  })

  it('Refreshes collection metadata', function () {
    cy.get('[data-cy=refresh-nft-collection-button]')
      .click({ force: true })
    cy.get('[data-cy=refresh-nft-collection-ok]')
      .clickMetaButton()
  })

  // TODO: how to automatically test batch initialization?

  it('Initializes a new NFT', function () {
    cy.get('[data-cy=initialize-nft-button]')
      .click({ force: true })
      .wait(1000) // TODO: replace with better code when generated UI supports it.
    cy.get('[data-cy=initialize-nft-ok]')
      .clickMetaButton()
  })

  it('Navigates into an NFT', function () {
    cy.get('[data-cy=view-nft]')
      .first()
      .click({ force: true })
  })

  it('Checks that NFT detail page elements render', function () {
    cy.get('[data-cy=nft-overview-card]')
    // cy.get('[data-cy=nft-game-state-card]')
    cy.get('[data-cy=nft-public-data-preview-card]')
    cy.get('[data-cy=nft-audit-log-card]')
  })

  it('Refreshes NFT ownership', function () {
    cy.get('[data-cy=refresh-nft-button]')
      .click({ force: true })
    cy.get('[data-cy=refresh-nft-ok]')
      .clickMetaButton()
  })

  it('Re-saves NFT metadata', function () {
    cy.get('[data-cy=republish-nft-metadata-button]')
      .click({ force: true })
    cy.get('[data-cy=republish-nft-metadata-ok]')
      .clickMetaButton()
  })

  it('Re-initializes the NFT', function () {
    cy.get('[data-cy=edit-nft-button]')
      .click({ force: true })
      .wait(1000) // TODO: replace with better code when generated UI supports it.
    cy.get('[data-cy=edit-nft-ok]')
      .clickMetaButton()
  })
})
