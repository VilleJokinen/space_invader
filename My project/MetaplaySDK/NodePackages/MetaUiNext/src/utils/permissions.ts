// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import { shallowRef } from 'vue'

/**
 * A reactive reference to the permissions the user is aware of and wether they have it.
 */
const allPermissions = shallowRef<string[]>()

/**
 * Set the permissions for this user to be used by the `doesHavePermission` function.
 * @param permissions A dictionary of permissions the user should be aware of and if they currently have it.
 */
export function setPermissions (permissions: string[]) {
  allPermissions.value = permissions
}

/**
 * Checks if the user has a given permission.
 * @param permission The permission to check.
 */
export function doesHavePermission (permission: string | undefined) {
  if (!permission) return true
  else if (allPermissions.value === undefined) {
    throw new Error(`Trying to check permission ${permission} before permissions have been set. Call the setPermissions function before using doesHavePermission.`)
  } else {
    return allPermissions.value.includes(permission)
  }
}

/**
 * A composable to manage permissions.
 */
export function usePermissions () {
  return {
    allPermissions,
    setPermissions,
    doesHavePermission,
  }
}
