// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Audit Log', [], function () {
  before(function () {
    cy.visit(`/players/${this.testPlayer.id}?tab=1`)
  })

  it('Finds audit events list on player detail page', function () {
    cy.get('[data-cy=audit-log-card]')
  })

  it('Creates an audit log event and finds evidence of it', function () {
    // We need an idempotent event that will always be logged
    // GDPR export is the perfect thing
    cy.get('[data-cy=gdpr-export-button]')
      .click({ force: true })

    cy.get('#action-gdpr-export')
      .type('{esc}')

    cy.get('[data-cy=audit-log-card]')
      .contains('GDPR data exported')
  })

  it('Event ID link on player detail page goes to detailed view page', function () {
    cy.get('[data-cy=audit-log-card]')
      .find('[data-cy=log-event-row]')
      .contains('GDPR data exported')
      .closest('[data-cy=log-event-row]')
      .find('[data-cy=view-more-link]')
      .click({ force: true })

    cy.contains('GDPR data of player exported')
  })

  it('Target link on event detail page goes to main audit log page with pre-filled search fields', function () {
    let userId = ''
    cy.get('[data-cy=detailed-event-card]')
      .contains(/Player:.{10}/)
      .then((el) => { userId = el.text().split(':')[1] })
      .click({ force: true })
    cy.contains('View Audit Logs')
    cy.location().should(loc => {
      expect(loc.search).to.include('targetType=Player')
      expect(loc.search).to.include(`targetId=${userId}`)
    })
    cy.contains('GDPR data exported')
    cy.contains('No search results').should('not.exist')
  })
})
