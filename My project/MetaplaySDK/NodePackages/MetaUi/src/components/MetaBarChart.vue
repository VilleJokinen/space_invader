<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- There seems to be a typings issue where this element is supposed to now use `data` instead of `chart-data` but it doesn't work.
bar(
  :options="options || defaultOptions"
  :data="data"
  :height="200"
  chart-id="concurrents-chart"
  )
</template>

<script lang="ts" setup>
import { Bar } from 'vue-chartjs'
import { Chart as ChartJS, BarElement, CategoryScale, LinearScale, type ChartOptions, type ChartData } from 'chart.js'

ChartJS.register(BarElement, CategoryScale, LinearScale)

interface Props {
  /**
   * Chart data.
   * @example {
   *  labels: ['one', 'two', 'three'],
   *  datasets: [
   *   { data: [1, 2, 3] }
   *  ]
   * }
   */
  data: ChartData<'bar'>
  /**
   * Unique ID for the chart element.
   */
  chartId: string
  /**
   * Optional: Custom chart options.
   */
  options?: ChartOptions<'bar'>
  /**
   * Optional: Chart height.
   * @default 200
   */
  height?: number
}

withDefaults(defineProps<Props>(), {
  options: undefined, // Typings didn't work here. Using the defaultOptions below instead.
  height: 200,
})

const defaultOptions: ChartOptions<'bar'> = {
  responsive: true,
  scales: {
    x: {
      display: false,
    },
    y: {
      min: 0,
      suggestedMax: 10,
      grid: {
        display: false,
      },
      border: {
        display: false,
      },
    }
  },
  datasets: {
    bar: {
      borderRadius: 2000,
      borderSkipped: false,
    }
  },
  backgroundColor: 'rgb(45, 144, 220)',
}
</script>
