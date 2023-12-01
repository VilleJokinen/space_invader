<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!--
The is an example of a Vue Single-File Component (SFC).

The structure consists of three main blocks i.e.  <template>, <script>, and <style>.

The <template> block is where you write your HTML (or Pug) code and this renders the actual view.
The <script> block encapsulates the component's logic i.e. JavaScript or TypeScript code.
The <style> block is where you can add your custom CSS styles.

In this particular component, there is no style block because it uses the globally imported Bootstrap v4 CSS styles.

Check out the following resources to learn more about Vue and our dashboard tech stack:
- Vue: https://vuejs.org/.
- Bootstrap v4: https://bootstrap-vue.js.org/.
- Pug: https://pugjs.org/.
- TypeScript: https://www.typescriptlang.org/.
-->

<template lang="pug">
b-card.shadow-sm
  b-card-title Custom component
  div.text-center.font-weight-bolder.m-2(style="font-size: 5em") Hello, World!
  div.py-3.text-center Total number of clicks: {{ playerData.model.numClicks }}
</template>

<script lang="ts" setup>
import { useSubscription } from '@metaplay/subscriptions'
import { getSinglePlayerSubscriptionOptions } from '@metaplay/core'

// This component takes in the player as a property and will automatically react to any updates to this passed data.
const props = defineProps<{
  playerId: string
}>()

// Fetch data from the game server using our subscriptions.
const {
  data: playerData
} = useSubscription(() => getSinglePlayerSubscriptionOptions(props.playerId))
</script>
