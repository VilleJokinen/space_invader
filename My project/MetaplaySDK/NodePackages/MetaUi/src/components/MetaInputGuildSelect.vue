<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">

//- TODO:
  - Infinite scroll?
  - Nicer "no results" message?

meta-input-select(
  :value="value"
  @input="$emit('input', $event)"
  :placeholder="`Search for a guild...`"
  :options="search"
  no-deselect
  no-clear
  data-cy="input-guild-select"
  )
  template(#option="{ option }")
    MListItem(v-if="option?.phase == 'Closed'" class="!tw-py-0").text-danger
      | ☠️ {{ option.displayName }}
      template(#top-right) {{ option.entityId }}
      template(#bottom-left) Guild deleted
      template(#bottom-right) Created #[meta-time(:date="option.createdAt" disableTooltip)]

    //- Note: Doing the v-else-if here to hint to the compiler that option is not null.
    MListItem(v-else-if="option" class="!tw-py-0") {{ option.displayName }}
      template(#top-right) {{ option.entityId }}
      template(#bottom-left) Last login #[meta-time(:date="option.lastLoginAt" disableTooltip)]
      template(#bottom-right) Created #[meta-time(:date="option.createdAt" disableTooltip)]
</template>

<script lang="ts" setup>
import { useGameServerApi } from '@metaplay/game-server-api'
import { MListItem } from '@metaplay/meta-ui-next'
import type { MetaInputSelectOption, GuildSearchResult } from '../additionalTypes'

defineProps<{
  /**
   * Optional: The currently selected entity.
   */
  value?: GuildSearchResult
}>()

defineEmits(['input'])

const gameServerApi = useGameServerApi()

async function search (query?: string): Promise<Array<MetaInputSelectOption<GuildSearchResult>>> {
  const res = await gameServerApi.get(`/guilds/?query=${encodeURIComponent(query ?? '')}`)
  return res.data
    .map((entity: GuildSearchResult): MetaInputSelectOption<GuildSearchResult> => ({
      id: entity.entityId,
      value: entity
    }))
}

</script>
