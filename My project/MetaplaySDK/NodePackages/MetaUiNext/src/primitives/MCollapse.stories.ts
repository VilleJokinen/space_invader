import MCollapse from './MCollapse.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MCollapse> = {
  component: MCollapse,
  tags: ['autodocs'],
  argTypes: {
  }
}

export default meta
type Story = StoryObj<typeof MCollapse>

export const Default: Story = {
  render: (args) => ({
    components: { MCollapse },
    setup: () => ({ args }),
    template: `
    <MCollapse v-bind="args">
      <template #header>
        <span>Header</span>
      </template>
      <p>This content goes into the body.</p>
    </MCollapse>
    `,
  }),
  args: {
  },
}

export const Primary: Story = {
  args: {
    variant: 'primary',
  },
}

export const Warning: Story = {
  args: {
    variant: 'warning',
  },
}

export const Danger: Story = {
  args: {
    variant: 'danger',
  },
}

export const Success: Story = {
  args: {
    variant: 'success',
  },
}
