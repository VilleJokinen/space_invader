// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

module.exports = {
  plugins: [
    'cypress'
  ],
  env: {
    'cypress/globals': true
  },
  rules: {
    strict: 'off',
    '@typescript-eslint/restrict-template-expressions': 'off', // Cypress uses template expressions in its expect() calls
    '@typescript-eslint/strict-boolean-expressions': 'off' // Cypress types are complex and return 'any' in some cases
  }
}
