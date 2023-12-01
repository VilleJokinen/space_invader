<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container
  template(#overview)
    b-card.shadow-sm.mb-3(data-cy="user-overview")
      b-row(align-h="center")
        b-avatar(:src="gameServerApiStore.auth.userDetails.picture" variant="light" size="7em")

      MList(showBorder)
        MListItem(class="tw-px-5") Name
          template(#top-right) {{ gameServerApiStore.auth.userDetails.name }}
        MListItem(class="tw-px-5") Email
          template(#top-right) {{ gameServerApiStore.auth.userDetails.email }}
        MListItem(class="tw-px-5") ID
          template(#top-right) {{ gameServerApiStore.auth.userDetails.id }}
        MListItem(class="tw-px-5") {{ roleLabel }}
          template(#top-right)
            span.text-right(v-if="allLocalUserRolesSortedMinusPrefix.length > 0")
              div(v-for="role in allLocalUserRolesSortedMinusPrefix" :key="role") {{ role }}
            span(v-else)
              span.text-warning None 😢
        MListItem(class="tw-px-5")
          template(#default)
            div(style="padding-top: 0.1rem").font-weight-bold.mr-2 #[fa-icon(:icon="uiStore.isSafetyLockOn ? 'lock' : 'lock-open'" style="width: 1rem")] Engage Safety Locks by Default
          template(#top-right)
            meta-input-checkbox(v-model="uiStore.isSafetyLockOn" size="lg" name="safety-toggle" showAs="switch" @change="uiStore.toggleSafetyLock")
        MListItem(class="tw-px-5") Show Developer UI
          MBadge(tooltip="Adds advanced technical information onto most pages." shape="pill").ml-1 ?
          template(#top-right)
            meta-auth-tooltip(permission="dashboard.developer_mode")
              meta-input-checkbox(v-model="uiStore.showDeveloperUi" name="dev-toggle" size="lg" showAs="switch" :disabled="!gameServerApiStore.doesHavePermission('dashboard.developer_mode')" @change="uiStore.toggleDeveloperUi")

  b-row(align-h="center")
    b-col(md="6")
      meta-list-card(
        data-cy="user-permissions-card"
        title="Permissions"
        description="These are the combined permissions granted by all your roles."
        emptyMessage="You have no permissions on this user account!"
        :searchFields="['permissionDetails.group', 'permissionDetails.name', 'permissionDetails.description']"
        :itemList="decoratedPermissions"
        :getItemKey="getPermissionsItemKey"
        :sortOptions="permissionListSortOptions",
        :defaultSortOption="1"
        :filterSets="permissionListFilterSets"
      )
        template(#item-card="slot")
          MListItem
            MBadge(:variant="slot.item.hasPermission ? 'success' : 'neutral'") {{ slot.item.permissionDetails.name }}
            span(v-if="!slot.item.hasPermission").ml-1.text-muted.small.font-italic You do not have this permission.
            template(#bottom-left) {{ slot.item.permissionDetails.description }}
            template(#top-right) {{ slot.item.permissionDetails.group }}

    b-col(md="6")
      b-card.shadow-sm.mb-3(data-cy="assume-role-card" :style="canAssumeRoles ? '' : 'bg-light'")
        b-card-title Assume Roles
        //- TODO: Fix this default line height BS.
        div(v-if="canAssumeRoles" style="line-height: 1.15").mb-3
          span.small This is an advanced developer feature for quickly testing different roles.

          b-form-group(v-slot="{ ariaDescribedby }").my-3
            b-form-checkbox-group(
              data-cy="roles-to-assume"
              v-model="rolesToAssume"
              :options="allRolesSorted"
              stacked
              )

        div(v-else).text-center.text-muted.mt-4.font-italic Assuming roles is disabled in this environment.

  meta-raw-data(:kvPair="gameServerApiStore.auth.userDetails" name="userDetails")
  meta-raw-data(:kvPair="coreStore.hello.authConfig" name="authConfig")
</template>

<script lang="ts" setup>
import { computed, ref, watch } from 'vue'

import { MBadge, MList, MListItem } from '@metaplay/meta-ui-next'

import { assumeRoles, useGameServerApiStore } from '@metaplay/game-server-api'
import type { PermissionDetails } from '@metaplay/game-server-api'
import { useCoreStore } from '../coreStore'
import { useUiStore, maybePlural, MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'

interface DecoratedPermission {
  permissionDetails: PermissionDetails
  hasPermission: boolean
}

const coreStore = useCoreStore()
const uiStore = useUiStore()
const gameServerApiStore = useGameServerApiStore()

// Take all possible permissions and decorate them with extra info.
const decoratedPermissions = computed<DecoratedPermission[]>(() => {
  return gameServerApiStore.auth.serverPermissions.map(permissionDetails => {
    return {
      permissionDetails,
      hasPermission: gameServerApiStore.doesHavePermission(permissionDetails.name),
    }
  })
})

// MetaListCard sort options for the permissions list.
const permissionListSortOptions = [
  MetaListSortOption.asUnsorted(),
  new MetaListSortOption('Name', 'permissionDetails.name', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'permissionDetails.name', MetaListSortDirection.Descending),
  new MetaListSortOption('Group', 'permissionDetails.group', MetaListSortDirection.Ascending),
  new MetaListSortOption('Group', 'permissionDetails.group', MetaListSortDirection.Descending),
  new MetaListSortOption('Type', 'permissionDetails.type', MetaListSortDirection.Ascending),
  new MetaListSortOption('Type', 'permissionDetails.type', MetaListSortDirection.Descending),
]

// MetaListCard filter sets for the permissions list.
const permissionListFilterSets = computed(() => {
  return [
    new MetaListFilterSet('hasPermission',
      [
        new MetaListFilterOption('Has permission', (x) => (x as DecoratedPermission).hasPermission, true),
        new MetaListFilterOption('Does not have permission', (x) => !(x as DecoratedPermission).hasPermission),
      ]
    ),
    new MetaListFilterSet('group',
      decoratedPermissions.value // Start with all possible permissions.
        .map((x) => x.permissionDetails.group) // Get the group of each permission.
        .filter((value, index, self) => self.indexOf(value) === index) // Filter to get unique values.
        .map((group) => new MetaListFilterOption(group, (y) => (y as DecoratedPermission).permissionDetails.group === group)) // Create option.
    ),
    new MetaListFilterSet('type',
      decoratedPermissions.value // Start with all possible permissions.
        .map((x) => x.permissionDetails.type) // Get the type of each permission.
        .filter((value, index, self) => self.indexOf(value) === index) // Filter to get unique values.
        .map((type) => new MetaListFilterOption(type, (y) => (y as DecoratedPermission).permissionDetails.type === type)) // Create option.
    ),
  ]
})

function getPermissionsItemKey (permissionItem: any): string {
  return (permissionItem as DecoratedPermission).permissionDetails.name
}

// Information about user roles.
const allRolesSorted = [...gameServerApiStore.auth.serverRoles].sort()
const allUserRolesSorted = computed(() => [...gameServerApiStore.auth.userRoles].sort())
const allLocalUserRolesSortedMinusPrefix = computed(() => {
  const rolePrefix = gameServerApiStore.auth.rolePrefix
  return allUserRolesSorted.value
    .filter(role => role.startsWith(rolePrefix))
    .map(role => role.slice(rolePrefix.length))
})

// The role label is complex. It needs to show single and plural role counts, and needs to consider whether the
// user has assumed roles or not.
const roleLabel = computed(() => {
  const baseLabel = gameServerApiStore.auth.userAssumedRoles.length > 0 ? 'Assumed Role' : 'Role'
  return maybePlural(allUserRolesSorted.value.length, baseLabel, false)
})

// Assumed roles.
const canAssumeRoles = gameServerApiStore.auth.canAssumeRoles
const rolesToAssume = ref(gameServerApiStore.auth.userAssumedRoles)
watch(rolesToAssume, async (newVal) => {
  await assumeRoles(newVal)
})
</script>
