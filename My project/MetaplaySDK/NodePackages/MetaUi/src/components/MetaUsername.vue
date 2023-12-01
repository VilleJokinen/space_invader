<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!--
A small component to display dashboard usernames. The result is a clickable link that takes you to the audit log page.
-->

<template lang="pug">
meta-button(
  :to="auditLogSearchLink"
  :variant="isOwnUsername ? 'success' : undefined"
  permission="api.audit_logs.search"
  link
  ) {{ humanizeUsername(username) }}
</template>

<script lang="ts" setup>
import { humanizeUsername } from '../utils'
import { useGameServerApiStore } from '@metaplay/game-server-api'
import { computed } from 'vue'

const props = defineProps<{
  /**
   * The username to render.
   */
  username: string
}>()

/**
 * Does the supplied user name match with the currently authenticated user name?
 */
const isOwnUsername = computed(() => {
  return props.username === useGameServerApiStore().auth.userDetails.email
})

/**
 * Create a link to the audit logs out of the username.
 */
const auditLogSearchLink = computed(() => {
  return `/auditLogs?sourceId=${props.username}`
})
</script>
