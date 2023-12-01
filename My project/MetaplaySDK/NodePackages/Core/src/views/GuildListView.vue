<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!activeGuildsData"
  :meta-api-error="activeGuildsError"
  )
  //- Search
  b-row(
    class="justify-content-center"
    style="margin-top: 8rem; margin-bottom: 7rem;"
    )
    b-col(md="10" lg="7" xl="5")
      h4.ml-2 #[fa-icon(icon="search")] Find a guild
      meta-input-guild-select(
        :value="selectedGuild"
        @input="onGuildSelected"
        )

  //- Active guilds list.
  div(v-if="!activeGuildsData && uiStore.showDeveloperUi").w-100.text-center.pt-5
    b-spinner.mt-5(label="Loading...")/

  div(v-else-if="uiStore.showDeveloperUi")
    b-row.justify-content-center
      b-col(md="10")
        b-card(data-cy="recently-active").shadow-sm
          b-card-title
            span.ml-2 Recently Active Guilds

          b-alert(show v-if="activeGuildsData.length === 0" variant="secondary") No active guilds since last server reboot.

          b-table.table-fixed-column(v-else small striped hover responsive :items="activeGuildsData" :fields="tableFields" primary-key="entityId" sort-by="startAt" sort-desc :tbody-tr-class="rowClass" @row-clicked="rowClicked")
            template(#cell(entityId)="data")
              span {{ data.item.entityId }}

            template(#cell(displayName)="data")
              span(v-if="data.item.phase === 'Closed'") ☠️
              span(v-else) {{ data.item.displayName }}

            template(#cell(numMembers)="data")
              span {{ data.item.numMembers }}

            template(#cell(lastLoginAt)="data")
              meta-time(v-if="!isEpochTime(data.item.lastLoginAt)" :date="data.item.lastLoginAt" disableTooltip showAs="timeagoSentenceCase")
              span(v-else) Closed

  meta-raw-data(:kvPair="activeGuildsData" name="activeGuilds")
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import type { GuildSearchResult } from '@metaplay/meta-ui/src/additionalTypes'
import { useUiStore } from '@metaplay/meta-ui/src/uiStore'
import { useSubscription } from '@metaplay/subscriptions'

import { isEpochTime } from '../coreUtils'
import { getActiveGuildsSubscriptionOptions } from '../subscription_options/guilds'

const uiStore = useUiStore()

const {
  data: activeGuildsData,
  error: activeGuildsError
} = useSubscription(getActiveGuildsSubscriptionOptions())

const tableFields = computed(() => {
  const allFields = [
    {
      key: 'entityId',
      label: 'ID'
    },
    {
      key: 'displayName',
      label: 'Name'
    },
    {
      key: 'numMembers',
      label: 'Members'
    },
    {
      key: 'lastLoginAt',
      label: 'Last Active'
    }
  ]
  if (Math.max(document.documentElement.clientWidth || 0, window.innerWidth || 0) < 576) {
    return filterTableFields(allFields, ['entityId', 'displayName'])
  } else {
    return allFields
  }
})

function filterTableFields (allFields: any, desiredFields: any) {
  return allFields.filter((element: any) => {
    let key
    if (typeof element === 'string') {
      key = element
    } else {
      key = element.key
    }
    return desiredFields.indexOf(key) !== -1
  })
}

const router = useRouter()

const selectedGuild = ref<GuildSearchResult>()

async function onGuildSelected (guild: GuildSearchResult) {
  selectedGuild.value = guild
  await router.push(`/guilds/${guild.entityId}`)
}

async function rowClicked (item: any) {
  await router.push(`/guilds/${item.entityId}`)
}

function rowClass (item: any, type: string) {
  if (!item || type !== 'row') {
    return
  }
  if (item.phase === 'Closed') {
    return 'text-danger table-row-link'
  } else {
    return 'table-row-link'
  }
}
</script>
