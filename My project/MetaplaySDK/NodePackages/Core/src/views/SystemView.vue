<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container
  //- TODO: consider adding an overview card to this page. What would be in the description?
  template(#overview)
    meta-page-header-card(title="Manage Deployment")
      p Advanced features to help manage deployment settings.

  template(#default)
    core-ui-placement(placementId="System/Details")

</template>

<script lang="ts" setup>
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'
</script>
