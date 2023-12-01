<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div(data-cy="player-admin-actions-card")
  //- Admin actions
  b-card.shadow-sm
    b-card-title
      fa-icon(icon="wrench").mr-2
      span Admin Tools
    b-row
      b-col(md="4").mb-2.mb-md-0.border-right-md
        div.mb-1.font-weight-bold Gentle Actions
        core-ui-placement(
          placementId="Players/Details/AdminActions:Gentle"
          :playerId="playerId"
          always-full-width
          smallBottomMargin
          )

      b-col(md="4").mb-2.mb-md-0.border-right-md
        div.mb-1.font-weight-bold Disruptive Actions
        core-ui-placement(
          placementId="Players/Details/AdminActions:Disruptive"
          :playerId="playerId"
          always-full-width
          smallBottomMargin
          )

      b-col(md="4").mb-2.mb-md-0
        div.mb-1.font-weight-bold Dangerous Actions
        core-ui-placement(
          placementId="Players/Details/AdminActions:Dangerous"
          :playerId="playerId"
          always-full-width
          smallBottomMargin
          )
</template>

<script lang="ts" setup>
import CoreUiPlacement from '../system/CoreUiPlacement.vue'

defineProps<{
  /**
   * ID of the player to show admin actions for.
   */
  playerId: string
}>()
</script>
