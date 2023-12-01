<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
div#system-import-entities-card
  //- Card
  b-card(title="Import Game Entities")
    p You can use this feature to import an archive of one or more game entities into the current deployment.

    meta-action-modal-button.float-right(
      id="batch-import-entities"
      permission="api.entity_archive.import"
      action-button-text="Open Import Menu"
      :on-ok="importArchive"
      @show="resetModal"
      modal-title="Import Game Entities"
      ok-button-text="Import Archive"
      :ok-button-disabled="!isFormValid"
      modal-size="lg"
      )
        b-row
          b-col(lg="6").mb-3
            h6 Paste Archive Data
            p You can upload the serialized data of an #[MBadge entity archive] here to import them into the current deployment.

            div.mb-1.font-weight-bold Paste in an archive...
            b-form-textarea.mb-2(
              v-model="entityArchiveText"
              :placeholder="entityArchiveFile != null ? 'File upload selected' : `{'entities':{'player':...`"
              rows="5"
              max-rows="10"
              :state="isFormValid"
              :disabled="entityArchiveFile != null "
              @update="validateArchive"
              )

            div.mb-1.font-weight-bold ...or upload as a file
            b-form-file.mb-3(
              v-model="entityArchiveFile"
              :state="Boolean(entityArchiveFile) ? true : null"
              :disabled="entityArchiveText !== ''"
              :placeholder="entityArchiveText === '' ? 'Choose or drop an entity archive file' : 'Manual paste selected'"
              drop-placeholder="Almost there!"
              accept=".json")

            MInputSegmentedSwitch(
              :model-value="overwritePolicy"
              @update:model-value="updateOverwritePolicy($event)"
              :options="overwritePolicyOptions"
              label="Overwrite policy"
              )

            div.text-muted.mt-2
              p.mb-1
                small Overwrite policy determines how to deal with conflicting entities during import:
              p.m-0
                small #[span.font-weight-bold Ignore] - Duplicate entities will not be imported.
              p.m-0
                small #[span.font-weight-bold Overwrite] - Duplicate entities will overwrite existing ones.
              p.m-0
                small #[span.font-weight-bold Create New] - Duplicate entities will be imported as new entities with new unique ID's.

          b-col(lg="6")
            h6.mb-3 Preview Incoming Data
            b-alert(v-if="!validationResultsObject" show variant="secondary") Paste in a valid #[MBadge entity archive entity archive] from a compatible game version to preview what data will be copied over.

            MList(v-if="validationResultsObject && validationResultsObject.length > 0" showBorder)
              MListItem(
                v-for="entity in validationResultsObject"
                :key="entity.sourceId"
                class="tw-px-3"
                )
                span.font-weight-bold Source: {{ entity.sourceId }}
                template(#top-right)
                  MBadge(v-if="!entity.error" variant="success") Validation ok
                  MBadge(v-else variant="danger") Validation error

                template(#bottom-left)
                  span(v-if="entity.error")
                    div(class="tw-text-red-500") {{ entity.error.message }}
                    div(v-if="entity.error.details" class="tw-text-red-500") {{ entity.error.details }}
                  span(v-else) {{ importMessage(entity, 'preview') }}
            b-alert(v-else show variant="warning") Entity archive is empty. Are you sure you pasted the right thing?

            div.mt-3(v-if="validationResultDiff")
              div.code-box.text-monospace.border.rounded.bg-light.w-100(style="max-height: 20.3rem")
                pre {{ validationResultDiff }}

            b-alert(v-if="displayError" show variant="warning")
              div(v-if="displayError.title").font-weight-bolder {{ displayError?.title }}
              p(v-if="displayError.message") {{ displayError?.message }}
                pre(v-if="displayError.details" style="font-size: 70%") {{ displayError?.details }}

        b-row.justify-content-center
          meta-no-seatbelts.mt-3/

  b-modal#import-entities-results-modal(title="Import completed" size="md" ok-only centered no-close-on-backdrop)
    MList(v-if="importResultsObject && importResultsObject.length > 0" showBorder)
      MListItem(
        v-for="entity in importResultsObject"
        :key="entity.sourceId"
        class="tw-px-3"
        )
        span(v-if="String(entity.destinationId).startsWith('Player')") #[fa-icon(icon="user")]  {{ entity.destinationId }}
        span(v-else-if="String(entity.destinationId).startsWith('Guild')") #[fa-icon(icon="chess-rook")] {{ entity.destinationId }}]
        span(v-else) {{ entity.destinationId }}
        template(#top-right)
          MBadge(v-if="entity.status === 'Success'" variant="success") Imported
          MBadge(v-else-if="entity.status === 'Error'" variant="danger") Error
          MBadge(v-else variant="warning") Skipped
        template(#bottom-left) {{ importMessage(entity) }}
        template(#bottom-right)
          span(v-if="entity.status === 'Success'")
            span(v-if="String(entity.destinationId).startsWith('Player')") #[router-link(:to="`/players/${entity.destinationId}`") View player]
            span(v-else-if="String(entity.destinationId).startsWith('Guild')") #[router-link(:to="`/guilds/${entity.destinationId}`") View guild]
    b-alert(v-else show variant="warning") Empty entity archive.

</template>

<script lang="ts" setup>
import axios from 'axios'
import type { CancelTokenSource } from 'axios'
import { BFormFile } from 'bootstrap-vue'
import { computed, getCurrentInstance, ref, watch } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { MList, MListItem, MBadge, MInputSegmentedSwitch } from '@metaplay/meta-ui-next'

const gameServerApi = useGameServerApi()
const entityArchiveText = ref('')
const entityArchiveFile = ref<any>()
const entityArchiveFileContents = ref('')
const validationResultsObject = ref<any>()
const validationResultDiff = ref('')
const displayError = ref<{
  title?: string
  message?: string
  details?: string
}>()
const overwritePolicy = ref<'ignore' | 'overwrite' | 'createnew'>('ignore')

function updateOverwritePolicy (value: string) {
  if (value === 'ignore' || value === 'overwrite' || value === 'createnew') {
    overwritePolicy.value = value
    validateArchive().catch(error => console.error(`Error validating archive: ${error}`))
  } else {
    console.error(`Invalid value for overwritePolicy: ${value}`)
  }
}

const importResultsObject = ref<any>()

const isFormValid = computed(() => {
  const hasArchive = entityArchiveText.value !== '' || entityArchiveFile.value !== null
  if (hasArchive && !validationResultsObject.value) {
    return undefined
  } else if (validationResultsObject.value) {
    return true
  } else {
    return false
  }
})

watch(entityArchiveFile, (newFile) => {
  if (newFile) {
    entityArchiveFileContents.value = ''
    const reader = new FileReader()
    reader.addEventListener('load', (event) => {
      entityArchiveFileContents.value = String(event?.target?.result)
    })
    reader.readAsText(newFile)
  }
})

const overwritePolicyOptions = [
  { value: 'ignore', label: 'Ignore' },
  { value: 'overwrite', label: 'Overwrite' },
  { value: 'createnew', label: 'Create New' }
]

function resetModal () {
  entityArchiveText.value = ''
  entityArchiveFile.value = null
  entityArchiveFileContents.value = ''
  validationResultsObject.value = undefined
  displayError.value = undefined
  overwritePolicy.value = 'ignore'
}

watch(entityArchiveFileContents, validateArchive)

let cancelTokenSource: CancelTokenSource

async function validateArchive (newText?: string) {
  displayError.value = undefined
  validationResultsObject.value = undefined
  validationResultDiff.value = ''

  if (entityArchiveText.value !== '' || entityArchiveFileContents.value !== '') {
    // Get the payload data that we want to validate.
    let payload
    try {
      payload = calculatePayload()
    } catch (e: any) {
      displayError.value = { message: e.message }
      return
    }

    // Send the payload to the server to validate it.
    try {
      if (cancelTokenSource) { cancelTokenSource.cancel('Request canceled by user interaction.') }
      cancelTokenSource = axios.CancelToken.source()
      const result = (await gameServerApi.post(`/entityArchive/validate?overwritePolicy=${overwritePolicy.value}`, payload)).data
      if (result.error) {
        // If the result has an error object then it failed.
        displayError.value = {
          title: 'Validation failed',
          message: result.error.message,
          details: result.error.details
        }
      } else {
        // Otherwise there must be a data object, indicating success.
        validationResultsObject.value = result.entities
        // Otherwise there must be a data object, indicating success.
        result.entities.map((res: any) => {
          if (res.overwriteDiff !== null) {
            validationResultDiff.value = res.overwriteDiff
          }
          return validationResultDiff.value
        })
      }
    } catch (e: any) {
      if (axios.isCancel(e)) {
        console.log(e)
        // Ignore.
      } else {
        displayError.value = { message: e }
      }
    }
  }
}
const vueInstance = getCurrentInstance()?.proxy as any

/**
 * Text to display to let user know what happens after entity is imported.
 * @param entity  The imported entity.
 * @param displayStatus Current stage at which message is being shown. Either during 'Preview' or after import is complete.
 */
function importMessage (entity: any, importStage?: string) {
  if (entity.status === 'Success' && overwritePolicy.value === 'ignore') {
    return importStage === 'preview' ? 'New entity will be created.' : 'New entity created.'
  } else if (entity.status === 'Ignored') {
    return importStage === 'preview' ? 'Existing entity will be preserved.' : `Existing entity ${entity.sourceId} has been preserved.`
  }
  if (entity.status === 'Success' && overwritePolicy.value === 'overwrite' && entity.overwriteDiff !== null) {
    return importStage === 'preview' ? 'Existing entity will be overwritten.' : `Existing entity ${entity.sourceId} has been overwritten.`
  } else if (entity.status === 'Success' && overwritePolicy.value === 'overwrite' && entity.overwriteDiff === null) {
    return importStage === 'preview' ? 'New entity will be created.' : 'New entity created.'
  } else if (entity.status === 'Success' && overwritePolicy.value === 'createnew') {
    return importStage === 'preview' ? `A new entity will be created from ${entity.sourceId}.` : `A new entity ${entity.destinationId} has been created. `
  } else return ''
}

async function importArchive () {
  const payload = calculatePayload()
  const result = (await gameServerApi.post(`/entityArchive/import?overwritePolicy=${overwritePolicy.value}`, payload)).data

  if (result.error) {
    // \todo: custom DisplayError type for API errors?
    throw new Error(`${result.error.message}\n${result.error.details}`)
  }

  importResultsObject.value = result.entities
  vueInstance?.$bvModal.show('import-entities-results-modal')
}

function calculatePayload () {
  // Get the entity archive text from whichever source is available.
  const source: string = entityArchiveText.value !== '' ? entityArchiveText.value : entityArchiveFileContents.value
  let payload: any
  try {
    payload = JSON.parse(source)
  } catch (e: any) {
    throw new Error(`Could not parse archive. Got '${e.message}'.`)
  }

  // Validate the entity archive somewhat.
  if (typeof payload !== 'object') {
    throw new Error(`Entity archive must be an object. Got '${typeof payload}'.`)
  }
  if (Array.isArray(payload)) {
    throw new Error('Entity archive must be an object. Got \'array\'.')
  }
  if (!('entities' in payload)) {
    throw new Error('Entity archive must contain entities but none found.')
  }
  return payload
}
</script>
