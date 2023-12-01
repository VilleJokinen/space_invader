import MInputDuration from './MInputDuration.vue'
import type { Meta, StoryObj } from '@storybook/vue3'
import { DateTime, Duration } from 'luxon'

const meta: Meta<typeof MInputDuration> = {
  component: MInputDuration,
  tags: ['autodocs'],
}

export default meta
type Story = StoryObj<typeof MInputDuration>

export const Default: Story = {
  render: (args) => ({
    components: { MInputDuration },
    setup: () => ({ args }),
    data: () => ({ duration: Duration.fromObject({ hours: 1, minutes: 30 }) }),
    template: `<div>
      <MInputDuration v-bind="args" v-model="duration"/>
      <pre class="tw-mt-2">Output: {{ duration }}</pre>
    </div>`,
  }),
  args: {
    label: 'Duration',
    hintMessage: 'Negative values not allowed. Fractions are.',
  },
}

export const StartTime: Story = {
  args: {
    label: 'Duration from a start time (2021-1-1 12:00 UTC)',
    modelValue: Duration.fromObject({ days: 1, hours: 2, minutes: 30 }),
    referenceDateTime: DateTime.fromISO('2021-01-01T12:00:00.000Z'),
  },
}

export const Disabled: Story = {
  args: {
    label: 'Duration (disabled)',
    modelValue: Duration.fromObject({ hours: 12 }),
    disabled: true,
  },
}

export const Invalid: Story = {
  args: {
    label: 'Duration (undefined)',
    modelValue: Duration.fromObject({ hours: 0 }),
  },
}

export const NoLabel: Story = {
  args: {
    modelValue: Duration.fromObject({ days: 1, hours: 20, minutes: 9 }),
  },
}
