// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

export {
  assumeRoles,
  initialize as initializeAuth,
  login,
  logout,
} from './auth/auth'

export type {
  PermissionDetails,
  UserDetails,
} from './auth/authProvider'

export {
  useGameServerApi,
  registerErrorVisualizationHandler
} from './gameServerApi'

export { ApiPoller } from './apiPoller'
export { useGameServerApiStore } from './gameServerApiStore'
export { SseHandler } from './sseHandler'
