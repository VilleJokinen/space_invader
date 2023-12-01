// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { defineStore } from 'pinia'
import type { GameSpecificReward } from './rewardUtils'

export interface UiStoreState {
  showDeveloperUi: boolean
  showSidebar: boolean
  isNewGameConfigAvailable: boolean
  isSafetyLockOn: boolean
  gameSpecific: {
    playerRewards: GameSpecificReward[]
  }
}

const defaultState: UiStoreState = {
  showDeveloperUi: false,
  showSidebar: true,
  isNewGameConfigAvailable: false,
  isSafetyLockOn: false,
  gameSpecific: {
    playerRewards: []
  }
}

/**
 * Use a Pinia store to remember connection states. This makes them easy to view in the Vue debugger.
 */
export const useUiStore = defineStore('meta-ui', {
  state: () => defaultState,
  actions: {
    /**
     * Shows or hides the extra technical information on the various dashboard screens.
     * @param isShown Should the extra information be shown?
     */
    toggleDeveloperUi (isShown: boolean) {
      this.showDeveloperUi = isShown
      localStorage.showDeveloperUi = isShown // Persist in browser storage
    },
    /**
     * Enables or disables meta-buttons that have the " safety-lock" property.
     * @param isLocked Should the safety lock be on?
     */
    toggleSafetyLock (isLocked: boolean) {
      this.isSafetyLockOn = isLocked
      localStorage.isSafetyLockOn = isLocked // Persist in browser storage
    },
  }
})
