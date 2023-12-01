// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { type AuthConfig } from './auth'
import { type AuthInitResult, AuthProvider, type UserDetails } from './authProvider'

/**
 * Config options that come from the game server.
 */
export interface AuthConfigNoAuth extends AuthConfig {
}

/**
 * Simple "no auth" authentication provider. Used when authentication is disabled on the game server.
 */

export class AuthProviderNoAuth extends AuthProvider {
  /**
   * @param authConfig Auth config from the game server.
   */
  public constructor (authConfig: AuthConfigNoAuth) {
    super('')
  }

  /**
   * Initialize the provider.
   * @returns Result of initialization.
   */
  public async initialize (): Promise<AuthInitResult> {
    // Always immediately succeeds.
    return {
      state: 'success',
      details: ''
    }
  }

  /**
   * Call to initiate the auth providers login flow.
   */
  public login (): void {
    // There is no login flow for this provider.
    throw new Error('No login possible')
  }

  /**
   * Call to initiate the auth providers logout flow.
   */
  public logout (): void {
    // There is no logout flow for this provider.
    throw new Error('No logout possible')
  }

  /**
   * @returns True if the auth provider supports logging out.
   */
  public getCanLogout (): boolean {
    // This provider does not support logging out.
    return false
  }

  /**
   * @returns True if the auth provider supports assuming rules.
   */
  public getCanAssumeRoles (): boolean {
    return true
  }

  /**
   * @returns User's current bearer token.
   */
  public async getBearerToken (): Promise<string | null> {
    // No bearer token for the provider.
    return null
  }

  /**
   * @returns Details of the current user.
   */
  public async getUserDetails (): Promise<UserDetails | null> {
    // No real users here, so these results are hardcoded defaults.
    return {
      name: 'Anonymous Moomin',
      email: 'None',
      id: 'Anomoomin',
      picture: '' // TODO REPLACE WITH SOMETHING ELSE
    }
  }
}
