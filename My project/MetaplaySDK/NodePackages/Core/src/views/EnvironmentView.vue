<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!databaseStatusData"
  :meta-api-error="databaseStatusError"
  )
  template(#overview)
    meta-page-header-card
      template(#title) View Environment Details

      p Information about the environment you are currently accessing.
      p.small.text-muted Metaplay game servers typically run on a cluster of virtual machines in the cloud. Here you can see an overview of how the cluster has been configured to distribute the workload.

  template(#default)
    //- Cluster
    b-row(no-gutters).mt-3.mb-2
      h3 Cluster

    b-row(align-h="center")
      b-col(md="9" xl="8")
        meta-list-card(
          data-cy="shard-sets"
          title="Shard Sets"
          :itemList="clusterConfig.nodeSets"
          v-if="clusterConfig"
        ).list-group-stripes
          template(#item-card="slotProps")
            MListItem {{ slotProps.item.shardName }}
              template(#top-right) Host: #[span(style="font-size: .8rem") {{ slotProps.item.hostName }}:{{ slotProps.item.port }}]
              template(#bottom-right) Nodes: #[span(style="font-size: .8rem") {{ slotProps.item.nodeCount }}]
              template(#bottom-left)
                  MBadge.mr-1(v-for="entityKind in slotProps.item.entityKindMask" :key="entityKind") {{ entityKind }}

    //- Database
    b-row(no-gutters align-v="center").mt-3.mb-2
      h3.mr-2 Database
      h6(v-if="databaseStatusPermission && databaseStatusData"): MBadge(variant="primary") {{ dbOptions.backend }}

    div(v-if="!databaseStatusPermission")
      b-row.justify-content-center.mt-5
        b-col(md="9" xl="8").mb-3
          b-alert(show variant="danger") Could not render this component you don't have the required permissions to view the data!

    div(v-else-if="!databaseStatusData")
      b-row.justify-content-center.mt-5
          b-spinner.mt-5(label="Loading...")/

    div(v-else)
      b-row(v-if='databaseStatusPermission')
        b-col(lg="6").mb-3
          meta-list-card(
            data-cy="database-shards"
            title="Database Shards"
            icon="database"
            :itemList="dbOptions.shards"
            ).list-group-stripes
              template(#item-card="slotProps")
                MListItem
                  | \#{{ slotProps.index }}
                  template(#top-right) {{ slotProps.item.userId }}
                  template(#bottom-left)
                    div(v-if="dbOptions.backend === 'MySql'")
                      div Database: {{ slotProps.item.databaseName }}
                      div RW host: {{ slotProps.item.readWriteHost }}
                      div RO host: {{ slotProps.item.readOnlyHost }}
                    div(v-if="dbOptions.backend === 'Sqlite'")
                      div File path: {{ slotProps.item.filePath }}

        b-col(lg="6").mb-3
          b-card(title="Database Items" data-cy="database-items").mb-3.shadow-sm
            span.font-weight-bold Totals
            b-table-simple.mt-1(small)
              b-tbody
                b-tr(v-for="(itemCount, tableName) in totalItemCounts" :key="tableName")
                  b-td {{ tableName }}
                  b-td.text-right #[meta-abbreviate-number(:value="itemCount")]

            div.text-right
              meta-action-modal-button(
                class="mr-2"
                id="items-per-shard"
                action-button-text="Show Items per Shard"
                only-close
                modal-size="lg"
                modal-title="Database Items per Shard"
                :on-ok="async () => {}"
                v-if="databaseStatusData"
                )
                  ul(
                    class="tw-border tw-border-neutral-300 tw-rounded-md tw-divide-y tw-divide-neutral-200"
                    style="font-size: .8rem"
                    )
                    li(
                      v-for="(shardSpec, shardNdx) in dbOptions.shards"
                      :key="shardNdx"
                      class="tw-px-5 tw-py-3 even:tw-bg-neutral-100"
                      )
                      div(v-if="shardNdx < databaseStatusData.numShards")
                        b-row(no-gutters)
                          h6 Shard &#35{{ shardNdx }}
                        b-row
                          b-col(sm="4" v-for="(shardItemCounts, tableName) in databaseStatusData.shardItemCounts" :key="`counts-${tableName}`")
                            b-row(align-h="between" no-gutters :class="shardItemCounts[shardNdx] > 0 ? '' : 'text-muted'")
                              span {{ tableName }}
                              span.text-monospace #[meta-abbreviate-number(:value="shardItemCounts[shardNdx]")]
                      b-row(v-else no-gutters)
                        span.text-muted Shard &#35{{ shardNdx }} inactive

              meta-action-modal-button(
                id="inspect-entity-modal"
                permission="api.database.inspect_entity"
                action-button-text="Inspect Entity"
                ok-button-text="Inspect"
                :ok-button-disabled="!isEntityIdValid"
                :on-ok="inspectEntity"
                modal-title="Inspect an Entity"
                @show="resetModal"
                no-safety-lock
                )
                  p.mb-3 Entity inspection loads the stored data for the chosen entity and deserializes it into a human readable form without affecting the actual data.
                  small.text-muted Inspecting the raw data of an entity may help you spot problems with the related actor, as you can see the data before any potential mutations during wake-up.

                  div.mb-1.mt-3: strong Entity ID
                  //- TODO: replace this with a generic entity search & select component once we have one.
                  b-form-input(
                    name="entityId"
                    data-cy="player-id-input"
                    v-model="entityId"
                    placeholder="Player:000000003v"
                    required
                    :state="isEntityIdValid"
                    )
                  b-form-invalid-feedback(force-show) {{ isEntityIdValidReason || '&nbsp' }}

    //- Raw data
    meta-raw-data(:kvPair="databaseStatusData" name="dbStatus")
</template>

<script lang="ts" setup>
import { debounce } from 'lodash-es'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import { useGameServerApi } from '@metaplay/game-server-api'
import { MBadge, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import { getDatabaseStatusSubscriptionOptions, getStaticConfigSubscriptionOptions } from '../subscription_options/general'
import { isValidEntityId } from '../coreUtils'

const gameServerApi = useGameServerApi()

const {
  data: databaseStatusData,
  hasPermission: databaseStatusPermission,
  error: databaseStatusError
} = useSubscription(getDatabaseStatusSubscriptionOptions())

const {
  data: staticConfigData,
} = useSubscription(getStaticConfigSubscriptionOptions())

const dbOptions = computed(() => {
  return databaseStatusData.value?.options.values
})

/**
 * The total number of items in the database per item type.
 */
const totalItemCounts = computed((): { [id: string]: number } => {
  if (!databaseStatusData.value) return {}

  const totalItemCounts: { [id: string]: number } = {}
  for (const tableName of Object.keys(databaseStatusData.value.shardItemCounts)) {
    const shardItemCounts = databaseStatusData.value.shardItemCounts[tableName]
    totalItemCounts[tableName] = shardItemCounts.reduce((sum: number, v: number) => sum + v, 0)
  }
  return totalItemCounts
})

const clusterConfig = computed(() => {
  return staticConfigData.value?.clusterConfig
})

/**
 * Entity ID as entered into the modal.
 */
const entityId = ref('')

/**
 * Validation state for the entity ID input.
 */
const isEntityIdValid = ref<boolean | null>(false)

/**
 * Human-readable reason for the validation state.
 */
const isEntityIdValidReason = ref('')

/**
 * Validate 'entityId' when user input changes.
 */
watch(entityId, () => {
  isEntityIdValid.value = null
  isEntityIdValidReason.value = ''
  void debouncedValidation()
})

/**
 * Helper function to validate the `entityId` form input. Can be safely called on every keypress.
 */
const debouncedValidation = debounce(async () => {
  if (!entityId.value) {
    isEntityIdValid.value = null
    isEntityIdValidReason.value = ''
  } else if (!isValidEntityId(entityId.value)) {
    isEntityIdValid.value = false
    isEntityIdValidReason.value = 'Not a valid entity ID.'
  } else {
    void gameServerApi.get(`/entities/${entityId.value}/exists`)
      .then((result) => {
        const exists: boolean = result.data
        isEntityIdValid.value = exists
        isEntityIdValidReason.value = exists ? '' : 'Entity does not exist.'
      })
  }
}, 300)

/**
 * Reset contents of the modal when it is opened.
 */
function resetModal () {
  entityId.value = ''

  // Note: These two will be set to these values by `debouncedValidation` in response to `entityId` being reset, but
  // that takes a few hundred millisecond so we'll see a flash if we don't also initialize them here.
  isEntityIdValid.value = null
  isEntityIdValidReason.value = ''
}

const router = useRouter()
async function inspectEntity () {
  await router.push(`/entities/${entityId.value}/dbinfo`)
}
</script>
