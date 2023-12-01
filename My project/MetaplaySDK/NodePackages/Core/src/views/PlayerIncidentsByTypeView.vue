<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container
  template(#overview)
    meta-page-header-card
      template(#title) Player Incidents By Type
      p.small.text-muted View a list of all unique errors collected from your game clients within the last 24 hours.

  core-ui-placement(placementId="PlayerIncidents/ByType" :fingerprint="route.params.fingerprint")

</template>

<script lang="ts" setup>
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
import { useRoute } from 'vue-router'

const route = useRoute()
</script>
