<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<!--
  This is a highly opinionated wrapper around the Multiselect component so we get good typings and can potentially
  swap the underlying implementation later.

  The component exposes two named slots that you can use to customize rendering of the options:
    #option - This is used to render all items in the drop down list.
    #selectedOption - This is used to render a single option in the input box after the user has selected one.
  Note that the component is smart enough to fallback to using one template if you don't provide both. This means that
  you only need to provide one template (either one will do) if you want items to look the same both in the drop down
  list and in the selection box.
-->

<!--
  TODO: This component should warn if duplicate IDs are found in the options array, because that will cause problems.
-->

<template lang="pug">
Multiselect(
  :value="valueForMultiSelectComponent"
  @input="onUpdateInput"
  :options="isOptionsAFunction ? lazyOptionsForMultiSelectComponent : optionsForMultiSelectComponent"
  :filter-results="!isOptionsAFunction"
  :delay="isOptionsAFunction ? 500 : undefined"
  object
  :mode="multiselect ? 'tags' : 'single'"
  :disabled="disabled"
  :required="required"
  :close-on-select="!multiselect"
  :searchable="isSearchable"
  :searchFilter="searchFilter"
  @search-change="onSearchChange"
  :placeholder="placeholder"
  :canClear="!noClear"
  :canDeselect="!noDeselect"
  :dataCy="dataCy"
  :clearOnSelect="isOptionsAFunction ? false : true"
  :clearOnBlur="isOptionsAFunction ? false : true"
  )
  //- `#option` template slot.
  //- Passthrough slot for customizing the option (list item) rendering.
  template(#option="item: { option: MultiselectOption<T> }")
    div(v-if="getOptionValueById(item.option.label)").w-100.small
      slot(v-if="$slots.option" name="option" :option="getOptionValueById(item.option.label)")
      slot(v-else-if="$slots.selectedOption" name="selectedOption" :option="getOptionValueById(item.option.label)")
      span(v-else) {{ item.option.label }}

  //- `#singlelabel` template slot.
  //- Passthrough slot for customizing the selected option (single item) rendering.
  //- Defaults to using the list item rendering if this slot is not explicitly defined.
  template(#singlelabel="item: { value: MultiselectOption<T> }")
    div(v-if="getOptionValueById(item.value.label)" style="z-index: 1;").w-100.multiselect-single-label
      slot(v-if="$slots.selectedOption" name="selectedOption" :option="getOptionValueById(item.value.label)")
      slot(v-else-if="$slots.option" name="option" :option="getOptionValueById(item.value.label)")
      span(v-else).multiselect-single-label-text {{ item.value.label }}

  //- `#tag` template slot.
  //- Passthrough slot for tag rendering.
  //- Defaults to using the list item rendering if this slot is not explicitly defined.
  template(#tag="item: { option: MultiselectOption<T>, handleTagRemove: (option: MultiselectOption<T>, event: Event) => void, disabled: boolean }")
    div(class="multiselect-tag is-user")
      slot(v-if="$slots.selectedOption" name="selectedOption" :option="getOptionValueById(item.option.label)")
      slot(v-else-if="$slots.option" name="option" :option="getOptionValueById(item.option.label)")
      span(v-else) {{ item.option.label }}
      span(v-if="!item.disabled" class="multiselect-tag-remove" @click="item.handleTagRemove(item.option, $event)")
        span(class="multiselect-tag-remove-icon")
</template>

<script lang="ts" setup generic="T">
import { computed, ref, type Ref } from 'vue'
import { isEqual } from 'lodash-es'

import { type MetaInputSelectOption } from '../additionalTypes'
import { showErrorToast } from '../toasts'
import { resolve } from '../utils'

import Multiselect from '@vueform/multiselect'

/*
multiselect props label set to 'id' means we could loose some of translation stuff???
*/

// https://github.com/vueform/multiselect#configuration
// eslint-disable-next-line @typescript-eslint/consistent-type-definitions -- Generics don't seem to work with interfaces as of Jun 2023
type Props = {
  /**
   * The selected option's value (or an array of values if in multiselect mode). Also sets the default value.
   */
  value: T | T[]
  /**
   * The options to choose from. Can be an array of objects with a value and label property, or an async function that resolves to an array of objects.
   * @example // For a case where `T` is `string`:
   * [
   *   { id: '1', value: 'One'},
   *   { id: '2', value: 'Two' },
   *   { id: '3', value: 'Three', disabled: true  },
   * ]
   */
  options: Array<MetaInputSelectOption<T>> | ((query?: string) => Promise<Array<MetaInputSelectOption<T>>>)
  /**
   * Optional: Disable the input element.
   */
  disabled?: boolean
  /**
   * Optional: Set this input as required in the form.
   */
  required?: boolean
  /**
   * Optional: Allow selecting multiple values. Also sets the default value to an array.
   */
  multiselect?: boolean
  /**
   * Optional: Placeholder text for the input element.
   */
  placeholder?: string
  /**
   * Optional: Hide the "x" button.
   */
  noClear?: boolean
  /**
   * Optional: Prevent re-selecting the currently selected option to toggle it.
   */
  noDeselect?: boolean
  /**
   * Optional: A list of fields to search against when using the search functionality.
   * @example
   * ['field', 'deep.field']
   */
  searchFields?: string[]
  /**
   * Optional: Cypress data-cy attribute.
   */
  dataCy?: string
}
const props = defineProps<Props>()

/**
 * Multiselect doesn't expose a type for `options` so we define our own, based on what is written in the docs.
 */
// eslint-disable-next-line @typescript-eslint/consistent-type-definitions -- Generics don't seem to work with interfaces as of Jun 2023
type MultiselectOption<T> = {
  label: string
  value: T
  disabled?: boolean
}

const isOptionsAFunction = !Array.isArray(props.options)
const cachedOptions = ref<Array<MetaInputSelectOption<T>>>([]) as Ref<Array<MetaInputSelectOption<T>>> // Manual type fix, see https://github.com/vuejs/core/issues/2136

/**
 * Check for duplicate IDs in the options array - these would cause the Multiselect component to show incorrect results.
 * If a duplicate is found then we pop up an error toast.
 * @param options The options array to check.
 */
function checkForUniqueIds (options: Array<MultiselectOption<T>>) {
  const ids = options.map((option) => option.label)
  const uniqueIds = new Set(ids)
  if (ids.length !== uniqueIds.size) {
    showErrorToast('Duplicate IDs found in options array. This will cause the component to display incorrect results.', 'MetaInputSelect Error')
  }
}

/**
 * Utility computed to get the options for the Multiselect component.
 */
const optionsForMultiSelectComponent = computed((): Array<MultiselectOption<T>> => {
  const options = (props.options as Array<MetaInputSelectOption<T>>).map((option) => {
    return {
      label: option.id,
      value: option.value,
      disabled: option.disabled
    }
  })
  checkForUniqueIds(options)
  return options
})

/**
 * Utility function to resolve the option functions for the Multiselect component. Caches the results internally.
 * @param searchString The search string to filter the options by.
 */
async function lazyOptionsForMultiSelectComponent (searchString: string) {
  const searchFunction = props.options as (search?: string) => Promise<Array<MetaInputSelectOption<T>>>
  const results = await searchFunction(searchString)
  cachedOptions.value = results
  const translatedResults: Array<MultiselectOption<T>> = results.map((option) => {
    return {
      label: option.id,
      value: option.value,
      disabled: option.disabled
    }
  })
  checkForUniqueIds(translatedResults)
  return translatedResults
}

/**
 * Utility computed to get the currently selected options for the Multiselect component.
 */
const valueForMultiSelectComponent = computed(() => {
  if (isOptionsAFunction) {
    return cachedOptions.value.find((option) => isEqual(option.value, props.value))
  } else if (Array.isArray(props.value)) {
    return props.value.map((v) => optionsForMultiSelectComponent.value.find((option) => isEqual(option.value, v)))
  } else {
    return optionsForMultiSelectComponent.value.find((option) => isEqual(option.value, props.value))
  }
})

/**
 * Utility function to get the value of an option by its ID.
 * @param id ID of the option to get.
 */
function getOptionValueById (id: string): T | undefined {
  let option: T | undefined
  if (isOptionsAFunction) {
    option = cachedOptions.value.find((option) => option.id === id)?.value
  } else {
    option = (props.options).find((option) => option.id === id)?.value
  }
  return option
}

// Searching ----------------------------------------------------------------------------------------------------------

const searchQuery = ref<string>()

/**
 * Determines whether the input is searchable or not.
 */
const isSearchable = computed(() => {
  if (typeof props.options === 'function') {
    // If the options are a function then they are always searchable.
    return true
  } else {
    // Otherwise the options are an array..
    if (props.options === undefined || props.options.length === 0) {
      // No options supplied, so no searchable.
      return false
    } else if (typeof props.options[0].value === 'object' && props.searchFields) {
      // If the options are an array of objects and search fields are explicitly supplied then it's searchable.
      return true
    } else if (typeof props.options[0].value !== 'object') {
      // If the options are an array of non-objects (eg: strings or numbers) then it's searchable.
      return true
    } else {
      return false
    }
  }
})

/**
 * Triggered when the user types in the search box. We store the query string in a ref so we can use it in the search.
 * @param query The new query string
 */
function onSearchChange (query: any) {
  // Note: Something up with the typings here. `@search-change` seems to be defined to return some sort of event, but
  // it's really returning a `string`.
  searchQuery.value = (query as string)?.toLocaleLowerCase()
}

/**
 * The actual search function, called directly from the Multiselect.
 * @param option The option to search.
 */
function searchFilter (option: MetaInputSelectOption<T>) {
  if (typeof option.value === 'object') {
    // If the option is an object then we need to search against the `searchFields` prop.
    return (props.searchFields ?? []).some((searchField) => {
      const value = resolve(option.value, searchField)
      return value.toString().toLowerCase().includes(searchQuery.value)
    })
  } else if (searchQuery.value) {
    // If the option is not an object then we can just search against the value directly. Note that we search against
    // both value and label here, and we blindly cast them to strings.
    return (option.value as string).toString().toLowerCase().includes(searchQuery.value) || (option as any).label.toString().toLowerCase().includes(searchQuery.value)
  } else {
    return true
  }
}

// Emits --------------------------------------------------------------------------------------------------------------

const emit = defineEmits<{
  // Note: The type of value should be `T | T[]` but that doesn't seem to work.
  input: [value: any]
}>()

/**
 * Callback for when the user selects an option.
 */
function onUpdateInput (newValue?: MultiselectOption<T> | Array<MultiselectOption<T>>) {
  // Fire the appropriate event depending on whether this is a multiselect or not.
  if (!props.multiselect) {
    emit('input', (newValue as any)?.value) // Issue with TS generics here. Emit type defined above so this still works as expected.
  } else {
    emit('input', (newValue as any[])?.map(option => option.value)) // Issue with TS generics here. Emit type defined above so this still works as expected.
  }
}
</script>

<!-- TODO: replace with the raw Tailwind CSS rules and do a theming pass. -->
<style src="@vueform/multiselect/themes/default.css">
</style>

<style>
:root {
  --ms-max-height: 20rem;
  --ms-font-size: 0.8rem;
  --ms-tag-font-size: 0.75rem;
  --ms-tag-font-weight: 500;
  --ms-tag-bg: var(--metaplay-blue);
  --ms-option-bg-selected: var(--metaplay-blue);
  --ms-option-bg-selected-pointed: #2c76b4;
}

.multiselect-tag {
  white-space: unset;
  word-break: break-all;
}

.multiselect-option {
  word-break: break-all;
}

.multiselect-single-label {
  position: inherit;
}

.multiselect-wrapper {
  height: 100%;
}

.custom-select {
  cursor: pointer;
}
</style>
