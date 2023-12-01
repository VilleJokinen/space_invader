// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// NOTE: When editing this file, please make sure to also update the typings in cypress.d.ts and make sure it ends up getting copied to userland projects.

cy.maybeDescribe = (name, dependencies, testFunction) => {
  const skipFeatureFlags = Cypress.env('metaplay').skipFeatureFlags as string[]

  const skipReasons = [
    ...skipFeatureFlags.filter(skipFeature => dependencies.find(feature => feature.toLowerCase() === skipFeature.toLowerCase())).map(skipFeature => `SkipFeatureFlag '${skipFeature}'`)
  ]
  if (skipReasons.length === 0) {
    describe(name, { testIsolation: false }, testFunction)
  } else {
    describe(name, () => { it('Skipped by configuration: ' + skipReasons.map(reason => '"' + reason + '"').join(', ')) })
  }
}

Cypress.Commands.add('paste', { prevSubject: true }, ($element, text) => {
  const subString = text.slice(1, text.length)
  const firstChar = text.slice(0, 1)

  cy.get($element)
    .click({ force: true })
    .then(() => {
      $element.text(subString)
      $element.val(subString)
      cy.get($element).type(firstChar)
    })
})

Cypress.Commands.add('clickMetaButton', { prevSubject: true }, (subject: JQuery<HTMLElement>) => {
  // Disable the safety-lock if it is on. I hate this code so much. Let's revisit after MetaButton is migrated to MButton as that critically affects this business logic.
  // Short explanation: Click the button mutates multiple parent nodes so Cypress loses the handle. We need to store the non-mutating root node.
  let parent = cy.wrap(subject).parent().parent().parent()

  cy.wrap(subject)
    .should('have.attr', 'safety-lock-active-cy').then((attr) => {
      if (attr as unknown as string === 'yes') {
        parent
          .find('[data-cy="safety-lock-button"]')
          .trigger('click', { force: true })

        // The above code re-assigns the parent (ugh!) so we reset it here back to the initial value.
        parent = parent.parent()
      }
    })

  // Now click the button.
  parent
    .find('button')
    .should('not.be.disabled')
    .click({ force: true })
})

Cypress.Commands.add('sidebarLinkToCurrentPageShouldExist', () => {
  cy.url().then((fullUrl) => {
    // Remove baseurl from full url
    const url = fullUrl.replace(String(Cypress.config().baseUrl), '')

    cy.get('[data-cy="sidebar"]')
      .find(`[href="${url}"]`)
      .should('exist')
      .should('not.be.disabled')
  })
})

export {}
