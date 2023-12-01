// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

/**
 * @fileoverview Provides an interface for adding toast messages to the Dashboard. Toasts come in three flavors:
 * success, warning and error.
 */

import MetaToast from './components/MetaToast.vue'
import Toast, { useToast, TYPE } from 'vue-toastification'
import type { ToastOptions } from 'vue-toastification/dist/types/types'
import type { App } from 'vue'

export function useToastsVuePlugin (app: App) {
  app.use(Toast, {
    containerClassName: 'toast-class',
    transition: 'Vue-Toastification__fade',
    maxToasts: 5,
    toastClassName: 'toast-class',
    hideProgressBar: true,
    closeOnClick: false,
    newestOnTop: true,
    closeButton: false,
  })

  const warningMessagesToIgnore = [
    '(deprecation COMPONENT_FUNCTIONAL)',
  ]

  const traceItemsToIgnore = [
    'BForm',
    'BFormCheckboxGroup',
    'BFormCheckbox',
    'BFormRadioGroup',
    'BFormRadio',
    'BFormGroup',
    'BFormInvalidFeedback',
    'BFormFile',
    'BTooltip',
    'BFormInput',
    'BCollapse',
    'BRow',
    'BCol',
    'BInputGroup',
    'BVCollapse',
    'BSkeleton',
    'BCardTitle',
    'BCardBody',
    'BCard',
    'BBadge',
    'BButton',
    'BLink',
    'BModal',
    'BListGroupItem',
    'BListGroup',
    'BContainer',
    'BVTransition',
    'BAlert',
    'BTable',
    'BTableCell',
    'BTr',
    'BTh',
    'BTbody',
    'BSpinner',
    'BTableSimple',
    'BVTransporter',
    'BFormTextarea',
    'BProgressBar',
    'BProgress',
    'BImg',
    'BIconBase',
    'BIconPersonFill',
    'BAvatar',
    'BaseTransition',
    'VueMultiselect',
    'SidebarComponent', // Caused by the tooltips component.
    'MetaAuthTooltip', // Caused by the tooltips component.
    'RouterLink', // ATTR_FALSE_VALUE warning that seemed to be intended behaviour.
    'MetaCollapse', // v-b-toggle and b-collapse causing CUSTOM_DIR warning.
    'MetaListCard', // WATCH_ARRAY warning -> non-issue.
    'MetaRawData', // WATCH_ARRAY warning -> non-issue.
    'MetaEventStreamCard', // WATCH_ARRAY warning -> non-issue.
    'MetaInputNumber', // ATTR_FALSE_VALUE -> non-issue.
    'EntityEventLogCard', // WATCH_ARRAY warning -> non-issue.
    'DatepickerInput', // ATTR_FALSE_VALUE warning that seemed to be intended behaviour.
    'MessageAudienceForm', // WATCH_ARRAY warning -> non-issue.
    'MonthYearPicker', // ATTR_FALSE_VALUE warning that seemed to be intended behaviour.
    'RegularPicker', // Extraneous props (value)
    'ActionRow', // ATTR_FALSE_VALUE warning that seemed to be intended behaviour.
    'TimePicker', // ATTR_FALSE_VALUE warning that seemed to be intended behaviour.
    'ActionIcon', // ATTR_FALSE_VALUE warning that seemed to be intended behaviour.
    'Calendar', // ATTR_FALSE_VALUE warning that seemed to be intended behaviour.
    'Multiselect', // WATCH_ARRAY warning -> non-issue.
    'MetaGeneratedForm', // WATCH_ARRAY warning -> non-issue.
    'AuditLogListView', // WATCH_ARRAY warning -> non-issue.
    'MetaGenericActivablesCard', // WATCH_ARRAY warning -> non-issue.
    'OfferGroupsCard', // WATCH_ARRAY warning -> non-issue.
    'App', // On-unmounted hook warning -> non-issue.
    'SelectionGrid', // Vue3 component that spawns a few warnings -> non-issue.
    'TimeInput', // Vue3 component that spawns a few warnings -> non-issue.
    'DatepickerMenu', // Vue3 component that spawns a few warnings -> non-issue.
    'UserView', // WATCH_ARRAY warning -> non-issue.
    'LeagueSeasonRankDivisionDetailView', // WATCH_ARRAY warning -> non-issue.
    'GeneratedUiFormAbstractField', // CUSTOM_DIR warnings -> non-issue.
    'GeneratedUiFormArrayField', // CUSTOM_DIR warnings -> non-issue.
    'GeneratedUiFormBooleanField', // CUSTOM_DIR warnings -> non-issue.
    'GeneratedUiFormContainerField', // CUSTOM_DIR warnings -> non-issue.
    'GeneratedUiFormDictionaryField', // CUSTOM_DIR warnings -> non-issue.
    'GeneratedUiFormDynamicComponent', // CUSTOM_DIR warnings -> non-issue.
    'GeneratedUiFormEnumField', // CUSTOM_DIR warnings -> non-issue.
    'GeneratedUiFormLocalizedField', // CUSTOM_DIR warnings -> non-issue.
    'GeneratedUiFormMetaTimeField', // CUSTOM_DIR warnings -> non-issue.
    'GeneratedUiFormNumberField', // CUSTOM_DIR warnings -> non-issue.
    'GeneratedUiFormStringIdField', // CUSTOM_DIR warnings -> non-issue.
    'GeneratedUiFormTextField', // CUSTOM_DIR warnings -> non-issue.
    'AsyncComponentWrapper', // Async loading warning -> non-issue.
    'MInputNumber', // ATTR_ENUMERATED_COERCION warning -> non-issue.
    'MetaLazyLoader', // INSTACE_DESTROY, look into this later.
    'MPopover', // ATTR_FALSE_VALUE warning that seemed to be intended behaviour.
    'MRootLayout', // INSTANCE_DESTROY warning -> non-issue.
    'MTooltip', // INSTANCE_EVENT_EMITTER warning. CUSTOM_DIR warning.
    'MInputTextArea', // ATTR_FALSE_VALUE warning that seemed to be intended behaviour.
    'MInputText', // ATTR_FALSE_VALUE warning that seemed to be intended behaviour.
    'LocalizationContentsCard', // WATCH_ARRAY warning -> non-issue.
  ]

  // Register global error handlers
  app.config.errorHandler = function (err: any, vm, info) {
    // eslint-disable-next-line
    console.error(err)
    if (err) showErrorToast(err.message, 'Frontend Error')
  }

  // Vue warnings only happen in development builds, so this code will never fire in production.
  app.config.warnHandler = function (msg, instance, trace) {
    if (warningMessagesToIgnore.some((warning) => msg.includes(warning))) return
    if (traceItemsToIgnore.some((warning) => trace.split('\n')[0].includes(warning))) return

    // eslint-disable-next-line
    console.warn(`${msg}\n${trace}`)
    showWarningToast(msg, 'Frontend Warning')
  }
}

/**
 * Show a success toast to the user.
 * @param message Body message for the toast.
 * @param title Optional title for the toast. If not supplied, will default to "Done".
 * @example
   import { showSuccessToast } from '@metaplay/meta-ui'
   showSuccessToast('Your action worked perfectly.')
 */
export function showSuccessToast (message: string, title: string = 'Done') {
  const toastOptions: ToastOptions = {
    timeout: 3000,
    type: TYPE.SUCCESS,
  }
  show(message, title, toastOptions)
}

/**
 * Show a warning toast to the user.
 * @param message Body message for the toast.
 * @param title Optional title for the toast. If not supplied, will default to "Warning".
 * @example
   import { showWarningToast } from '@metaplay/meta-ui'
   showWarningToast('Your action did not work but we handled it.')
 */
export function showWarningToast (message: string, title: string = 'Warning') {
  const toastOptions: ToastOptions = {
    timeout: 5000,
    type: TYPE.WARNING,
  }
  show(message, title, toastOptions)
}

/**
 * Show an error toast to the user.
 * @param message Body message for the toast.
 * @param title Optional title for the toast. If not supplied, will default to "Error".
 * @example
   import { showErrorToast } from '@metaplay/meta-ui'
   showErrorToast('Your action went badly wrong.')
 */
export function showErrorToast (message: string, title: string = 'Error') {
  const toastOptions: ToastOptions = {
    timeout: 7000,
    type: TYPE.ERROR,
  }
  show(message, title, toastOptions)
}

/**
 * Internal helper function for displaying toasts.
 * @param message Text of the toast's body message.
 * @param title Text of the toast's title.
 * @param toastOptions Toastification options.
 */
function show (message: string, title: string, toastOptions: ToastOptions) {
  const toast = useToast()

  // Generate a cache key from each toast's unique information.
  const cacheKey = toastOptions.type + message + title

  // Only show the toast if there isn't an identical one already visible.
  if (!toastVisibilityCache.has(cacheKey)) {
    toast({
      component: MetaToast,
      props: {
        title,
        message
      }
    }, {
      ...toastOptions,
      onClose: () => {
        // When the toast closes, remove it from the visibility cache.
        toastVisibilityCache.delete(cacheKey)
      }
    })

    // Add to the visibility cache.
    toastVisibilityCache.add(cacheKey)
  }
}

/* To prevent duplicate toasts from spamming the Dashboard, we automatically de-dupe them. This is done by using a
 * cache to remember which toasts are currently visible and ignoring requests to show the show toast. This is done
 * by using a simple `Set()` as a visibility cache.
 */
const toastVisibilityCache = new Set()
