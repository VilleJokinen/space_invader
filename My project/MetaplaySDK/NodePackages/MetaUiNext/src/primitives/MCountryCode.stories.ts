import MCountryCode from './MCountryCode.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MCountryCode> = {
  component: MCountryCode,
  argTypes: {
  }
}

export default meta
type Story = StoryObj<typeof MCountryCode>

export const Default: Story = {
  args: {
    isoCode: 'DE',
  },
}
