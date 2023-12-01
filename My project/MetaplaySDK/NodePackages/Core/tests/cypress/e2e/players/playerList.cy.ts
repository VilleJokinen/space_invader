// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Player List', [], function () {
  before(function () {
    cy.visit('/players')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Types something into the search box', function () {
    cy.get('.multiselect-search')
      .type('Guest')
      .should('have.value', 'Guest')
  })

  function searchForId (id, searchTerm): void {
    cy.get('.multiselect-search')
      .clear()
      .type(searchTerm)
    cy.get('#multiselect-options')
      .contains(id)
      .should('exist')
  }

  it('Searches for test player by id', function () {
    searchForId(this.testPlayer.id, this.testPlayer.id)
  })

  it('Searches for test player by id prefix', function () {
    searchForId(this.testPlayer.id, this.testPlayer.id.slice(0, -2))
  })

  it('Searches for test player by uppercase id', function () {
    const parts = this.testPlayer.id.split(':') as string[]
    searchForId(this.testPlayer.id, parts[0] + ':' + parts[1].toUpperCase())
  })

  it('Searches for test player by lowercase id', function () {
    const parts = this.testPlayer.id.split(':') as string[]
    searchForId(this.testPlayer.id, parts[0] + ':' + parts[1].toLowerCase())
  })
})
