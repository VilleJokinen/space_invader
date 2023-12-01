import MInputDateTime from './MInputDateTime.vue'
import type { Meta, StoryObj } from '@storybook/vue3'
import { DateTime, Duration } from 'luxon'

const meta: Meta<typeof MInputDateTime> = {
  component: MInputDateTime,
  tags: ['autodocs'],
}

export default meta
type Story = StoryObj<typeof MInputDateTime>

export const Default: Story = {
  render: (args) => ({
    components: { MInputDateTime },
    setup: () => ({ args }),
    data: () => ({ datetime: DateTime.now() }),
    template: `<div>
      <MInputDateTime v-bind="args" v-model="datetime"/>
      <pre class="tw-mt-2">Output: {{ datetime }}</pre>
    </div>`,
  }),
  args: {
    label: 'Date',
    hintMessage: 'Initial value must always be set.',
  },
}

export const Disabled: Story = {
  args: {
    label: 'Date (disabled)',
    modelValue: DateTime.now(),
    disabled: true,
  },
}

export const NoLabel: Story = {
  args: {
    modelValue: DateTime.now(),
  },
}
