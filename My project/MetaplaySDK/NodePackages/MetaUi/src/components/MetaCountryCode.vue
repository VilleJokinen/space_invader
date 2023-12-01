<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
MTooltip(:disabled="showName" :content="countryName" noUnderline)
  span(v-if="showName") {{ countryName }} {{ countryFlag }}
  span(v-else) {{ countryFlag }}
</template>

<script lang="ts" setup>
import { computed } from 'vue'
import { MTooltip } from '@metaplay/meta-ui-next'
import { isoCodeToCountryName, isoCodeToCountryFlag } from '../utils'

const props = defineProps<{
  isoCode?: string
  showName?: boolean
}>()

// Resolve ISOCode to country name and flag.
const countryName = computed(() => props.isoCode ? isoCodeToCountryName(props.isoCode) : 'Unknown')
const countryFlag = computed(() => props.isoCode ? isoCodeToCountryFlag(props.isoCode) : null)
</script>
