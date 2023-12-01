// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// Import commands to be used in tests.
import './commands'

// IntersectionObservers don't really work in tests because they hide elements until a user scrolls them into view. To
// fix this we need to mock them. This *absolutely minimal* mock always fires an "element is visible" event whenever
// observe() is called. The mock is installed below in `window:before:load`.
class mockIntersectionObserver {
  constructor (cb: any) {
    (this as any).cb = cb
  }

  observe (): void {
    (this as any).cb([{
      isIntersecting: true
    }])
  }

  unobserve (): void {
  }

  disconnect (): void {
  }
}

// This code runs once before all tests.
before(function () {
  // Create a new dash player for each full run to avoid sharing state between tests.
  cy.task('getTestPlayer', true).then((testPlayer) => {
    cy.wrap(testPlayer).as('testPlayer')
  })

  // Create a unique test token string for each full run to avoid sharing state between tests.
  const testToken = Date.now().toString().slice(-5)
  cy.wrap(testToken).as('testToken')
  cy.log('Generated a test token', testToken)
})

let spyWarn, spyError
Cypress.on('window:before:load', (win) => {
  // Insert our IntersectionObserver mock.
  (win as any).IntersectionObserver = mockIntersectionObserver

  // Spy on all console warnings and errors.
  spyWarn = cy.spy(win.console, 'warn')
  spyError = cy.spy(win.console, 'error')
})

// This code runs once after each and every test.
afterEach(function () {
  // After each test we check all warning and error messages and fail the test if there are any unexpected ones.
  // NB: There are some expected/acceptable messages that we need to ignore.
  const allowList = [
    // Vue and related system messages.
    '[HMR]',
    '[Vue warn]',
    '[BootstrapVue warn]',
    '(deprecation ',
    '^ The above deprecation\'s compat behavior is disabled and will likely lead to runtime errors.',
    'Lit is in dev mode. Not recommended for production!',
  ]

  // Find the text of every warning and error that was output during the test.
  const calls = [...(spyWarn ? spyWarn.getCalls() : []), ...(spyError ? spyError.getCalls() : [])]
  const messages = calls.map(call => call.args.join())

  // Check each text to see if it was allowed or not.
  messages.forEach(message => {
    // Raise as an error to fail the test if it's not in our allow list.
    if (!allowList.some(x => message.startsWith(x))) {
      assert.fail(`Failing test due to unexpected console message: "${message}"`)
    }
  })
})
