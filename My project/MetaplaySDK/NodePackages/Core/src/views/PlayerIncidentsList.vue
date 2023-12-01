<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container
  template(#overview)
    meta-page-header-card
      template(#title) View Player Incident Reports

      p Incident reports are enriched error messages collected from your game clients.
      p.small.text-muted Errors are collected by the Metaplay Unity SDK and sent on the next successful connection. They contain all the technical details a programmer typically needs to start to diagnose the issue at hand.

  core-ui-placement(placementId="PlayerIncidents/List")

</template>

<script lang="ts" setup>
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
</script>
