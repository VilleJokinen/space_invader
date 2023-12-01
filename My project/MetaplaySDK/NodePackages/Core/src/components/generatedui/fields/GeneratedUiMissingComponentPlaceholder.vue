<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div Missing Component for {{ fieldInfo.fieldType }}
</template>

<script lang="ts" setup>
import { generatedUiFieldBaseProps } from '../generatedFieldBase'

const props = defineProps(generatedUiFieldBaseProps)
</script>
