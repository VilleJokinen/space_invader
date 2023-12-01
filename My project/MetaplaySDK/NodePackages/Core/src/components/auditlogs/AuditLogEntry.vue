<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
MListItem(class="tw-px-3")
  | {{ item.displayTitle }}
  span.small.text-muted  by #[meta-username(:username="item.source.sourceId")] #[meta-country-code(:isoCode="item.sourceCountryIsoCode ? item.sourceCountryIsoCode : undefined")]
  template(#top-right): meta-time(:date="item.createdAt")
  template(#bottom-left)
    div {{ item.displayDescription }}
    div(v-if="showTarget") Target:
      |
      |
      meta-button(
        link
        permission="api.audit_logs.search"
        data-cy="view-event-link"
        :to="`/auditLogs?targetType=${item.target.targetType}&targetId=${item.target.targetId}`"
      ) {{ item.target.targetType.replace(/\$/, '') }}:{{ item.target.targetId }}
  template(#bottom-right): meta-button(link permission="api.audit_logs.view" :to="`/auditLogs/${item.eventId}`") View log event
</template>

<script lang="ts" setup>
import { MListItem } from '@metaplay/meta-ui-next'

defineProps<{
  item?: any
  showTarget?: boolean
}>()
</script>
