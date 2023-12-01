// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
/// <reference types="vue/client" />

import MetaAbbreviateNumber from './components/MetaAbbreviateNumber.vue'
import MetaAuthTooltip from './components/MetaAuthTooltip.vue'
import MetaButton from './components/MetaButton.vue'
import MetaClipboardCopy from './components/MetaClipboardCopy.vue'
import MetaCollapse from './components/MetaCollapse.vue'
import MetaCountryCode from './components/MetaCountryCode.vue'
import MetaDuration from './components/MetaDuration.vue'
import MetaInputCheckbox from './components/MetaInputCheckbox.vue'
import MetaInputNumber from './components/MetaInputNumber.vue'
import MetaInputSelect from './components/MetaInputSelect.vue'
import MetaInputPlayerSelect from 'components/MetaInputPlayerSelect.vue'
import MetaInputGuildSelect from 'components/MetaInputGuildSelect.vue'
import MetaIpAddress from './components/MetaIpAddress.vue'
import MetaLazyLoader from './components/MetaLazyLoader.vue'
import MetaListCard from './components/MetaListCard.vue'
import MetaNoSeatbelts from './components/MetaNoSeatbelts.vue'
import MetaOrdinalNumber from './components/MetaOrdinalNumber.vue'
import MetaPageContainer from './components/MetaPageContainer.vue'
import MetaPageHeaderCard from './components/MetaPageHeaderCard.vue'
import MetaPluralLabel from './components/MetaPluralLabel.vue'
import MetaRawData from './components/MetaRawData.vue'
import MetaTime from './components/MetaTime.vue'
import MetaUsername from './components/MetaUsername.vue'
import MetaActionModalButton from './components/MetaActionModalButton.vue'
import MetaAlert from './components/MetaAlert.vue'
import MetaBarChart from 'components/MetaBarChart.vue'

declare module 'vue' {
  export interface GlobalComponents {
    RouterLink: typeof import('vue-router')['RouterLink']
    RouterView: typeof import('vue-router')['RouterView']
    MetaAbbreviateNumber: typeof MetaAbbreviateNumber
    MetaAuthTooltip: typeof MetaAuthTooltip
    MetaButton: typeof MetaButton
    MetaClipboardCopy: typeof MetaClipboardCopy
    MetaCollapse: typeof MetaCollapse
    MetaCountryCode: typeof MetaCountryCode
    MetaDuration: typeof MetaDuration
    MetaInputCheckbox: typeof MetaInputCheckbox
    MetaInputNumber: typeof MetaInputNumber
    MetaInputSelect: typeof MetaInputSelect
    MetaInputPlayerSelect: typeof MetaInputPlayerSelect
    MetaInputGuildSelect: typeof MetaInputGuildSelect
    MetaIpAddress: typeof MetaIpAddress
    MetaLazyLoader: typeof MetaLazyLoader
    MetaListCard: typeof MetaListCard
    MetaNoSeatbelts: typeof MetaNoSeatbelts
    MetaOrdinalNumber: typeof MetaOrdinalNumber
    MetaPageContainer: typeof MetaPageContainer
    MetaPageHeaderCard: typeof MetaPageHeaderCard
    MetaPluralLabel: typeof MetaPluralLabel
    MetaRawData: typeof MetaRawData
    MetaTime: typeof MetaTime
    MetaUsername: typeof MetaUsername
    MetaActionModalButton: typeof MetaActionModalButton
    MetaAlert: typeof MetaAlert
    MetaBarChart: typeof MetaBarChart
  }
}
