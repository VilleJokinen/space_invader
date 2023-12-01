<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
iframe(:src="coreStore.hello.grafanaUri" frameborder="0" width="100%" height="800px")
</template>

<script lang="ts" setup>
import { useCoreStore } from '../coreStore'

const coreStore = useCoreStore()
</script>
