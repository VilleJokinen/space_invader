<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
span {{ isoCodeToCountryName(props.isoCode) }} {{ isoCodeToCountryFlag(props.isoCode) }}
</template>

<script lang="ts" setup>
import countryEmoji from 'country-emoji'

const props = defineProps<{
  isoCode: string
}>()

/**
 * Returns the display name of a given country based on it's ISO country code.
 * @param isoCode The source ISO country code.
 * @returns Display name of the country.
 */
function isoCodeToCountryName (isoCode: string): string {
  return countryEmoji.name(isoCode) ?? isoCode
}

/**
 * Returns the flag emoji of a given country based on it's ISO country code.
 * @param isoCode The source ISO country code.
 * @returns Flag emoji of the country.
 */
function isoCodeToCountryFlag (isoCode: string): string {
  return countryEmoji.flag(isoCode) ?? ''
}
</script>
