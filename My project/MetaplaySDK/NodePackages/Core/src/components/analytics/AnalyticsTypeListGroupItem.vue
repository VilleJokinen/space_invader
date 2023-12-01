<template lang="pug">
MListItem
  | {{ event.categoryName }} {{ event.displayName }}

  template(#top-right)
    span.text-muted Code: #[span.text-monospace(style="font-size: .7rem") {{ event.typeCode }}]

  template(#bottom-left)
    | {{ event.docString || "No description provided." }}
    //span.text-monospace.small(v-if="event.parameters.length > 0") {{ event.parameters.join(', ') }}
    //small(v-else).text-muted.font-italic No parameters

  template(#bottom-right)
    b-link(:to="`/analyticsEvents/${event.typeCode}`" data-cy="analytics-details-link") View details
</template>

<script lang="ts" setup>
import { MListItem } from '@metaplay/meta-ui-next'

defineProps({
  event: {
    type: Object,
    required: true
  }
})
</script>
