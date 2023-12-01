<template lang="pug">
MTooltip(
  :content="tooltipContent"
  no-underline
  )
  span(
    v-if="size === 'text'"
    v-bind="$attrs"
    class="tw-text-blue-500 hover:tw-text-blue-600 active:tw-text-blue-800 tw-cursor-pointer hover:tw-underline focus:tw-ring-blue-400 disabled:tw-text-blue-300 disabled:tw-cursor-not-allowed"
    role="link"
    @click.stop="onClick"
    )
    slot Link Text TBD

  component(
    v-else
    :is="to || href ? 'a' : 'button'"
    v-bind="$attrs"
    type="button"
    :class="['tw-inline-flex tw-items-center tw-rounded-full tw-text-white hover:tw-text-white tw-cursor-pointer tw-transition-colors tw-no-underline', buttonVariantClasses, buttonSizeClasses]"
    :style="buttonVariantStyles + 'text-decoration-line: none;'"
    :disabled="disabled || !hasGotPermission || disabledTooltip"
    @click.stop="onClick"
    )
      //- ~20px icons (tw-w-5 tw-h-5) seem to look good on the default size buttons.
      div(
        v-if="$slots.icon"
        :class="buttonIconSizeClasses"
        )
        slot(name="icon")
      div(
        v-if="$slots.default"
        :class="buttonLabelSizeClasses"
        )
        slot
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { doesHavePermission } from '../utils/permissions'
import type { Variant } from '../utils/types'
import { useRouter } from 'vue-router'

import MTooltip from './MTooltip.vue'

defineOptions({
  inheritAttrs: false
})

const props = withDefaults(defineProps<{
  /**
   * Optional: Set the size of the button.
   */
  size?: 'text' | 'small' | 'default'
  /**
   * Optional: Set the visual variant of the button. Defaults to 'primary'.
   */
  variant?: Variant
  icon?: string
  /**
   * Optional: Disable the button. Defaults to `false`.
   */
  disabled?: boolean
  /**
   * Optional: Disable the button and show a tooltip with the given text.
   */
  disabledTooltip?: string
  /**
   * Optional: The permission required to use this button. If the user does not have this permission the button will be disabled with a tooltip.
   */
  permission?: string
  /**
   * Optional: The route to navigate to when the button is clicked. If this is set the button will be rendered as a link.
   */
  to?: string
  /**
   * Optional: The href to navigate to when the button is clicked. If this is set the button will be rendered as a link.
   */
  href?: string
}>(), {
  size: 'default',
  variant: 'primary',
  icon: undefined,
  permission: undefined,
  to: undefined,
  href: undefined,
  hrefTarget: undefined,
  disabledTooltip: undefined,
})

const emit = defineEmits(['click'])

const router = useRouter()

const tooltipContent = computed(() => {
  if (props.disabledTooltip) return props.disabledTooltip
  if (props.permission && !hasGotPermission.value) return `You need the '${props.permission}' permission to use this feature.`
  return undefined
})

function onClick () {
  if (props.disabled || props.disabledTooltip) return
  if (props.permission && !hasGotPermission.value) return

  if (props.to && router) {
    void router.push(props.to)
    return
  }

  if (props.href) {
    window.open(props.href)
    return
  }

  emit('click')
}

const hasGotPermission = computed(() => {
  if (props.permission) return doesHavePermission(props.permission)
  return true
})

const buttonVariantClasses = computed(() => {
  const classes = {
    neutral: 'tw-bg-neutral-500 hover:tw-bg-neutral-600 active:tw-bg-neutral-700 focus:tw-ring-neutral-400 disabled:tw-bg-neutral-300 disabled:tw-cursor-not-allowed',
    success: 'tw-bg-green-500 hover:tw-bg-green-600 active:tw-bg-green-700 focus:tw-ring-green-400 disabled:tw-bg-green-300 disabled:tw-cursor-not-allowed',
    warning: 'tw-bg-orange-400 hover:tw-bg-orange-500 active:tw-bg-orange-600 focus:tw-ring-orange-400 disabled:tw-bg-orange-300 disabled:tw-cursor-not-allowed',
    danger: 'tw-bg-red-400 hover:tw-bg-red-500 active:tw-bg-red-600 focus:tw-ring-red-400 disabled:tw-bg-red-300 disabled:tw-cursor-not-allowed',
    primary: 'tw-bg-blue-500 hover:tw-bg-blue-600 active:tw-bg-blue-700 focus:tw-ring-blue-400 disabled:tw-bg-blue-300 disabled:tw-cursor-not-allowed',
  }

  return classes[props.variant]
})

const buttonVariantStyles = computed(() => {
  const base = 'box-shadow: inset 0 1px 0 rgba(white, .15), '

  const styles = {
    neutral: 'rgb(45, 144, 220)',
    success: 'rgb(45, 144, 220)',
    warning: 'rgb(45, 144, 220)',
    danger: 'rgb(45, 144, 220)',
    primary: 'rgb(45, 144, 220)',
  }

  return base + styles[props.variant] + ';'
})

const buttonSizeClasses = computed(() => {
  const classes = {
    text: undefined,
    small: 'tw-px-0.5',
    default: 'tw-px-3.5',
  }

  return classes[props.size]
})

const buttonIconSizeClasses = computed(() => {
  const classes = {
    text: undefined,
    small: 'tw-my-0.5',
    default: 'tw-mr-2',
  }

  return classes[props.size]
})

const buttonLabelSizeClasses = computed(() => {
  const classes = {
    text: undefined,
    small: 'tw-text-xs tw-my-0.5 tw-mx-1 tw-font-bold',
    default: 'tw-text-sm tw-my-2 tw-font-bold',
  }

  return classes[props.size]
})
</script>
