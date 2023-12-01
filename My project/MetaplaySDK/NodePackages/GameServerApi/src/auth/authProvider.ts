// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * Contains the result of calling the auth initialization function.
 */
export interface AuthInitResult {
  state: 'success' | 'error' | 'not_enough_permissions' | 'require_login'
  details: string
}

/**
 * Details about a Dashboard user.
 */
export interface UserDetails {
  /**
   * Name of the user.
   */
  name: string

  /**
   * Email address that is associated with this user.
   */
  email: string

  /**
   * Auth provider specific ID of the user.
   */
  id: string

  /**
   * User's avatar image.
   */
  picture: string
}

/**
 * Details about a permission.
 */
export interface PermissionDetails {
  /**
   * Name of the permission in dotted format.
   * @example `api.activables.view`
   */
  name: string

  /**
   * Description of the permission.
   */
  description: string

  /**
   * Group that the permission belongs to.
   * @example `Metaplay core game server` or `Game-specific`
   */
  group: string

  /**
   * Type of the permission
   * @example `api` or `dashboard`
   */
  type: string
}

export abstract class AuthProvider {
  /**
   * Role prefix that was added to the start of each role name.
   */
  private readonly rolePrefix: string

  /**
   * Constructor.
   * @param rolePrefix Role prefix that was added to the start of each role name.
   */
  public constructor (rolePrefix: string) {
    this.rolePrefix = rolePrefix
  }

  /**
   * Initialize the provider.
   * @returns Result of initialization.
   */
  abstract initialize (): Promise<AuthInitResult>

  /**
   * Call to initiate the auth providers login flow.
   */
  abstract login (): void

  /**
   * Call to initiate the auth providers logout flow.
   */
  abstract logout (): void

  /**
   * @returns True if the auth provider supports logging out.
   */
  abstract getCanLogout (): boolean

  /**
   * @returns True if the auth provider supports assuming rules.
   */
  abstract getCanAssumeRoles (): boolean

  /**
   * @returns User's current bearer token.
   */
  abstract getBearerToken (): Promise<string | null>

  /**
   * @returns Details of the current user.
   */
  abstract getUserDetails (): Promise<UserDetails | null>

  /**
   * @returns Fixed prefix string added to the front of all role names, can be an empty string.
   */
  public getRolePrefix (): string {
    return this.rolePrefix
  }
}
