<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :variant="singleNftCollectionData?.configWarning ? 'warning' : undefined"
  :is-loading="!singleNftCollectionData"
  :meta-api-error="singleNftCollectionError"
  :alerts="alerts"
)
  template(#overview)
    meta-page-header-card(
      data-cy="nft-collection-overview-card"
      :title="singleNftCollectionData?.ledgerInfo?.name ?? 'Name unknown'"
      :id="singleNftCollectionData.collectionId"
      :avatar="singleNftCollectionData?.ledgerInfo?.iconUrl"
    )
      template(#subtitle) {{ singleNftCollectionData?.ledgerInfo?.description }}

      div.font-weight-bold.mb-1 #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small responsive)
        b-tbody
          b-tr
            b-td Ledger
            b-td.text-right {{ singleNftCollectionData.ledger }}
          b-tr
            b-td Contract Address
            b-td.text-right(v-if="singleNftCollectionData.hasLedger" style="word-break: break-all;") {{ singleNftCollectionData.contractAddress }}
            b-td.text-right.text-muted(v-else) {{ singleNftCollectionData.ledger }} mode has no contracts
          b-tr
            b-td Metadata API URL
            b-td.text-right {{ singleNftCollectionData.metadataApiUrl }}
          b-tr
            b-td Metadata Management
            b-td.text-right
              MTooltip(:content="getMetadataManagementModeDescription(singleNftCollectionData.metadataManagementMode)") {{ singleNftCollectionData.metadataManagementMode }}

      //- Ledger-specific info. TODO: use a generated view instead?
      div.font-weight-bold.mb-1 #[fa-icon(icon="cubes")] Ledger Metadata
      div(v-if="!singleNftCollectionData.hasLedger").w-100.text-center
        p.text-muted {{ singleNftCollectionData.ledger }} mode has no associated ledger.
      div(v-else-if="!singleNftCollectionData.ledgerInfo").w-100.text-center
        p.text-muted Collection not configured in {{ singleNftCollectionData.ledgerName }}.
      div(v-else)
        b-table-simple(small responsive)
          b-tbody
            b-tr
              b-td Icon URL
              b-td.text-right: a(:href="singleNftCollectionData.ledgerInfo.iconUrl" target="_blank") {{ singleNftCollectionData.ledgerInfo.iconUrl }}
            b-tr
              b-td Image URL
              b-td.text-right: a(:href="singleNftCollectionData.ledgerInfo.collectionImageUrl" target="_blank") {{ singleNftCollectionData.ledgerInfo.collectionImageUrl }}

      template(#buttons)
        meta-action-modal-button(
          id="initialize-nft"
          permission="api.nft.initialize"
          action-button-text="Init single NFT"
          modal-title="Initialize a New NFT"
          ok-button-text="Initialize"
          :onOk="initializeNft"
          ).ml-1.mr-1
            p To mint a new NFT, its metadata need to be first initialized on the game server.
            meta-generated-form(
              typeName="Metaplay.Server.AdminApi.Controllers.NftController.NftInitializationParams"
              v-model="nftInitializationParams"
              @status="nftInitializationParamsValid = $event"
              )

        meta-action-modal-button(
          id="batch-initialize-nfts"
          actionButtonText="Init NFTs from CSV"
          modal-size="lg"
          modal-title="Batch Initialize new NFTs"
          ok-button-text="Batch Initialize"
          :onOk="batchInitializeNfts"
          :onShow="clearBatchInitializationState"
          ).ml-1.mr-1
            b-row
              b-col(lg="6").mb-3
                h6 Upload NFT Data
                p You can upload a list of NFTs in a CSV format to initialize them all in one go.

                div.mb-1.font-weight-bold Paste in a CSV string...
                b-form-textarea.mb-2(
                  v-model="csvString"
                  :placeholder="csvFile != null ? 'File upload selected' : singleNftCollectionData.batchInitPlaceholderText"
                  rows="5"
                  max-rows="10"
                  :state="csvString !== '' ? isCsvFormValid : null"
                  :disabled="csvFile != null"
                  @update="validateCsvContentDebounced")

                div.mb-1.font-weight-bold ...or upload as a file
                b-form-file.mb-3(
                  v-model="csvFile"
                  :state="Boolean(csvFile) ? isCsvFormValid : null"
                  :disabled="csvString !== ''"
                  :placeholder="csvString === '' ? 'Choose or drop a CSV file' : 'Manual paste selected'"
                  drop-placeholder="Almost there!"
                  accept=".csv")

                meta-input-checkbox(v-model="batchInitAllowOverwrite" name="Allow Overwrite" @change="validateCsvContentNow")
                  span Allow Overwrite
                  MBadge(tooltip="If overwriting is allowed, NFTs from this batch will overwrite the state of any existing NFTs with the same ids. Use with caution!" shape="pill").ml-1 ?

              b-col(lg="6")
                h6.mb-3 Preview Incoming Data
                b-alert(v-if="!nftPreview" show variant="secondary") Paste in a valid list of NFTs compatible with the game server's version to see a preview of the import results.
                b-spinner(v-if="nftPreviewIsLoading")
                b-alert(v-if="csvValidationError" show variant="warning") {{ csvValidationError }}

                div(v-if="nftPreview")
                  div {{ maybePlural(nftPreview.nfts.length, 'NFT') }} will be initialized.
                  div(v-if="batchInitAllowOverwrite") {{ maybePlural(nftPreview.numNftsOverwritten, 'existing NFT') }} will be overwritten.
                  MList.mt-2
                    MListItem(
                      v-for="nft in nftPreview.nfts.slice(0, batchInitPreviewMaxLength)"
                      :key="nft.tokenId"
                      :avatarUrl="nft.imageUrl"
                      )
                      span(v-if="nft.name") {{ nft.name }}
                      span.font-italic(v-else) Unnamed {{ nft.typeName }}
                      span.small.text-muted.ml-1 {{ collectionId }}/{{ nft.tokenId }}

                      template(#top-right) {{ getItemStatusText(nft) }}

                      template(#bottom-left)
                        div(v-if="nft.description") {{ nft.description }}
                        span.font-italic(v-else) No description.
                    MListItem(v-if="nftPreview.nfts.length > batchInitPreviewMaxLength" key="nfts-omitted-dummy")
                      span.small.text-muted.ml-2 ... and {{ maybePlural(nftPreview.nfts.length - batchInitPreviewMaxLength, 'more NFT') }} omitted from this preview.

        meta-action-modal-button(
          id="batch-initialize-nfts-from-metadata"
          permission="api.nft.initialize"
          action-button-text="Init NFTs from metadata"
          modal-title="Initialize NFTs from existing metadata"
          ok-button-text="Initialize"
          :onOk="batchInitializeNftsFromMetadata"
          ).ml-1.mr-1
            p You can initialize a batch of NFTs (with sequential ids) based on existing metadata publicly served at the NFTs' metadata URLs.

            meta-generated-form(
              typeName="Metaplay.Server.AdminApi.Controllers.NftController.NftBatchInitializationFromMetadataParams"
              v-model="nftInitializationFromMetadataParams"
              @status="nftInitializationFromMetadataParamsValid = $event"
              )

        meta-action-modal-button(
          id="refresh-nft-collection"
          actionButtonText="Refresh metadata"
          modal-title="Refresh NFT Collection Metadata"
          permission="api.nft.refresh_from_ledger"
          :onOk="refreshCollectionLedgerInfo"
          :actionButtonDisabled="!singleNftCollectionData.hasLedger"
          :actionButtonTooltip="!singleNftCollectionData.hasLedger ? `${singleNftCollectionData.ledger} mode has no ledger to refresh from.` : undefined"
          ).ml-1.mr-1
            p You can immediately trigger a refresh of the collection's ledger metadata from {{ singleNftCollectionData.ledgerName }}.
            div.text-muted.small This happens automatically in the background, so manual refreshing is not needed in daily operations.

  //- BODY CONTENT -------------------------------
  template(#default)
    b-row(no-gutters align-v="center").mt-3.mb-2
      h3 Collection Data

    b-row(align-h="center")
      b-col(lg="6").mb-3
        meta-list-card.h-100(
          data-cy="nft-collection-nft-list"
          :itemList="singleNftCollectionData.nfts"
          :searchFields="searchFields"
          :filterSets="filterSets"
          title="NFTs"
          emptyMessage="No NFTs in this collection"
          )
          template(#item-card="{ item }")
            MListItem(:avatarUrl="item.imageUrl")
              span(v-if="item.queryError !== null") 🛑 Failed to load!
              span(v-else-if="item.name") {{ item.name }}
              span.font-italic(v-else) Unnamed {{ item.typeName }}
              span.small.text-muted.ml-1 {{ collectionId }}/{{ item.tokenId }}

              template(v-if="item.queryError === null" #top-right) {{ getItemStatusText(item) }}

              template(v-if="item.queryError === null" #bottom-left)
                div(v-if="item.description") {{ item.description }}
                span.font-italic(v-else) No description.

              template(#bottom-right)
                meta-button(link :to="`/web3/nft/${collectionId}/${item.tokenId}`" data-cy="view-nft" permission="api.nft.view") View NFT

      b-col(lg="6").mb-3
        meta-list-card.h-100(
          data-cy="nft-collection-uninitialized-nfts-card"
          :itemList="singleNftCollectionData.uninitializedNfts"
          :searchFields="uninitializedNftsSearchFields"
          title="Recent orphan NFTs"
          emptyMessage="No orphan NFTs encountered for this collection."
          :description="`The most recently-encountered NFTs that have been minted in ${singleNftCollectionData.ledgerName} but haven't been initialized in the game. Ideally, this should never happen in production.`"
          dangerous
          )
          template(#item-card="{ item }")
            MListItem
              | {{ collectionId }}/{{ item.tokenId }}
              template(#top-right)
                span(v-if="item.owner === 'None'") No owning player
                span(v-else) Owning player: #[meta-button(link permission="api.players.view" :to="`/players/${item.owner}`") {{ item.owner }}]
              template(#bottom-left) Owner: {{ item.ownerAddress }}

    b-row(no-gutters align-v="center").mt-3.mb-2
      h3 Admin

    b-row(align-h="center")
      b-col(lg="6").mb-3
        audit-log-card.h-100(
          data-cy="nft-collection-audit-log-card"
          targetType="$NftCollection"
          :targetId="collectionId"
          )

    meta-raw-data(:kvPair="singleNftCollectionData", name="collection")
</template>

<script lang="ts" setup>
import { BFormFile } from 'bootstrap-vue'
import { debounce } from 'lodash-es'
import { computed, ref, watch } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { MetaListFilterOption, MetaListFilterSet, showSuccessToast, showErrorToast, maybePlural } from '@metaplay/meta-ui'
import { MBadge, MTooltip, MList, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import AuditLogCard from '../components/auditlogs/AuditLogCard.vue'
import { getSingleNftCollectionSubscriptionOptions } from '../subscription_options/web3'
import useHeaderbar from '../useHeaderbar'

import MetaGeneratedForm from '../components/generatedui/components/MetaGeneratedForm.vue'
import { isNullOrUndefined } from '../coreUtils'

const props = defineProps<{
  /**
   * ID of the collection this NFT belongs to.
   * @example 'SomeCollection'
   */
  collectionId: string
}>()

const {
  data: singleNftCollectionData,
  error: singleNftCollectionError,
  refresh: singleNftCollectionRefresh,
} = useSubscription(getSingleNftCollectionSubscriptionOptions(props.collectionId))

// Update the headerbar title dynamically as data changes.
useHeaderbar().setDynamicTitle(singleNftCollectionData, (singleNftCollectionData) => `Manage ${(singleNftCollectionData.value).collectionId || 'Collection'}`)

const alerts = computed(() => {
  const alerts = []

  if (singleNftCollectionData.value?.configWarning?.length > 0) {
    alerts.push({
      title: singleNftCollectionData.value.configWarning.title as string,
      message: singleNftCollectionData.value.configWarning.message as string,
    })
  }

  const numPendingMetadataWrites = getNumPendingMetadataWrites()
  if (numPendingMetadataWrites !== 0) {
    alerts.push({
      title: 'Metadata write operation in progress',
      message: `The metadata files of ${maybePlural(numPendingMetadataWrites, 'NFT')} in this collection are currently being written in the background. If new NFTs were just initialized, you should wait for this operation to complete before minting the NFTs.`
    })
  }

  if (isNullOrUndefined(singleNftCollectionData.value)) {
    for (const ongoingMetadataDownload of singleNftCollectionData.value.ongoingMetadataDownloads) {
      const firstToken = ongoingMetadataDownload.firstTokenId
      const lastToken = ongoingMetadataDownload.lastTokenId
      const numDownloaded = ongoingMetadataDownload.numDownloaded
      const numTotal = ongoingMetadataDownload.numTotal

      alerts.push({
        key: ongoingMetadataDownload.taskId,
        title: 'Metadata download in progress',
        message: `The server is currently downloading the metadata of tokens ${firstToken} to ${lastToken}. When the download is finished, the NFTs will get initialized in the server. Progress: ${numDownloaded}/${numTotal}.`
      })
    }
  }

  return alerts
})

function getNumPendingMetadataWrites (): number {
  if (!singleNftCollectionData.value) {
    return 0
  }

  let count = 0
  for (const nft of singleNftCollectionData.value.nfts) {
    if (nft.hasPendingMetadataWrite === true) {
      count += 1
    }
  }

  return count
}

// INDIVIDUAL NFT INIT STUFF ----------------------------------------------------

// TODO: form validation, error handling

const nftInitializationParams = ref<any>({})
const nftInitializationParamsValid = ref(false)

async function initializeNft (): Promise<void> {
  // \todo #nft #nft-init-token-id-kludge
  //       Getting tokenId from nftInitializationParams is hacky:
  //       really it shouldn't be a member of nftInitializationParams at all, but
  //       should be a separate variable with its own input in the form. I only
  //       did it this way to piggyback on the automatic form generation, to
  //       easily get the input for the tokenId.
  let tokenIdUrlPart = nftInitializationParams.value.tokenId
  // Empty token id means we want the server to auto-generate the id.
  // However, an empty url part seems to cause trouble, so encode differently.
  if (tokenIdUrlPart === '' || tokenIdUrlPart === null || tokenIdUrlPart === undefined) {
    tokenIdUrlPart = 'automaticTokenId'
  }

  await useGameServerApi().post(`nft/${props.collectionId}/${tokenIdUrlPart}/initialize`, nftInitializationParams.value)
  showSuccessToast('New NFT initialized!')
  singleNftCollectionRefresh()
  nftInitializationParams.value = {}
}

// BATCH INIT STUFF ----------------------------------------------------

const batchInitPreviewMaxLength = 5

const csvString = ref('')
const csvFile = ref<any>()
const csvFileContents = ref('')
const nftPreview = ref<any>()
const nftPreviewIsLoading = ref<boolean>(false)
const csvValidationError = ref<any>()
const batchInitAllowOverwrite = ref<boolean>(false)
const isCsvFormValid = computed(() => {
  const hasContent = getCsvContent() != null
  if (hasContent && !csvValidationError.value && !nftPreview.value) {
    return null
  } else if (nftPreview.value) {
    return true
  } else {
    return false
  }
})

watch(csvFile, (newFile) => {
  if (newFile) {
    csvFileContents.value = ''
    const reader = new FileReader()
    reader.addEventListener('load', (event) => {
      csvFileContents.value = String(event?.target?.result)
    })
    reader.readAsText(newFile)
  }
})

watch(csvFileContents, validateCsvContentNow)

function getCsvContent (): string | null {
  if (csvString.value.length > 0) {
    return csvString.value
  } else if (csvFileContents.value !== '') {
    return csvFileContents.value
  } else {
    return null
  }
}

function clearBatchInitializationState (): void {
  csvString.value = ''
  csvFile.value = null
  csvFileContents.value = ''
  nftPreview.value = null
  nftPreviewIsLoading.value = false
  csvValidationError.value = null
  batchInitAllowOverwrite.value = false
}

async function validateCsvContentNow (): Promise<void> {
  const csvContent = getCsvContent()
  if (csvContent === null) {
    return
  }

  nftPreviewIsLoading.value = true
  csvValidationError.value = null
  nftPreview.value = null

  try {
    const response = await useGameServerApi().post(`nft/${props.collectionId}/batchInitialize`, { csv: csvContent, allowOverwrite: batchInitAllowOverwrite.value, validateOnly: true })
    if (response.data.isSuccess) {
      csvValidationError.value = null
      nftPreview.value = {
        nfts: response.data.nfts,
        numNftsOverwritten: response.data.numNftsOverwritten
      }
    } else {
      csvValidationError.value = response.data.error.message + ' ' + response.data.error.details
      nftPreview.value = null
    }
  } catch (ex: any) {
    const error = ex.response.data.error
    csvValidationError.value = error.message + ' ' + error.details
    nftPreview.value = null
  } finally {
    nftPreviewIsLoading.value = false
  }
}
const validateCsvContentDebounced = debounce(validateCsvContentNow, 500)

async function batchInitializeNfts (): Promise<void> {
  const csvContent = getCsvContent()
  const response = await useGameServerApi().post(`nft/${props.collectionId}/batchInitialize`, { csv: csvContent, allowOverwrite: batchInitAllowOverwrite.value })
  if (response.data.isSuccess) {
    showSuccessToast(`Batch of ${maybePlural(response.data.nfts.length, 'NFT')} initialized!`)
    singleNftCollectionRefresh()
  } else {
    showErrorToast(response.data.error.message + ' ' + response.data.error.details)
  }
}

// BATCH INIT FROM METADATA STUFF ----------------------------------------------------

const nftInitializationFromMetadataParams = ref<any>({})
const nftInitializationFromMetadataParamsValid = ref(false)

async function batchInitializeNftsFromMetadata (): Promise<void> {
  const response = await useGameServerApi().post(`nft/${props.collectionId}/batchInitializeFromMetadata`, nftInitializationFromMetadataParams.value)
  showSuccessToast(`Batch of ${maybePlural(response.data.nfts.length, 'NFT')} initialized based on existing metadata!`)
  singleNftCollectionRefresh()
}

// REFRESH ----------------------------------------------------

async function refreshCollectionLedgerInfo (): Promise<void> {
  await useGameServerApi().post(`nft/${props.collectionId}/refresh`, nftInitializationParams.value)
  showSuccessToast('Ledger metadata refreshed!')
  singleNftCollectionRefresh()
}

// UI FILTERS, SEARCH, ETC. ----------------------------------------------------

const searchFields = [
  'name',
  'description',
  'tokenId',
  'owner',
  'ownerAddress',
  'typeName'
]

const uninitializedNftsSearchFields = [
  'tokenId',
  'owner',
  'ownerAddress'
]

const filterSets = [
  new MetaListFilterSet('status',
    [
      new MetaListFilterOption('Player-owned', (item: any) => item.owner !== 'None'),
      new MetaListFilterOption('Non-player-owned', (item: any) => item.owner === 'None' && item.isMinted),
      new MetaListFilterOption('Not minted', (item: any) => !item.isMinted)
    ]
  )
]

function getItemStatusText (item: any): string {
  if (item.owner !== 'None') {
    return 'Player-owned'
  } else if (item.ownerAddress !== 'None') {
    return 'Non-player-owned'
  } else {
    return 'Not minted'
  }
}

function getMetadataManagementModeDescription (mode: string): string | undefined {
  if (mode === 'Authoritative') {
    return 'NFT metadata is written by this game.'
  } else if (mode === 'Foreign') {
    return 'NFT metadata is written externally, and this game only reads the metadata.'
  } else {
    return undefined
  }
}
</script>
