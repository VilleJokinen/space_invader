<template lang="pug">
meta-action-modal-button(
  :id="id"
  permission="api.notifications.edit"
  variant="primary"
  modal-title="New Notification Campaign"
  modal-size="lg"
  ok-button-text="Start Campaign"
  :action-button-text="buttonText"
  :action-button-icon="buttonIcon"
  :ok-button-disabled="!isFormValid"
  :on-ok="createOrUpdateCampaign"
  @show="resetNewCampaign"
  )

  b-form
    b-row.mt-2
      b-col
        h6.mb-1 Name
          span.small.text-muted.text-no-transform.ml-2.d-none.d-lg-inline (not visible in the game)
        b-form-input(
          :value="campaignFormInfo.name"
          @input="campaignFormInfo.name = $event; autofillFirebaseAnalyticsLabelFromName()"
          :state="isNameValid"
          placeholder="Campaign name here..."
          required
          )

      b-col
        MInputDateTime(
          :model-value="campaignFormInfo.targetTime"
          @update:model-value="onTargetDateTimeChange"
          min-date-time="now"
          label="Date"
          )

    b-row.mt-3
      b-col
        h6.mb-1 Firebase Analytics Label
        b-form-input(v-model="campaignFormInfo.firebaseAnalyticsLabel" placeholder="Optional analytics label..." :state="isFirebaseAnalyticsLabelValid ? null : false")
        span.small.text-no-transform(v-if="!isFirebaseAnalyticsLabelValid") At most {{ firebaseAnalyticsLabelMaxLength }} characters; ASCII alphanumerics and <span class="text-monospace">{{ firebaseAnalyticsLabelSpecialCharacters }}</span>

    b-row.mt-3
      b-col
        message-audience-form(
          :value="campaignFormInfo.audience"
          @input="campaignFormInfo.audience = $event"
          )

    b-row.mt-4
      b-col
        meta-generated-form(
          typeName="Metaplay.Server.NotificationCampaign.NotificationContent"
          :value="campaignFormInfo.content"
          @input="campaignFormInfo.content = $event"
          @status="isContentValid = $event"
          :page="'NotificationFormButton'"
          )

    b-row(v-if="uiStore.showDeveloperUi").mt-4
      b-col
        div.bg-light.rounded.rounded-sm.border.p-3
          h6.mb-1 Debug Options
            span.small.text-no-transform.ml-2
              span.text-muted Visible in developer mode.
              span.text-danger.ml-1 Do not use these in production!

          div.d-flex.justify-content-between.mb-2.mt-2
            span.font-weight-bold Fake notification mode
            meta-input-checkbox(v-model="campaignFormInfo.debugFakeNotificationMode" name="Fake notification mode" size="lg" showAs="switch")
          span.mb-1 Debug Entity ID Bound #[small.text-muted (how many bots you are running)]
          b-form-input(
            :disabled="!campaignFormInfo.debugFakeNotificationMode"
            v-model="campaignFormInfo.debugEntityIdValueUpperBound"
            :state="campaignFormInfo.debugFakeNotificationMode ? campaignFormInfo.debugEntityIdValueUpperBound > 0 : null"
            type="number"
            min="1"
            @blur="campaignFormInfo.debugEntityIdValueUpperBound < 1 ? campaignFormInfo.debugEntityIdValueUpperBound = 1 : campaignFormInfo.debugEntityIdValueUpperBound = campaignFormInfo.debugEntityIdValueUpperBound")
</template>

<script lang="ts" setup>
import { DateTime } from 'luxon'
import { computed, ref } from 'vue'

import { useGameServerApi } from '@metaplay/game-server-api'

import { showSuccessToast, useUiStore } from '@metaplay/meta-ui'

import MessageAudienceForm from '../mails/MessageAudienceForm.vue'
import MetaGeneratedForm from '../generatedui/components/MetaGeneratedForm.vue'

import type { TargetConditionContent } from '../mails/mailUtils'

import { MInputDateTime } from '@metaplay/meta-ui-next'

/**
 * Server definition of localized string.
 */
interface LocalizedString {localizations: {[key: string]: string}}

/**
 * Server definition of localized content.
 */
interface LocalizedContent {title?: LocalizedString, body?: LocalizedString}

/**
 * Server definition of a notification.
 */
interface NotificationInfo {
  id?: string
  name: string
  targetTime: string
  content: LocalizedContent
  debugEntityIdValueUpperBound?: number
  debugFakeNotificationMode?: boolean
  firebaseAnalyticsLabel: string
  targetPlayers: string[]
  targetCondition: TargetConditionContent | null
}

const props = defineProps<{
  /**
   * Id of the modal to be shown.
   * @example 'duplicate-campaign'
   */
  id: string
  /**
   * Text for the modal's action button. Should be kept as short as possible.
   * @example 'New campaign'
   */
  buttonText: string
  /**
   * Optional: Existing notification content which is to be duplicated.
   */
  oldNotification?: NotificationInfo
  /**
   * Optional: Re-use an existing broadcast content.
   */
  update?: boolean
  /**
   * Optional: A Font-Awesome icon to place in the action button.
   * @example 'paper-plane'
   */
  buttonIcon?: string
}>()

// Subscriptions and core systems -------------------------------------------------------------------------------------

const gameServerApi = useGameServerApi()
const uiStore = useUiStore()

// Form data and validation -------------------------------------------------------------------------------------------

/**
 * The form on this page collects the following information about a campaign.
 */
interface CampaignFormInfo {
  name: string
  targetTime: DateTime
  content: LocalizedContent
  debugEntityIdValueUpperBound: number
  debugFakeNotificationMode: boolean
  firebaseAnalyticsLabel: string
  audience: any
}

/**
 * Campaign details to be collected using this form.
 */
const campaignFormInfo = ref(getNewCampaignFormInfo())

/**
 * Return initial data for a new campaign.
 */
function getNewCampaignFormInfo (): CampaignFormInfo {
  return {
    name: '',
    targetTime: DateTime.now(),
    content: { title: undefined, body: undefined },
    debugEntityIdValueUpperBound: 100,
    debugFakeNotificationMode: false,
    firebaseAnalyticsLabel: '',
    audience: MessageAudienceForm.props.value.default(),
  }
}

/**
 * Checks that the name field is not empty.
 */
const isNameValid = computed(() => {
  if (!campaignFormInfo.value.name || campaignFormInfo.value.name.length === 0) {
    return false
  } else {
    return true
  }
})

/**
 * Autofill the Firebase analytics label field based on the campaign's name.
 */
function autofillFirebaseAnalyticsLabelFromName () {
  campaignFormInfo.value.firebaseAnalyticsLabel = convertToValidFirebaseAnalyticsLabel(campaignFormInfo.value.name)
}

/**
 * List of special characters allowed in the Firebase analytics label. Limitation set by Firebase.
 */
const firebaseAnalyticsLabelSpecialCharacters = ['-', '_', '.', '~', '%']

/**
 * Maximum length of the Firebase analytics label. Limitation set by Firebase.
*/
const firebaseAnalyticsLabelMaxLength = 50

/**
 * Converts a string to a valid Firebase analytics label by removing spaces and special characters.
 * @param str The string to be converted.
 */
function convertToValidFirebaseAnalyticsLabel (str: string) {
  let result = ''
  for (let i = 0; i < str.length; i++) {
    const c = str.charAt(i)
    if (isValidFirebaseAnalyticsCharacter(c)) {
      result += c
    } else if (c === ' ') {
      result += '_'
    } else {
      result += '%'
    }
  }
  if (result.length > firebaseAnalyticsLabelMaxLength) {
    result = result.substring(0, firebaseAnalyticsLabelMaxLength)
  }
  return result
}

/**
 * Checks whether a given character valid for a Firebase analytics label.
 * @param c The character to check.
 */
function isValidFirebaseAnalyticsCharacter (c: string) {
  return (c >= 'a' && c <= 'z') ||
         (c >= 'A' && c <= 'Z') ||
         (c >= '0' && c <= '9') ||
         firebaseAnalyticsLabelSpecialCharacters.includes(c)
}

/**
 * Checks whether the Firebase analytics label field contains valid characters.
 */
const isFirebaseAnalyticsLabelValid = computed(() => {
  const label = campaignFormInfo.value.firebaseAnalyticsLabel

  if (label.length > firebaseAnalyticsLabelMaxLength) {
    return false
  }

  for (let i = 0; i < label.length; i++) {
    if (!isValidFirebaseAnalyticsCharacter(label.charAt(i))) {
      return false
    }
  }
  return true
})

const isContentValid = ref<boolean>(false)

/**
 * Checks that the form input fields have valid values.
 */
const isFormValid = computed(() => {
  if (!isNameValid.value) return false
  if (!isContentValid.value) return false
  if (!campaignFormInfo.value.audience.valid) return false
  if (!isFirebaseAnalyticsLabelValid.value) return false
  return true
})

/**
 * Utility function to prevent undefined inputs.
 */
function onTargetDateTimeChange (value?: DateTime) {
  if (!value) return
  campaignFormInfo.value.targetTime = value
}

// Modify/create notification -----------------------------------------------------------------------------------------

const emits = defineEmits(['refresh'])

/**
 * Send the updated or new notification payload to the server.
 */
async function createOrUpdateCampaign () {
  const payload: NotificationInfo = {
    name: campaignFormInfo.value.name,
    targetTime: campaignFormInfo.value.targetTime.toISO() as string,
    content: campaignFormInfo.value.content,
    firebaseAnalyticsLabel: campaignFormInfo.value.firebaseAnalyticsLabel,
    targetPlayers: campaignFormInfo.value.audience.targetPlayers,
    targetCondition: campaignFormInfo.value.audience.targetCondition
  }
  // Debug options.
  if (campaignFormInfo.value.debugFakeNotificationMode) {
    payload.debugFakeNotificationMode = true
    payload.debugEntityIdValueUpperBound = campaignFormInfo.value.debugEntityIdValueUpperBound
  }
  // If editing an update to an existing campaign...
  if (props.update) {
    // ...update.
    payload.id = props.oldNotification?.id
    if (!payload.id) throw new Error('Cannot update campaign without ID.')
    await gameServerApi.put(`/notifications/${payload.id}`, payload)
    const message = 'Campaign updated.'
    showSuccessToast(message)
  } else {
    // ...or else create a new one.
    await gameServerApi.post('/notifications', payload)
    const message = 'New push notification campaign created.'
    showSuccessToast(message)
  }
  // Instruct parent object to refresh.
  emits('refresh')
}

/**
 * Reset the state of the modal.
 */
function resetNewCampaign () {
  // Initialize with default values if an existing notification was given.
  if (props.oldNotification) {
    campaignFormInfo.value.name = props.update ? props.oldNotification.name : props.oldNotification.name + ' (copy)'
    campaignFormInfo.value.targetTime = props.update ? DateTime.fromISO(props.oldNotification.targetTime) : DateTime.now()
    campaignFormInfo.value.content = props.oldNotification.content
    campaignFormInfo.value.debugFakeNotificationMode = props.oldNotification.debugFakeNotificationMode ?? false
    campaignFormInfo.value.debugEntityIdValueUpperBound = props.oldNotification.debugEntityIdValueUpperBound ?? 100
    campaignFormInfo.value.firebaseAnalyticsLabel = props.oldNotification.firebaseAnalyticsLabel
    campaignFormInfo.value.audience.targetPlayers = props.oldNotification.targetPlayers
    campaignFormInfo.value.audience.targetCondition = props.oldNotification.targetCondition
    isContentValid.value = true
  } else {
    // Initialize an empty form.
    campaignFormInfo.value = getNewCampaignFormInfo()
  }
}

</script>
