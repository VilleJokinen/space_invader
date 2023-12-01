<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container
  b-row(align-h="center").pt-5
    b-col(md="11" lg="8" xl="7")
      b-card
        div.text-center
          b-img.p-3(:src="ImgUrl" fluid)
          h1.mt-1 The computer says no

        p.mt-4.mb-0 Page not found!
        span.small.text-muted Maybe check the URL? Do let us know if you think this a bug 🤔
</template>

<script lang="ts" setup>
import ImgUrl from '../assets/old-computer.png'
</script>
