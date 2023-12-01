<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!allActivePlayersData"
  :meta-api-error="allActivePlayersError"
  )
  //- Search
  b-row(
    class="justify-content-center"
    style="margin-top: 8rem; margin-bottom: 7rem;"
    )
    b-col(md="10" lg="7" xl="5")
      h4.ml-2(data-cy="player-search") #[fa-icon(icon="search")] Find a player
      meta-input-player-select(
        :value="selectedPlayer"
        @input="onPlayerSelected"
        )

  //- Active players list. Shown only when developer flag is on.
  div(v-if="!allActivePlayersData && uiStore.showDeveloperUi").w-100.text-center.pt-5
    b-spinner.mt-5(label="Loading...")/

  div(v-else-if="uiStore.showDeveloperUi")
    b-row.justify-content-center
      b-col(lg="10")
        b-card.shadow-sm(data-cy="active-players-list")
          b-card-title
            span.ml-2 Recently Active Players

          b-alert(show v-if="allActivePlayersData.length == 0" variant="secondary") No active players since last server reboot.

          b-table.table-fixed-column(v-else small striped hover responsive :items="allActivePlayersData" :fields="recentlyActiveTableFields" primary-key="entityId" sort-by="startAt" sort-desc :tbody-tr-class="rowClass" @row-clicked="rowClicked")
            template(#cell(entityId)="data")
              MTooltip(v-if="data.item.deletionStatus === 'Deleted'" content="Player has been deleted").mr-1 ☠️
              span {{ data.item.entityId }}

            template(#cell(displayName)="data")
              span {{ data.item.displayName }}
              MTooltip(v-if="data.item.totalIapSpend > 0" :content="'Total IAP spend: $' + data.item.totalIapSpend.toFixed(2, 2)" noUnderline).ml-2: fa-icon(icon="money-check-alt").text-muted
              MTooltip(v-if="data.item.isDeveloper" content="This player is a developer." noUnderline).ml-2: fa-icon(icon="user-astronaut" size="sm").text-muted

            template(#cell(createdAt)="data")
              meta-time(:date="data.item.createdAt")

            template(#cell(lastLoginAt)="data")
              meta-time(:date="data.item.lastLoginAt")

  meta-raw-data(:kvPair="allActivePlayersData" name="activePlayers")
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import type { PlayerSelectResult } from '@metaplay/meta-ui/src/additionalTypes'
import { MTooltip } from '@metaplay/meta-ui-next'
import { useUiStore } from '@metaplay/meta-ui/src/uiStore'
import { useSubscription } from '@metaplay/subscriptions'

import { getAllActivePlayersSubscriptionOptions } from '../subscription_options/players'

const uiStore = useUiStore()

const {
  data: allActivePlayersData,
  error: allActivePlayersError
} = useSubscription(getAllActivePlayersSubscriptionOptions())

const recentlyActiveTableFields = computed(() => {
  const allFields = [
    {
      key: 'entityId',
      label: 'ID'
    },
    {
      key: 'displayName',
      label: 'Name'
    },
    'level',
    {
      key: 'createdAt',
      label: 'Joined'
    }
  ]
  if (Math.max(document.documentElement.clientWidth || 0, window.innerWidth || 0) < 576) {
    return filterTableFields(allFields, ['entityId', 'displayName'])
  } else {
    return allFields
  }
})

function filterTableFields (allFields: any, desiredFields: any): any {
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

const selectedPlayer = ref<any>()

async function onPlayerSelected (player: PlayerSelectResult): Promise<void> {
  selectedPlayer.value = player
  await router.push(`/players/${player.id}`)
}

async function rowClicked (item: any): Promise<void> {
  await router.push(`/players/${item.entityId}`)
}

function rowClass (item: any, type: any): string {
  if (!item || type !== 'row') {
    return ''
  }
  if (item.deserializedSuccessfully === false) { // \note Don't match here if there's no deserializedSuccessfully field (like in 'active players' items)
    return 'text-danger table-row-link'
  } if (item.deletionStatus === 'Deleted') {
    return 'text-danger table-row-link'
  } else {
    return 'table-row-link'
  }
}
</script>
