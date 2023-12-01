<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- A small component to display a toast notification. -->

<template lang="pug">
div.d-flex.justify-content-between.align-items-center
  div.message
    div.title {{ title }}
    div.small {{ message }}
  button.toast-btn(@click="$emit('close-toast')") ×
</template>

<script lang="ts" setup>
defineProps<{
  /**
   * Title for the toast.
   */
  title: string
  /**
   * The message to be displayed.
   */
  message: string
}>()

defineEmits(['close-toast'])
</script>

<style scoped>
.message {
  max-width: 300px !important;
}
.title {
  font-weight: 600 !important;
}
.toast-btn {
  font-weight: bold;
  font-size: 24px;
  line-height: 24px;
  background-color: transparent;
  outline: none;
  border: none;
  padding: 0;
  padding-left: 10px;
  cursor: pointer;
  transition: 0.3s ease;
  align-items: center;
  color: #fff;
  transition: visibility 0s, opacity 0.2s linear;
  height: 100%;
}
</style>
