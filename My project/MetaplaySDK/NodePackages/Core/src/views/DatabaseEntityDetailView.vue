<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!databaseEntityData"
  permission="api.database.inspect_entity"
  :meta-api-error="databaseEntityError"
  )
  template(#overview)
    meta-page-header-card(data-cy="entity-overview" :id="id")
      template(#title) Database Entity

      div.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small responsive).mt-1
        b-tbody
          b-tr
            b-td Persisted At
            b-td(style="min-width: 12rem").text-right
              meta-time(:date="databaseEntityData.persistedAt" showAs="datetime")
          b-tr
            b-td
              div Entry Schema Version
              div.small.text-muted The version of the payload in the database. This can be lower than the latest version if entity is not yet migrated.
            b-td.text-right {{ databaseEntityData.schemaVersion }}
          b-tr
            b-td
              div Actor Schema Version
              div.small.text-muted The current schema version in use. Entities on older versions will be migrated on the next actor wake-up.
            b-td.text-right {{ databaseEntityData.currentSchemaVersion }}
          b-tr
            b-td
              div Is Final
              div.small.text-muted An entity is 'final' when it is not currently in use and has been written to the database cleanly.
            b-td.text-right: MBadge(:variant="databaseEntityData.isFinal ? 'success' : 'danger'") {{ databaseEntityData.isFinal }}
          b-tr
            b-td
              div Size on database
              div.small.text-muted The number of bytes this entity takes when stored in the database. The entity may be compressed.
            b-td.text-right
              meta-abbreviate-number(:value="databaseEntityData.compressedSize" unit="byte")
          b-tr
            b-td
              div Uncompressed Size
              div.small.text-muted The size of the entity state when read from the database and uncompressed.
            b-td.text-right
              meta-abbreviate-number(:value="databaseEntityData.uncompressedSize" unit="byte")

  template(#default)
    b-card.mt-3.mb-2.table-container( data-cy="entity-data")
      b-card-title Stored Data

      b-table-simple.text-break(v-if="databaseEntityData?.structure" small responsive)
        b-thead
          tr
            th Property
            th(colspan="2" :ariaSort="sortOrder" @click="sort").text-right.pr-4 Size

        b-tbody.text-monospace.small
          tr(v-for="row in visibleRows" :key="row.id" @click="toggleExpandedKey(row.id)" :class="{ highlight: row.hasChildren }").small
            td
              //- Row header
              div(:style="'padding-left: ' + row.depth + 'rem'" :class="{ open: row.isExpanded, 'table-row-link': row.hasChildren }").table-row
                fa-icon(v-if="row.hasChildren" icon="angle-right").mr-1
                | {{ row.name }} #[b-link(v-if="!selectedRows.includes(row.id)" @click.stop="toggleRowDetails(row.id)").small.inspect-button Inspect] #[b-link(v-else @click.stop="toggleRowDetails(row.id)").small Close]
              //- Row databaseEntityData
              div(:style="'margin-left: ' + row.depth + 'rem'" :key="row.id + '.info'" v-if="selectedRows.includes(row.id)").bg-light.rounded.border.p-2.small
                div Tag ID: {{ row.tagId || '-' }}
                div Wire Type: {{ row.wireType || '-' }}
                div Type: {{ row.type || '-' }}
                div Value: {{ row.value !== undefined ? row.value : '-' }}

            //- Size info
            td(:class="rowSizeToCssClass(row.size)" style="width: 6rem").text-right {{ row.size }}#[small.text-muted  bytes]
            td(:class="rowSizeToCssClass(row.size)" style="width: 5rem").text-right ({{ row.sizePercent.toFixed(2) }}%)

      div(v-else)
        meta-alert(title="Entity State Not Available.")
          div This entity's state has not yet been initialized. This usually means that the entity has been created, but it's initial data has not yet been set. It is normal that this initialization can take time to complete - try again in a few seconds.
          div.mt-2 If the problems persists then it may point to an issue.
</template>

<script lang="ts" setup>
import { computed, onMounted, ref } from 'vue'
import { useGameServerApi } from '@metaplay/game-server-api'
import { MBadge } from '@metaplay/meta-ui-next'

const props = defineProps<{
  id: string
}>()

const gameServerApi = useGameServerApi()
const sortOrder = ref('none')
const databaseEntityData = ref<any>()
const databaseEntityError = ref<Error>()

onMounted(async () => {
  // TODO: Consider using a subscription?
  try {
    const res = await gameServerApi.get(`/entities/${props.id}/dbinfo`)
    databaseEntityData.value = res.data

    // Pre-Expand all major fields.
    expandedKeys.value = new Set()
    getAllRows().forEach(row => {
      if (row.sizePercent >= 10.0) {
        expandedKeys.value.add(row.id)
      }
    })
  } catch (e) {
    databaseEntityError.value = e as Error
  }
})

const visibleRows = computed(() => {
  return getAllRows(expandedKeys.value)
})

function getAllRows (expandedKeys?: any) {
  if (!databaseEntityData.value?.structure) {
    return []
  }

  const totalSize = databaseEntityData.value.structure.EnvelopeEndOffset - databaseEntityData.value.structure.EnvelopeStartOffset

  function addVisibleRows (rows: any, structure: any, path: any, depth: any, sortOrder: any, forcedName?: any) {
    const envelopeSize = structure.EnvelopeEndOffset - structure.EnvelopeStartOffset
    const payloadSize = structure.PayloadEndOffset - structure.PayloadStartOffset
    const hasHeader = structure.EnvelopeStartOffset !== structure.PayloadStartOffset
    const hasTrailer = structure.EnvelopeEndOffset !== structure.PayloadEndOffset
    const hasPrimitivePayload = structure.PrimitiveValue !== undefined
    const hasMembers = structure.Members !== undefined
    const hasElements = structure.Elements !== undefined
    const hasDictionary = structure.Dictionary !== undefined
    const isExpanded = !expandedKeys ? true : expandedKeys.has(path)
    const insertFns = []
    insertFns.push({
      size: envelopeSize,
      fn: () => rows.push({
        id: path,
        depth,
        name: forcedName || structure.FieldName || (structure.TagId && `[member ${structure.TagId}]`) || structure.TypeName || '[unknown]',
        size: envelopeSize,
        sizePercent: envelopeSize * 100.0 / totalSize,
        type: structure.TypeName,
        wireType: structure.WireType,
        value: structure.PrimitiveValue,
        tagId: structure.TagId,
        hasChildren: hasHeader || hasTrailer || hasPrimitivePayload || hasMembers || hasElements || hasDictionary,
        isExpanded
      })
    })
    if (isExpanded) {
      if (hasHeader) {
        insertFns.push({
          size: structure.PayloadStartOffset - structure.EnvelopeStartOffset,
          fn: () => rows.push({
            id: path + '.i',
            depth: depth + 1,
            name: '[header]',
            size: structure.PayloadStartOffset - structure.EnvelopeStartOffset,
            sizePercent: (structure.PayloadStartOffset - structure.EnvelopeStartOffset) * 100.0 / totalSize,
            type: undefined,
            wireType: undefined,
            value: undefined,
            tagId: undefined,
            hasChildren: false,
            isExpanded: false
          })
        })
      }
      if (hasPrimitivePayload) {
        insertFns.push({
          size: payloadSize,
          fn: () => rows.push({
            id: path + '.p',
            depth: depth + 1,
            name: '[payload]',
            size: payloadSize,
            sizePercent: payloadSize * 100.0 / totalSize,
            type: undefined,
            wireType: undefined,
            value: undefined,
            tagId: undefined,
            hasChildren: false,
            isExpanded: false
          })
        })
      }
      if (hasMembers) {
        for (let ndx = 0; ndx < structure.Members.length; ++ndx) {
          insertFns.push({
            size: structure.Members[ndx].EnvelopeEndOffset - structure.Members[ndx].EnvelopeStartOffset,
            fn: () => addVisibleRows(rows, structure.Members[ndx], `${path}.m${ndx}`, depth + 1, sortOrder)
          })
        }
      }
      if (hasElements) {
        for (let ndx = 0; ndx < structure.Elements.length; ++ndx) {
          insertFns.push({
            size: structure.Elements[ndx].EnvelopeEndOffset - structure.Elements[ndx].EnvelopeStartOffset,
            fn: () => addVisibleRows(rows, structure.Elements[ndx], `${path}.e${ndx}`, depth + 1, sortOrder)
          })
        }
      }
      if (hasDictionary) {
        for (let ndx = 0; ndx < structure.Dictionary.length; ++ndx) {
          const keySize = structure.Dictionary[ndx].Key.EnvelopeEndOffset - structure.Dictionary[ndx].Key.EnvelopeStartOffset
          const valueSize = structure.Dictionary[ndx].Value.EnvelopeEndOffset - structure.Dictionary[ndx].Value.EnvelopeStartOffset
          const entrySize = keySize + valueSize
          const entryPath = `${path}.e${ndx}`
          const entryIsExpanded = !expandedKeys ? true : expandedKeys.has(entryPath)
          insertFns.push({
            size: entrySize,
            fn: () => {
              rows.push({
                id: entryPath,
                depth: depth + 1,
                name: `[entry ${ndx}]`,
                size: entrySize,
                sizePercent: entrySize * 100.0 / totalSize,
                type: undefined,
                wireType: undefined,
                value: undefined,
                tagId: undefined,
                hasChildren: true,
                isExpanded: entryIsExpanded
              })
              if (entryIsExpanded) {
                addVisibleRows(rows, structure.Dictionary[ndx].Key, `${entryPath}.k`, depth + 2, sortOrder, structure.Dictionary[ndx].Key.TypeName ? `[key: ${structure.Dictionary[ndx].Key.TypeName}]` : '[key]')
                addVisibleRows(rows, structure.Dictionary[ndx].Value, `${entryPath}.v`, depth + 2, sortOrder, structure.Dictionary[ndx].Value.TypeName ? `[value: ${structure.Dictionary[ndx].Value.TypeName}]` : '[value]')
              }
            }
          })
        }
      }
      if (hasTrailer) {
        insertFns.push({
          size: structure.EnvelopeEndOffset - structure.PayloadEndOffset,
          fn: () => rows.push({
            id: path + '.t',
            depth: depth + 1,
            name: '[trailer]',
            size: structure.EnvelopeEndOffset - structure.PayloadEndOffset,
            sizePercent: (structure.EnvelopeEndOffset - structure.PayloadEndOffset) * 100.0 / totalSize,
            type: undefined,
            wireType: undefined,
            value: undefined,
            tagId: undefined,
            hasChildren: false,
            isExpanded: false
          })
        })
      }
    }
    // Sort this current single parent->N x child subtree. Parent is always before children.
    const topmost = insertFns.shift()
    if (sortOrder === 'ascending') {
      insertFns.sort((lhs, rhs) => rhs.size - lhs.size)
    } else if (sortOrder === 'descending') {
      insertFns.sort((lhs, rhs) => lhs.size - rhs.size)
    }
    insertFns.unshift(topmost)
    insertFns.forEach(insertFn => insertFn?.fn())
  }

  const rows: any[] = []
  addVisibleRows(rows, databaseEntityData.value.structure, '1', 0, sortOrder.value)
  return rows
}

function rowSizeToCssClass (size: number) {
  if (size >= 512) return 'medium'
  else if (size >= 1024) return 'large'
  else if (size >= 10240) return 'huge'
  else return ''
}

// UI states ----------------------------------------------------------------------------------------------------------

const expandedKeys = ref(new Set())
function toggleExpandedKey (id: string) {
  if (expandedKeys.value.has(id)) {
    expandedKeys.value.delete(id)
  } else {
    expandedKeys.value.add(id)
  }
  expandedKeys.value = new Set(expandedKeys.value)
}

function sort () {
  if (sortOrder.value === 'none') {
    sortOrder.value = 'ascending'
  } else if (sortOrder.value === 'ascending') {
    sortOrder.value = 'descending'
  } else {
    sortOrder.value = 'none'
  }
}

const selectedRows = ref<string[]>([])

function toggleRowDetails (id: string) {
  const location = selectedRows.value.indexOf(id)
  if (location > -1) selectedRows.value.splice(location, 1)
  else selectedRows.value.push(id)
}
</script>

<style scoped>
.open .fa-angle-right {
  transform: rotateZ(90deg);
}

.fa-angle-right {
  transition: transform 0.3s;
}

.highlight:hover {
  background-color: var(--metaplay-grey);
}

.medium {
  background-color: #ffffee;
}

.large {
  background-color: #ffffcc;
}

.huge {
  background-color: #ffeecc;
}

.table-container {
  overflow-x: auto;
}

.table-row .inspect-button {
  display: none;
}

.table-row:hover .inspect-button {
  display: inline-block;
}
</style>
