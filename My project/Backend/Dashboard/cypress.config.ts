// Use SDK's pre-defined Cypress config as a base.
import defaultMetaplayCypressConfig from '@metaplay/core/cypress.config'
import { dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))

defaultMetaplayCypressConfig.prepareCoreSpecs(__dirname)
export default defaultMetaplayCypressConfig
