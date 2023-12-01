import MViewContainer, { type MViewContainerAlert } from './MViewContainer.vue'
import MCard from '../../primitives/MCard.vue'
import MErrorCallout from '../../primitives/MErrorCallout.vue'
import MSingleColumnLayout from '../../layouts/MSingleColumnLayout.vue'
import MTwoColumnLayout from '../../layouts/MTwoColumnLayout.vue'
import type { Meta, StoryObj } from '@storybook/vue3'
import { usePermissions } from '../../utils/permissions'

import { DisplayError } from '../../utils/DisplayErrorHandler'

const meta: Meta<typeof MViewContainer> = {
  component: MViewContainer,
  tags: ['autodocs'],
  parameters: {
    layout: 'fullscreen',
  }
}

export default meta
type Story = StoryObj<typeof MViewContainer>

export const Default: Story = {
  render: (args) => ({
    components: {
      MViewContainer,
      MCard,
      MTwoColumnLayout,
    },
    setup: () => ({ args }),
    template: `
      <MViewContainer v-bind="args">
        <template #overview>
          <MCard title="Overview card placeholder" class="tw-h-52 tw-w-96">
            <p>Lorem ipsum dolor sit amet.</p>
          </MCard>
        </template>

        <MTwoColumnLayout>
          <MCard title="Example content 1">
            <p>Lorem ipsum dolor sit amet.</p>
          </MCard>
          <MCard title="Example content 2">
            <p>Lorem ipsum dolor sit amet.</p>
          </MCard>
          <MCard title="Example content 3">
            <p>Lorem ipsum dolor sit amet.</p>
          </MCard>
        </MTwoColumnLayout>
      </MViewContainer>
    `,
  }),
  args: {},
}

export const CenterColumn: Story = {
  render: (args) => ({
    components: {
      MViewContainer,
      MCard,
      MSingleColumnLayout,
    },
    setup: () => ({ args }),
    template: `
      <MViewContainer v-bind="args">
        <template #overview>
          <MCard title="Overview card placeholder" class="tw-h-52 tw-w-96">
            <p>Lorem ipsum dolor sit amet.</p>
          </MCard>
        </template>

        <MSingleColumnLayout>
          <MCard title="Example content">
            <p>Lorem ipsum dolor sit amet.</p>
          </MCard>
        </MSingleColumnLayout>
      </MViewContainer>
    `,
  }),
  args: {},
}

export const Loading: Story = {
  render: (args) => ({
    components: {
      MViewContainer,
      MCard,
    },
    setup: () => ({ args }),
    template: `
      <MViewContainer v-bind="args">
        <MCard title="Example content">
          <p>Lorem ipsum dolor sit amet.</p>
        </MCard>
      </MViewContainer>
    `,
  }),
  args: {
    isLoading: true,
  },
}

export const LoadingError: Story = {
  render: (args) => ({
    components: {
      MViewContainer,
      MErrorCallout,
    },
    setup: () => ({ args }),
    template: `
      <MViewContainer v-bind="args">
        <MErrorCallout>
        </MErrorCallout>
      </MViewContainer>
    `,
  }),
  args: {
    isLoading: true,
    error: new DisplayError(
      'Loading error',
      'This is an example loading error message.',
    ),
  },
}

export const NoPermission: Story = {
  render: (args) => ({
    components: {
      MViewContainer,
      MCard,
    },
    setup: () => {
      usePermissions().setPermissions(['example-permission'])

      return {
        args
      }
    },
    template: `
      <MViewContainer v-bind="args">
        <MCard title="Example content">
          <p>Lorem ipsum dolor sit amet.</p>
        </MCard>
      </MViewContainer>
    `,
  }),
  args: {
    permission: 'example-permission2',
  },
}

export const Alerts: Story = {
  render: (args) => ({
    components: {
      MViewContainer,
      MCard,
    },
    setup: () => ({ args }),
    template: `
      <MViewContainer v-bind="args">
        <MCard title="Example content">
          <p>Lorem ipsum dolor sit amet.</p>
        </MCard>
      </MViewContainer>
    `,
  }),
  args: {
    // @ts-expect-error Some typings issue with Storybook?
    alerts: [
      {
        title: 'Example Warning',
        message: 'This is an example warning message.',
        variant: 'warning',
      },
      {
        title: 'Example Warning (danger)',
        message: 'This is another example warning message.',
        variant: 'danger',
      }
    ] satisfies MViewContainerAlert[],
  },
}
