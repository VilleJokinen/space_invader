// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { type AuthConfig } from './auth'
import { AuthProvider, type AuthInitResult, type UserDetails } from './authProvider'
import { useGameServerApi } from '../gameServerApi'
import { type AxiosInstance } from 'axios'

/**
 * Config options that come from the game server.
 */
export interface AuthConfigJwt extends AuthConfig {
  rolePrefix: string
  bearerToken: string
  logoutUri: string
  userInfoUri: string
}

/**
 * Jwt authentication provider.
 */

export class AuthProviderJwt extends AuthProvider {
  private readonly bearerToken: string
  private readonly userInfoUri: string
  private readonly logoutUri: string
  private userDetails?: UserDetails | null
  private readonly gameServerApi: AxiosInstance

  /**
   * @param authConfig Auth config from the game server.
   */
  public constructor (authConfig: AuthConfigJwt) {
    super(authConfig.rolePrefix)

    this.bearerToken = authConfig.bearerToken
    if (!this.bearerToken) {
      throw new Error('Bearer token expected in authConfig but none found')
    }
    this.logoutUri = authConfig.logoutUri
    this.userInfoUri = authConfig.userInfoUri
    if (!this.userInfoUri) {
      console.error('userInfoUri expected in authConfig but none found')
    }

    this.gameServerApi = useGameServerApi()
  }

  /**
   * Initialize the provider.
   * @returns Result of initialization.
   */
  public async initialize (): Promise<AuthInitResult> {
    // Auth has already succeeded if we got this far.
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
    window.location.replace(this.logoutUri)
  }

  /**
   * @returns True if the auth provider supports logging out.
   */
  public getCanLogout (): boolean {
    return !!this.logoutUri
  }

  /**
   * @returns True if the auth provider supports assuming rules.
   */
  public getCanAssumeRoles (): boolean {
    return false
  }

  /**
   * @returns User's current bearer token.
   */
  public async getBearerToken (): Promise<string | null> {
    return this.bearerToken
  }

  /**
   * @returns Details of the current user.
   */
  public async getUserDetails (): Promise<UserDetails | null> {
    if (this.userDetails === undefined) {
      this.userDetails = await this.fetchUserDetails()
    }
    return this.userDetails
  }

  /**
   * Fetch user details and cache them.
   */
  private async fetchUserDetails (): Promise<UserDetails | null> {
    let fetchedUserDetails: UserDetails | null = null
    if (this.userInfoUri) {
      await this.gameServerApi.get(this.userInfoUri)
        .then((response) => {
          fetchedUserDetails = {
            name: response.data.name || 'No name',
            email: response.data.email || 'No email',
            id: response.data.sub || 'No ID',
            picture: response.data.picture || 'No picture'
          }
        })
        .catch((error) => {
          console.warn(`Failed to get userinfo from '${this.userInfoUri}: ${error.message}`)
        })
    } else {
      console.warn('Failed to get userinfo: No userInfoUri endpoint defined')
    }
    return fetchedUserDetails
  }
}
