/* eslint-env node */
require("@rushstack/eslint-patch/modern-module-resolution")

module.exports = {
  env: {
    browser: true,
    es2021: true
  },
  extends: [
    'plugin:vue/vue3-recommended',
    '@vue/eslint-config-standard-with-typescript',
  ],
  parserOptions: {
    ecmaVersion: 'latest',
    parser: '@typescript-eslint/parser',
    sourceType: 'module'
  },
  plugins: [
    'vue',
    '@typescript-eslint'
  ],
  ignorePatterns: [
    '**/node_modules/*',
    '**/dist/*',
    '*.config.*',
    'metaplay_temporary_copy_of_core_tests/**/*'
  ],
  rules: {
    'no-console': process.env.NODE_ENV === 'production' ? ['warn', { allow: ['warn', 'error'] }] : 'off', // console.log() not allowed in production code
    'no-debugger': process.env.NODE_ENV === 'production' ? 'warn' : 'off', // debugger not allowed in production code
    'comma-dangle': ['error', 'only-multiline'], // multiline objects and arrays may use a trailing comma at the end of the line
    '@typescript-eslint/no-unused-vars': 'off', // this conflicts with Vue templates
    '@typescript-eslint/restrict-template-expressions': 'off', // We can only enable this after having end-to-end type safety
    '@typescript-eslint/strict-boolean-expressions': 'off', // We can only enable this after having end-to-end type safety
    '@typescript-eslint/restrict-plus-operands': 'off', // We can only enable this after having end-to-end type safety
    '@typescript-eslint/explicit-function-return-type': 'off', // We have too many of these to refactor now
    'eslintvue/prefer-import-from-vue': 'off', // This is messing with compatibility with Vue 2
    '@typescript-eslint/triple-slash-reference': 'off', // This goes against Vue's sample code. Better to stay consistent with the samples.
    'import/export': 'off', // This goes against Vue's sample code. Better to stay consistent with the samples.
  }
}
