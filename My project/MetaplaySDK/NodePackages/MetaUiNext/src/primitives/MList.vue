<template lang="pug">
//- Container
div(:class="['tw-overflow-hidden', variantClassesBorder]")
  //- List root
  ul(
    role="list"
    :class="['tw-divide-y', variantClassesDivider, 'tw-m-0']"
    )
    slot Add "MListItems" here!
</template>
<script lang="ts" setup>
import { computed } from 'vue'
import type { Variant } from '../utils/types'

const props = withDefaults(defineProps<{
  showBorder?: boolean
  variant?: Variant
}>(), {
  variant: 'neutral'
})

const variantClassesDivider = computed(() => {
  const variantToDividerClasses: { [index: string]: string } = {
    primary: 'tw-divide-blue-200',
    success: 'tw-divide-green-200',
    danger: 'tw-divide-red-200',
    warning: 'tw-divide-orange-200',
    default: 'tw-divide-neutral-200'
  }
  return variantToDividerClasses[props.variant] || variantToDividerClasses.default
})

const variantClassesBorder = computed(() => {
  if (props.showBorder) {
    const variantToColorClasses: { [index: string]: string } = {
      primary: 'tw-border-blue-300',
      success: 'tw-border-green-300',
      danger: 'tw-border-red-200',
      warning: 'tw-border-orange-300',
      default: 'tw-border-neutral-300'
    }
    const colorClass = variantToColorClasses[props.variant] || variantToColorClasses.default
    return `tw-border tw-rounded-md ${colorClass}`
  } else {
    return undefined
  }
})
</script>
