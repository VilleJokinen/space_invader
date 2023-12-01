import LoadingLayout from './LoadingLayout.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof LoadingLayout> = {
  component: LoadingLayout,
}

export default meta
type Story = StoryObj<typeof LoadingLayout>

export const Default: Story = {
  render: (args) => ({
    components: { LoadingLayout },
    setup: () => ({ args }),
    template: '<loading-layout v-bind="args"></loading-layout>',
  }),
  args: {},
}
