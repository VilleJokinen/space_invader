<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A utility component to create consistent looking pages.

# Page structure
If error: show error card with error-card-message and error card-body slot
If loading: show spinner
Else:
  1. Alerts slot
  2. Overview card slot
  3. Default slot
-->

<template lang="pug">
//- No permission.
b-container(v-if="permission && !gameServerApiStore.doesHavePermission(permission)").py-5
  b-row(align-h="center")
    b-col(lg="8" xl="7")
      meta-alert(
        title="Missing Permission"
        :message="`You need the '${permission}' permission to access this page.`"
        variant="danger"
        )

//- Loading errors.
b-container(v-else-if="showErrorCard || metaApiError").pt-5
  b-row(align-h="center")
    b-col(lg="8" xl="7")
      b-card(title="Oops!").shadow
        //- Slot for custom error message.
        slot(name="error-card-message")
          p Oh no, something went wrong while trying to load the page!

        //- If available, pretty print an API error.
        MErrorCallout(
          v-if="metaApiError"
          :error="metaApiError"
          )
        //- Additional slot to be displayed below the Error Callout. E.g Display aditional buttons or links.
        slot(name="error-card-body")

//- Loading spinner.
b-container(v-else-if="isLoading").py-5
  b-row(align-h="center")
    b-col(lg="8" xl="7")
      meta-page-header-card
        template(#title)
          div Loading...
        b-skeleton(width="85%")
        b-skeleton(width="55%")
        b-skeleton(width="70%")
        b-skeleton(width="65%")
        b-skeleton(width="80%")

//- Page content
div(
  v-else
  :class="[backgroundClass, 'tw-min-h-full']"
  )
  component(
    :is="fluid ? 'div' : 'b-container'"
    :class="[{ 'pb-5': !noBottomPadding }, 'pt-4']"
    )
      //- Page alerts.
      component(:is="!fluid ? 'div' : 'b-container'")
        b-row(v-show="alerts || $slots.alerts").justify-content-center
          b-col(lg="8" xl="7")
            //- Programmatic alerts.
            meta-alert(
              v-for="alert in alerts"
              :key="alert.key ?? alert.title"
              :title="alert.title"
              :message="alert.message"
              :data-cy="alert.dataCy"
              :variant="alert.variant"
            )

            //- Slot for manual alerts.
            slot(name="alerts")

      //- Overview card.
      component(v-if="$slots.overview" :is="!fluid ? 'div' : 'b-container'").mb-4
        b-row(align-h="center")
          b-col(lg="8" xl="7").mb-4
            slot(name="overview")

      //- Single centered column for pages with only one list.
      b-row(v-if="$slots['center-column']" align-h="center")
        b-col(md="10" xl="9").mb-3
          slot(name="center-column")

      //- Body.
      div
        slot
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import { useGameServerApiStore } from '@metaplay/game-server-api'

import { type MetaPageContainerAlert } from '../additionalTypes'
import { DisplayError, MErrorCallout } from '@metaplay/meta-ui-next'

const props = withDefaults(defineProps<{
  /**
   * Optional: Background stripes of the selected color for the page.
   * Defaults to the most severe alert variant passed to `alerts` or undefined if there are no alerts.
   * @example 'warning'
   */
  variant?: 'default' | 'primary' | 'secondary' | 'info' | 'warning' | 'danger'
  /**
   * Optional: Skip adding padding to the bottom of the page.
   */
  noBottomPadding?: boolean
  /**
   * Optional: Make page body full width. Good for pages with very wide content.
   */
  fluid?: boolean
  /**
   * Optional: Array of alerts to show on the top of the page.
   * Also automatically sets the default background variant for the page.
   * @example [{
   *  title: 'Example Warning',
   *  message: 'Your mood has cooled down. Consider playing a trance anthem to get back into the zone.'
   * }]
   */
  alerts?: MetaPageContainerAlert[]
  /**
   * Optional: Show a loading indicator.
   */
  isLoading?: boolean
  /**
   * Optional: Show a pre-formatted error card. Use the error-card-body slot to show a useful and actionable error message.
   */
  showErrorCard?: boolean
  /**
   * Optional: Show an error originating from the game server API. You can directly pass in the `error` property from subscriptions.
   */
  metaApiError?: Error | DisplayError
  /**
   * Optional: Show an error if the user does not have this permission.
   */
  permission?: string
}>(), {
  variant: 'default',
  alerts: undefined,
  permission: undefined,
  metaApiError: undefined,
})

const gameServerApiStore = useGameServerApiStore()

/**
 * Background stripes of the selected color for the page.
 */
const backgroundClass = computed(() => {
  if (props.variant === 'primary') return 'primaryStripes'
  else if (props.variant === 'secondary') return 'secondaryStripes'
  else if (props.variant === 'info') return 'infoStripes'
  else if (props.variant === 'warning') return 'warningStripes'
  else if (props.variant === 'danger') return 'dangerStripes'
  else if (props.alerts?.some((alert) => alert.variant === 'danger')) return 'dangerStripes'
  else if (props.alerts?.some((alert) => alert.variant === 'warning')) return 'warningStripes'
  else if (props.alerts?.some((alert) => alert.variant === 'secondary')) return 'secondaryStripes'
  else return undefined
})
</script>
