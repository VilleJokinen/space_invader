<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!--
This is a reusable utility component for easily rendering nice looking lists in the dashboard.
All lists will come with sensible defaults like paging and a rudimentary default renderer.

You can optionally configure features like searching, sorting and filtering in the utilities menu.

Have a look at the Vue 'props' object to see the various configurations that can be used to customise the look and feel for various use-cases!
-->

<template lang="pug">
b-card.shadow-sm(:class="{ 'bg-light': !hasContent || !hasPermission }" style="min-height: 12rem" no-body)
  //- Header.
  b-row(align-h="between" align-v="center" no-gutters @click="toggleUtilitiesMenu" :class="{ 'table-row-link': hasContent && showUtilitiesMenu }").card-manual-title-padding
    MTooltip(:content="tooltip")
      b-card-title.d-flex.align-items-center
        fa-icon(v-if="icon" :icon="icon").mr-2
        | {{ title }}
        MBadge(v-if="itemList && hasPermission" shape="pill" :variant="itemCountClass").ml-2 {{ itemCountText }}
    span(style="margin-top: -1rem")
      small(v-if="filtersActive && sortActive").text-muted.font-italic.mr-2 filtered & sorted
      small(v-else-if="filtersActive").text-muted.font-italic.mr-2 filtered
      small(v-else-if="sortActive").text-muted.font-italic.mr-2 sorted
      b-button(v-if="showUtilitiesMenu" :disabled="!hasContent" variant="outline-secondary" :class="{ 'text-primary': filtersOrSortActive }").p-1.subtle-button
        fa-icon(icon="angle-right" size="sm").mr-1
        fa-icon(icon="search" size="sm")

  //- Permission handling.
  div(v-if="!hasPermission").text-center.mb-4.pt-4.small.text-muted You need the #[MBadge {{ permission }}] permission to view this card.

  //- Loading.
  div(v-else-if="!itemList").w-100.card-manual-content-padding.pt-2
    b-skeleton(width="85%")
    b-skeleton(width="55%")
    b-skeleton(width="70%")

    b-skeleton(width="80%").mt-3
    b-skeleton(width="65%")

  //- Main body.
  div(v-else-if="hasContent").d-flex.flex-column.justify-content-between.h-100
    //- Sticks to the top.
    div
      //- Utilities menu.
      MTransitionCollapse
        div(v-if="showUtilitiesMenu && utilitiesMenuExpanded").bg-light.w-100.card-manual-content-padding.border-top.border-bottom
          div.mt-3.mb-3
            div.mb-2
                div.small.font-weight-bold Search
                div.mt-1(v-if="searchFields")
                  b-input-group
                    b-form-input(size="sm" v-model="searchString" :state="searchActive ? true : null" :placeholder="searchPlaceholder" debounce="200")
                    b-button(size="sm" variant="outline-secondary" :disabled="!searchActive" @click="clearSearchString()" style="border-radius: 0 3px 3px 0; background: gray; color: white"): fa-icon(icon="times")

                span(v-else).text-muted.small Search not available for this card.
            b-row
              b-col
                div.small.font-weight-bold Filter
                template(v-if="filterSets")
                  b-form-checkbox-group(
                    v-for="(filterSet, filterSetIndex) in filterSets" :key="filterSetIndex"
                    v-model="selectedFilters"
                    name="filters"
                  )
                    b-form-checkbox(
                      v-for="filter in filterSet.filterOptions"
                      :key="filter.displayName"
                      :value="makeFilterKey(filterSet, filter)"
                    )
                      span(:class="isFilterSetActive(filterSet, selectedFilters) ? '' : 'text-muted'")
                        span.small {{ filter.displayName }} ({{ getSearchFilteredItemList([makeFilterKey(filterSet, filter)]).length }})
                span(v-else).text-muted.small Filters not available for this card.

              b-col
                div.small.font-weight-bold Sort
                div
                  b-form-radio-group(
                    v-if="sortOptions"
                    :class="{ 'text-muted': !sortActive }"
                    v-model="sortOption"
                    :options="radioSortOptions"
                    button-variant="outline-primary"
                    size="sm"
                  )
                  span(v-else).text-muted.small Sorting not available for this card.

      //- Description.
      div(v-if="description").small.card-manual-content-padding {{ description }}
      div.small.card-manual-content-padding
        slot(name="description")

      //- Pause stream.
      div(v-if="allowPausing").text-center.text-muted.small.pb-2.mt-1
        span(v-show="!isPaused") Last update #[meta-time(:date="lastUpdated")].
        span(v-show="isPaused") Updates paused #[meta-time(:date="lastUpdated")].
        meta-button(link @click="togglePlayPauseState" data-cy="play-pause-button").ml-1 {{ isPaused ? 'Resume' : 'Pause' }} updates
        div
          MBadge(v-if="updateAvailable" shape="pill" variant="primary") New data available

      //- List of items.
      div(v-if="currentPageItemList.length > 0")
        MList(v-if="listLayout === 'list'")
          //- Render actual items.
          div(
            v-for="(item, index) in currentPageItemList"
            :key="getItemKey ? getItemKey(item) : index"
            class="tw-px-5"
            ref="itemsElements"
            )
            slot(name="item-card" v-bind:item="item" v-bind:index="`${index}`") {{ item }}
          //- Render empty slots if needed.
          template(v-if="currentPage > 0")
            div(
              v-for="index in pageSize - currentPageItemList.length"
              :key="uniqueKeyPrefix + index.toString()"
              :style="`min-height: ${emptyListItemHeight}px`"
            )

        //- Flex layout.
        div(v-else-if="listLayout === 'flex'").d-flex.flex-wrap.card-manual-content-padding.my-3
          div(v-for="(item, index) in currentPageItemList" :key="index").mr-2.mb-2
            slot(name="item-card" v-bind:item="item" v-bind:index="`${index}`")
              pre(style="font-size: .7rem") {{ item }}

      //- Empty list after filtering.
      b-row(align-h="center" v-else no-gutters).text-center.mb-4.pt-4
        span.small.text-muted {{ noResultsMessage }}

    //- Sticks to the bottom.
    div.mb-3.mt-3(v-if="moreInfoUri || showPagingControls")
      //- "View more" link.
      div.w-100.text-center.mb-2(v-if="moreInfoUri")
        span.small.text-muted
          | View more items at the&nbsp;
          meta-auth-tooltip(v-if="moreInfoPermission" :permission="moreInfoPermission")
            b-link(:to="moreInfoUri" :disabled="!gameServerApiStore.doesHavePermission(moreInfoPermission)") {{ moreInfoLabel }} page
          span(v-else)
            b-link(:to="moreInfoUri") {{ moreInfoLabel }} page
          | !

      //- Pagination controls.
      div.text-center.mb-1(v-if="showPagingControls")
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

  //- Empty.
  div(v-else)
    //- Description.
    div(v-if="description").small.card-manual-content-padding {{ description }}
    div.mb-2
      slot(name="description")
    div.pb-4.text-center.my-5.px-3.text-muted {{ emptyMessage }}

</template>

<script lang="ts" setup>
import { BButtonGroup } from 'bootstrap-vue'
import { clamp, clone, isEqual, meanBy } from 'lodash-es'
import { DateTime } from 'luxon'
import { computed, onMounted, ref, watch } from 'vue'
import { MTransitionCollapse, MTooltip, MList, MListItem, MBadge, type Variant } from '@metaplay/meta-ui-next'

import { useGameServerApiStore } from '@metaplay/game-server-api'
import { MetaListFilterOption, MetaListFilterSet, MetaListSortOption } from '../metaListUtils'
import { resolve, makeIntoUniqueKey } from '../utils'

// Props ------------------------

const props = withDefaults(defineProps<{
  /**
   * Title of the card.
   */
  title: string
  /**
   * Optional: Font-Awesome icon shown before the card's title.
   * @example 'table'
   */
  icon?: string
  /**
   * Optional: Tooltip to be shown when hovering over the card's title.
   */
  tooltip?: string
  /**
   * Optional: Use red styling for the card.
   */
  dangerous?: boolean
  /**
   * Array of items to be shown.
   */
  itemList?: any[] | null // TODO: consider a better type for this. object[] was too narrow as one customer used string[]
  /**
   * By default, the items in the list are uniquely keyed by their index. This is adequate in most cases, but sometimes
   * the items can be reordered and sometimes this causes rendering issues. For example, items that can be
   * collapsed/expanded will get confused if itemList is changed while items are expanded.
   * @example (item) => item.id
  */
  getItemKey?: (item: string | object) => string
  /**
   * Optional: Maximum amount of list items shown on one page. Defaults 8.
   */
  pageSize?: number
  /**
   * Optional: Array of list item property names that can be searched.
   * @example: ['id', 'contents.title']
   */
  searchFields?: string[]
  /**
   * Optional: Placeholder text for the search box.
   */
  searchPlaceholder?: string
  /**
   * Optional: List of filter sets that can be applied.
   */
  filterSets?: MetaListFilterSet[]
  /**
   * Optional: List of sort types that can be applied.
   */
  sortOptions?: MetaListSortOption[]
  /**
   * Optional: Index of sort option to choose by default.
   */
  defaultSortOption?: number
  /**
   * Optional: Message to be shown when itemList is null or empty.
   */
  emptyMessage?: string
  /**
   * Optional: Message to be shown when there are no search results.
   */
  noResultsMessage?: string
  /**
   * Optional: URL to show a call-to-action link at the bottom of the card.
   */
  moreInfoUri?: string
  /**
   * Optional: Custom word to use in the call-to-action link at the bottom of the card.
   */
  moreInfoLabel?: string
  /**
   * Optional: Permissions needed by the call-to-action link at the bottom of the card.
   */
  moreInfoPermission?: string
  /**
   * Optional: Alternative layout for the list. Default is `list`.
   */
  listLayout?: 'flex' | 'list'
  /**
   * Optional: Apply pointer cursor style to the list.
   */
  clickable?: boolean
  /**
   * Optional: Permission needed to view the card's data.
   */
  permission?: string
  /**
   * Optional: Header subtitle for the card.
   */
  description?: string
  /**
   * Optional: Show the controls to pause and resume the event stream.
   */
  allowPausing?: boolean
}>(), {
  icon: undefined,
  tooltip: undefined,
  itemList: undefined,
  getItemKey: undefined,
  pageSize: 8,
  searchFields: undefined,
  searchPlaceholder: 'Type your search here...',
  filterSets: undefined,
  sortOptions: undefined,
  defaultSortOption: 0,
  emptyMessage: 'Nothing to list!',
  noResultsMessage: 'No items found. Try a different search string or filters? 🤔',
  moreInfoUri: undefined,
  moreInfoLabel: 'relevant',
  moreInfoPermission: undefined,
  listLayout: 'list',
  clickable: false,
  permission: undefined,
  description: undefined,
  allowPausing: false,
})

// Setup and utils ------------------------

const gameServerApiStore = useGameServerApiStore()

/**
 * Allows us to prefix each item on the page with a unique key to prevent clashes between multiple instances of
 * MetaListCard on the same page.
 */
const uniqueKeyPrefix = makeIntoUniqueKey

onMounted(() => {
  // Calculate empty line height once the card has loaded.
  setTimeout(updateEmptyLineHeight, 0)

  // Enable initially active filter options.
  if (props.filterSets) {
    props.filterSets.forEach(filterSet => {
      filterSet.filterOptions.forEach(filterOption => {
        if (filterOption.initiallyActive) {
          selectedFilters.value.push(makeFilterKey(filterSet, filterOption))
        }
      })
    })
  }

  // Initial sort option copied from props.
  sortOption.value = props.defaultSortOption

  // When the card loads for the first time we need to copy across the data.
  deferredItemList.value = clone(props.itemList) ?? []
  updateAvailable.value = false
  lastUpdated.value = DateTime.now()
})

/**
 * Store a reference to all items. Used to calculate empty list item height.
 */
const itemsElements = ref([])

/**
 * Height to render empty items at, as calculated by `updateEmptyLineHeight`.
 */
const emptyListItemHeight = ref(10)

/**
 * Calculate a sensible default for the empty list items. Items with no content are set to this height, which makes
 * the layout more pleasing.
 */
function updateEmptyLineHeight () {
  // Set to average.
  const newHeight = meanBy(itemsElements.value as any[], (ref: any) => ref.clientHeight)

  // Only grow, don't shrink - this makes the empty list height behave more consistently.
  if (newHeight > emptyListItemHeight.value) emptyListItemHeight.value = newHeight
}

/**
 * Does user have permission to view the card?
 */
const hasPermission = computed((): boolean => {
  if (props.permission) return gameServerApiStore.doesHavePermission(props.permission)
  else return true
})

/**
 * Is there any content available at all, ie: does `deferredItemList` contain any items?
 */
const hasContent = computed((): boolean => {
  return (deferredItemList.value?.length ?? 0) > 0
})

// Utilities menu ------------------------

/**
 * Only need to show the utilities menu if we have something to show in it (ie: search, sort of filtering).
 */
const showUtilitiesMenu = computed((): boolean => {
  return (props.searchFields && props.searchFields.length > 0) ??
    (props.sortOptions && props.sortOptions.length > 1) ??
    !!(props.filterSets && props.filterSets.length > 0)
})

/**
 * Is the utilities menu expanded or not?
 */
const utilitiesMenuExpanded = ref(false)

/**
 * Expand or collapse the utilities menu.
 */
function toggleUtilitiesMenu () {
  utilitiesMenuExpanded.value = !utilitiesMenuExpanded.value
}

// Searching, filtering and sorting ------------------------

/**
 * Search string entered by user.
 */
const searchString = ref('')

/**
 * Array of active filters.
 */
const selectedFilters = ref<string[]>([])

/**
 * Index into `sortOptions` for currently selected sort option.
 */
const sortOption = ref(0)

/**
 * Calculate a list of all items after searching, filtering and sorting. This is the list of all items, and it is
 * further cropped by `currentPageItemList` before being rendered.
 */
const searchedFilteredSortedItemList = computed((): any[] => {
  let items = getSearchFilteredItemList(selectedFilters.value)
  if (props.sortOptions) {
    const sortOptionX = props.sortOptions[sortOption.value]
    const sortField = sortOptionX.dataFieldName
    const sortDirection = sortOptionX.direction
    items = [...items].sort((a, b) => {
      let order = 0
      let valueA = (sortField ? resolve(a, sortField) : a)
      let valueB = (sortField ? resolve(b, sortField) : b)
      if (typeof valueA === 'string') valueA = valueA.toLowerCase()
      if (typeof valueB === 'string') valueB = valueB.toLowerCase()
      if (valueA > valueB) {
        order = +1
      } else if (valueA < valueB) {
        order = -1
      }
      order = order * sortDirection
      return order
    })
  }
  return items
})

/**
 * Create details for the sort options radio group.
 */
const radioSortOptions = computed(() => {
  if (props.sortOptions) {
    return props.sortOptions.map((item, index) => {
      return {
        value: index,
        text: item.displayName + (item.direction < 0 ? ' ↓' : item.direction > 0 ? ' ↑' : '')
      }
    })
  } else {
    return []
  }
})

/**
 * Is search active?
 */
const searchActive = computed((): boolean => {
  return searchString.value.length > 0
})

/**
 * Are any filters active?
 */
const filtersActive = computed((): boolean => {
  return selectedFilters.value.length > 0 ||
    searchActive.value
})

/**
 * Is a sort option active? If we have `sortOptions` then one will always be active.
 */
const sortActive = computed((): boolean => {
  return hasContent.value && props.sortOptions !== undefined
})

/**
 * Filters or sort active?
 */
const filtersOrSortActive = computed((): boolean => {
  return filtersActive.value || sortActive.value
})

/**
 * Create a unique id string for a given filter.
 * @param filterSet
 * @param filter
 */
function makeFilterKey (filterSet: MetaListFilterSet, filter: MetaListFilterOption): string {
  return `${filterSet.key}-${filter.displayName}`
}

/**
 * Determines whether the given filter is currently active or not.
 * @param filterSet
 * @param selectedFilters
 */
function isFilterSetActive (filterSet: MetaListFilterSet, selectedFilters: String[]): boolean {
  return filterSet.filterOptions.some(filter => selectedFilters.includes(makeFilterKey(filterSet, filter)))
}

/**
 * Clear the user's search string.
 */
function clearSearchString () {
  searchString.value = ''
}

/**
 * Return a list of items after searching and filtering.
 * @param selectedFilters
 */
function getSearchFilteredItemList (selectedFilters: String[]): any[] {
  let items = deferredItemList.value ?? []

  // Filter first..
  const filterSets = props.filterSets
  if (filterSets) {
    items = items.filter(item => {
      let passAll = true
      filterSets.forEach(filterSet => {
        if (isFilterSetActive(filterSet, selectedFilters)) {
          let passFilter = false
          filterSet.filterOptions.forEach(filter => {
            if (selectedFilters.includes(makeFilterKey(filterSet, filter)) && filter.filterFn(item)) {
              passFilter = true
            }
          })
          if (!passFilter) {
            passAll = false
          }
        }
      })
      return passAll
    })
  }

  // ..then search
  const searchFields = props.searchFields
  if (searchActive.value && searchFields) {
    const searchStringLower = searchString.value.toLowerCase()
    items = items.filter(item => {
      return searchFields.some(searchField => {
        if (searchField) {
          const value = resolve(item, searchField)
          if (value === undefined) {
            if (!warnedMissingSearchFields.value.includes(searchField)) {
              // Warn the first time we try to search on a non-existent search field.
              // eslint-disable-next-line
              console.warn(`Could not find search field '${searchField}' inside item for meta-list-card '${props.title}'`)
              warnedMissingSearchFields.value.push(searchField)
            }
            return false
          } else {
            const valueLower = String(value).toLowerCase()
            return valueLower.includes(searchStringLower)
          }
        } else {
          const valueLower = String(item).toLowerCase()
          return valueLower.includes(searchStringLower)
        }
      })
    })
  }

  return items
}

/**
 * Cache of search fields that we have warned about. We keep this so that we can only warn about each missing search
 * field once.
 */
const warnedMissingSearchFields = ref<string[]>([])

/**
 * Item count to show in the card's header.
 */
const itemCountText = computed((): string => {
  const total = deferredItemList.value?.length ?? 0
  if (searchActive.value || filtersActive.value) {
    const current = searchedFilteredSortedItemList.value?.length || 0
    return `${current} / ${total}`
  } else {
    return `${total}`
  }
})

/**
 * Highlight style for the item count.
 */
const itemCountClass = computed((): Variant => {
  if ((deferredItemList.value?.length ?? 0) === 0) {
    return 'neutral'
  } else {
    return props.dangerous ? 'danger' : 'primary'
  }
})

// Paging ------------------------

/**
 * This is the page that we have chosen to view. Sometimes this page ends up being outside the valid page range (eg:
 * when a search/filter changes the total number of items/pages) but we keep track of the original number to prevent
 * surprising page changes as searches/filters are changed.
 */
const desiredCurrentPage = ref(0)

/**
 * Returns one page of items. This is the data that actually gets rendered.
 */
const currentPageItemList = computed((): any[] => {
  if (showPagingControls.value) {
    const start = currentPage.value * props.pageSize
    return searchedFilteredSortedItemList.value.slice(start, start + props.pageSize)
  } else {
    return searchedFilteredSortedItemList.value
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
  const itemCount = searchedFilteredSortedItemList.value?.length
  return Math.ceil(itemCount / props.pageSize)
})

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
  updateEmptyLineHeight()
}

/**
 * Jump to a relative page.
 * @param offset Offset to current page, eg +1 or -1. Input is clamped to valid range.
 */
function gotoPageRelative (offset: number) {
  desiredCurrentPage.value = clamp(currentPage.value + offset, 0, totalPageCount.value - 1)
  updateEmptyLineHeight()
}

// Pausing ------------------------

/**
 * When paused is true, `deferredItemList` does not update when the 'itemList' prop changes.
 */
const isPaused = ref(false)

/**
 * True when the stream is paused but we have received new data.
 */
const updateAvailable = ref(false)

/**
 * Remembers the last time that we updated `deferredItemList` from `itemList`.
 */
const lastUpdated = ref(DateTime.now())

/**
 * Toggle pause state.
 */
function togglePlayPauseState () {
  isPaused.value = !isPaused.value
}

/**
 * To handle pausing, we don't deal with 'itemList' directly. Instead we copy that data to `deferredItemList` when it
 * changes. If we are paused then those updates are simply not copied.
 */
const deferredItemList = ref<any[] | null>(null)

/**
 * Watch `itemList` and `isPaused` so that we can react to changes in them.
 */
watch([() => props.itemList, isPaused],
  ([itemList, isPaused], [_previousItemList, previousIsPaused]) => {
    if (!isPaused) {
      // If data updates but we are paused, then we just don't update `itemList` with the new data. If the user
      // unpauses then this watcher will fire and the data will get updated immediately.
      deferredItemList.value = clone(itemList) ?? []
      updateAvailable.value = false
      lastUpdated.value = DateTime.now()
    } else if (isPaused && previousIsPaused) {
      // If we are are paused now and we were paused before this watcher fired then it must be `itemList` that
      // changed. Now we know that we are paused and new data has arrived.
      if (!isEqual(deferredItemList.value, itemList)) {
        updateAvailable.value = true
      }
    }
  }
)

</script>

<style scoped>
.pagination-shadow {
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.15), inset 0 -4px 1px #494949;
}
</style>
