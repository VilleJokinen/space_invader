// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Experiments', [], function () {
  before(function () {
    cy.task('makeApiRequest', { endpoint: '/api/experiments' })
      .then((data: any) => {
        this.hasExperiments = data.experiments.length > 0
      })

    cy.visit('/experiments')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Checks that list page element renders', function () {
    cy.get('[data-cy=all-experiments]')
  })

  it('Navigates into an experiment', function () {
    if (this.hasExperiments) {
      cy.get('[data-cy=view-experiment]')
        .first()
        .click({ force: true })
    } else {
      this.skip()
    }
  })

  it('Checks that detail page elements render', function () {
    if (this.hasExperiments) {
      cy.get('[data-cy=overview]')
      cy.get('[data-cy=segments]')
      cy.get('[data-cy=variants]')
      cy.get('[data-cy=testers]')
      cy.get('[data-cy=audit-log]')
      cy.get('[data-cy=config-contents]')
    } else {
      this.skip()
    }
  })
})
