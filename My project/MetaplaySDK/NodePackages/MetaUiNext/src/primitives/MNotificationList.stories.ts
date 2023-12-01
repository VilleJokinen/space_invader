import MNotificationList from './MNotificationList.vue'
import MButton from './MButton.vue'

import type { Meta, StoryObj } from '@storybook/vue3'
import { useNotifications } from './useNotifications'
import { onMounted, onUnmounted, ref } from 'vue'

const meta: Meta<typeof MNotificationList> = {
  component: MNotificationList,
  argTypes: {}
}

export default meta
type Story = StoryObj<typeof MNotificationList>

export const Default: Story = {
  render: (args) => ({
    components: {
      MNotificationList,
      MButton,
    },
    setup: () => {
      const { showErrorNotification, showSuccessNotification, showWarningNotification, notificationsToShow, updateNotification } = useNotifications()

      let timer: ReturnType<typeof setTimeout> | undefined

      function showNotification () {
        showSuccessNotification('Timers work!', 'Success')
        timer = setTimeout(() => {
          showNotification()
        }, 4000)
      }

      const lastNotificationId = ref<number>()

      onMounted(() => showNotification())
      onUnmounted(() => timer && clearInterval(timer))

      return {
        args,
        showErrorNotification,
        showSuccessNotification,
        showWarningNotification,
        notificationsToShow,
        updateNotification,
      }
    },
    template: `
    <MButton @click="lastNotificationId = showSuccessNotification('Lorem ipsum dolor sit amet.')" variant="success">Success</MButton>
    <MButton @click="updateNotification(lastNotificationId, {title: 'Updated', message: 'Yeah ok I was updated. Better now?'})" variant="success">Update Success</MButton>
    <MButton @click="showWarningNotification('Lorem ipsum dolor sit amet.')" variant="warning">Warning</MButton>
    <MButton @click="showErrorNotification('Lorem ipsum dolor sit amet.')" variant="danger">Error</MButton>
    <MNotificationList v-bind="args">
    </MNotificationList>
    <pre class="tw-text-xs">{{ notificationsToShow }}</pre>
    `,
  }),
}

export const TooLong1: Story = {
  render: (args) => ({
    components: {
      MNotificationList,
      MButton,
    },
    setup: () => {
      const { showErrorNotification, showSuccessNotification, showWarningNotification, notificationsToShow } = useNotifications()

      let timer: ReturnType<typeof setTimeout> | undefined

      function showNotification () {
        showSuccessNotification(
          'Excepturi vitae eaque aut. Corporis iusto corrupti enim possimus quod. Voluptatem excepturi eos aut tenetur vel aspernatur modi sit. Suscipit odio voluptates neque adipisci similique deserunt. Praesentium nemo voluptate labore. Accusamus accusamus saepe laboriosam aut. Odio similique aut voluptatum ea similique non non voluptatem. Nulla inventore eveniet dolor consequatur sed sed. Ea ad et molestiae qui a dicta. Eum quasi illo aliquid sit. Hic explicabo soluta hic eos repellendus aut libero asperiores.',
          'A very long title that should wrap on to multiple lines and probably get truncated at some point because it is too long.'
        )
        timer = setTimeout(() => {
          showNotification()
        }, 4000)
      }

      onMounted(() => showNotification())
      onUnmounted(() => timer && clearInterval(timer))

      return {
        args,
        showErrorNotification,
        showSuccessNotification,
        showWarningNotification,
        notificationsToShow,
      }
    },
    template: `
    <MNotificationList v-bind="args">
    </MNotificationList>
    <pre class="tw-text-xs">{{ notificationsToShow }}</pre>
    `,
  }),
}

export const TooLong2: Story = {
  render: (args) => ({
    components: {
      MNotificationList,
      MButton,
    },
    setup: () => {
      const { showErrorNotification, showSuccessNotification, showWarningNotification, notificationsToShow } = useNotifications()

      let timer: ReturnType<typeof setTimeout> | undefined

      function showNotification () {
        showSuccessNotification(
          'Excepturivitaeeaqueaut.Corporisiustocorruptienimpossimusquod.Voluptatemexcepturieosautteneturvelaspernaturmodisit.Suscipitodiovoluptatesnequeadipiscisimiliquedeserunt.Praesentiumnemovoluptatelabore.Accusamusaccusamussaepelaboriosamaut.Odiosimiliqueautvoluptatumeasimiliquenonnonvoluptatem.Nullainventoreevenietdolorconsequatursedsed.Eaadetmolestiaequiadicta.Eumquasiilloaliquidsit.Hicexplicabosolutahiceosrepellendusautliberoasperiores.',
          'Averylongtitlethatcannotwrapontomultiplelinesandprobablyshouldgettruncatedatsomepointbecauseitistoolong.'
        )
        timer = setTimeout(() => {
          showNotification()
        }, 4000)
      }

      onMounted(() => showNotification())
      onUnmounted(() => timer && clearInterval(timer))

      return {
        args,
        showErrorNotification,
        showSuccessNotification,
        showWarningNotification,
        notificationsToShow,
      }
    },
    template: `
    <MNotificationList v-bind="args">
    </MNotificationList>
    <pre class="tw-text-xs">{{ notificationsToShow }}</pre>
    `,
  }),
}
