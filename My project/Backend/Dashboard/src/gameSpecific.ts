// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// Import the integration API.
import { setGameSpecificInitialization } from '@metaplay/core'
import type { App } from 'vue'

/**
 * This is a Vue 3 plugin function that gets called after the SDK CorePlugin is registered but before the application is mounted.
 * Use this function to register any Vue components or plugins that you want to use to customize the dashboard.
 * @param app The Vue app instance.
 */
export function GameSpecificPlugin (app: App): void {
  // Feel free to add any customization logic here for your game!

  setGameSpecificInitialization(async (initializationApi) => {
    // Inject custom content into the player details page.
    initializationApi.addUiComponent(
      'Players/Details/Tab0',
      {
        uniqueId: 'HelloWorld',
        vueComponent: async () => await import('./HelloWorld.vue')
      },
      { position: 'before' }
    )
  })
}
