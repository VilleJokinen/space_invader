<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!-- An opinionated component to render a developer friendly list of arbitrary data. -->

<template lang="pug">
b-row.justify-content-center.mt-5(v-if="uiStore.showDeveloperUi")
  b-col(lg="10")
    div
      fa-icon.mr-2.icon(icon="code")/
      span Raw data
      span(v-if="name").ml-1
        span for
        MBadge.ml-1 '{{ name }}'
      span
        span.small.text-muted.ml-2.d-inline-block (enabled in developer mode)

    div(v-if="kvPair != null")
      ul(v-if="Object.keys(kvPair).length > 0" class="tw-text-xs tw-divide-y tw-divide-neutral-200")
        li.pl-0.pr-0.pb-0.pb-2.text-monospace(
          v-for="(rootValue, rootKey) in kvPair"
          :key="rootKey"
          class="tw-py-3"
          )
          b-row(no-gutters align-v="center" @click="toggleProp(String(rootKey))" :class="{ 'not-collapsed': isPropOpen(rootKey) }").pointer
            fa-icon(icon="angle-right" size="1x").mr-1
            span {{ rootKey }}
            //- Muted preview when closed.
            small(v-if="!isPropOpen(rootKey)").ml-2.text-muted.small
              //- null
              span(v-if="rootValue === null") null
              //- Array.
              span(v-else-if="Array.isArray(rootValue)") {{ renderArray(rootValue) }}
              //- Object.
              span(v-else-if="typeof rootValue === 'object'") {{ renderObject(rootValue) }}
              //- String.
              span(v-else-if="typeof rootValue === 'string'")
                span(v-if="!rootValue") empty string
                span(v-else-if="rootValue.length > 40") {{ rootValue.slice(0, 40) }}...
                span(v-else) {{ rootValue }}
              //- Boolean.
              span(v-else-if="typeof rootValue === 'boolean'")
                span {{ rootValue }}
              //- Others.
              span(v-else-if="typeof rootValue === 'number'") {{ rootValue }}
              span(v-else) {{ typeof rootValue }}
          div(v-if="isPropOpen(rootKey)")
            //- Null.
            MBadge(v-if="rootValue == null").mb-3 null
            //- Object.
            div(v-else-if="typeof rootValue === 'object'").pl-2
              div(v-for="childKey in Object.keys(rootValue)" :key="childKey" style="margin-bottom: 0.1rem")
                div(@click="toggleProp(rootKey, childKey)" :class="{ 'not-collapsed': isPropOpen(rootKey, childKey) }").pointer
                  fa-icon(icon="angle-right" size="1x").mr-1
                  | {{ childKey }}
                  // TODO: make this a component instead of clever copy-pasta.
                  //- Muted preview when closed.
                  span(v-if="!isPropOpen(rootKey, childKey)").ml-2.text-muted.small
                    //- null.
                    span(v-if="rootValue[childKey] === null") null
                    //- Array.
                    span(v-else-if="Array.isArray(rootValue[childKey])") {{ renderArray(rootValue[childKey]) }}
                    //- Object.
                    span(v-else-if="typeof rootValue[childKey] === 'object'") {{ renderObject(rootValue[childKey]) }}
                    //- String.
                    span(v-else-if="typeof rootValue[childKey] === 'string'")
                      span(v-if="!rootValue[childKey]") empty string
                      span(v-else-if="rootValue[childKey].length > 40") {{ rootValue[childKey].slice(0, 40) }}...
                      span(v-else) {{ rootValue[childKey] }}
                    //- Boolean
                    span(v-else-if="typeof rootValue[childKey] === 'boolean'")
                      span {{ rootValue[childKey] }}
                    //- Others.
                    span(v-else-if="typeof rootValue[childKey] === 'number'") {{ rootValue[childKey] }}
                    span(v-else) {{ typeof rootValue[childKey] }}
                div(v-if="isPropOpen(rootKey, childKey)").pl-2
                  MBadge(v-if="rootValue[childKey] == null").mb-1 null
                  MBadge(v-else-if="rootValue[childKey] === ''").mb-1 empty string
                  pre(v-else) {{ rootValue[childKey] }}

            //- String.
            span(v-else-if="typeof rootValue === 'string'")
              MBadge(v-if="rootValue === ''").mb-1 empty string
              pre {{ rootValue }}
            //- Others.
            pre(v-else) {{ rootValue }}

      b-alert.mt-2(v-else show variant="secondary").text-center 0 results
    b-alert.mt-2(v-else show variant="warning").text-center No data!
</template>

<script lang="ts" setup>
import { roughSizeOfObject } from '../utils'
import { useUiStore } from '../uiStore'
import { ref, watch } from 'vue'
import { MBadge } from '@metaplay/meta-ui-next'

const props = defineProps<{
  kvPair?: { [key: string | number]: any}
  name?: string
}>()

const uiStore = useUiStore()
const openProps = ref<string[]>([])

const unwatch = watch(() => props.kvPair, (newVal: { [key: string | number]: any}, oldVal: { [key: string | number]: any}) => {
  if (!oldVal && newVal) {
    // Open small root entries by default.
    for (const key of Object.keys(newVal)) {
      if (roughSizeOfObject(newVal[key]) < 800) {
        openProps.value.push(key)
      }
    }

    // We only want to do this the first time that the source object is set, so we unwatch now.
    unwatch()
  }
}, {
  deep: false
})

function isPropOpen (...keys: any[]) {
  const key = keys.toString()
  return openProps.value.includes(key)
}

function toggleProp (...keys: any[]) {
  const key = keys.toString()
  if (openProps.value.includes(key)) openProps.value = openProps.value.filter(e => e !== key)
  else openProps.value.push(key)
}

/**
 * Stringifies an array for display purposes.
 * If the array has more than 5 elements, only the first 5 are included in the string.
 * If the array is empty, it returns "Array (0)".
 *
 * @param rootValue An array of any type.
 * @returns A string representation of the array.
 */
function renderArray (rootValue: any[]) {
  if (rootValue.length > 5) {
    const firstFive = rootValue.slice(0, 5).map((value) => `${typeof value === 'object' ? 'object' : value},`).join(' ')
    return `[ ${firstFive} ... ] (${rootValue.length})`
  } else if (rootValue.length > 0) {
    const allValues = rootValue.map((value) => `${typeof value === 'object' ? 'object' : value}`).join(', ')
    return `[ ${allValues} ] (${rootValue.length})`
  } else {
    return 'Array (0)'
  }
}

/**
 * Stringifies an object for display purposes.
 * If the object has more than 5 properties, only the first 5 are included in the string.
 * If the object has no properties, it returns "Object (0)".
 *
 * @param rootValue An object of any type.
 * @returns A string representation of the object.
 */
function renderObject (rootValue: object) {
  const keys = Object.keys(rootValue)
  if (keys.length > 5) {
    const firstFiveKeys = keys.slice(0, 5).join(', ')
    return `{ ${firstFiveKeys}, ... } (${keys.length})`
  } else if (keys.length > 0) {
    const allKeys = keys.join(', ')
    return `{ ${allKeys} } (${keys.length})`
  } else {
    return 'Object (0)'
  }
}

</script>

<style scoped>
.icon {
  margin-top: 0.21rem;
}

.list-group-item {
  background-color: transparent;
  font-size: 9pt;
}

pre {
  white-space: pre-wrap;
  margin-bottom: 0;
}

.not-collapsed svg {
  transform: rotateZ(90deg);
}

.not-collapsed .when-closed {
  display: none;
}

svg {
  transition: 0.3s;
}
</style>
