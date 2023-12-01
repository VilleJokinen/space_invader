import { defineConfig } from 'cypress'
import axios from 'axios'
import fs from 'fs-extra'
import { dirname } from 'path'
import { fileURLToPath } from 'url'
import chokidar from 'chokidar'

const __dirname = dirname(fileURLToPath(import.meta.url))
const coreTestTemporaryDirectoryPrefix = 'metaplay_temporary_copy_of_core_tests'

export default {
  ...defineConfig({
    waitForAnimations: false,
    video: false,
    e2e: {
      async setupNodeEvents (on, config) {
        return await setupNodeEvents(on, config)
      },
      baseUrl: 'http://localhost:5551',
      defaultCommandTimeout: 10000,
      specPattern: [
        'gamespecific_tests/e2e/**/*.cy.{js,jsx,ts,tsx}',
        coreTestTemporaryDirectoryPrefix + '/e2e/**/*.cy.{js,jsx,ts,tsx}'
      ],
      supportFile: coreTestTemporaryDirectoryPrefix + '/support/index.ts',
      fixturesFolder: 'gamespecific_tests/e2e/fixtures',
    },
  }),

  // prepare specs
  prepareCoreSpecs: (rootPath: string) => {
    const pathToSdkCoreModule = __dirname
    const pathToSdkCoreTests = pathToSdkCoreModule + '/tests/cypress'
    const pathToGameSpecificTests = rootPath + '/gamespecific_tests'
    const pathToGameSpecificCoreTests = rootPath + '/' + coreTestTemporaryDirectoryPrefix

    // Check that we can find both the core and game specific tests. If we can't then we probably messed up the paths
    // somewhere and it's safest to abort right now.
    if (!fs.existsSync(pathToSdkCoreTests)) {
      throw new Error(`Core tests not found in '${pathToSdkCoreTests}'`)
    }
    if (!fs.existsSync(pathToGameSpecificTests)) {
      throw new Error(`Game specific tests not found in '${pathToGameSpecificTests}'`)
    }

    // Copy the contents of pathToSdkCoreTests to pathToGameSpecificCoreTests.
    fs.copySync(pathToSdkCoreTests, pathToGameSpecificCoreTests)

    const watcher = chokidar.watch(pathToSdkCoreTests, { ignoreInitial: true, persistent: true,  })
    watcher.on('all', (event, path) => {
      const relativePath = path.replace(pathToSdkCoreTests, '')
      const destinationPath = pathToGameSpecificCoreTests + relativePath
      if (event === 'add' || event === 'change') {
        fs.copySync(path, destinationPath)
      } else if (event === 'unlink') {
        fs.removeSync(destinationPath)
      }
    })

    // Add a .gitignore files so that this copy does not get into source control.
    fs.writeFileSync(pathToGameSpecificCoreTests + '/.gitignore', '/*\n')
  }
}

/**
 * Modify Cypress behavior to suit our needs - add tasks, configure skip flags, etc..
 * @param on Used to register event listeners.
 * @param config Resolved Cypress config.
 * @returns New config object.
 */
const setupNodeEvents = async (on: any, config: any) => {
  // We'll create a test player and store its Id here.
  let testPlayer: { id: string }

  // In localhost development we know the API is on port 5550 instead of the default baseUrl.
  let apiBaseUrl: string = config.baseUrl
  if (apiBaseUrl.includes(':5551')) {
    apiBaseUrl = 'http://localhost:5550'
  }

  // Add some custom tasks.
  on('task', {
    // A task to get a test player. Only one test player is created per run and the result is cached, so this task can
    // safely be called from anywhere.
    async 'getTestPlayer' (skipCache = false) {
      if (!testPlayer || skipCache) {
        const data = (await axios.post(apiBaseUrl + '/api/testing/createPlayer', {})).data
        testPlayer = data
      }

      return testPlayer
    },

    /**
     * A task to make a raw API request to the game server.
     * Arguments are an object:
     *  endpoint: The endpoint to hit, e.g. '/api/leagues'.
     *  method: The HTTP method to use, e.g. 'get' or 'post'. Defaults to 'get'.
     *  payload: The optional payload to send. Defaults to {}.
     */
    async 'makeApiRequest' (args: {endpoint: string, method?: string, payload?: object}) {
      return (await axios({
        url: apiBaseUrl + args.endpoint,
        method: args.method || 'get',
        data: args.payload || {}
      })).data
    }
  })

  // Initialize our Metaplay env config to empty if it wasn't passed from cypress.json, command line or something else.
  config.env = config.env || {}
  config.env.metaplay = config.env.metaplay || {}
  config.env.metaplay.skipFeatureFlags = config.env.metaplay.skipFeatureFlags || []
  // config.env.metaplay.skipSpecPaths = config.env.metaplay.skipSpecPaths || []

  // Helper function to add a skip feature flag.
  // @param: isEnabled True if the feature is enabled.
  // @param: featureName Name of the skip feature flag to use if this feature is not enabled.s
  function setFeatureFlag (isEnabled: boolean, featureName: string) {
    if (!isEnabled) {
      if (!config.env.metaplay.skipFeatureFlags.includes(featureName)) config.env.metaplay.skipFeatureFlags.push(featureName)
    }
  }

  // Helper to print game-server's errors
  function gameServerErrorToHumanString (error: any) {
    if (error.response && error.response.data) {
      // The game server returns the error in response body. Handle all types, but special
      // case for the "happy" case of { error: { message, details, [stackTrace] } }
      let message;
      if (error.response.data.error) {
          if (error.response.data.error.message && error.response.data.error.details) {
            message = `${error}: ${error.response.data.error.message}: ${error.response.data.error.details}`
          } else {
            message = `${error}: ${JSON.stringify(error.response.data.error)}`
          }
      } else {
        message = `${error}: ${JSON.stringify(error.response.data)}`
      }
      if (error.response.data.stackTrace) {
        message = message + '\n' + error.response.data.stackTrace
      }
      return message
    }
    return `${error}`
  }

  // Figure out feature skip flags and store them in the environment.
  try {
    const helloData = (await axios.get(apiBaseUrl + '/api/hello', {})).data
    Object.entries(helloData.featureFlags).forEach(([featureName, isEnabled]) => {
      setFeatureFlag(!!isEnabled, featureName)
    })
  } catch (error) {
    throw new Error(`Failed to get feature flags from 'hello' endpoint. ${gameServerErrorToHumanString(error)}`)
  }

  // Ensure a game config has been loaded.
  // \note: This is intentionally interleaved here so that server launch failure fail at /api/hello query.
  try {
    const activeGameConfigId = (await axios.get(apiBaseUrl + '/api/activeGameConfigId', {})).data
  } catch (error: any) {
    if (error.response && error.response.status == 404) {
      throw new Error(
        'Game server has no active GameConfig. These tests validate functionality that requires GameConfig. Testing cannot proceed. '
        + 'Make sure the built server image contains a valid GameConfig archive.')
    } else {
      throw new Error(`Failed to access 'activeGameConfigId' endpoint. ${gameServerErrorToHumanString(error)}`)
    }
  }

  // If events don't exist as an activable category then there are no events to test.
  try {
    const staticConfigData = (await axios.get(apiBaseUrl + '/api/staticConfig', {})).data
    setFeatureFlag(staticConfigData.serverReflection.activablesMetadata.categories.Event, 'events')
  } catch (error) {
    throw new Error(`Failed to get event activables from 'staticConfig' endpoint. ${gameServerErrorToHumanString(error)}`)
  }
  // Check for any offers and offer groups.
  try {
    const offersData = (await axios.get(apiBaseUrl + '/api/offers', {})).data
    setFeatureFlag(Object.keys(offersData.offers).length > 0, 'offers')
    setFeatureFlag(Object.keys(offersData.offerGroups).length > 0, 'offerGroups')
  } catch (error) {
    throw new Error(`Failed to get offers from 'offers' endpoint: ${gameServerErrorToHumanString(error)}`)
  }

  // Segments?
  try {
    const segmentationData = (await axios.get(apiBaseUrl + '/api/segmentation', {})).data
    setFeatureFlag(segmentationData.segments.length > 0, 'segments')
  } catch (error) {
    throw new Error(`Failed to get segments from 'segmentation' endpoint. ${gameServerErrorToHumanString(error)}`)
  }

  // Always add a get-out-of-jail-free way to skip tests easily in customer projects.
  config.env.metaplay.skipFeatureFlags.push('customer-feature-missing')

  // Return the new config object.
  return config
}
