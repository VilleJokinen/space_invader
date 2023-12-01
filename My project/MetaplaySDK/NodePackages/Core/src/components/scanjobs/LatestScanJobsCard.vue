<template lang="pug">
meta-list-card(
  data-cy="past-scan-jobs-by-type-card"
  icon="clipboard-list"
  title="Latest Scan Jobs by Type"
  :item-list="allScanJobsData?.latestFinishedJobsOnePerKind"
  )
  template(#item-card="slot")
    MCollapse(extraMListItemMargin)
      template(#header)
        MListItem {{ slot.item.jobTitle }}
          template(#top-right)
            span Finished #[meta-time(:date="slot.item.endTime")]
      MList(showBorder class="tw-text-sm tw-my-2")
        MListItem(
          v-for="(value, key) in slot.item.summary"
          :key="slot.item.id + '_' + key"
          class="tw-px-4"
          condensed
          striped
          )
          span {{ key }}
          template(#top-right)
            span {{ value }}
</template>

<script lang="ts" setup>
import { MCollapse, MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getAllScanJobsSubscriptionOptions } from '../../subscription_options/scanJobs'

const {
  data: allScanJobsData,
} = useSubscription(getAllScanJobsSubscriptionOptions())
</script>
