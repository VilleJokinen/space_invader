// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

cy.maybeDescribe('Scan Jobs', [], function () {
  before(function () {
    cy.visit('/scanJobs')
  })

  it('Checks that an active sidebar link exists to the current page', function () {
    cy.sidebarLinkToCurrentPageShouldExist()
  })

  it('Checks static page elements', function () {
    cy.get('[data-cy=scan-jobs-overview]')
    cy.get('[data-cy=active-scan-jobs-card]')
    cy.get('[data-cy=past-scan-jobs-by-type-card]')
    cy.get('[data-cy=past-scan-jobs-card]')
  })

  it('Pauses and resumes all scan jobs', function () {
    // Open the modal.
    cy.get('[data-cy=pause-all-jobs-button]')
      .click({ force: true })
    cy.get('[data-cy=pause-all-jobs-modal')
      .should('exist')

    // Toggle the pause switch.
    cy.get('[data-cy=pause-jobs-toggle]')
      .parent()
      .click()

    // Confirm the pause.
    cy.get('[data-cy=pause-all-jobs-ok]')
      .clickMetaButton()
    cy.get('[data-cy=pause-all-jobs-modal')
      .should('not.exist')

    // Check that the pause alert is visible.
    cy.get('[data-cy=all-jobs-paused-alert]')
      .should('exist')

    // Open the modal again.
    cy.get('[data-cy=pause-all-jobs-button]')
      .click({ force: true })
    cy.get('[data-cy=pause-all-jobs-modal')
      .should('exist')

    // Toggle the pause switch.
    cy.get('[data-cy=pause-jobs-toggle]')
      .parent()
      .click()

    // Confirm the pause.
    cy.get('[data-cy=pause-all-jobs-ok]')
      .clickMetaButton()
    cy.get('[data-cy=pause-all-jobs-modal')
      .should('not.exist')

    // Check that the alert is no longer visible.
    cy.get('[data-cy=all-jobs-paused-alert]')
      .should('not.exist')
  })

  // \note This check is just a prerequisite for running the rest of the tests
  it.skip('Checks there is no active player refresher job', function () {
    cy.get('[data-cy=active-scan-jobs-card]')
      .should('not.contain', 'Refresher for Players')
  })

  it.skip('Starts a new player refresher job', function () {
    cy.get('[data-cy=new-scan-job-button]')
      .click({ force: true })

    cy.get('[data-cy=job-kind-select]')
      .select('Refresher for Players')

    cy.get('[data-cy=new-scan-job-ok]')
      .clickMetaButton()
  })

  // \todo [paul] Disabled these tests as they were still a bit too time-sensitive. Fix in r22.

  // \todo [nuutti] This is timing-sensitive: if the job completes very quickly, then the dashboard
  //                might never observe it in active state. Currently it works (if it works) because
  //                even "no-op" jobs take a few moments to complete.

  it.skip('Cancels the newly started job', function () {
    cy.get('[data-cy=active-scan-jobs-card]')
      .find('[data-cy=scan-jobs-entry]')
      .should('contain', 'Refresher for Players')
      .and('contain', 'Started a few seconds ago')
      .find('button')
      .first()
      .click({ force: true })

    cy.get('.modal-footer')
      .find('.btn-danger')
      .clickMetaButton()
  })

  it.skip('Checks the cancelled job is no longer active', function () {
    // It can take several seconds for the scan job to cancel and for the dashboard subscription to refresh the status.
    // The default timeout is often not quite long enough, so we increase it.
    cy.get('[data-cy=active-scan-jobs-card]', { timeout: 10000 })
      .should('not.contain', 'Refresher for Players')
  })

  it.skip('Checks the cancelled job is in job history', function () {
    cy.get('[data-cy=past-scan-jobs-card]')
      .find('[data-cy=scan-jobs-entry]')
      .should('contain', 'Refresher for Players')
  })
})
