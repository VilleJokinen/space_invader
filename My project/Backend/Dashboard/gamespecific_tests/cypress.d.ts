// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

declare global {
  namespace Cypress {
    interface Chainable {
      /**
       * Metaplay's own conditional test runner. This allows us to skip tests based on feature flags.
       * @param name The name of the test suite as shown in the test runner.
       * @param dependencies Feature flags that this test suite depends on. Pass [] for tests that should always run.
       * @param testFunction The tests themselves. This is what would normally go inside a describe block.
       * @example: cy.maybeDescribe('My new feature', ['Dependency1', 'Dependency2'], () => {
       */
      maybeDescribe: (name: string, dependencies: string[], testFunction: () => void) => void
      /**
       * Simulates a paste by dumping test into a field and typing the last value to trigger element updates.
       */
      paste: (text: string) => void
      /**
       * Choose an option from a multi-select.
       */
      multiselectChoose: (option: string, closeAfterSelect: boolean) => void
      /**
       * Same as `click()` but also handles the potential safety lock.
       */
      clickMetaButton: () => void
      /**
       * Helper to assert that a non-disabled element that links to the current URL exists in the sidebar.
       */
      sidebarLinkToCurrentPageShouldExist: () => void
    }
  }
}

export {}
