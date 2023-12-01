// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

import '@vuepic/vue-datepicker/dist/main.css'

import MetaToast from './components/MetaToast.vue'

import { useToastsVuePlugin } from './toasts'
import type { App } from 'vue'
import { defineAsyncComponent } from 'vue'

// Shoelace components initialization
import '@shoelace-style/shoelace/dist/themes/light.css'
import { setBasePath } from '@shoelace-style/shoelace'

setBasePath('dist/shoelace')

// Vue plugin to register all the global Metaplay components
export default function (app: App) {
  app.component('MetaAbbreviateNumber', defineAsyncComponent(async () => await import('./components/MetaAbbreviateNumber.vue')))
  app.component('MetaAuthTooltip', defineAsyncComponent(async () => await import('./components/MetaAuthTooltip.vue')))
  app.component('MetaButton', defineAsyncComponent(async () => await import('./components/MetaButton.vue')))
  app.component('MetaClipboardCopy', defineAsyncComponent(async () => await import('./components/MetaClipboardCopy.vue')))
  app.component('MetaCollapse', defineAsyncComponent(async () => await import('./components/MetaCollapse.vue')))
  app.component('MetaCountryCode', defineAsyncComponent(async () => await import('./components/MetaCountryCode.vue')))
  app.component('MetaDuration', defineAsyncComponent(async () => await import('./components/MetaDuration.vue')))
  app.component('MetaInputCheckbox', defineAsyncComponent(async () => await import('./components/MetaInputCheckbox.vue')))
  app.component('MetaInputNumber', defineAsyncComponent(async () => await import('./components/MetaInputNumber.vue')))
  app.component('MetaInputSelect', defineAsyncComponent(async () => await import('./components/MetaInputSelect.vue')))
  app.component('MetaInputPlayerSelect', defineAsyncComponent(async () => await import('./components/MetaInputPlayerSelect.vue')))
  app.component('MetaInputGuildSelect', defineAsyncComponent(async () => await import('./components/MetaInputGuildSelect.vue')))
  app.component('MetaIpAddress', defineAsyncComponent(async () => await import('./components/MetaIpAddress.vue')))
  app.component('MetaLazyLoader', defineAsyncComponent(async () => await import('./components/MetaLazyLoader.vue')))
  app.component('MetaListCard', defineAsyncComponent(async () => await import('./components/MetaListCard.vue')))
  app.component('MetaNoSeatbelts', defineAsyncComponent(async () => await import('./components/MetaNoSeatbelts.vue')))
  app.component('MetaOrdinalNumber', defineAsyncComponent(async () => await import('./components/MetaOrdinalNumber.vue')))
  app.component('MetaPageContainer', defineAsyncComponent(async () => await import('./components/MetaPageContainer.vue')))
  app.component('MetaPageHeaderCard', defineAsyncComponent(async () => await import('./components/MetaPageHeaderCard.vue')))
  app.component('MetaPluralLabel', defineAsyncComponent(async () => await import('./components/MetaPluralLabel.vue')))
  app.component('MetaRawData', defineAsyncComponent(async () => await import('./components/MetaRawData.vue')))
  app.component('MetaToast', defineAsyncComponent(async () => await import('./components/MetaToast.vue')))
  app.component('MetaTime', defineAsyncComponent(async () => await import('./components/MetaTime.vue')))
  app.component('MetaUsername', defineAsyncComponent(async () => await import('./components/MetaUsername.vue')))
  app.component('MetaRewardBadge', defineAsyncComponent(async () => await import('./components/MetaRewardBadge.vue')))
  app.component('MetaActionModalButton', defineAsyncComponent(async () => await import('./components/MetaActionModalButton.vue')))
  app.component('MetaAlert', defineAsyncComponent(async () => await import('./components/MetaAlert.vue')))
  app.component('MetaBarChart', defineAsyncComponent(async () => await import('./components/MetaBarChart.vue')))

  app.use(useToastsVuePlugin)
}

export type {
  MetaInputSelectOption,
  MetaPageContainerAlert,
} from './additionalTypes'

export {
  abbreviateNumber,
  ensureEndsWith,
  getLanguageName,
  humanizeUsername,
  isoCodeToCountryFlag,
  isoCodeToCountryName,
  maybePlural,
  metaJsonDurationToLuxonDuration,
  parseAuthMethods, // Should this be refactored away?
  pascalToDisplayName,
  toSentenceCase,
  getObjectPrintableFields,
  toTitleCase,
  makeIntoUniqueKey,
  roughSizeOfObject,
  experimentPhaseDetails,
} from './utils'

export {
  showErrorToast,
  showSuccessToast,
  showWarningToast,
} from './toasts'

export {
  MetaListFilterOption,
  MetaListFilterSet,
  MetaListSortDirection,
  MetaListSortOption,
} from './metaListUtils'

export {
  useUiStore
} from './uiStore'

export { MetaToast }

export type { GameSpecificReward } from './rewardUtils'

export { rewardWithMetaData, rewardsWithMetaData } from './rewardUtils'
