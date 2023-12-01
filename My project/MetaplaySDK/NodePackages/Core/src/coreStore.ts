// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
import { defineStore } from 'pinia'
import { useAttrs } from 'vue'

import type {
  GameSpecificInAppPurchaseContent,
  GameSpecificPlayerResource,
} from './integration_api/integrationApi'

import type {
  UiPlacement,
  UiPlacementInfo
} from './integration_api/uiPlacementApis'

import type {
  OverviewListItem,
} from './integration_api/overviewListsApis'

import {
  DefaultGeneratedUiFormComponents,
  DefaultGeneratedUiViewComponents
} from './components/generatedui/defaultGeneratedUiComponentRules'

import type {
  IGeneratedUiComponentRule,
  IGeneratedUiFieldTypeSchema
} from './components/generatedui/generatedUiTypes'

const isProd = import.meta.env.PROD

export interface CoreStoreState {
  isProd: boolean
  backendConnectionStatus: {
    status: string
    displayName: string
    error: { stepName: string, errorMessage: string, errorResolution: string, errorObject: Error } | null
    numOngoingRequests: number
  }
  hello: any | null
  gameSpecific: {
    gameIconUrl: string
    playerResources: GameSpecificPlayerResource[]
    iapContents: GameSpecificInAppPurchaseContent[]
    activableCustomization: any
  }
  uiComponents: {
    [key in UiPlacement]?: UiPlacementInfo[]
  }
  overviewLists: {
    player: OverviewListItem[]
    playerReconnectPreview: OverviewListItem[]
    guild: OverviewListItem[]
  }
  actorOverviews: {
    overviewListEntries: Array<{ key: string, displayName: string }>
    charts: Array<{ key: string, displayName: string }>
    databaseListEntries: Array<{ key: string, displayName: string }>
  }
  generatedUiViewComponents: IGeneratedUiComponentRule[]
  generatedUiFormComponents: IGeneratedUiComponentRule[]
  schemas: {
    [key: string]: IGeneratedUiFieldTypeSchema | null
  }
  stringIdDecorators: {[stringId: string]: (stringId: string) => string}
}

const defaultState: CoreStoreState = {
  isProd,
  backendConnectionStatus: {
    status: 'uninitialized',
    displayName: 'Backend connection uninitialized',
    error: null,
    numOngoingRequests: 0,
  },
  hello: null,
  gameSpecific: {
    gameIconUrl: '/metaplay-monogram-256.png',
    playerResources: [],
    iapContents: [
      {
        $type: 'Metaplay.Core.Offers.MetaOfferGroupOfferDynamicPurchaseContent',
        getDisplayContent: (purchase) => [`${purchase.offerId} in ${purchase.groupId}`]
      }
    ],
    activableCustomization: {
      OfferGroup: { icon: 'tags', sidebarNavName: 'Offers', pathName: 'offerGroups' }
    },
  },
  uiComponents: {},
  overviewLists: {
    player: [],
    playerReconnectPreview: [],
    guild: [],
  },
  actorOverviews: {
    overviewListEntries: [
      { key: 'Connection', displayName: 'Live Connections' },
      { key: 'Player', displayName: 'Live Player Actors' },
    ],
    charts: [
      { key: 'Player', displayName: 'Player Actors' },
    ],
    databaseListEntries: [
      { key: 'Player', displayName: 'Total Player Accounts' },
    ],
  },
  generatedUiFormComponents: DefaultGeneratedUiFormComponents,
  generatedUiViewComponents: DefaultGeneratedUiViewComponents,
  schemas: {},
  stringIdDecorators: {},
}

/**
 * A function to get access to the main Pinia store of the LiveOps Dashboard.
 * Calling this function before the app has been mounted will result in an error.
 */
export const useCoreStore = defineStore('core', {
  state: () => defaultState,
  actions: {
    /**
     * Save a type schema in the store.
     * @param typeName The typeName of the schema. Will be used as the key.
     * @param schema the schema to be saved, or null if not loaded yet.
     */
    setSchemaForType (typeName: string, schema: IGeneratedUiFieldTypeSchema | null) {
      this.schemas[typeName] = schema
    },
    /**
     * Add a new generated Ui view component to the beginning of the list.
     * @param rule The component filter rule for the new component.
     */
    addGeneratedUiViewComponent (rule: IGeneratedUiComponentRule) {
      this.generatedUiViewComponents.unshift(rule)
    },
    /**
     * Add a new generated Ui form component to the beginning of the list.
     * @param rule The component filter rule for the new component.
     */
    addGeneratedUiFormComponent (rule: IGeneratedUiComponentRule) {
      this.generatedUiFormComponents.unshift(rule)
    },
    isUiPlacementEmpty (placement: UiPlacement) {
      return this.uiComponents[placement]?.length === 0
    },
  }
})

export function getIsProductionEnvironment () {
  return useCoreStore().isProd
}
