<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!allLocalizationData"
  :meta-api-error="allLocalizationError"
  )
  template(#overview)
    meta-page-header-card
      template(#title) View Localizations
      p Localizations are a way to deliver localized text content to your players.
      div.small.text-muted You can upload new localization builds and review them before publishing. Published localizations will be delivered over-the-air and do not require players to update their clients.

  template(#center-column)
    core-ui-placement(placementId="Localization/List")
</template>

<script lang="ts" setup>
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'

import { useSubscription } from '@metaplay/subscriptions'
import { getAllLocalizationsSubscriptionOptions } from '../subscription_options/localization'
import type { LocalizationInfo } from '../localizationServerTypes'

const {
  data: allLocalizationData,
  error: allLocalizationError
} = useSubscription<LocalizationInfo[]>(getAllLocalizationsSubscriptionOptions())
</script>
