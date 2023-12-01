// This file is part of Metaplay SDK which is released under the Metaplay SDK License.
/// <reference types="vite/client" />

import MetaAbbreviateNumber from '@metaplay/meta-ui/src/components/MetaAbbreviateNumber.vue'
import MetaAuthTooltip from '@metaplay/meta-ui/src/components/MetaAuthTooltip.vue'
import MetaButton from '@metaplay/meta-ui/src/components/MetaButton.vue'
import MetaClipboardCopy from '@metaplay/meta-ui/src/components/MetaClipboardCopy.vue'
import MetaCollapse from '@metaplay/meta-ui/src/components/MetaCollapse.vue'
import MetaCountryCode from '@metaplay/meta-ui/src/components/MetaCountryCode.vue'
import MetaDuration from '@metaplay/meta-ui/src/components/MetaDuration.vue'
import MetaInputCheckbox from '@metaplay/meta-ui/src/components/MetaInputCheckbox.vue'
import MetaInputNumber from '@metaplay/meta-ui/src/components/MetaInputNumber.vue'
import MetaInputSelect from '@metaplay/meta-ui/src/components/MetaInputSelect.vue'
import MetaInputPlayerSelect from '@metaplay/meta-ui/src/components/MetaInputPlayerSelect.vue'
import MetaInputGuildSelect from '@metaplay/meta-ui/src/components/MetaInputGuildSelect.vue'
import MetaIpAddress from '@metaplay/meta-ui/src/components/MetaIpAddress.vue'
import MetaLazyLoader from '@metaplay/meta-ui/src/components/MetaLazyLoader.vue'
import MetaListCard from '@metaplay/meta-ui/src/components/MetaListCard.vue'
import MetaNoSeatbelts from '@metaplay/meta-ui/src/components/MetaNoSeatbelts.vue'
import MetaOrdinalNumber from '@metaplay/meta-ui/src/components/MetaOrdinalNumber.vue'
import MetaPageContainer from '@metaplay/meta-ui/src/components/MetaPageContainer.vue'
import MetaPageHeaderCard from '@metaplay/meta-ui/src/components/MetaPageHeaderCard.vue'
import MetaPluralLabel from '@metaplay/meta-ui/src/components/MetaPluralLabel.vue'
import MetaRawData from '@metaplay/meta-ui/src/components/MetaRawData.vue'
import MetaTime from '@metaplay/meta-ui/src/components/MetaTime.vue'
import MetaUsername from '@metaplay/meta-ui/src/components/MetaUsername.vue'
import MetaRewardBadge from '@metaplay/meta-ui/src/components/MetaRewardBadge.vue'
import MetaActionModalButton from '@metaplay/meta-ui/src/components/MetaActionModalButton.vue'
import MetaAlert from '@metaplay/meta-ui/src/components/MetaAlert.vue'
import MetaBarChart from '@metaplay/meta-ui/src/components/MetaBarChart.vue'

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
    MetaRewardBadge: typeof MetaRewardBadge
    MetaActionModalButton: typeof MetaActionModalButton
    MetaAlert: typeof MetaAlert
    MetaBarChart: typeof MetaBarChart
  }
}

export {}
