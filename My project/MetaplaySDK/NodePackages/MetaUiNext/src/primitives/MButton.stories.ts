import MButton from '../primitives/MButton.vue'
import type { Meta, StoryObj } from '@storybook/vue3'
import { usePermissions } from '../utils/permissions'

const meta = {
  component: MButton,
  tags: ['autodocs'],
  argTypes: {
    variant: {
      control: {
        type: 'select',
      },
      options: ['default', 'neutral', 'success', 'danger', 'warning'],
    },
    size: {
      control: {
        type: 'radio',
      },
      options: ['text', 'small', 'default']
    },
    disabled: {
      control: {
        type: 'radio'
      },
      options: [true, false]
    }
  }
} satisfies Meta<typeof MButton>

export default meta
type Story = StoryObj<typeof MButton>

export const Default: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Button Content TBD
    </MButton>
    `,
  }),
  args: {},
}

export const Small: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    size: 'small',
  },
}

export const InlineSmall: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    Small buttons need to work next to text: <MButton v-bind="args">
      <template #icon>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-4 tw-h-4">
          <path d="M10 2a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 2zM10 15a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 15zM10 7a3 3 0 100 6 3 3 0 000-6zM15.657 5.404a.75.75 0 10-1.06-1.06l-1.061 1.06a.75.75 0 001.06 1.06l1.06-1.06zM6.464 14.596a.75.75 0 10-1.06-1.06l-1.06 1.06a.75.75 0 001.06 1.06l1.06-1.06zM18 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 0118 10zM5 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 015 10zM14.596 15.657a.75.75 0 001.06-1.06l-1.06-1.061a.75.75 0 10-1.06 1.06l1.06 1.06zM5.404 6.464a.75.75 0 001.06-1.06l-1.06-1.06a.75.75 0 10-1.061 1.06l1.06 1.06z" />
        </svg>
      </template>
    </MButton>
    `,
  }),
  args: {
    size: 'small',
  },
}

export const Text: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    Smallest button is just text: <MButton v-bind="args"></MButton>
    `,
  }),
  args: {
    size: 'text',
  },
}

export const Icon: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      <template #icon>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-5 tw-h-5">
          <path d="M10 2a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 2zM10 15a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 15zM10 7a3 3 0 100 6 3 3 0 000-6zM15.657 5.404a.75.75 0 10-1.06-1.06l-1.061 1.06a.75.75 0 001.06 1.06l1.06-1.06zM6.464 14.596a.75.75 0 10-1.06-1.06l-1.06 1.06a.75.75 0 001.06 1.06l1.06-1.06zM18 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 0118 10zM5 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 015 10zM14.596 15.657a.75.75 0 001.06-1.06l-1.06-1.061a.75.75 0 10-1.06 1.06l1.06 1.06zM5.404 6.464a.75.75 0 001.06-1.06l-1.06-1.06a.75.75 0 10-1.061 1.06l1.06 1.06z" />
        </svg>
      </template>

      Button Content TBD
    </MButton>
    `,
  }),
  args: {
  },
}

export const SmallWithIcon: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      <template #icon>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-4 tw-h-4">
          <path d="M10 2a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 2zM10 15a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 15zM10 7a3 3 0 100 6 3 3 0 000-6zM15.657 5.404a.75.75 0 10-1.06-1.06l-1.061 1.06a.75.75 0 001.06 1.06l1.06-1.06zM6.464 14.596a.75.75 0 10-1.06-1.06l-1.06 1.06a.75.75 0 001.06 1.06l1.06-1.06zM18 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 0118 10zM5 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 015 10zM14.596 15.657a.75.75 0 001.06-1.06l-1.06-1.061a.75.75 0 10-1.06 1.06l1.06 1.06zM5.404 6.464a.75.75 0 001.06-1.06l-1.06-1.06a.75.75 0 10-1.061 1.06l1.06 1.06z" />
        </svg>
      </template>

      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    size: 'small',
  },
}

export const Disabled: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Just disabled
    </MButton>
    `,
  }),
  args: {
    disabled: true,
  },
}

export const DisabledWithATooltip: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Disabled for a reason
    </MButton>
    `,
  }),
  args: {
    disabledTooltip: 'This is why it is disabled.',
  },
}

export const HasPermission: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => {
      usePermissions().setPermissions(['example-permission'])

      return {
        args
      }
    },
    template: `
    <MButton v-bind="args">
      This Should Work
    </MButton>
    `,
  }),
  args: {
    permission: 'example-permission',
  },
}

export const NoPermission: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => {
      usePermissions().setPermissions(['example-permission'])

      return {
        args
      }
    },
    template: `
    <MButton v-bind="args">
      This Should Not Work
    </MButton>
    `,
  }),
  args: {
    permission: 'example-permission2',
  },
}

export const Neutral: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'neutral',
  },
}

export const DisabledNeutral: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'neutral',
    disabled: true,
  },
}

export const SmallNeutral: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      <template #icon>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-4 tw-h-4">
          <path d="M10 2a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 2zM10 15a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 15zM10 7a3 3 0 100 6 3 3 0 000-6zM15.657 5.404a.75.75 0 10-1.06-1.06l-1.061 1.06a.75.75 0 001.06 1.06l1.06-1.06zM6.464 14.596a.75.75 0 10-1.06-1.06l-1.06 1.06a.75.75 0 001.06 1.06l1.06-1.06zM18 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 0118 10zM5 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 015 10zM14.596 15.657a.75.75 0 001.06-1.06l-1.06-1.061a.75.75 0 10-1.06 1.06l1.06 1.06zM5.404 6.464a.75.75 0 001.06-1.06l-1.06-1.06a.75.75 0 10-1.061 1.06l1.06 1.06z" />
        </svg>
      </template>

      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'neutral',
    size: 'small',
  },
}

export const Success: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'success',
  },
}

export const DisabledSuccess: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'success',
    disabled: true,
  },
}

export const SmallSuccess: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      <template #icon>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-4 tw-h-4">
          <path d="M10 2a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 2zM10 15a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 15zM10 7a3 3 0 100 6 3 3 0 000-6zM15.657 5.404a.75.75 0 10-1.06-1.06l-1.061 1.06a.75.75 0 001.06 1.06l1.06-1.06zM6.464 14.596a.75.75 0 10-1.06-1.06l-1.06 1.06a.75.75 0 001.06 1.06l1.06-1.06zM18 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 0118 10zM5 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 015 10zM14.596 15.657a.75.75 0 001.06-1.06l-1.06-1.061a.75.75 0 10-1.06 1.06l1.06 1.06zM5.404 6.464a.75.75 0 001.06-1.06l-1.06-1.06a.75.75 0 10-1.061 1.06l1.06 1.06z" />
        </svg>
      </template>

      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'success',
    size: 'small',
  },
}

export const Danger: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'danger',
  },
}

export const DisabledDanger: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'danger',
    disabled: true,
  },
}

export const SmallDanger: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      <template #icon>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-4 tw-h-4">
          <path d="M10 2a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 2zM10 15a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 15zM10 7a3 3 0 100 6 3 3 0 000-6zM15.657 5.404a.75.75 0 10-1.06-1.06l-1.061 1.06a.75.75 0 001.06 1.06l1.06-1.06zM6.464 14.596a.75.75 0 10-1.06-1.06l-1.06 1.06a.75.75 0 001.06 1.06l1.06-1.06zM18 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 0118 10zM5 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 015 10zM14.596 15.657a.75.75 0 001.06-1.06l-1.06-1.061a.75.75 0 10-1.06 1.06l1.06 1.06zM5.404 6.464a.75.75 0 001.06-1.06l-1.06-1.06a.75.75 0 10-1.061 1.06l1.06 1.06z" />
        </svg>
      </template>

      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'danger',
    size: 'small',
  },
}

export const Warning: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'warning',
  },
}

export const DisabledWarning: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'warning',
    disabled: true,
  },
}

export const SmallWarning: Story = {
  render: (args) => ({
    components: { MButton },
    setup: () => ({ args }),
    template: `
    <MButton v-bind="args">
      <template #icon>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="tw-w-4 tw-h-4">
          <path d="M10 2a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 2zM10 15a.75.75 0 01.75.75v1.5a.75.75 0 01-1.5 0v-1.5A.75.75 0 0110 15zM10 7a3 3 0 100 6 3 3 0 000-6zM15.657 5.404a.75.75 0 10-1.06-1.06l-1.061 1.06a.75.75 0 001.06 1.06l1.06-1.06zM6.464 14.596a.75.75 0 10-1.06-1.06l-1.06 1.06a.75.75 0 001.06 1.06l1.06-1.06zM18 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 0118 10zM5 10a.75.75 0 01-.75.75h-1.5a.75.75 0 010-1.5h1.5A.75.75 0 015 10zM14.596 15.657a.75.75 0 001.06-1.06l-1.06-1.061a.75.75 0 10-1.06 1.06l1.06 1.06zM5.404 6.464a.75.75 0 001.06-1.06l-1.06-1.06a.75.75 0 10-1.061 1.06l1.06 1.06z" />
        </svg>
      </template>

      Button Content TBD
    </MButton>
    `,
  }),
  args: {
    variant: 'warning',
    size: 'small',
  },
}
