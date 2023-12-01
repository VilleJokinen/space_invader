<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
b-row.justify-content-center
  b-col(lg="9" xl="8")
    b-card(title="Cluster Metrics & Logs" :class="grafanaEnabled ? '' : 'bg-light'" style="min-height: 11rem").mb-3.shadow-sm
      div(v-if="grafanaEnabled")
        p Grafana is an industry standard tool for diving into the "engine room" level system health metrics and server logs.
        div.text-right
          meta-button(permission="dashboard.grafana.view" variant="primary" target="_blank" :href="grafanaMetricsLink" :disabled="!grafanaEnabled")
            fa-icon(icon="external-link-alt")
            span.ml-2 View Metrics
          meta-button(permission="dashboard.grafana.view" variant="primary" target="_blank" :href="grafanaLogsLink" :disabled="!grafanaEnabled").ml-2
            fa-icon(icon="external-link-alt")
            span.ml-2 View Logs
      div(v-else).text-center.mt-5
        div.text-muted Grafana has not been configured for this environment.

</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { useCoreStore } from '../../coreStore'

const coreStore = useCoreStore()

const grafanaEnabled = computed(() => !!coreStore.hello.grafanaUri)

/**
 * Link to Grafana metrics dashboard.
 */
const grafanaMetricsLink = computed(() => {
  if (grafanaEnabled.value) {
    return coreStore.hello.grafanaUri + '/d/rCI05Y4Mz/metaplay-server'
  } else {
    return undefined
  }
})

/**
 * Link to the Grafana Loki logs.
 */
const grafanaLogsLink = computed(() => {
  if (grafanaEnabled.value) {
    const namespaceStr = coreStore.hello.kubernetesNamespace ? `,namespace=\\"${coreStore.hello.kubernetesNamespace}\\"` : ''
    return `${coreStore.hello.grafanaUri}/explore?orgId=1&left={"datasource": "Loki", "queries":[{"expr":"{app=\\"metaplay-server\\"${namespaceStr}}"}],"range":{"from":"now-1h","to":"now"}}`
  } else {
    return undefined
  }
})
</script>
