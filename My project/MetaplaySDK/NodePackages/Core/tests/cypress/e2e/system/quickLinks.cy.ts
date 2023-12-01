// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Quick Links', [], function () {
  before(function () {
    // Intercept the quickLinks API request and mock the returned data.
    cy.intercept('/api/quickLinks', (request) => {
      request.reply([
        {
          icon: '@game-icon',
          title: 'Mocked title 1',
          uri: '/players',
          color: 'rgb(134, 199, 51)'
        },
        {
          icon: '@game-icon',
          title: 'Mocked title 2',
          uri: '/runtimeOptions',
          color: 'rgb(134, 199, 51)'
        },
        {
          icon: '@game-icon',
          title: 'Mocked title 3',
          uri: '/guilds',
          color: 'rgb(134, 199, 51)'
        }
      ])
    })

    cy.visit('/')
  })

  it('Checks that the quick links modal is enabled when links are defined', function () {
    cy.get('[data-cy=quick-link]')
      .should('not.have.attr', 'disabled')
  })

  it('Checks that the quick links modal opens', function () {
    cy.get('[data-cy=quick-link]')
      .click({ force: true })
  })

  it('Checks that the quick link list is not empty.', function () {
    cy.get('div[data-cy^="quick-link-"]').should('have.length', 3)
    cy.get('div[data-cy="quick-link-0"]').find('span').should('have.text', 'Mocked title 1')
    cy.get('div[data-cy="quick-link-2"]').find('span').should('have.text', 'Mocked title 3')
  })
})
