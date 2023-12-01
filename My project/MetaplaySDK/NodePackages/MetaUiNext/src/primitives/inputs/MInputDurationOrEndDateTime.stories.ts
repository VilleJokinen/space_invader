import MInputDurationOrEndDateTime from './MInputDurationOrEndDateTime.vue'
import type { Meta, StoryObj } from '@storybook/vue3'
import { DateTime, Duration } from 'luxon'

const meta: Meta<typeof MInputDurationOrEndDateTime> = {
  component: MInputDurationOrEndDateTime,
  tags: ['autodocs'],
}

export default meta
type Story = StoryObj<typeof MInputDurationOrEndDateTime>

export const Default: Story = {
  render: (args) => ({
    components: { MInputDurationOrEndDateTime },
    setup: () => ({ args }),
    data: () => ({ selectedDuration: Duration.fromObject({ hours: 1, minutes: 30 }) }),
    template: `<div>
      <MInputDurationOrEndDateTime v-bind="args" v-model="selectedDuration"/>
      <pre class="tw-mt-2">Output: {{ selectedDuration }}</pre>
    </div>`,
  }),
  args: {
    label: 'Duration',
    referenceDateTime: DateTime.now(),
    hintMessage: 'Initial value and a reference date time must always be set.',
  },
}

export const EndDateTime: Story = {
  args: {
    label: 'End date time',
    inputMode: 'endDateTime',
    modelValue: Duration.fromObject({ hours: 1, minutes: 30 }),
    referenceDateTime: DateTime.now(),
  },
}

export const Disabled: Story = {
  args: {
    label: 'Duration (disabled)',
    inputMode: 'duration',
    modelValue: Duration.fromObject({ hours: 1, minutes: 30 }),
    referenceDateTime: DateTime.now(),
    disabled: true,
  },
}

export const NoLabel: Story = {
  args: {
    inputMode: 'duration',
    modelValue: Duration.fromObject({ hours: 1, minutes: 30 }),
    referenceDateTime: DateTime.now(),
  },
}
