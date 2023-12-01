<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
MTooltip(
  :content="!disabled && !doesHavePermission(permission) ? `You need the '${permission}' permission to view this page.` : undefined"
  trigger-html-element="div"
  )
  li(
    v-bind="$attrs"
    role="link"
    :class="['tw-py-2.5 tw-px-5 tw-flex tw-space-x-1', conditionalClasses]"
    :disabled="!permission || disabled || !doesHavePermission(permission)"
    )
    div(
      v-if="$slots.icon"
      class="tw-w-6 tw-h-6 tw-shrink-0"
      )
        slot(name="icon")
    div {{ label }}
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { doesHavePermission } from '../../utils/permissions'
import MTooltip from '../../primitives/MTooltip.vue'

defineOptions({
  inheritAttrs: false
})

const props = defineProps<{
  /**
   * The text label of the link. Keep these as short as possible.
   * @example 'Players'
   */
  label: string
  /**
   * Optional: If this string matches any part of the current url, the link will be highlighted.
   * @example '/players'
   */
  activePathFragment?: string
  /**
   * Optional: If the current url is exatly this string, the link will be highlighted.
   * @example '/'
   */
  activeExactPath?: string
  /**
   * Optional: The permission required to view this link. If the user does not have this permission, the link will be disabled.
   * @example 'api.players.view'
   */
  permission?: string
  /**
   * Optional: Disable the link.
   */
  disabled?: boolean
}>()

const route = useRoute()

const active = computed(() => {
  if (!route) return false // This makes storybook work without a router.

  if (props.activeExactPath === route.fullPath) return true
  if (!props.activePathFragment) return false
  return route.fullPath.includes(props.activePathFragment)
})

const conditionalClasses = computed(() => {
  const classes = []

  if (props.disabled || (props.permission && !doesHavePermission(props.permission))) {
    classes.push('tw-bg-neutral-100 tw-text-neutral-400 tw-pointer-events-none')
  } else if (!props.permission || doesHavePermission(props.permission)) {
    classes.push('tw-text-neutral-700 tw-cursor-pointer')

    if (active.value) {
      classes.push('tw-bg-blue-500 tw-text-white')
    } else {
      classes.push('hover:tw-bg-neutral-100 active:tw-bg-neutral-200')
    }
  }

  return classes.join(' ')
})
</script>
