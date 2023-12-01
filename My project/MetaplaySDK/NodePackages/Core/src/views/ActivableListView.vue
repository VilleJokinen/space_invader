<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Note: error handling not necessary due to staticConfigData already loaded during initialization.
meta-page-container(
  :is-loading="!activablesMetadata"
  )
  template(#overview)
    meta-page-header-card
      template(#title) View {{ categoryDisplayName }}
      p {{ categoryDescription }}

  template(#center-column)
    meta-generic-activables-card(
      data-cy="all"
      :category="categoryKey"
      :longList="true"
      :title="categoryDisplayName"
      :emptyMessage="`No ${categoryDisplayName} defined. Set them up in your game configs to start using the feature!`"
      :customEvaluationIsoDateTime="customEvaluationTime ? String(customEvaluationTime.toISO()) : undefined"
      noCollapse
    )

    b-row(align-h="center").mt-3
      b-col(md="10" xl="9").mb-3
        div(data-cy="custom-time").pl-3.pr-3.pb-3.bg-white.rounded.border.shadow-sm
          b-row(align-h="between" no-gutters).mb-2.mt-3
            span.font-weight-bold Enable Custom Evaluation Time
              MBadge(tooltip="The phases on the page are evaluated according to the local time of your browser. Enabling custom evaluation allows you to set an exact time to evaluate against." shape="pill").ml-1 ?
            meta-input-checkbox(v-model="userEvaluationEnabled" name="User Evaluation" size="lg" showAs="switch")
          div(v-if="userEvaluationEnabled").border-top.mt-3.pt-2
            MInputDateTime(
              label="Evaluation Time"
              :model-value="userEvaluationTime"
              @update:model-value="onDateTimeChange"
              )
          div.w-100.text-center.mt-2
            span.small.font-italic.text-muted Schedules evaluated at {{ evaluationTimeUsed }}

  template(#default)
    meta-raw-data(:kvPair="activablesMetadata" name="activablesMetadata")
</template>

<script setup lang="ts">
import { DateTime } from 'luxon'
import { computed, ref } from 'vue'

import { MBadge, MInputDateTime } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import MetaGenericActivablesCard from '../components/activables/MetaGenericActivablesCard.vue'
import { getStaticConfigSubscriptionOptions } from '../subscription_options/general'

// Props --------------------------------------------------------------------------------------------------------------

const props = defineProps<{
  categoryKey: string
}>()

// Custom user evaluation time ----------------------------------------------------------------------------------------

/**
 * Model for whether custom user evaluation time is enabled ore not.
 */
const userEvaluationEnabled = ref(false)

/**
 * Model for custom user evaluation time input.
 */
const userEvaluationTime = ref(DateTime.now())

/**
 * What time to use for evaluating the activables card/
 */
const customEvaluationTime = computed((): DateTime | undefined => {
  if (userEvaluationEnabled.value) {
    return userEvaluationTime.value
  } else {
    return undefined
  }
})

/**
 * Utility function to prevent undefined inputs.
 */
function onDateTimeChange (value?: DateTime) {
  if (!value) return
  userEvaluationTime.value = value
}

/**
 * Returns ISO string of time that is being used to evaluate availability of activables.
 */
const evaluationTimeUsed = computed((): string => {
  if (userEvaluationEnabled.value) {
    return userEvaluationTime.value.toISO() as string
  } else {
    return DateTime.now().toISO() as string
  }
})

// Activables data ----------------------------------------------------------------------------------------------------

const {
  data: staticConfigData
} = useSubscription(getStaticConfigSubscriptionOptions())

const categoryInfo = computed((): any => {
  return activablesMetadata.value.categories[props.categoryKey]
})

const categoryDisplayName = computed((): string => {
  return categoryInfo.value.displayName
})

const categoryDescription = computed((): string => {
  return categoryInfo.value.description
})

const activablesMetadata = computed((): any => {
  return staticConfigData.value?.serverReflection.activablesMetadata
})
</script>
