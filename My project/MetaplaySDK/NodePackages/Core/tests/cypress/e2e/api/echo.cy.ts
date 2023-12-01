// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Echo endpoint', [], function () {
  it('Succeeds', function () {
    cy.task('makeApiRequest', { endpoint: '/api/echo' }).then((data) => {
      expect(data).to.have.property('headers')
      expect(data).to.have.property('metaplay')
    })
  })
})
