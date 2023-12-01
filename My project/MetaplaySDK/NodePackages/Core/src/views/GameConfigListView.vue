<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  :is-loading="!allGameConfigsData"
  :meta-api-error="allGameConfigsError"
  permission="api.game_config.view"
  )
  template(#overview)
    meta-page-header-card
      template(#title) View Game Configs
      p Game configs contain all your game data, such as the economy balancing.
      div.small.text-muted You can make new game configs builds and review them before publishing. Published game configs will be delivered over-the-air and do not require players to update their clients.

      template(#buttons)
        meta-action-modal-button(
          id="new-config-form"
          permission="api.game_config.edit"
          action-button-text="New Build"
          :actionButtonDisabled="!staticConfigData?.configBuildInfo.gameConfigBuildSupported"
          :action-button-tooltip="!staticConfigData?.configBuildInfo.gameConfigBuildSupported ? 'Game config builds have not been enabled.' : undefined"
          title="Build New Game Config"
          modal-size="lg"
          ok-button-text="Build Config"
          :ok-button-disabled="!formValidationState"
          :onOk="buildGameConfig"
          @show="resetBuildNewConfigModal"
          )

          // TODO: use MetaUiNext inputs instead.
          b-row
            b-col(lg).mb-3
              p You can configure and trigger a new game configs build to happen directly on the game server. It may take a few minutes for large projects.
              div.font-weight-bold.mb-1 Game Config Name
              b-form-input(
                v-model="gameConfigName"
                :state="nameValidationState"
                @submit.prevent=""
                placeholder="For example: 1.3.2"
                )

              div.mb-1.mt-2.font-weight-bold Game Config Description
              b-form-textarea(
                v-model="gameConfigDescription"
                rows="3"
                no-resize
                :state="descriptionValidationState"
                @submit.prevent=""
                placeholder="For example: Reduced the difficulty of levels between 5 and 10."
                )

            b-col(lg)
              //- Use a generated form for the rest of the build params.
              div(class="tw-border tw-border-neutral-200 tw-p-4 tw-rounded-md tw-bg-neutral-100")
                  div(class="tw-font-semibold tw-mb-2") Build Parameters
                  div.small Optional configuration for how the game config should be built. You can, for example, pull data from a different source or only build a subset of the configs.
                  //- NOTE: This is a hack as the form has some invisible margin that would otherwise create a horizontal scrollbar. Investigate later.
                  div
                    meta-generated-form(
                      typeName='Metaplay.Core.Config.GameConfigBuildParameters'
                      :abstractTypeFilter="buildParamsTypeFilter"
                      :value="buildParams"
                      @input="buildParams = $event"
                      :page="'GameConfigBuildCard'"
                      @status="buildParamsValidationState = $event"
                      ).mt-2

  template(#default)
    core-ui-placement(placementId="GameConfigs/List")

    meta-raw-data(:kv-pair="allGameConfigsData" name="allGameConfigsData")
</template>

<script lang="ts" setup>
import CoreUiPlacement from '../components/system/CoreUiPlacement.vue'

import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'
import { showSuccessToast } from '@metaplay/meta-ui'
import { MCallout } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getStaticConfigSubscriptionOptions } from '../subscription_options/general'
import { getAllGameConfigsSubscriptionOptions } from '../subscription_options/gameConfigs'
import MetaGeneratedForm from '../components/generatedui/components/MetaGeneratedForm.vue'

import type { StaticConfig } from '../subscription_options/generalTypes'
import type { IGeneratedUiFieldSchemaDerivedTypeInfo } from '../components/generatedui/generatedUiTypes'
import type { StaticGameConfigInfo } from '../gameConfigServerTypes'

const gameServerApi = useGameServerApi()

// Subscribe to the data that we need ---------------------------------------------------------------------------------

const {
  data: staticConfigData,
} = useSubscription<StaticConfig>(getStaticConfigSubscriptionOptions())

const {
  data: allGameConfigsData,
  error: allGameConfigsError,
  refresh: allGameConfigsTriggerRefresh
} = useSubscription<StaticGameConfigInfo[]>(getAllGameConfigsSubscriptionOptions())

// Form data ----------------------------------------------------------------------------------------------------------

/**
 * Optional name for the new game config.
 */
const gameConfigName = ref<string>()

/**
 * Optional description for the new game config.
 */
const gameConfigDescription = ref<string>()

const buildParams = ref<any>()

/**
 * Reset state of the new build modal.
 */
function resetBuildNewConfigModal () {
  buildParams.value = null
  gameConfigName.value = ''
  gameConfigDescription.value = ''
}

function buildParamsTypeFilter (abstractType: string) {
  if (abstractType === 'Metaplay.Core.Config.GameConfigBuildParameters') {
    const hasCustomBuildParams = staticConfigData.value != null &&
    staticConfigData.value.configBuildInfo.gameConfigBuildParametersNamespaceQualifiedName !== 'Metaplay.Core.Config.DefaultGameConfigBuildParameters'
    if (hasCustomBuildParams) { return (concreteType: IGeneratedUiFieldSchemaDerivedTypeInfo) => concreteType.typeName !== 'Metaplay.Core.Config.DefaultGameConfigBuildParameters' }
  }
  return () => true
}

// Form validation ----------------------------------------------------------------------------------------------------

/**
 *  Validation check for the name input field.
 */
const nameValidationState = computed((): true | false | null => {
  if (gameConfigName.value && gameConfigName.value.length > 0) {
    return true
  }
  // Optional validation here (eg: check for length, invalid chars, etc.) could return false if invalid.
  return false
})

/**
 *  Validation check for the description input field.
 */
const descriptionValidationState = computed((): true | false | null => {
  if (gameConfigDescription.value && gameConfigDescription.value.length > 0) {
    return true
  }
  // Optional validation here (eg: check for length, invalid chars, etc.) could return false if invalid.
  return null
})

/**
 * Validation state of the generated form.
 */
const buildParamsValidationState = ref<boolean>()

/**
 * Overall validation state of the entire modal.
 */
const formValidationState = computed(() => {
  return nameValidationState.value === true &&
    descriptionValidationState.value !== false &&
    buildParamsValidationState.value
})

// Sending build command to the game server ---------------------------------------------------------------------------

/**
 * Build game config from source data.
 */
async function buildGameConfig () {
  const params = {
    SetAsActive: false,
    Properties: {
      Name: gameConfigName.value,
      Description: gameConfigDescription.value
    },
    BuildParams: buildParams.value
  }

  await gameServerApi.post('/gameConfig/build', params)

  showSuccessToast('Game config build started.')
  allGameConfigsTriggerRefresh()
}
</script>
