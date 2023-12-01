<template lang="pug">
MCard(
  data-cy="localization-contents-card"
  title="Localization Contents"
  :badgeVariant="badgeVariant"
  :badge="badgeValue"
  :isLoading="!localizationTableData"
  )
  //- Header.
  template(#icon)
    fa-icon(icon="language")

  template(#header-right)
    MInputText(
      :model-value="searchString"
      @update:modelValue="updateSearchString"
      :placeholder="'Search...'"
      )

  template(#subtitle v-if="totalMissingLocalizationValues > 0")
    div(class="tw-text-red-500") {{ totalMissingLocalizationValues }} missing localizations!
    div
      span Show only keys with missing values
      MInputSwitch(
        :model-value="showMissingValuesOnly"
        @update:modelValue="switchValue => showMissingValuesOnly = switchValue"
        name="Show only keys with missing values"
        size="xs"
        class="tw-ml-2"
        )

  div(class="tw-overflow-x-auto")
    table(v-if="!(searchString && filteredItemList.length === 0)" class="tw-w-full")
      thead
        tr(class="tw-text-xs+")
          th(class="tw-pl-2") Localization Key
          th(
            v-for="languageId in languageIds"
            :key="languageId"
            class="tw-pl-2"
            ) {{ languageId }}
      tbody
        //- Library is empty.
        tr(v-if="localizationTableData?.table.length === 0" class="tw-border-x tw-border-neutral-200 tw-border-b")
          td(
            v-for="col in localizationTableData.table.length + 2"
            :class="['tw-px-2 tw-text-xs tw-italic tw-text-neutral-400 tw-border-neutral-200 tw-border-t', { 'tw-border-l': col > 1 }]"
            ) Library is empty.
        tr(
          v-else
          v-for="localizationEntry in currentPageItemList"
          :key="localizationEntry.title"
          :class="['tw-text-xs tw-border-neutral-200 tw-border odd:tw-bg-neutral-100 tw-align-top', { 'hover:tw-cursor-pointer hover:tw-bg-neutral-300 active:tw-bg-neutral-400': localizationEntry.isAnyLanguageValueTruncated }]"
          @click="onLocalizationEntryClicked(localizationEntry.title)"
          )
            //- Row has values that are too long -> show truncated elements.
            template(v-if="localizationEntry.isAnyLanguageValueTruncated")
              td(class="tw-pl-2 tw-py-1.5 tw-font-mono tw-whitespace-nowrap")
                div(style="min-width: 14rem")
                  fa-icon(icon="angle-right" :class="['tw-mr-1', { 'tw-rotate-90': expandedLocalizationKeys[localizationEntry.title] }]")
                  span {{ localizationEntry.title }}
                  fa-icon(v-if="localizationEntry.isAnyLanguageValueMissing" icon="circle-exclamation" class="tw-text-red-500 tw-ml-1 tw-relative -tw-bottom-[1px]")

              td(
                v-for="languageId in languageIds"
                :key="languageId"
                class="tw-border-l tw-border-neutral-200 tw-pl-2 tw-py-1.5"
                )
                div(:class="['tw-w-56 tw-break-words', { 'tw-truncate': !expandedLocalizationKeys[localizationEntry.title], 'tw-whitespace-pre-wrap': expandedLocalizationKeys[localizationEntry.title], 'tw-text-red-500 tw-italic': localizationEntry.values[languageId].startsWith('#missing') }]") {{ localizationEntry.values[languageId].startsWith('#missing') ? 'Missing value' : localizationEntry.values[languageId] }}
                //- Alternative render style for reference.
                //- div(:class="['tw-w-56 tw-break-words', { 'tw-line-clamp-3': !expandedLocalizationKeys[localizationEntry.title], 'tw-whitespace-pre-wrap': expandedLocalizationKeys[localizationEntry.title], 'tw-text-red-500 tw-italic': !localizationEntry.values[languageId] }]") {{ localizationEntry.values[languageId] ? localizationEntry.values[languageId] : 'Missing value' }}
            template(v-else)
              td(class="tw-pl-2 tw-py-1.5 tw-font-mono tw-whitespace-nowrap")
                div(style="min-width: 14rem")
                  span {{ localizationEntry.title }}
                  fa-icon(icon="circle-exclamation" v-if="localizationEntry.isAnyLanguageValueMissing" class="tw-text-red-500 tw-ml-1 tw-relative -tw-bottom-[1px]")
              td(
                v-for="languageId in languageIds"
                :key="languageId"
                class="tw-border-l tw-border-neutral-200 tw-pl-2 tw-py-1.5"
                )
                div(:class="['tw-w-56 tw-overflow-x-hidden', { 'tw-text-red-500 tw-italic': localizationEntry.values[languageId].startsWith('#missing') }]") {{ localizationEntry.values[languageId].startsWith('#missing') ? 'Missing value' : localizationEntry.values[languageId] }}
    div(v-else class="tw-italic tw-text-neutral-500") No results. Try a different search?

  //- Pagination controls.
  //- TODO: remove bootstrap styles in favor of tailwind like the outmost div
  div(v-if="showPagingControls" class="tw-text-center tw-mt-4")
    b-button-group(size="sm")
      b-button(:disabled="currentPage == 0" @click="gotoPageAbsolute(0)")
        fa-icon(:icon="['fas', 'fast-backward']")
      b-button(:disabled="currentPage == 0" @click="gotoPageRelative(-1)")
        fa-icon(icon="backward")
      div.px-3.bg-secondary.text-light.pagination-shadow(style="padding-top: 0.3rem; min-width: 4rem")
        small {{ currentPage + 1 }} / {{ totalPageCount }}
      b-button(:disabled="currentPage == (totalPageCount - 1)" @click="gotoPageRelative(+1)")
        fa-icon(icon="forward")
      b-button(:disabled="currentPage == (totalPageCount - 1)" @click="gotoPageAbsolute(totalPageCount - 1)")
        fa-icon(:icon="['fas', 'fast-forward']")
</template>

<script lang="ts" setup>
import { computed, ref, watch } from 'vue'
import { useSubscription } from '@metaplay/subscriptions'

import { BButtonGroup } from 'bootstrap-vue'
import { MCard, MInputText, MInputSwitch } from '@metaplay/meta-ui-next'
import type { LocalizationTable, LocalizationTableItem } from '../../localizationServerTypes'
import { clamp, debounce } from 'lodash-es'

import { getSingleLocalizationTableSubscriptionOptions } from '../../subscription_options/localization'

const props = defineProps<{localizationId: string}>()

interface DecoratedLocalizationTableItem extends LocalizationTableItem {
  /**
   * Indicates true if the localization entry has a value that is missing.
   */
  isAnyLanguageValueMissing?: boolean
  /**
   * Indicates true if the localization entry has a value that is too long to display in the table.
   */
  isAnyLanguageValueTruncated?: boolean
}

/**
 * Search string entered by user.
 */
const searchString = ref<string>()

// Data ---------------------------------------------------------------------------------------------------------------

const {
  data: localizationTableData,
} = useSubscription<LocalizationTable>(getSingleLocalizationTableSubscriptionOptions(props.localizationId))

const languageIds = ref(localizationTableData.value?.languageIds)

/**
 * A computed list of all items, each enhanced with a flag indicating if any language value is missing.
 */
const decoratedItemList = computed((): DecoratedLocalizationTableItem[] => {
  return localizationTableData.value
    ? localizationTableData.value.table.map(item => ({
      ...item,
      isAnyLanguageValueMissing: (languageIds.value?.some(languageId => item.values[languageId] === undefined || item.values[languageId] === '' || item.values[languageId].startsWith('#missing'))) ?? false
    }))
    : []
})

/**
 * Returns one page of items. This is the data that actually gets rendered.
 */
const filteredItemList = ref<DecoratedLocalizationTableItem[]>([])

/**
 * Filters the displayed item list based on the search string. This is debounced to avoid lagging the UI.
 */
const filterItemList = debounce(() => {
  const toggleMissingLanguageValuesItemList = showMissingValuesOnly.value
    ? decoratedItemList.value.filter(item => item.isAnyLanguageValueMissing)
    : decoratedItemList.value

  if (searchString.value && searchString.value.length > 0) {
    const searchTerm: string = searchString.value.toLowerCase()
    filteredItemList.value = toggleMissingLanguageValuesItemList.filter(item => {
      if (item.title.toLowerCase().includes(searchTerm)) return true
      if (Object.values(item.values).some(value => value?.toLowerCase().includes(searchTerm))) return true
      return false
    })
  } else {
    filteredItemList.value = toggleMissingLanguageValuesItemList
  }
}, 200)

/**
 * Value for MInputSwitch toggle. When true, filters the displayed items to only those with missing language values.
 */
const showMissingValuesOnly = ref(false)

watch(showMissingValuesOnly, filterItemList, { immediate: true })

// Search -------------------------------------------------------------------------------------------------------------

/**
 * Updates the search string and triggers a debounced item filtering.
 * @param input - The new search string.
 */
function updateSearchString (input: string | undefined) {
  searchString.value = input
  filterItemList()
}

// UI -----------------------------------------------------------------------------------------------------------------

const badgeVariant = computed(() => {
  return localizationTableData.value?.info.status === 'Succeeded' ? 'primary' : 'danger'
})

/**
 * When unfiltered, shows total number of rows, and when filtered, shows number of currently visible rows.
 */
const badgeValue = computed(() => {
  const tableLength = localizationTableData.value?.table.length
  if (localizationTableData.value?.info.status === 'Succeeded') {
    if (filteredItemList.value.length === tableLength) {
      return tableLength
    } else {
      return filteredItemList.value.length + ' / ' + tableLength
    }
  } else {
    return 'Error'
  }
})

const totalMissingLocalizationValues = computed(() => {
  return showMissingValuesOnly.value ? filteredItemList.value.length : filteredItemList.value.filter(item => item.isAnyLanguageValueMissing).length
})

/**
 * List of localization keys that are expanded (ie: showing the full text of all values).
 */
const expandedLocalizationKeys = ref<{ [key: string]: boolean }>({})

/**
 * Toggle the expansion state of a localization key.
 */
function onLocalizationEntryClicked (keyId: string) {
  expandedLocalizationKeys.value[keyId] = !expandedLocalizationKeys.value[keyId]
}

// Pagination ---------------------------------------------------------------------------------------------------------

const pageSize = 100

/**
 * A computed list of items for the current page, each enhanced with a flag indicating if any language value is truncated.
 */
const currentPageItemList = computed((): DecoratedLocalizationTableItem[] => {
  if (showPagingControls.value) {
    const start = currentPage.value * pageSize
    const pageItems = filteredItemList.value.slice(start, start + pageSize)
    return pageItems.map(item => ({
      ...item,
      isAnyLanguageValueTruncated: Object.values(item.values).some(value => !value.startsWith('#missing') && value?.length > 37)
    }))
  } else {
    return filteredItemList.value.map(item => ({
      ...item,
      isAnyLanguageValueTruncated: Object.values(item.values).some(value => value?.length > 37)
    }))
  }
})

/**
 * Only show the paging controls if we have >1 page of items.
 */
const showPagingControls = computed((): boolean => {
  return totalPageCount.value > 1
})

/**
 * How many pages we would need to show all of the items.
 */
const totalPageCount = computed((): number => {
  const itemCount = filteredItemList.value.length
  return Math.ceil(itemCount / pageSize)
})

/**
 * This is the page that we have chosen to view. Sometimes this page ends up being outside the valid page range (eg:
 * when a search/filter changes the total number of items/pages) but we keep track of the original number to prevent
 * surprising page changes as searches/filters are changed.
 */
const desiredCurrentPage = ref(0)

/**
 * The current page that we are viewing. Takes `desiredCurrentPage` and clamps it to a valid range.
 */
const currentPage = computed((): number => {
  return Math.min(desiredCurrentPage.value, totalPageCount.value - 1)
})

/**
 * Jump directly to a specific page. Input is clamped to valid range.
 * @param page
 */
function gotoPageAbsolute (page: number) {
  desiredCurrentPage.value = clamp(page, 0, totalPageCount.value - 1)
}

/**
 * Jump to a relative page.
 * @param offset Offset to current page, eg +1 or -1. Input is clamped to valid range.
 */
function gotoPageRelative (offset: number) {
  desiredCurrentPage.value = clamp(currentPage.value + offset, 0, totalPageCount.value - 1)
}
</script>
