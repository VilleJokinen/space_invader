<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-list-card(
  v-if="playerData"
  data-cy="player-login-history-card"
  title="Latest Logins"
  icon="clipboard-list"
  tooltip="Shows 20 latest logins and their source."
  :itemList="decoratedItems"
  :searchFields="searchFields"
  :filterSets="filterSets"
  :sortOptions="sortOptions"
  :defaultSortOption="defaultSortOption"
  emptyMessage="No logins yet!"
  )
  template(#item-card="{ item: login }")
    MListItem
      span #[meta-country-code(isoCode="slotProps.item.location ? slotProps.item.location.country.isoCode : null")] {{ login.deviceModel }}
      span.text-muted.small.ml-2 (client {{login.clientVersion }})

      template(#top-right)
        meta-time(:date="login.timestamp" showAs="timeagoSentenceCase")

      template(#bottom-left)
        //- TODO: Rename 'DeviceId' to 'Client token' in the API.
        span Authentication: {{ login.authenticationKey.platform === 'DeviceId' ? 'Client token' : login.authenticationKey.platform }}
        span(v-if="login.ipAddress")  | IP: #[meta-ip-address(:ip-address="login.ipAddress")]

      template(#bottom-right)
        MTooltip(:content="!grafanaEnabled ? 'Grafana is not configured for this environment' : undefined" noUnderline)
          meta-button(
            permission="dashboard.grafana.view"
            :disabled="!grafanaEnabled"
            :href="getGrafanaLogsUrl(login)"
            link
            target="_blank"
            )
              fa-icon(icon="external-link-alt" size="sm")
              span.ml-2 View logs
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { MTooltip, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { useCoreStore } from '../../coreStore'
import { MetaListSortOption, MetaListSortDirection, MetaListFilterSet, isoCodeToCountryName, isoCodeToCountryFlag } from '@metaplay/meta-ui'
import { getSinglePlayerSubscriptionOptions } from '../../subscription_options/players'

const props = defineProps<{
  /**
   * ID of the player whose login history to show.
   */
  playerId: string
}>()

const { data: playerData } = useSubscription(getSinglePlayerSubscriptionOptions(props.playerId))

/**
 * Typings for the login history items returned by the game server as of R24.
 */
interface PlayerLoginEvent {
  timestamp: string
  daviceId: string
  deviceModel: string
  clientVersion: string
  location: {
    country: {
      isoCode: string
    }
  } | null
  authenticationKey: {
    platform: string
    id: string
  }
}

/**
 * Login history items with locally resolved country name and flag.
 */
interface DecoratedPlayerLoginEvent extends PlayerLoginEvent {
  countryName: string
  countryFlag?: string
  ipAddress?: string // TODO: Add to backend response to resolve a customer diff.
}

/**
 * Login history items with added country name and flag.
 * TODO: Should be done in the backend.
 */
const decoratedItems = computed((): DecoratedPlayerLoginEvent[] => {
  return playerData.value.model.loginHistory.map((login: PlayerLoginEvent) => {
    return {
      ...login,
      countryName: login.location ? isoCodeToCountryName(login.location.country.isoCode) : 'Unknown',
      countryFlag: login.location ? isoCodeToCountryFlag(login.location.country.isoCode) : undefined
    }
  })
})

const coreStore = useCoreStore()

const grafanaEnabled = computed(() => !!coreStore.hello.grafanaUri)

/**
 * Returns a URL to the Grafana logs for the given login event.
 */
function getGrafanaLogsUrl (ev: any) {
  if (grafanaEnabled.value) {
    const startAt = new Date(ev.timestamp)
    const endAt = new Date(+startAt + 15 * 60_000) // Assume 15min sessions.
    const namespaceStr = coreStore.hello.kubernetesNamespace ? `,namespace=\\"${coreStore.hello.kubernetesNamespace}\\"` : ''
    return `${coreStore.hello.grafanaUri}/explore?orgId=1&left={"datasource": "Loki", "queries":[{"expr":"{app=\\"metaplay-server\\"${namespaceStr}} |= \\"${props.playerId}\\""}],"range":{"from":"${+startAt - 3000}","to":"${+endAt + 3000}"}}`
  } else {
    return undefined
  }
}

// Searching, sorting & filtering -------------------------------------------------------------------------------------

const searchFields = [
  'deviceModel',
  'countryName',
  'countryFlag'
]

const sortOptions = [
  new MetaListSortOption('Time', 'timestamp', MetaListSortDirection.Ascending),
  new MetaListSortOption('Time', 'timestamp', MetaListSortDirection.Descending)
]

const defaultSortOption = 1

const filterSets = computed(() => {
  return [
    MetaListFilterSet.asDynamicFilterSet(decoratedItems.value, 'device-model', (x: any) => x.deviceModel),
    MetaListFilterSet.asDynamicFilterSet(decoratedItems.value, 'country-name', (x: any) => x.countryName)
  ]
})
</script>
