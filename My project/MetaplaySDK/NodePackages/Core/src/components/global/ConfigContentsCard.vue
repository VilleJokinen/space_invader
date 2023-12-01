<template lang="pug">
MCard(
  data-cy="config-contents-card"
  title="Game Config Contents"
  :isLoading="!libraries"
  :error="error"
  )
  //- Header.
  template(#icon)
    fa-icon(icon="table")

  template(#header-right)
    //- Hide the selected if the page has fatal errors, otherwise it looks odd.
    div(v-if="!error")
      meta-input-select(
        v-if="showExperimentSelector"
        :value="selectedExperimentIdInput"
        @input="onSelectedExperimentIdChanged($event)"
        :options="experimentSelectionOptions"
        placeholder="Select an experiment..."
        )
        template(#option="{ option }")
          div {{ option === undefined ? 'error' : allExperiments[option].displayName }}
      div(v-else class="text-sm") Showing variants from #[MBadge(variant="primary") {{ selectedExperimentId }}]

  //- Labels for when there is nothing to show.
  div(
    v-if="hideNoDiffs && librariesToShow.length === 0"
    class="tw-text-center tw-px-3 tw-py-10 tw-my-auto tw-text-neutral-400"
    )
    span(v-if="selectedExperimentId === 'Baseline'") You have not selected an experiment, so there are no differences to the baseline configuration to display.
    span(v-else) The selected experiment contains no differences to the baseline configuration.

  div(v-else class="tw-overflow-x-auto")
    table(class="tw-w-full")
      //- For each library...
      tbody(
        v-for="(library, libraryIndex) in librariesToShow"
        :key="library.id"
        )
        //- Empty space between libraries.
        tr(
          v-if="libraryIndex > 0"
          class="tw-h-4"
          )
        //- Library title row.
        tr(
          class="tw-bg-neutral-100 hover:tw-bg-neutral-200 active:tw-bg-neutral-300 tw-border tw-border-neutral-200 tw-font-normal tw-cursor-pointer"
          @click="toggleLibraryExpanded(library.id)"
          data-cy="library-title-row"
          )
          td(
            class="tw-flex tw-justify-between tw-py-3 tw-px-5"
            )
            //- Title.
            span
              fa-icon(
                icon="angle-right"
                :class="{'tw-rotate-90': isLibraryExpanded(library.id)}"
                )
              span(class="tw-ml-2 tw-font-semibold") {{ library.id }}
              MBadge(
                v-if='!library.error'
                class="tw-ml-2 tw-text-xs"
                :variant="isLibraryExpanded(library.id) ? 'primary' : 'neutral'"
                shape="pill"
                ) {{ library.numTopLevelItems }}
              MBadge(
                v-else
                class="tw-ml-2 tw-text-xs"
                variant="danger"
                shape="pill"
                ) Error

          //- Nasty hack to create cells to fill the header. Colspan did not work for some reason? Long titles look fugly atm.
          td(
            v-for="col in selectedExperimentVariantIds.length + 1"
            class="tw-text-right tw-px-3"
            )
            span(v-if="col === selectedExperimentVariantIds.length + 1")
              MBadge(
                v-if="allExperiments[selectedExperimentId].patchedLibraries.includes(library.id)"
                size="small"
                variant="primary"
                ) Variants
              //- Modification summary.
              //- MBadge(
              //-   v-if="library.summary === ItemDiffSummary.Added"
              //-   variant="success"
              //-   size="small"
              //-   ) Added
              //- MBadge(
              //-   v-else-if="library.summary === ItemDiffSummary.Modified"
              //-   variant="warning"
              //-   size="small"
              //-   ) Modified
              //- MBadge(
              //-   v-else-if="library.summary === ItemDiffSummary.Removed"
              //-   variant="danger"
              //-   size="small"
              //-   ) Removed

        //- If the library is open...
        template(v-if="isLibraryExpanded(library.id)")
          //- Library has an error.
          template(v-if="library.error")
            td(class="tw-border-neutral-200 tw-border" colspan="999")
              MErrorCallout(:error="library.error")

          //- Library contents.
          template(v-else)
            tr(class="tw-text-xs tw-border-neutral-200 tw-border-x")
              //- Library column header cells.
              th(
                class="tw-pl-2"
                :style="selectedExperimentId !== 'Baseline' ? {'min-width': '14rem'} : {'min-width': ''}"
                ) Key
              th(
                class="tw-pl-2 tw-border-l tw-border-neutral-200"
                :style="selectedExperimentId !== 'Baseline' ? {'max-width': '9rem' } : {'max-width': ''}"
                ) {{ selectedExperimentVariantIds.length === 0 ? 'Value' : 'Control' }}
              th(
                v-for="selectedExperimentVariantId in selectedExperimentVariantIds"
                :key="selectedExperimentVariantId"
                class="tw-pl-2 tw-border-l tw-border-neutral-200"
                )
                div(class="tw-text-neutral-600 tw-font-light") {{ allExperiments[selectedExperimentId].displayName }}
                div {{ selectedExperimentVariantId }}

            //- Library loading state.
            template(v-if="!libraryDetails[library.id] || !libraryDetails[library.id].hasLoaded")
              tr(class="tw-border-x tw-border-neutral-200 tw-border-b")
                td(
                  v-for="col in selectedExperimentVariantIds.length + 2"
                  :class="['tw-px-2 tw-text-xs tw-italic tw-text-neutral-400 tw-border-neutral-200 tw-border-t', { 'tw-border-l': col > 1 }]"
                  ) Loading...

            //- Library loaded, but it's empty.
            template(v-else-if="libraryDetails[library.id].flattenedConfigItems.length === 0")
              tr(class="tw-border-x tw-border-neutral-200 tw-border-b")
                td(
                  v-for="col in selectedExperimentVariantIds.length + 2"
                  :class="['tw-px-2 tw-text-xs tw-italic tw-text-neutral-400 tw-border-neutral-200 tw-border-t', { 'tw-border-l': col > 1 }]"
                  ) Library is empty.

            //- Library loaded.
            template(v-else)
              //- Library item rows.
              // TODO: diffs
                :class="['tw-border-t tw-font-mono', { 'tw-bg-red-500': item.differences, 'tw-bg-green-400': item.summary === ItemDiffSummary.Added, 'tw-bg-red-400': item.summary === ItemDiffSummary.Removed, 'tw-bg-orange-400': item.summary === ItemDiffSummary.Modified }]"
              tr(
                v-for="(item, index) in libraryDetails[library.id].flattenedConfigItems"
                :key="index"
                :class="['tw-text-xs tw-font-mono hover:tw-bg-neutral-50 tw-border-x tw-border-neutral-200', { 'tw-border-t': item.indentation === 0, 'tw-border-b': index === libraryDetails[library.id].flattenedConfigItems.length - 1 }]"
                )

                //- Item key. May be a clickable list or a single value.
                td(
                  :class="['tw-pl-2 tw-pt-0.5', { 'tw-cursor-pointer hover:tw-bg-neutral-200 active:tw-bg-neutral-300': item.clickAction }]"
                  @click="item.clickAction ? togglePathExpanded(library.id, item.path) : null"
                  )
                  span(
                    v-if="item.indentation >= 1"
                    :style="`padding-left: ${0.8 * (item.indentation - 1)}rem`"
                    class="tw-mr-1 tw-text-neutral-700"
                    ) ┗━

                  //- Note: Rotation animation is currently defined globally. Inline it here if we want to change it.
                  fa-icon(
                    v-if="item.clickAction"
                    icon="angle-right"
                    :class="['tw-mr-1', { 'tw-rotate-90': expandedPaths[library.id]?.includes(item.path) }]"
                    )
                  span(class="tw-mr-1 tw-font-mono") {{ item.title }}
                  span(
                    v-if="item.subtitle"
                    class="tw-text-neutral-400 tw-mr-1"
                    ) {{ item.subtitle }}
                  MBadge(
                    v-if="item.differences"
                    size="small"
                    class="tw-relative"
                    style="bottom: 0.1rem"
                    variant="primary"
                    ) V
                  //- MBadge(
                  //-   v-if="item.summary === ItemDiffSummary.Added"
                  //-   variant="success"
                  //-   size="small"
                  //-   ) A
                  //- MBadge(
                  //-   v-else-if="item.summary === ItemDiffSummary.Modified"
                  //-   variant="warning"
                  //-   size="small"
                  //-   ) M
                  //- MBadge(
                  //-   v-else-if="item.summary === ItemDiffSummary.Removed"
                  //-   variant="danger"
                  //-   size="small"
                  //-   ) R

                //- TODO: can we pretty print some hard-to-read values like durations?
                //- Baseline value.
                td(class="tw-border-l tw-border-neutral-200 tw-pl-2 tw-pt-0.5 tw-break-words")
                  span(v-if="item.values")
                    span(v-if="item.values.Baseline !== undefined") {{ item.values.Baseline }}
                    span(v-else class="tw-italic tw-text-orange-400") missing

                //- Variant values.
                td(
                  v-for="(selectedExperimentVariantId, index) in selectedExperimentVariantIds"
                  :key="item.path + index"
                  class="tw-border-l tw-border-neutral-200 tw-pl-2 tw-pt-0.5 tw-break-words"
                  )
                    span(v-if="item.values") {{ item.values[`${selectedExperimentId}.${selectedExperimentVariantId}`] }}
</template>

<script lang="ts" setup>
import { computed, type ComputedRef, ref, onMounted } from 'vue'

import { fetchSubscriptionDataOnceOnly } from '@metaplay/subscriptions'

import type { MetaInputSelectOption } from '@metaplay/meta-ui'
import { MBadge, MCard, MErrorCallout, DisplayError } from '@metaplay/meta-ui-next'

import { flattenLibraryConfigItems } from '../../gameConfigUtils'
import type { LibraryFlattenedItem } from '../../gameConfigUtils'
import type { LibraryConfigItem, LibraryCountGameConfigInfo, GameConfigLibraryDetails } from '../../gameConfigServerTypes'
import { getSingleGameConfigCountsSubscriptionOptions, getSingleGameConfigLibraryDetailsSubscriptionOptions } from '../../subscription_options/gameConfigs'

const props = withDefaults(defineProps<{
  /**
   * Optional: ID of the game config to show. Defaults to currently active.
   */
  gameConfigId?: string
  /**
   * Optional: Which experiment to show in the game config.
   */
  experimentId?: string
  /**
   * Optional: Show the experiment selector.
   */
  showExperimentSelector?: boolean
  /**
   * Optional: Hide libraries that don't contain any differences.
   */
  hideNoDiffs?: boolean
  /**
   * Optional: Ignore server libraries and only show shared libraries.
   */
  excludeServerLibraries?: boolean
}>(), {
  gameConfigId: '$active',
  experimentId: 'Baseline',
})

// Libraries. -------------------------------------------------------------------------------------

/**
 * Describes overview information about a library.
 */
interface Library {
  id: string
  error?: Error | DisplayError
  numTopLevelItems?: number
}

/**
 * Contains overview information about all libraries.
 */
const libraries = ref<Library[]>([])

/**
 * Error object, set if a data fetch errors out.
 */
const error = ref<Error>()

/**
 * Contains overview information about all libraries that we want to display in the card. It's possible that we want to
 * hide libraries that don't contain any differences.
 */
const librariesToShow = computed((): Library[] => {
  if (props.hideNoDiffs) {
    return libraries.value.filter((library) => {
      return allExperiments.value[selectedExperimentId.value].patchedLibraries.includes(library.id)
    })
  } else {
    return libraries.value
  }
})

/**
 * When the page mounts, kick off a request to get the game config overview.
 */
onMounted(async () => {
  await fetchSubscriptionDataOnceOnly<LibraryCountGameConfigInfo>(getSingleGameConfigCountsSubscriptionOptions(props.gameConfigId))
    .then(data => {
      // Do we want to filter out the server libraries?
      let filteredLibraries = { ...data.contents.sharedLibraries }
      if (!props.excludeServerLibraries) {
        filteredLibraries = { ...filteredLibraries, ...data.contents.serverLibraries }
      }

      // Build information about all experiments. First add `Baseline` as an experiment..
      allExperiments.value = {
        Baseline: {
          displayName: 'Baseline',
          variants: [],
          patchedLibraries: [],
        }
      }

      // ..then add all "real" experiments.
      Object.values(data.experiments).forEach((experiment: any) => {
        const newExperimentDataId = experiment.id
        const newExperimentData = {
          displayName: experiment.displayName,
          variants: experiment.variants,
          patchedLibraries: experiment.patchedLibraries,
        }
        allExperiments.value[newExperimentDataId] = newExperimentData
      })

      // Build a list of overview information for all interesting libraries.
      // Note: Libraries that contain errors might be in this list *and* the `libraryParsingErrors`. We filter those
      // out here so that we don't see duplicates.
      // Further Note: This should only be the case for the `PlayerExperiments` library, because it's loading in multiple stages.
      //   First we load the raw experiment data, then we try to create a config item that has the patched values in it, the second step is the most likely to fail due to it deserializing to a user type (which might no longer be compatible with old data)/
      libraries.value = Object.keys(filteredLibraries)
        .sort((a, b) => a.localeCompare(b))
        .filter((a) => data.libraryParsingErrors && data.libraryParsingErrors[a] === undefined)
        .map(id => {
          return {
            id,
            numTopLevelItems: filteredLibraries[id],
          }
        })

      // Add entries for all libraries that have errors in them.
      Object.entries(data.libraryParsingErrors ?? []).forEach(([id, error]) => {
        const translatedError = new DisplayError(
          'Library Error',
          error.message,
          undefined,
          [
            {
              title: 'Exception Type',
              content: error.exceptionType
            },
            {
              title: 'Full Exception',
              content: error.fullException,
            }
          ]
        )
        libraries.value.push({
          id,
          error: translatedError,
        })
      })

      // Finally, sort into order.
      libraries.value = libraries.value.sort((a: Library, b: Library) => a.id.localeCompare(b.id))
    })
    .catch((e) => {
      // Error fetching initial game config data.
      error.value = e
    })
})

/**
 * Describes overview information about an experiment.
 */
interface ExperimentDetails {
  displayName: string
  variants: string[]
  patchedLibraries: string[]
}

/**
 * Contains information about all experiments.
 */
const allExperiments = ref<{[id: string]: ExperimentDetails}>({})

interface LibraryDetails {
  /**
   * Library is initially created empty and then filled in as the data is loaded.
   */
  hasLoaded: boolean
  /**
   * These are the raw config items that the server gives us. For each library we cache the config items for
   * every experiment. These lazily loaded as required.
   */
  experimentConfigItems: { [experimentId: string]: LibraryConfigItem[] }
  /**
   * These are essentially display lists for the library, ie: they are a linear list of items that the
   * render code can iterate over to generate the UI. They are generated from `experimentConfigItems`.
   */
  flattenedConfigItems: LibraryFlattenedItem[]
}

/**
 * Details for all libraries.
 */
const libraryDetails = ref<{[libraryId: string]: LibraryDetails}>({})

/**
 * Asynchronously load a library. This is idempotent - it's safe to call even if the library is already loaded.
 * @param libraryId ID of the library to load.
 * @param experimentId ID of the experiment that we want to view, or `Baseline` for none.
 */
async function tryLoadLibrary (libraryId: string, experimentId: string) {
  // Don't try to load libraries that have errors.
  const library = libraries.value.find(x => x.id === libraryId)
  if (library === undefined || library?.error) {
    return
  }

  // Get or create an entry for this library.
  libraryDetails.value[libraryId] = libraryDetails.value[libraryId] || {
    hasLoaded: false,
    experimentConfigItems: {},
    flattenedConfigItems: [],
  } satisfies LibraryDetails
  const libraryDetail = libraryDetails.value[libraryId]

  if (libraryDetail.experimentConfigItems[experimentId]) {
    // Library already loaded or currently loading.
  } else {
    // Start loading the library.
    await fetchSubscriptionDataOnceOnly<GameConfigLibraryDetails>(getSingleGameConfigLibraryDetailsSubscriptionOptions(props.gameConfigId, libraryId, experimentId === 'Baseline' ? undefined : experimentId))
      .then((data) => {
        // Library has loaded.
        const libraryConfigItems: LibraryConfigItem[] | undefined = data.config[libraryId].children
        if (!libraryConfigItems) {
          throw new Error(`Library ${libraryId} has no children`)
        }
        libraryDetail.experimentConfigItems[experimentId] = libraryConfigItems
        libraryDetail.hasLoaded = true

        // Once the library has loaded we make a display list for it.
        libraryDetail.flattenedConfigItems = flattenLibraryConfigItems(libraryConfigItems, expandedPaths[libraryId] || [])
      })
      .catch((error: Error) => {
        // Error fetching library data.
        library.error = error
      })
  }
}

/**
 * The currently selected experiment ID, or undefined for none.
 */
const selectedExperimentIdInput = ref<string | undefined>(props.experimentId)

/**
 * The currently selected experiment ID, or 'Baseline' for none.
 */
const selectedExperimentId = computed(() => {
  return selectedExperimentIdInput.value ?? 'Baseline'
})

/**
 * Describes an experiment for the 'MetaInputSelect'.
 */
interface ExperimentSelectionOption {
  experimentId: string
  displayName: string
}

/**
 * Options for the 'MetaInputSelect'.
 */
const experimentSelectionOptions: ComputedRef<Array<MetaInputSelectOption<string>>> = computed(() => {
  // Note that 'Baseline' is not selectable as an option, so we filter it out here. To view the baseline config
  // we use the clear button on the input select.
  return Object.keys(allExperiments.value)
    .filter(id => id !== 'Baseline')
    .map((id) => {
      return {
        id,
        value: id,
      }
    })
})

/**
 * Callback fired when a new experiment ID is selected.
 * @param newId Newly selected ID.
 */
function onSelectedExperimentIdChanged (newId: string | undefined) {
  // Store the new value.
  selectedExperimentIdInput.value = newId

  // Whenever we change experiment ID we need to (potentially) load all visible libraries or just regenerate their
  // display lists.
  expandedLibraries.value.forEach((libraryId) => {
    const library = libraries.value.find(library => library.id === libraryId)
    // Don't try to expand libraries that have errors.
    if (!library?.error) {
      const libraryDetail = libraryDetails.value[libraryId]
      libraryDetail.flattenedConfigItems = []
      if (libraryDetail.experimentConfigItems[selectedExperimentId.value]) {
        // We already have the library data for this experiment, so we just need to create the display list.
        const libraryConfigItems: LibraryConfigItem[] = libraryDetail.experimentConfigItems[selectedExperimentId.value]
        libraryDetail.flattenedConfigItems = flattenLibraryConfigItems(libraryConfigItems, expandedPaths[libraryId] || [])
      } else {
        // We don't have the library data for the particular experiment yet so let's start loading it.
        void tryLoadLibrary(libraryId, selectedExperimentId.value)
      }
    }
  })
}

/**
 * All IDs for the currently selected experiment.
 */
const selectedExperimentVariantIds = computed((): string[] => {
  return allExperiments.value[selectedExperimentId.value].variants
})

// Expansion of libraries in the UI. --------------------------------------------------------------

/**
 * List of libraries that are expanded, ie: open and visible.
 */
const expandedLibraries = ref<string[]>([])

/**
 * Helper function to find out if a library is expanded or not.
 * @param libraryId Library ID.
 */
function isLibraryExpanded (libraryId: string) {
  return expandedLibraries.value.includes(libraryId)
}

/**
 * Helper function to toggle a library open/closed.
 */
function toggleLibraryExpanded (libraryId: string) {
  if (isLibraryExpanded(libraryId)) {
    expandedLibraries.value = expandedLibraries.value.filter(expandedLibraryId => expandedLibraryId !== libraryId)
  } else {
    expandedLibraries.value.push(libraryId)

    // When we open a library we also need to make sure that the data is loaded.
    void tryLoadLibrary(libraryId, selectedExperimentId.value)
  }
}

// Expansion of paths within libraries in the UI. -------------------------------------------------

/**
 * Nested list of expanded paths of each library
 */
const expandedPaths: {[libraryId: string]: string[]} = {}

/**
 * Helper function to toggle a path expanded/collapsed.
 * @param libraryId Library ID.
 * @param path Path Id.
 */
function togglePathExpanded (libraryId: string, path: string) {
  const libraryPaths = expandedPaths[libraryId]
  if (libraryPaths) {
    if (libraryPaths.includes(path)) {
      expandedPaths[libraryId] = expandedPaths[libraryId].filter(extendedPath => extendedPath !== path)
    } else {
      expandedPaths[libraryId].push(path)
    }
  } else {
    // First time expanding this path.
    expandedPaths[libraryId] = [path]
  }

  // Whenever we expand/collapse a library we need to regenerate the display list.
  const libraryDetail = libraryDetails.value[libraryId]
  libraryDetail.flattenedConfigItems = flattenLibraryConfigItems(libraryDetail.experimentConfigItems[selectedExperimentId.value], expandedPaths[libraryId])
}
</script>
