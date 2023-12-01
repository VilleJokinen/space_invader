<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->
<!-- An opinionated wrapper for the standard b-collapse component for consistent styling. -->

<template lang="pug">
div.meta-collapse(ref="root")
  div(v-b-toggle:[collapseId] :class="{ 'meta-collapse-header-gradient': gradient, 'table-row-link': hasCollapseContent, 'not-collapsed': true }")
    slot(name="header")
      h4.meta-collapse-header-padding.m-0
        b-row(no-gutters align-v="center")
          fa-icon(icon="angle-right" size="1x").mr-2
          | {{ title }}
          MBadge(v-if="number" shape="pill").color-badge.ml-2 {{ number }}

  b-collapse(:id="collapseId" :visible="startExpanded" @shown="$emit('shown')" @hidden="$emit('hidden')")
    slot
</template>

<script lang="ts" setup>
import { getCurrentInstance, onBeforeUnmount, onMounted, ref, useSlots } from 'vue'
import { makeIntoUniqueKey } from '../utils'
import { MBadge } from '@metaplay/meta-ui-next'

const props = defineProps({
  title: {
    type: String,
    default: 'Untitled'
  },
  number: {
    type: Number,
    default: null
  },
  id: {
    type: String,
    required: true,
    default: null
  },
  gradient: {
    type: Boolean,
    default: false
  },
  startExpanded: {
    type: Boolean,
    default: false
  }
})

defineEmits(['shown', 'hidden'])

const collapseId = makeIntoUniqueKey(props.id)

// Detect if the collapse content has changed and trigger a re-render.
const slots = useSlots()
const hasCollapseContent = ref<boolean>(false)
const observer = new MutationObserver(() => {
  hasCollapseContent.value = slots.default !== undefined
})

// Register / de-register the observer.
const root = ref<HTMLElement>()
onMounted(() => {
  hasCollapseContent.value = slots.default !== undefined
  observer.observe(root.value as Node, {
    childList: true,
    subtree: true
  })
})
onBeforeUnmount(() => {
  observer.disconnect()
})
</script>

<style>
.meta-collapse-container:hover {
  background: var(--metaplay-grey-light);
}

.meta-collapse .meta-collapse-header-padding {
  padding: 16px 20px;
}

.meta-collapse .not-collapsed .color-badge {
  background: var(--metaplay-blue);
}

.meta-collapse .color-badge {
  background: var(--metaplay-grey-dark);
}

.meta-collapse .meta-collapse-header-gradient.collapsed {
  background-color: var(--metaplay-grey-lightest);
  background-image: linear-gradient(131deg, white 0%, white 74.9%, #f2f2f2 75%, #f2f2f2 100%);
}

.meta-collapse .meta-collapse-header-gradient.not-collapsed {
  background: linear-gradient(131deg, white 0%, white 74.9%, var(--metaplay-grey-light) 75%, var(--metaplay-grey-light) 100%);
}

.meta-collapse .meta-collapse-header-gradient:hover {
  background: linear-gradient(131deg, var(--metaplay-grey-light) 0%, var(--metaplay-grey-light) 74.9%, var(--metaplay-grey) 75%, var(--metaplay-grey) 100%);
}
</style>
