<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
b-card#guild-admin-actions-card.shadow-sm
  b-card-title
      fa-icon(icon="wrench").mr-2
      span Admin Tools
  b-row
    b-col(md).border-right-md
      div.font-weight-bold Gentle Actions
      core-ui-placement(
        placementId="Guilds/Details/AdminActions:Gentle"
        :guildId="guildId"
        alwaysFullWidth
        smallBottomMargin
      )
    b-col(md)
      div.font-weight-bold Disruptive Actions
      core-ui-placement(
        placementId="Guilds/Details/AdminActions:Disruptive"
        :guildId="guildId"
        alwaysFullWidth
        smallBottomMargin
        )
</template>

<script lang="ts" setup>
import CoreUiPlacement from '../../components/system/CoreUiPlacement.vue'

defineProps<{
  guildId: string
}>()
</script>
