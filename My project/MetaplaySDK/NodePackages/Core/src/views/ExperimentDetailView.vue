<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
meta-page-container(
  no-bottom-padding
  :is-loading="!singleExperimentData || !playerExperiments"
  :meta-api-error="singleExperimentError"
  :alerts="headerAlerts"
  fluid
  )
  template(#error-card-message)
    p Oh no, something went wrong while trying to access the experiment!

  template(#overview)
    meta-page-header-card(data-cy="overview" :id="experimentId")
      template(#title) {{ singleExperimentData.stats.displayName }}
      template(#subtitle)
        div {{ singleExperimentData.stats.description }}

      template(#buttons)
        div(v-for="button in phaseInfo.buttons").mb-2.d-inline-flex
          meta-button.mr-2(permission="api.experiments.edit" variant="primary" :disabled="!button.modal" :modal="button.modal" @click="lastClickedButton = button") {{ button.title }}

      div.d-md-flex.justify-content-around.mt-5.mb-5.d-none(style="font-size: 130%")
        MBadge(:variant="(phase === 'Testing') ? 'primary' : 'neutral'").mx-md-2 {{ phaseInfos.Testing.title }}
        fa-icon(icon="arrow-right").mt-2
        MBadge(:variant="(phase === 'Ongoing' || phase === 'Paused') ? 'primary' : 'neutral'").mx-md-2 {{ phaseInfos[phase === 'Paused' ? 'Paused' : 'Ongoing'].title }}
        fa-icon(icon="arrow-right").mt-2
        MBadge(:variant="phase === 'Concluded' ? 'primary' : 'neutral'").mx-md-2 {{ phaseInfos.Concluded.title }}
      div.text-center(style="font-size: 130%").mt-5.mb-5.d-md-none
        MBadge(:variant="(phase === 'Testing') ? 'primary' : 'neutral'").mx-md-2 {{ phaseInfos.Testing.title }}
        div: fa-icon(icon="arrow-down")
        MBadge(:variant="(phase === 'Ongoing' || phase === 'Paused') ? 'primary' : 'neutral'").mx-md-2 {{ phaseInfos[phase === 'Paused' ? 'Paused' : 'Ongoing'].title }}
        div: fa-icon(icon="arrow-down")
        MBadge(:variant="phase === 'Concluded' ? 'primary' : 'neutral'").mx-md-2 {{ phaseInfos.Concluded.title }}

      span.font-weight-bold #[fa-icon(icon="chart-bar")] Overview
      b-table-simple(small responsive).mt-1
        b-tbody
          b-tr
            b-td Status
            b-td.text-right: MBadge(:variant="phaseInfo.titleVariant") {{ phaseInfo.title }}
          b-tr
            b-td Rollout
            b-td.text-right(v-if="singleExperimentData.state.isRolloutDisabled") #[MBadge(variant='warning') Disabled]
            b-td.text-right(v-else-if="singleExperimentData.state.targetCondition != null") {{ singleExperimentData.state.rolloutRatioPermille / 10 }}% of players matching segmentation
            b-td.text-right(v-else) {{ singleExperimentData.state.rolloutRatioPermille / 10 }}% of everyone
          b-tr
            b-td Enroll Trigger
            b-td.text-right: MBadge {{ singleExperimentData.state.enrollTrigger }}
          b-tr
            b-td Audience Size Estimate
            b-td.text-right #[meta-audience-size-estimate(:sizeEstimate="estimatedAudienceSize")]
          b-tr
            b-td Max Capacity
            b-td(v-if="singleExperimentData.state.hasCapacityLimit").text-right #[meta-abbreviate-number(:value="singleExperimentData.state.maxCapacity" unit="player")]
            b-td(v-else).text-right.text-muted ∞
          b-tr
            b-td Created At
            b-td.text-right #[meta-time(:date="singleExperimentData.stats.createdAt" showAs="timeagoSentenceCase")]
          b-tr
            b-td Experiment Analytics ID
            b-td(v-if="!isExperimentMissing" :class="{ 'text-danger': !experiment.experimentAnalyticsId }").text-right {{ experiment.experimentAnalyticsId || 'None' }}
            b-td(v-else).text-right.text-danger None

      span.font-weight-bold #[fa-icon(icon="chart-line")] Statistics
      b-table-simple(small responsive).mt-1
        b-tbody
          b-tr
            b-td Total Participants
            b-td.text-right #[meta-abbreviate-number(:value="singleExperimentData.state.numPlayersInExperiment" unit="player")]
          b-tr
            b-td Rollout Started At
            b-td(v-if="phase === 'Testing' && singleExperimentData.stats.ongoingFirstTimeAt === null").text-right.text-muted.font-italic
              span Not started
            b-td(v-else-if="phase === 'Testing'").text-right
              span.text-muted.font-italic Not started
              MBadge(tooltip="Experiment has previously been rolled out." shape="pill").ml-1 ?
            b-td(v-else-if="singleExperimentData.stats.ongoingFirstTimeAt === singleExperimentData.stats.ongoingMostRecentlyAt").text-right
              meta-time(:date="singleExperimentData.stats.ongoingFirstTimeAt" showAs="timeagoSentenceCase")
            b-td(v-else).text-right
              meta-time(:date="singleExperimentData.stats.ongoingMostRecentlyAt" showAs="timeagoSentenceCase")
              MBadge(tooltip="Experiment has previously been rolled out." shape="pill").ml-1 ?
          b-tr
            b-td Running Time
            b-td(v-if="['Ongoing', 'Paused', 'Concluded'].includes(phase)").text-right
              meta-duration(:duration="totalOngoingDuration.toString()" showAs="humanizedSentenceCase")
              MBadge(v-if="singleExperimentData.stats.ongoingFirstTimeAt !== singleExperimentData.stats.ongoingMostRecentlyAt" tooltip="Experiment has been active more than once." shape="pill").ml-1 ?
            b-td(v-else).text-right
              span.text-muted.font-italic Not started
              MBadge(v-if="singleExperimentData.stats.ongoingFirstTimeAt !== null" tooltip="Experiment has previously been rolled out." shape="pill").ml-1 ?
          b-tr
            b-td Reached Capacity At
            b-td(v-if="!singleExperimentData.state.hasCapacityLimit").text-right.text-muted.font-italic
              span No max capacity
            b-td(v-else-if="singleExperimentData.stats.reachedCapacityFirstTimeAt === null").text-right.text-muted.font-italic
              span Not reached
            b-td(v-else-if="singleExperimentData.stats.reachedCapacityFirstTimeAt === singleExperimentData.stats.reachedCapacityMostRecentlyAt").text-right
              meta-time(:date="singleExperimentData.stats.reachedCapacityFirstTimeAt" showAs="timeagoSentenceCase")
            b-td(v-else).text-right
              meta-time(:date="singleExperimentData.stats.reachedCapacityMostRecentlyAt" showAs="timeagoSentenceCase")
              MBadge(tooltip="Experiment has reached capacity more than once." shape="pill").ml-1 ?
          b-tr
            b-td Concluded At
            b-td(v-if="['Testing', 'Ongoing', 'Paused'].includes(phase) && singleExperimentData.stats.concludedFirstTimeAt === null").text-right.text-muted.font-italic
              span Not concluded
            b-td(v-else-if="['Testing', 'Ongoing', 'Paused'].includes(phase)").text-right
              span.text-muted.font-italic Not concluded
              MBadge(tooltip="Experiment has previously been concluded." shape="pill").ml-1 ?
            b-td(v-else-if="singleExperimentData.stats.concludedFirstTimeAt === singleExperimentData.stats.concludedMostRecentlyAt").text-right
              meta-time(:date="singleExperimentData.stats.concludedFirstTimeAt" showAs="timeagoSentenceCase")
            b-td(v-else).text-right
              meta-time(:date="singleExperimentData.stats.concludedMostRecentlyAt" showAs="timeagoSentenceCase")
              MBadge(tooltip="Experiment has been concluded more than once." shape="pill").ml-1 ?

      //- TODO: show the right label conditionally. Could also do something better if there are good ideas?
      div.font-weight-bold Performance Tip
      p(v-if="['Ongoing'].includes(phase)").small.mb-0
        | This experiment is currently adding {{ singleExperimentData.combinations.currentCombinations - singleExperimentData.combinations.newCombinations }} live game config combinations to the total of {{ singleExperimentData.combinations.currentCombinations }} possible combinations.
      p(v-if="['Testing', 'Paused', 'Concluded'].includes(phase)").small.mb-0
        | This experiment is currently not running and thus is not affecting game server memory use.

  template(#default)
    b-container
      b-row(no-gutters align-v="center").mt-3.mb-2
        h3 Configuration

      b-row(align-h="center")
        b-col(md="6").mb-3
          targeting-card(
            data-cy="segments"
            :targetCondition="singleExperimentData.state.targetCondition"
            ownerTitle="This experiment"
          )

        b-col(md="6").mb-3
          experiment-variants-card(:experiment-id="experimentId")

      b-row(align-h="center").mb-3
        b-col(md="6")
          player-list-card(
            data-cy="testers"
            :playerIds="singleExperimentData.state.testerPlayerIds"
            title="Test Players"
            emptyMessage="No players have been assigned to test this experiment."
          )

      b-row(no-gutters align-v="center").mt-3.mb-2
        h3 Admin

      b-row(align-h="center").mb-3
        b-col(md="6")
          audit-log-card(
            data-cy="audit-log"
            targetType="$Experiment"
            :targetId="experimentId"
            )

      //- Advance phase modal
      b-modal#action-advance-experiment-phase(:title="lastClickedButton.modalTitle" @ok="setPhaseToLastClickedButton" size="md" centered no-close-on-backdrop)
        div.mb-4 {{ lastClickedButton.modalText }}

        meta-alert(
          v-if="phaseInfo.nextPhasePerformanceTip === 'increase' && singleExperimentData.combinations.exceedsThreshold"
          noShadow
          variant="warning"
          title="Performance Tip"
          :message="`Rolling this experiment out will increase the total number of live game config combinations to ${singleExperimentData.combinations.newCombinations} from the current ${singleExperimentData.combinations.currentCombinations}. This is a very high number and may cause a large use of memory on your game servers depending on how the experiments have been targeted. Consider concluding other experiments to keep the number of live game config combinations low.`"
          )
        meta-alert(
          v-else-if="phaseInfo.nextPhasePerformanceTip === 'increase'"
          noShadow
          variant="secondary"
          title="Performance Tip"
          :message="`Rolling this experiment out will increase the total number of live game config combinations to ${singleExperimentData.combinations.newCombinations} from the current ${singleExperimentData.combinations.currentCombinations}. Remember to keep an eye on your game server memory use when running many experiments in parallel.`"
          )
        meta-alert(
          v-else-if="phaseInfo.nextPhasePerformanceTip === 'decrease'"
          noShadow
          variant="secondary"
          title="Performance Tip"
          :message="`Concluding or pausing this experiment will decrease the total number of live game config combinations to ${singleExperimentData.combinations.newCombinations} from the current ${singleExperimentData.combinations.currentCombinations}. This will reduce the memory use on your game servers.`"
          )

        template(#modal-footer="{ ok, cancel }")
          meta-button(variant="secondary" @click="cancel") Cancel
          meta-button(variant="primary" @click="ok" safety-lock) {{ lastClickedButton.modalOkButtonText }}

      //- Edit modal
      experiment-form(
        :experimentId="experimentId"
        :title="lastClickedButton.modalTitle"
        :warningMessage="lastClickedButton.modalText"
        :okButtonText="lastClickedButton.modalOkButtonText"
        @ok="setPhaseToLastClickedButton"
        )

    b-container(fluid)
      b-row(no-gutters align-v="center").mt-3.mb-2
        h3 Experiment Data

      config-contents-card(
        v-if="!isExperimentMissing"
        data-cy="config-contents"
        :experiment-id="experimentId"
        hide-no-diffs
        exclude-server-libraries
        )
      b-card(v-else)
        b-row.justify-content-center.py-5 This experiment is missing from the game config and cannot be displayed.

    b-container
      meta-raw-data(:kvPair="experiment" name="experiment")
      meta-raw-data(:kvPair="singleExperimentData" name="experimentInfo")
</template>

<script lang="ts" setup>
import { DateTime } from 'luxon'
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'

import { useGameServerApi } from '@metaplay/game-server-api'
import { metaJsonDurationToLuxonDuration, showSuccessToast } from '@metaplay/meta-ui'
import { MBadge, type Variant } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'

import AuditLogCard from '../components/auditlogs/AuditLogCard.vue'
import ExperimentForm from '../components/experiments/ExperimentForm.vue'
import ExperimentVariantsCard from '../components/experiments/ExperimentVariantsCard.vue'
import ConfigContentsCard from '../components/global/ConfigContentsCard.vue'
import PlayerListCard from '../components/global/PlayerListCard.vue'
import TargetingCard from '../components/mails/TargetingCard.vue'
import MetaAudienceSizeEstimate from '../components/MetaAudienceSizeEstimate.vue'

import { getSingleExperimentSubscriptionOptions } from '../subscription_options/experiments'
import { getDatabaseItemCountsSubscriptionOptions, getGameDataSubscriptionOptions, getPlayerSegmentsSubscriptionOptions } from '../subscription_options/general'
import { estimateAudienceSize, routeParamToSingleValue, isNullOrUndefined } from '../coreUtils'

const gameServerApi = useGameServerApi()
const route = useRoute()

const {
  data: playerSegmentsData,
} = useSubscription(getPlayerSegmentsSubscriptionOptions())

const {
  data: databaseItemCountsData,
} = useSubscription(getDatabaseItemCountsSubscriptionOptions())
const totalPlayerCount = computed(() => databaseItemCountsData.value?.totalItemCounts.Players || 0)

const {
  data: gameData,
} = useSubscription(getGameDataSubscriptionOptions())

// PHASE STUFF -----------------------------------------

interface PhaseInfoButton {
  title: string
  modal?: string
  modalTitle?: string
  modalText?: string
  modalOkButtonText?: string
  nextPhase?: string
  nextPhaseToast?: string
}

const lastClickedButton = ref<PhaseInfoButton>({
  title: '',
})

interface PhaseInfo {
  title: string
  titleVariant: Variant
  buttons: PhaseInfoButton[]
  nextPhasePerformanceTip?: 'increase' | 'decrease'
}

const phase = computed(() => singleExperimentData.value?.state.lifecyclePhase)
const phaseInfo = computed(() => phaseInfos[phase.value])

const phaseInfos: { [key: string]: PhaseInfo } = {
  Testing: {
    title: 'Testing',
    titleVariant: 'primary',
    buttons: [
      {
        title: 'Configure',
        modal: 'action-edit-experiment',
        modalTitle: 'Configure Experiment',
        modalOkButtonText: 'Update Configuration',
      },
      {
        title: 'Pause',
      },
      {
        title: 'Rollout',
        modal: 'action-advance-experiment-phase',
        modalTitle: 'Rollout Experiment',
        modalText: 'Rolling out an experiment will enable it for the targeted players. Please make sure you are happy with how the experiment is configured, because changing an experiment while it is running may make it harder to analyse the results.',
        modalOkButtonText: 'Rollout',
        nextPhase: 'Ongoing',
        nextPhaseToast: 'Experiment [EXPERIMENT_NAME] rolled out to players!'
      }
    ],
    nextPhasePerformanceTip: 'increase',
  },
  Ongoing: {
    title: 'Active',
    titleVariant: 'success',
    buttons: [
      {
        title: 'Reconfigure',
        modal: 'action-edit-experiment',
        modalTitle: 'Reconfigure Experiment',
        modalText: 'Reconfiguring an experiment after it has been rolled out may not achieve the expected result and is therefore generally discouraged. For example, decreasing rollout % or capacity limit will have no effect if those limits have already been reached. Changing targeting or weights may make it harder to analyse the experiment results.',
        modalOkButtonText: 'Update configuration',
      },
      {
        title: 'Pause',
        modal: 'action-advance-experiment-phase',
        modalTitle: 'Pause Experiment',
        modalText: 'Pausing an experiment will stop further rollout to players and only players who were enrolled as testers for the experiment will see the variants. Use this if you are unsure of your variants and you need to re-test them before you continue.',
        modalOkButtonText: 'Pause',
        nextPhase: 'Paused',
        nextPhaseToast: 'Experiment [EXPERIMENT_NAME] paused.'
      },
      {
        title: 'Conclude',
        modal: 'action-advance-experiment-phase',
        modalTitle: 'Conclude Experiment',
        modalText: 'Concluding an experiment will prevent further people from joining it. After concluding, all participating players will see the control variant of the experiment.',
        modalOkButtonText: 'Conclude',
        nextPhase: 'Concluded',
        nextPhaseToast: 'Experiment [EXPERIMENT_NAME] concluded.'
      }
    ],
    nextPhasePerformanceTip: 'decrease',
  },
  Paused: {
    title: 'Paused',
    titleVariant: 'warning',
    buttons: [
      {
        title: 'Reconfigure',
        modal: 'action-edit-experiment',
        modalTitle: 'Reconfigure Experiment',
        modalText: 'Reconfiguring an experiment after it has been enabled may not achieve the expected result and is therefore generally discouraged. For example, decreasing rollout % or capacity limit will have no effect if those limits have already been reached. Changing targeting or weights may make it harder to analyse the experiment results.',
        modalOkButtonText: 'Update Configuration',
      },
      {
        title: 'Continue',
        modal: 'action-advance-experiment-phase',
        modalTitle: 'Continue Experiment',
        modalText: 'Continuing the experiment will put it back into the active state. Players who are already in the experiment will start seeing their variants again, and new players will get enrolled into the experiment.',
        modalOkButtonText: 'Continue',
        nextPhase: 'Ongoing',
        nextPhaseToast: 'Experiment [EXPERIMENT_NAME] unpaused.'
      },
      {
        title: 'Conclude',
        modal: 'action-advance-experiment-phase',
        modalTitle: 'Conclude Experiment',
        modalText: 'Concluding an experiment will prevent further people from joining it. After concluding, all participating players will see the control variant of the experiment.',
        modalOkButtonText: 'Conclude',
        nextPhase: 'Concluded',
        nextPhaseToast: 'Experiment [EXPERIMENT_NAME] concluded.'
      }
    ],
    nextPhasePerformanceTip: 'increase',
  },
  Concluded: {
    title: 'Concluded',
    titleVariant: 'neutral',
    buttons: [
      {
        title: 'Reconfigure',
      },
      {
        title: 'Pause',
      },
      {
        title: 'Restart',
        modal: 'action-advance-experiment-phase',
        modalTitle: 'Restart Experiment',
        modalText: 'Restarting an experiment will place it back into the testing phase. Players who were enrolled as testers for the experiment will start to see their assigned variants again. Players who joined the experiment naturally will see the control variant until the experiment is rolled out again.',
        modalOkButtonText: 'Restart',
        nextPhase: 'Testing',
        nextPhaseToast: 'Experiment [EXPERIMENT_NAME] restarted.'
      }
    ]
  }
}

async function setPhaseToLastClickedButton () {
  const nextPhase = lastClickedButton.value.nextPhase
  if (nextPhase && phase.value !== nextPhase) {
    // Change phase
    await gameServerApi.post(`/experiments/${experimentId}/phase`, { Phase: nextPhase, Force: true })

    // Toast the success
    if (lastClickedButton.value.nextPhaseToast) {
      const message = lastClickedButton.value.nextPhaseToast.replace('[EXPERIMENT_NAME]', `'${singleExperimentData.value.stats.displayName}'`)
      showSuccessToast(message)
    }
    singleExperimentRefresh()
  }
}

// EXPERIMENTS -----------------------------------------

const experimentId = routeParamToSingleValue(route.params.id)
const {
  data: singleExperimentData,
  refresh: singleExperimentRefresh,
  error: singleExperimentError
} = useSubscription(getSingleExperimentSubscriptionOptions(experimentId || ''))

const playerExperiments = computed(() => gameData.value?.serverGameConfig.PlayerExperiments)
const experiment = computed(() => gameData.value?.serverGameConfig.PlayerExperiments[experimentId])

const isExperimentMissing = computed(() => !experiment.value)

// MISC UI -----------------------------------------

const headerAlerts = computed(() => {
  const alerts: Array<{
    title: string
    message: string
    dataCy?: string | undefined
    variant?: 'secondary' | 'warning' | 'info' | 'danger' | undefined
  }> = []

  // Experiment missing
  if (isExperimentMissing.value) {
    alerts.push({
      title: 'Experiment removed',
      variant: 'danger',
      message: `The experiment '${singleExperimentData.value?.stats.displayName}' is missing from the game config and has been disabled. Restore the experiment to your game config to re-enable it.`
    })
  }

  // Missing variants
  const missingVariantIds: string[] = []
  if (isNullOrUndefined(singleExperimentData.value)) {
    Object.entries(singleExperimentData.value.state.variants).forEach(([id, variant]) => {
      if ((variant as any).isConfigMissing === true) {
        missingVariantIds.push(id)
      }
    })
    if (missingVariantIds.length === 1) {
      alerts.push({
        title: 'Variant removed',
        variant: 'warning',
        message: `The variant '${missingVariantIds[0]}' has been removed from the game config and has been disabled. Restore the variant to your game config to re-enable it.`
      })
    } else if (missingVariantIds.length > 1) {
      let variantNameList = ''
      while (missingVariantIds.length > 0) {
        variantNameList += `'${missingVariantIds.shift()}'`
        if (missingVariantIds.length > 1) variantNameList += ', '
        else if (missingVariantIds.length === 1) variantNameList += ' and '
      }
      alerts.push({
        title: 'Variants removed',
        variant: 'warning',
        message: `The variants ${variantNameList} have been removed from the game config and has been disabled. Restore the variant to your game config to re-enable it.`
      })
    }

    // Empty variant weights
    const weightlessVariantIds = []
    if (singleExperimentData.value.state.controlWeight === 0) {
      weightlessVariantIds.push('Control group')
    }
    Object.entries(singleExperimentData.value.state.variants).forEach(([id, variant]) => {
      if ((variant as any).weight === 0) {
        weightlessVariantIds.push(id)
      }
    })
    if (weightlessVariantIds.length === 1) {
      alerts.push({
        title: 'Variant inaccessible',
        variant: 'warning',
        message: `The variant '${weightlessVariantIds[0]}' has a weight of 0%. This means that the variant will never be shown to any players. Is this what you intended?`
      })
    } else if (weightlessVariantIds.length > 1) {
      let variantNameList = ''
      while (weightlessVariantIds.length > 0) {
        variantNameList += `'${weightlessVariantIds.shift()}'`
        if (weightlessVariantIds.length > 1) variantNameList += ', '
        else if (weightlessVariantIds.length === 1) variantNameList += ' and '
      }
      alerts.push({
        title: 'Variants inaccessible',
        variant: 'warning',
        message: `The variants ${variantNameList} have been removed from the game config and has been disabled. Restore the variant to your game config to re-enable it.`
      })
    }
  }

  return alerts
})

const backgroundVariant = computed(() => {
  if (headerAlerts.value.some(headerAlert => headerAlert.variant === 'danger')) return 'danger'
  else if (headerAlerts.value.some(headerAlert => headerAlert.variant === 'warning')) return 'warning'
  return undefined
})

// UNSORTED -------------------------------------------

const totalOngoingDuration = computed(() => {
  if (['Ongoing'].includes(phase.value)) {
    let duration = DateTime.now().diff(DateTime.fromISO(singleExperimentData.value.stats.ongoingMostRecentlyAt))
    const ongoingDurationBeforeCurrentSpan = metaJsonDurationToLuxonDuration(singleExperimentData.value.stats.ongoingDurationBeforeCurrentSpan)

    if (ongoingDurationBeforeCurrentSpan) {
      duration = duration.plus(ongoingDurationBeforeCurrentSpan)
    }
    return duration
  } else {
    return singleExperimentData.value.stats.ongoingDurationBeforeCurrentSpan
  }
})

const estimatedAudienceSize = computed((): number | null => {
  if (playerSegmentsData.value) {
    let totalAudience: number | null = 0
    if (singleExperimentData.value.state.targetCondition) {
      totalAudience = estimateAudienceSize(playerSegmentsData.value.segments, { targetCondition: singleExperimentData.value.state.targetCondition })
    } else {
      totalAudience = totalPlayerCount.value
    }
    if (totalAudience == null) {
      return null
    }
    return totalAudience * singleExperimentData.value.state.rolloutRatioPermille / 1000
  } else {
    return null
  }
})

</script>
