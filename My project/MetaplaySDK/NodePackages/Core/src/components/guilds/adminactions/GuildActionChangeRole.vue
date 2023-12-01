<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-action-modal-button(
  id="action-change-role"
  action-button-text="Change a Role"
  modal-title="Change a Guild Member's Role"
  :onOk="changeRole"
  @show="resetModal"
  :ok-button-disabled="!roleChanges"
  ok-button-text="Change Role"
  permission="api.guilds.edit_roles"
  block
  )
  div.mb-1.font-weight-bold 1. Select a Guild Member
  meta-input-select(
    class="mb-3"
    :value="chosenPlayer"
    @input="chosenPlayer = $event"
    :options="playerList"
    placeholder="Select a player..."
    :searchFields="['displayName', 'playerId', 'roleId']"
    )
    template(#option="{ option }")
      MListItem(class="!tw-py-0")
        span(class="tw-text-xs+") {{ option?.displayName }} - {{ option?.roleId }}
        template(#top-right) {{ option?.playerId }}

  div.mb-1(:class="{'text-muted': !chosenPlayer}").font-weight-bold 2. Select a New Role
  meta-input-select(
    class="mb-3"
    :value="chosenRole"
    @input="chosenRole = $event"
    :options="roleList"
    :placeholder="!chosenPlayer ? 'Select a guild member first' : 'Select a role...'"
    :searchFields="['displayName', 'id']"
    :disabled="!chosenPlayer"
    )
    template(#option="{ option }")
      div {{ option?.displayName }}

  div.mb-1(:class="{'text-muted': !chosenPlayer || !chosenRole}").font-weight-bold.mb-3 3. Preview Role Changes
  div(v-if="roleChangePreviewLoading")
    b-row.justify-content-center.mt-5
      b-spinner.mt-4(label="Loading...")/

  div(v-else-if="!roleChangePreview").text-muted.font-italic.text-center.pt-3.mb-3.small Choose a player and a role to preview the results.

  meta-alert(
    v-else-if="roleChangePreview.length == 0"
    variant="warning"
    title="No Changes to Roles"
    message="This change is no-op, invalid, or the resulting guild state would break the role invariants. Contact Metaplay if you think this is a bug!"
    )

  MList(v-else showBorder)
    MListItem(v-for="change in roleChangePreview"
      :key="change.displayName"
      class="tw-px-5"
      )
      span #[fa-icon(icon="user")] {{ change.displayName || 'n/a' }}
      template(#top-right): b-link(:to="`/players/${change.playerId}`") View player
      template(#bottom-left) Role will be changed from #[MBadge(:variant="getRoleVariant(change.oldRole)") {{ guildRoleDisplayString(change.oldRole) }}] to #[MBadge(:variant="getRoleVariant(change.newRole)") {{ guildRoleDisplayString(change.newRole) }}]
</template>

<script lang="ts" setup>
import { computed, ref, watch } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import type { MetaInputSelectOption } from '@metaplay/meta-ui'
import { MBadge, MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getSingleGuildSubscriptionOptions } from '../../../subscription_options/guilds'
import { guildRoleDisplayString } from '../../../coreUtils'

const props = defineProps<{
  guildId: string
}>()

const gameServerApi = useGameServerApi()

/**
 * Subscribe to guild data.
 */
const {
  data: guildData,
  refresh: guildRefresh,
} = useSubscription(getSingleGuildSubscriptionOptions(props.guildId))

interface PlayerInfo {
  playerId: string
  displayName: string
  roleId: string
}

interface RoleInfo {
  id: string
  displayName: string
}

/**
 * List of players to be displayed as options on the multiselect dropdown.
 */
const playerList = ref<Array<MetaInputSelectOption<PlayerInfo>>>([])

/**
 * The selected player.
 */
const chosenPlayer = ref<PlayerInfo>()

/**
 * List of roles to be displayed as options on the multiselect dropdown.
 */
const roleList = computed((): Array<MetaInputSelectOption<RoleInfo>> => {
  return ['Leader', 'MiddleTier', 'LowTier'].map((id): MetaInputSelectOption<RoleInfo> => {
    return {
      id,
      value: {
        id,
        displayName: guildRoleDisplayString(id),
      },
      disabled: chosenPlayer.value?.roleId === id
    }
  })
})

/**
 * The selected role.
 */
const chosenRole = ref<RoleInfo>()

const roleChanges = ref<any>()
const roleChangePreview = ref<any>()
const roleChangePreviewLoading = ref(false)

/**
 * Reset the modal.
 */
function resetModal () {
  // Reset.
  chosenPlayer.value = undefined
  chosenRole.value = undefined
  roleChanges.value = null
  roleChangePreview.value = null

  // Update the available players list as the guild data changes and can't be hot-loaded.
  const newPlayerList: Array<MetaInputSelectOption<PlayerInfo>> = []
  for (const playerId in guildData.value.model.members) {
    const member = guildData.value.model.members[playerId]
    newPlayerList.push({
      id: playerId,
      value: {
        playerId,
        displayName: member.displayName,
        roleId: guildRoleDisplayString(member.role),
      }
    })
  }
  playerList.value = newPlayerList

  // Preselect the remaining member if only one left.
  if (playerList.value.length === 1) {
    chosenPlayer.value = playerList.value[0].value
  }
}

watch(chosenPlayer, validateEdit)
watch(chosenRole, validateEdit)

/**
 * Checks that the selected role change is valid for the selected player.
 * Returns the role change preview that is displayed.
 */
async function validateEdit () {
  roleChangePreview.value = null
  roleChanges.value = null
  // If all fields are selected...
  if (chosenPlayer.value && chosenRole.value) {
    // Preview.
    roleChangePreviewLoading.value = true
    const reply = await gameServerApi.post(`/guilds/${guildData.value.id}/validateEditRole`, { playerId: chosenPlayer.value.playerId, role: chosenRole.value.id })
    roleChangePreview.value = []
    Object.entries(reply.data).forEach(playerIdRole => {
      const [playerId, newRole] = playerIdRole
      roleChangePreview.value.push({
        playerId,
        newRole,
        displayName: guildData.value.model.members[playerId].displayName || 'n/a',
        oldRole: guildData.value.model.members[playerId].role
      })

      // Move selected player to the top.
      const i = roleChangePreview.value.findIndex((o: any) => o.playerId === chosenPlayer.value?.playerId)
      if (i > 0) {
        const p = roleChangePreview.value[i]
        roleChangePreview.value.splice(i, 1)
        roleChangePreview.value.unshift(p)
      }
    })
    if (Object.keys(reply.data).length > 0) {
      roleChanges.value = reply.data
    }
    roleChangePreviewLoading.value = false
  } else if (!chosenPlayer.value) {
    // Deselect role when deselecting player.
    chosenRole.value = undefined
  }
}

/**
 * Update's the chosen player(s) new role(s) on the game server.
 * When the 'Leader' role is changed a new 'Leader' is nominated the affected players' roles will be updated.
 */
async function changeRole () {
  await gameServerApi.post(`/guilds/${guildData.value.id}/editRole`, { playerId: chosenPlayer.value?.playerId, role: chosenRole.value?.id, expectedChanges: roleChanges.value })
  showSuccessToast(`${chosenPlayer.value?.displayName} is now a ${chosenRole.value?.displayName}.`)
  guildRefresh()
}

/**
 * Selects the color variant to use when rendering the role badge.
 * @param role Player's role in the guild.
 */
function getRoleVariant (role: string) {
  if (role === 'Leader') return 'primary'
  else return 'neutral'
}
</script>
