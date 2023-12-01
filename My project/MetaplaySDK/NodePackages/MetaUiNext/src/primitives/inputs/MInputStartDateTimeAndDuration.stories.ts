import MInputStartDateTimeAndDuration from './MInputStartDateTimeAndDuration.vue'
import type { Meta, StoryObj } from '@storybook/vue3'
import { DateTime, Duration } from 'luxon'

const meta: Meta<typeof MInputStartDateTimeAndDuration> = {
  component: MInputStartDateTimeAndDuration,
  tags: ['autodocs'],
}

export default meta
type Story = StoryObj<typeof MInputStartDateTimeAndDuration>

export const Default: Story = {
  render: (args) => ({
    components: { MInputStartDateTimeAndDuration },
    setup: () => ({ args }),
    data: () => ({
      startDateTime: DateTime.now(),
      duration: Duration.fromObject({ hours: 1 })
    }),
    template: `<div>
      <MInputStartDateTimeAndDuration :startDateTime="startDateTime" @update:startDateTime="startDateTime = $event" :duration="duration" @update:duration="duration = $event"/>
      <pre class="tw-mt-2">Output startDateTime: {{ startDateTime }}</pre>
      <pre class="tw-mt-2">Output duration: {{ duration }}</pre>
    </div>`,
  }),
  args: {},
}

export const Disabled: Story = {
  args: {
    startDateTime: DateTime.now(),
    duration: Duration.fromObject({ hours: 1 }),
    disabled: true,
  },
}
