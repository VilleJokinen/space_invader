<template lang="pug">
div
  meta-list-card(
    title="All Participants"
    icon="users"
    :itemList="participants"
    :searchFields="['playerAvatar.displayName']"
    :sortOptions="sortOptions"
    :filterSets="filterSets"
    )
    template(#item-card="{ item: participant }")
      MCollapse(extraMListItemMargin)
        //- Header
        template(#header)
          MListItem
            span(class="tw-mr-1") {{ participant.playerAvatar.displayName }}
            MTooltip(v-if="!isRealParticipant(participant.participantId)" content="Placeholder player that will get automatically deleted once a real player joins." noUnderline) #[fa-icon(icon="robot")]
            template(#top-right)
              span.mr-1 Position: #[meta-ordinal-number(:number="participant.sortOrderIndex + 1")]
              MTooltip(:content="deleteButtonTooltipContent(participant.participantId)" noUnderline)
                meta-button(
                    modal="confirm-participant-remove"
                    variant="outline-danger"
                    size="sm"
                    permission="api.leagues.participant_edit"
                    @click="() => participantToRemove = participant"
                    :disabled="!isCurrentSeason || !isRealParticipant(participant.participantId)"
                    subtle
                    class="tw-relative tw-bottom-0.5"
                    )
                    fa-icon(icon="trash-alt")
            template(#bottom-left)
              div {{ participant.participantInfo }}
              div Last action:
                meta-time(v-if="!isEpochTime(participant.divisionScore.lastActionAt)" :date="participant.divisionScore.lastActionAt").ml-1
                span(v-else).ml-1 No actions
              div(v-if="!isCurrentSeason")
                div(v-if="!participant.resolvedDivisionRewards")
                  span No reward received.
                div(v-else-if="participant.resolvedDivisionRewards")
                  div(v-for="reward in participant.resolvedDivisionRewards.rewards")
                    meta-reward-badge(:reward="reward")
                  span(v-if="participant.resolvedDivisionRewards.isClaimed") Reward claimed
                  span(v-else) Reward unclaimed
            template(#bottom-right)
              meta-button(
                v-if="isRealParticipant(participant.participantId)"
                link
                :to="`/players/${participant.participantId}`"
                permission="api.players.view"
                data-cy="view-participant"
                ) View participant
        //- Content
        pre.code-box.border.rounded.mt-2.bg-light {{ participant }}
  //- Modal
  b-modal#confirm-participant-remove(title="Remove Participant" size="md" @hidden="() => participantToRemove = undefined" @ok="removeParticipant" centered no-close-on-backdrop)
    p You are about to remove #[MBadge {{ participantToRemove.playerAvatar.displayName }}] from the division.
    meta-no-seatbelts(
      message=" The participant will lose all their progress in the league, and will have to start again from scratch. This action can't be undone."
      :name="participantToRemove.playerAvatar.displayName"
      )

    template(#modal-footer="{ ok, cancel }")
      meta-button(variant="secondary" @click="cancel") Cancel
      meta-button(variant="danger" @click="ok" safety-lock).text-white Remove Participant
</template>

<script lang="ts" setup>
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'

import { useGameServerApi } from '@metaplay/game-server-api'

import { MetaListFilterOption, MetaListFilterSet, MetaListSortDirection, MetaListSortOption } from '@metaplay/meta-ui'
import { showErrorToast, showSuccessToast } from '@metaplay/meta-ui/src/toasts'
import { MBadge, MCollapse, MTooltip, MListItem } from '@metaplay/meta-ui-next'
import { useSubscription } from '@metaplay/subscriptions'
import { getSingleDivisionSubscriptionOptions } from '../../subscription_options/leagues'

import { isEpochTime, routeParamToSingleValue } from '../../coreUtils'

const route = useRoute()
const gameServerApi = useGameServerApi()

const props = defineProps<{
  divisionId: string
}>()

const {
  data: singleDivisionData,
  refresh: singleDivisionRefresh
} = useSubscription(getSingleDivisionSubscriptionOptions(props.divisionId))

const leagueId = routeParamToSingleValue(route.params.leagueId)
const participantToRemove = ref()

const participants = computed(() => {
  if (!singleDivisionData.value) {
    return []
  }
  const participants = Object.values(singleDivisionData.value.model.participants)
  return participants.sort((a: any, b: any) => a.sortOrderIndex - b.sortOrderIndex)
})

const isCurrentSeason = computed(() => {
  if (!singleDivisionData.value) {
    return false
  }
  return !singleDivisionData.value.model.isConcluded
})

async function removeParticipant () {
  const response = await gameServerApi.post(`/leagues/${leagueId}/participant/${participantToRemove.value.participantId}/remove`)
  if (response.data.success) {
    showSuccessToast('Participant successfully removed from division.')
  } else {
    showErrorToast(response.data.errorMessage)
  }
  singleDivisionRefresh()
}

/**
 * Checks if the participant is a real player or a placeholder.
 */
function isRealParticipant (participantId: string): boolean {
  if (participantId === 'None') {
    return false
  } else {
    return true
  }
}

/**
 * Determines the disabled delete button content depending on past season or placeholder player.
 */
function deleteButtonTooltipContent (participantId: string) {
  if (!isCurrentSeason.value) {
    return 'Cannot remove participant since season has concluded.'
  } else if (!isRealParticipant(participantId)) {
    return 'This is a placeholder player that cannot be removed.'
  } else {
    return undefined
  }
}

/**
 * Sorting for the MetaListCard.
 */
const sortOptions = [
  new MetaListSortOption('Position', 'sortOrderIndex', MetaListSortDirection.Ascending),
  new MetaListSortOption('Position', 'sortOrderIndex', MetaListSortDirection.Descending),
  new MetaListSortOption('Name', 'playerAvatar.displayName', MetaListSortDirection.Ascending),
  new MetaListSortOption('Name', 'playerAvatar.displayName', MetaListSortDirection.Descending),
]

const filterSets = [
  new MetaListFilterSet('resolvedDivisionRewards',
    [
      new MetaListFilterOption('Reward claimed', (participant: any) => participant.resolvedDivisionRewards?.isClaimed === true, false),
      new MetaListFilterOption('Reward unclaimed', (participant: any) => participant.resolvedDivisionRewards?.isClaimed === false, false),
      new MetaListFilterOption('Reward not received', (participant: any) => participant.resolvedDivisionRewards === null, false),
    ]
  ),
]
</script>
