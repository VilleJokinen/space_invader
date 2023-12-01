// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import distance from 'jaro-winkler'
import { defineStore } from 'pinia'
import type { PermissionDetails, UserDetails } from './auth/authProvider'

export interface GameServerApiStoreState {
  isConnected: boolean
  hasConnected: boolean
  auth: {
    requiresBasicPermissions: boolean
    requiresLogin: boolean
    userRoles: string[]
    userPermissions: string[]
    userDetails: UserDetails
    userAssumedRoles: string[]
    serverRoles: string[]
    serverPermissions: PermissionDetails[]
    canLogout: boolean
    canAssumeRoles: boolean
    bearerToken: string | null
    rolePrefix: string
    hasTokenExpired: boolean
  }
}

const defaultState: GameServerApiStoreState = {
  isConnected: false,
  hasConnected: false,
  auth: {
    requiresBasicPermissions: false,
    requiresLogin: false,
    userRoles: [],
    userPermissions: [],
    userDetails: {
      name: '',
      email: '',
      id: '',
      picture: ''
    },
    userAssumedRoles: [],
    serverRoles: [],
    serverPermissions: [],
    canLogout: false,
    canAssumeRoles: false,
    bearerToken: null,
    rolePrefix: '',
    hasTokenExpired: false,
  },
}

/**
 * Use a Pinia store to remember connection states. This makes them easy to view in the Vue debugger.
 */
export const useGameServerApiStore = defineStore('game-server-api', {
  state: () => defaultState,
  getters: {
    doesHavePermission: (state) => (permission?: string) => {
      if (!permission) return true
      // Return true if both the server and the user have the permission
      if (state.auth.serverPermissions.map(serverPermission => serverPermission.name).includes(permission) && state.auth.userPermissions.includes(permission)) return true
      else {
        // If the server didn't have the permission, raise an error
        if (!state.auth.serverPermissions.map(serverPermission => serverPermission.name).includes(permission)) {
          // Find the distance between the requested permission and all possible permissions, then sort to find closest.
          const distances: Array<{ name: string, distance: number }> = state.auth.serverPermissions.map((serverPermission) => {
            return {
              name: serverPermission.name,
              distance: distance(permission, serverPermission.name)
            }
          }).sort((a, b) => b.distance - a.distance)

          throw new Error(`Checked for permission '${permission}' but that permission does not exist on the server. Did you mean '${distances[0].name}'?`)
        }
        return false
      }
    },
  }
})
