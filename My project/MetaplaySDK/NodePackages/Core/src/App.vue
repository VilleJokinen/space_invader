<!-- This file is part of Metaplay SDK which is released under the Metaplay SDK License. -->

<template lang="pug">
//- Handle initialization and error states.
//- Two nested containers to center the content.
div(
  v-if="viewState !== 'loaded' && viewState !== 'token-expired'"
  class="tw-container tw-mx-auto"
  )
  div(
    class="tw-flex tw-flex-col tw-justify-center tw-h-screen tw-py-10"
    )
    //- Not logged in.
    login-card(
      v-if="viewState === 'login'"
      class="tw-max-w-md tw-mx-auto"
      )

    //- Initialization.
    div(v-else-if="viewState === 'loading'")
      //- Monogram and loading state.
      MetaplayMonogram(
        :class="['tw-w-24 tw-fill-green-500 tw-mx-auto', { bounce: coreStore.backendConnectionStatus.status !== 'error' }]"
        )
      div(class="tw-text-center tw-mt-8 tw-text-neutral-500 tw-font-bold") {{ coreStore.backendConnectionStatus.displayName }}...

      //- Show initialization errors.
      div(
        v-if="coreStore.backendConnectionStatus.status === 'error'"
        class="tw-mt-8 tw-max-w-3xl tw-mx-auto"
        )
          MCard(
            variant="danger"
            title="Oh no, a connection error!"
            )
              div {{ coreStore.backendConnectionStatus.error?.errorMessage }}
              div {{ coreStore.backendConnectionStatus.error?.errorResolution }}
              div(class="tw-bg-neutral-100 tw-border tw-border-neutral-300 tw-p-2 tw-rounded tw-mt-4 tw-text-neutral-700")
                div(v-for="iterationAlert in initializationAlerts")
                  span(class="tw-font-semibold") {{ iterationAlert.title }}
                  pre(v-if="iterationAlert.message") {{ iterationAlert.message }}
                  pre(v-else) Not available

              template(#buttons)
                meta-clipboard-copy(:contents="JSON.stringify(initializationAlerts)" :subtle="false").float-right Copy to Clipboard

    //- User without any permissions.
    new-user-card(
      v-else-if="viewState === 'new-user'"
      class="tw-max-w-lg tw-mx-auto"
      )

//- Auth token expired. Can happen after initialization passes.
MCard(
  v-else-if="viewState === 'token-expired'"
  title="Session Expired"
  class="tw-max-w-md tw-mx-auto"
  )
    span You have been away for awhile and your session has expired. Please refresh the page to log in again.

    template(#icon)
      fa-icon(icon="shield-halved")

    template(#buttons)
      meta-button(@click="refreshPage" variant="primary") Refresh

//- The main app view.
CoreRootLayout(v-else-if="viewState === 'loaded'")
</template>

<script lang="ts" setup>
import CoreRootLayout from './components/CoreRootLayout.vue'
import NewUserCard from './components/system/NewUserCard.vue'
import LoginCard from './components/system/LoginCard.vue'

import { useCoreStore } from './coreStore'
import { MetaplayMonogram, MCard } from '@metaplay/meta-ui-next'
import { useGameServerApiStore } from '@metaplay/game-server-api'
import { initializationStepsRunner } from './initialization'
import { computed, onMounted } from 'vue'

const coreStore = useCoreStore()
const gameServerApiStore = useGameServerApiStore()

// Init can now happen as the Vue instance is created and global state is safe to mutate
// Note: not awaiting this, as it will take a long time and we want the rendering active meanwhile
onMounted(initializationStepsRunner)

const viewState = computed(() => {
  if (gameServerApiStore.auth.requiresLogin) return 'login'
  if (gameServerApiStore.auth.requiresBasicPermissions) return 'new-user'
  if (coreStore.backendConnectionStatus.status !== 'completed') return 'loading'
  if (gameServerApiStore.auth.hasTokenExpired) return 'token-expired'
  return 'loaded'
})
function refreshPage () {
  window.location.reload()
}

interface AlertProperty {
  title: string
  message: string
}

/**
 * Information when the step initialization fails.
 */
const initializationAlerts = computed((): AlertProperty[] => {
  return [
    {
      title: 'Connection Step',
      message: coreStore.backendConnectionStatus.error?.stepName
    },
    {
      title: 'Build Number',
      message: coreStore.hello?.buildNumber
    },
    {
      title: 'Commit ID',
      message: coreStore.hello?.commitId
    },
    {
      title: 'Error Message',
      message: coreStore.backendConnectionStatus.error?.errorMessage
    },
    {
      title: 'Error Resolution',
      message: coreStore.backendConnectionStatus.error?.errorResolution
    },
    {
      title: 'Error Object',
      message: `${coreStore.backendConnectionStatus.error?.errorObject.name}: ${coreStore.backendConnectionStatus.error?.errorObject.message}`
    },
    {
      title: 'Stack Trace',
      message: coreStore.backendConnectionStatus.error?.errorObject.stack
    },
  ]
})
</script>

<style>
:root {
  /* --sidebar-width: 14rem;
  --header-height: 3.5rem; */

  --metaplay-green: #86c733;
  --metaplay-green-dark: #72bf33;
  --metaplay-green-darker: #71c313;
  --metaplay-red: #fa603f;
  --metaplay-yellow: #e5aa00;
  --metaplay-blue: #2d90dc;
  --metaplay-purple: #7750ab;

  --metaplay-dark: #3b3b3b;
  --metaplay-grey: #d8d8d8;
  --metaplay-grey-dark: #686868;
  --metaplay-grey-light: #e6e6e6;
  --metaplay-grey-lightest: #fafafa;

  --filter-metaplay-green: invert(67%) sepia(32%) saturate(1068%) hue-rotate(42deg) brightness(101%) contrast(84%);
  --filter-metaplay-grey: invert(43%) sepia(13%) saturate(3%) hue-rotate(322deg) brightness(90%) contrast(85%);
  --filter-metaplay-grey-dark: invert(44%) sepia(0%) saturate(37%) hue-rotate(159deg) brightness(92%) contrast(94%);
  --filter-metaplay-grey-light: invert(97%) sepia(6%) saturate(244%) hue-rotate(67deg) brightness(110%) contrast(80%);
  --filter-metaplay-grey-lightest: invert(100%) sepia(55%) saturate(0%) hue-rotate(21deg) brightness(107%) contrast(95%);
}

.bounce {
  animation: bounce 0.5s;
  animation-direction: alternate;
  animation-timing-function: cubic-bezier(0.5, 0.05, 1, 0.5);
  animation-iteration-count: infinite;
}

@keyframes bounce {
  from {
    transform: translateY(0);
  }

  to {
    transform: translateY(10px);
  }
}

.fade-enter-from {
  opacity: 0;
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity .5s;
}

.fade-leave-to {
  opacity: 0;
}

.header-notification {
  border: 1px solid transparent;
  border-top: none;
}
</style>
