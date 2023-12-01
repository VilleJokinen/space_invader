// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { type AuthInitResult, AuthProvider, type PermissionDetails } from './authProvider'
import { type AuthConfigJwt, AuthProviderJwt } from './authProviderJwt'
import { type AuthConfigNoAuth, AuthProviderNoAuth } from './authProviderNoAuth'
import { useGameServerApi } from '../gameServerApi'
import { useGameServerApiStore } from '../gameServerApiStore'

/**
 * Authentication is initialized during startup by calling initialize() and passing in auth config.
 * This allows the auth system to select an AuthProvider object based on the auth type that has been
 * configured in the game server. The actual hard work (and authentication platform specific work)
 * of authentication is delegated to this object.
 */

let authProvider: AuthProvider | null = null

/**
 * The contents of the AuthConfig object is specific to each authentication type, but they must all
 * include 'type' as a minimum.
 */
export interface AuthConfig {
  type: string
}

/**
 * Initialize the authentication component.
 */
export async function initialize (authConfig: AuthConfig): Promise<AuthInitResult> {
  const gameServerApiStore = useGameServerApiStore()
  const gameServerApi = useGameServerApi()

  // Select the correct authentication provider.
  switch (authConfig.type) {
    case 'None': authProvider = new AuthProviderNoAuth(authConfig as AuthConfigNoAuth); break
    case 'JWT': authProvider = new AuthProviderJwt(authConfig as AuthConfigJwt); break
  }
  if (authProvider === null) {
    return {
      state: 'error',
      details: `Could not create auth provider for "${authConfig.type}" type`
    }
  }

  // Initialize the auth provider.
  const authInitResult: AuthInitResult = await authProvider.initialize()

  // Auth flow has completed at this point, and we have the result.
  if (authInitResult.state === 'success') {
    // Auth succeeded
    try {
      // Re-assume previous roles if needed.
      const canAssumeRoles = authProvider.getCanAssumeRoles()
      if (canAssumeRoles) {
        const previousAssumedRoles = sessionStorage.getItem('assumedRoles')
        if (previousAssumedRoles) {
          const assumedRoles = JSON.parse(previousAssumedRoles)
          if (assumedRoles?.length !== 0) {
            await assumeRoles(assumedRoles)
          }
        }
      }

      // Add auth headers & token refreshing to every call.
      gameServerApi.interceptors.request.use(
        async req => {
          const token = await authProvider?.getBearerToken()
          if (token) {
            // If token has changed, save it.
            if (token !== gameServerApiStore.auth.bearerToken) gameServerApiStore.auth.bearerToken = token
            if (req.headers) req.headers.Authorization = `Bearer ${token}`
          }
          return req
        },
        async err => {
          return await Promise.reject(err)
        }
      )

      // See what permissions the user has.
      const user = await gameServerApi.get('/authDetails/user')
      gameServerApiStore.auth.userRoles = user.data.roles as string[]
      gameServerApiStore.auth.userPermissions = user.data.permissions as string[]

      // Get user details.
      gameServerApiStore.auth.userDetails = await authProvider.getUserDetails() ?? {
        name: 'Failed to retrieve user name',
        email: 'Failed to retrieve user email',
        id: 'Failed to retrieve user id',
        picture: 'Failed to retrieve user picture',
      }

      // Check that the user has the most basic permissions required to do anything useful.
      if (!user.data.permissions.includes('dashboard.view') || !user.data.permissions.includes('api.general.view')) {
        return {
          state: 'not_enough_permissions',
          details: 'User does not have enough permissions to continue'
        }
      }

      // Fetch list of all available roles and permissions.
      const allPermissionsAndRoles = (await gameServerApi.get('/authDetails/allPermissionsAndRoles')).data
      const allRoles = allPermissionsAndRoles.roles
      const allPermissions: PermissionDetails[] = [].concat(...allPermissionsAndRoles.permissionGroups.map((permissionGroup: any) =>
        permissionGroup.permissions.map((permission: any): PermissionDetails => {
          return {
            group: permissionGroup.title.split(' permissions')[0],
            name: permission.name,
            type: permission.name.split('.')[0],
            description: permission.description,
          }
        })
      ))

      // Store state.
      gameServerApiStore.auth.serverRoles = allRoles
      gameServerApiStore.auth.serverPermissions = allPermissions
      gameServerApiStore.auth.canLogout = authProvider.getCanLogout()
      gameServerApiStore.auth.canAssumeRoles = authProvider.getCanAssumeRoles()
      gameServerApiStore.auth.bearerToken = await authProvider.getBearerToken()
      gameServerApiStore.auth.rolePrefix = authProvider.getRolePrefix()

      // And we're done..
      return {
        state: 'success',
        details: 'Authentication succeeded'
      }
    } catch (err) {
      // Error thrown during initialization.
      return {
        state: 'error',
        details: 'Failed to read user and permission data: ' + err
      }
    }
  } else if (authInitResult.state === 'require_login' || authInitResult.state === 'error') {
    // Auth returned either an error or that login is required.
    return authInitResult
  } else {
    // Auth provider didn't return a proper response.
    return {
      state: 'error',
      details: 'Auth provider failed to initialize properly'
    }
  }
}

/**
 * Cause the user to assume the given roles.
 * @param rolesToAssume
 */
export async function assumeRoles (rolesToAssume: string[] | null) {
  const gameServerApiStore = useGameServerApiStore()
  const gameServerApi = useGameServerApi()

  if (authProvider?.getCanAssumeRoles()) {
    // Set or clear gameServerApi header.
    const customHeaderName = 'Metaplay-AssumedUserRoles'
    if (rolesToAssume) {
      gameServerApi.defaults.headers.common[customHeaderName] = rolesToAssume.toString()
      sessionStorage.setItem('assumedRoles', JSON.stringify(rolesToAssume))
      gameServerApiStore.auth.userAssumedRoles = rolesToAssume
    } else {
      // Note: using delete here instead of setting to undefined, because the latter will still potentially send the header.
      // eslint-disable-next-line @typescript-eslint/no-dynamic-delete
      delete gameServerApi.defaults.headers.common[customHeaderName]
      sessionStorage.removeItem('assumedRoles')
      gameServerApiStore.auth.userAssumedRoles = []
    }

    // Fetch updated user roles and permissions.
    await gameServerApi.get('/authDetails/user').then(result => {
      gameServerApiStore.auth.userRoles = result.data.roles
      gameServerApiStore.auth.userPermissions = result.data.permissions
    })
  } else {
    throw new Error('Assuming roles is disabled')
  }
}

/**
 * Call to initiate the auth providers login flow.
 */
export function login () {
  authProvider?.login()
}

/**
 * Call to initiate the auth providers logout flow.
 */
export function logout () {
  authProvider?.logout()
}
