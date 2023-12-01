<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A base component to create consistent looking pages. -->

<template lang="pug">
div(
  class="tw-min-h-screen tw-py-6"
  :style="backgroundStyle"
  )

  //- Permission error state.
  MCard(
    v-if="permission && !doesHavePermission(permission)"
    title="Not Authorized"
    variant="warning"
    class="tw-w-full tw-max-w-xl tw-mx-1 sm:tw-mx-auto"
    )
    div Unfortunately, you do not have the #[MBadge(variant="warning") {{ permission }}] permission to view this page.

  //- Loading error state.
  MErrorCallout(
    v-if="error"
    :error="error"
    class="tw-max-w-xl tw-mx-2 sm:tw-mx-auto"
    )

  //- Loading state.
  div(
    v-else-if="isLoading"
    class="tw-flex tw-justify-center"
    )
      // TODO: Skeleton overview card.
      div Loading...

  //- Page content container.
  div(
    v-else
    :class="[{ 'tw-max-w-6xl tw-px-1 sm:tw-px-4 tw-mx-auto tw-@container': !fullWidth }]"
    )
    //- Page alerts.
    div(
      v-if="alerts || $slots.alerts"
      class="tw-w-full tw-max-w-xl tw-mx-auto tw-mb-6"
      )
      //- Programmatic alerts.
      div(v-if="alerts || $slots.alerts")
        // TODO: Redo with a proper component.
        MCallout(
          v-for="alert in alerts"
          :key="alert.key || alert.title"
          :title="alert.title"
          :variant="alert.variant"
          :data-test="alert.dataTest"
          class="tw-mb-3"
          ) {{ alert.message }}
      //- Manual alerts.
      div(v-if="$slots.alerts")
        slot(name="alerts")

    //- Page overview card slot.
    div(
      v-if="$slots.overview"
      class="tw-mb-12 tw-mx-auto tw-max-w-2xl"
      )
      slot(name="overview")

    //- Page content slot.
    slot
</template>

<script lang="ts" setup>
import { computed } from 'vue'

import MCallout from '../../primitives/MCallout.vue'
import MCard from '../../primitives/MCard.vue'
import MBadge from '../../primitives/MBadge.vue'
import MErrorCallout from '../../primitives/MErrorCallout.vue'
import { usePermissions } from '../../utils/permissions'
import { DisplayError } from '../../utils/DisplayErrorHandler'

const { doesHavePermission } = usePermissions()

/**
 * Alert type used by the MetaPageContainer component.
 * @example {
 *  title: 'Example Warning',
 *  message: 'Your mood has cooled down. Consider playing a trance anthem to get back into the zone.'
 * }
 */
export interface MViewContainerAlert {
  /**
   * Title of the alert.
   */
  title: string
  /**
   * Main body of the alert message.
   */
  message: string
  /**
   * Optional: The variant of the alert controls the styling. In MetaPageContainerAlert this also controls the styling
   * of the page background.
   */
  variant?: 'neutral' | 'warning' | 'danger'
  /**
   * Optional: Add a `data-test` element to the alert. Used in checking for the presence of elements in E2E tests.
   */
  dataTest?: string
  /**
   * Optional: Key to use in the alert list v-for. Defaults to the alert title.
   */
  key?: string
  /**
   * Optional: Permission to check for before showing the page.
   */
  permission?: string
}

const props = defineProps<{
  /**
   * Optional: Background stripes of the selected color for the page.
   * Defaults to the most severe alert variant passed to `alerts` or undefined if there are no alerts.
   * @example 'warning'
   */
  variant?: 'neutral' | 'warning' | 'danger'
  /**
   * Optional: Make the view full width. Good for pages with very wide content.
   */
  fullWidth?: boolean
  /**
   * Optional: Array of alerts to show on the top of the page.
   * Also automatically sets the default background variant for the page.
   * @example [{
   *  title: 'Example Warning',
   *  message: 'Your mood has cooled down. Consider playing a trance anthem to get back into the zone.'
   * }]
   */
  alerts?: MViewContainerAlert[]
  /**
   * Optional: Show a loading indicator.
   */
  isLoading?: boolean
  /**
   * Optional: Show an error message instead of page content.
   */
  error?: DisplayError
  /**
   * Optional: Show an error if the user does not have this permission.
   */
  permission?: string
}>()

/**
 * Background stripes of the selected color for the page.
 */
const backgroundStyle = computed(() => {
  const neutral = 'background: repeating-linear-gradient(135deg, rgba(104, 104, 104, 0.05), rgba(104, 104, 104, 0.05) 10px, rgba(0,0,0,0) 10px, rgba(0,0,0,0) 20px)'
  const warning = 'background: repeating-linear-gradient(135deg, rgba(229, 170, 0, 0.05), rgba(229, 170, 0, 0.05) 10px, rgba(0,0,0,0) 10px, rgba(0,0,0,0) 20px)'
  const danger = 'background: repeating-linear-gradient(135deg, rgba(250, 96, 63, 0.05), rgba(250, 96, 63, 0.05) 10px, rgba(0,0,0,0) 10px, rgba(0,0,0,0) 20px)'

  if (props.variant === 'neutral') return neutral
  else if (props.variant === 'warning') return warning
  else if (props.variant === 'danger') return danger
  else if (props.alerts?.some((alert) => alert.variant === 'danger')) return danger
  else if (props.alerts?.some((alert) => alert.variant === 'warning')) return warning
  else if (props.alerts?.some((alert) => alert.variant === 'neutral')) return neutral
  else if (props.permission && !doesHavePermission(props.permission)) return warning
  else if (props.error) return danger
  else return undefined
})
</script>
