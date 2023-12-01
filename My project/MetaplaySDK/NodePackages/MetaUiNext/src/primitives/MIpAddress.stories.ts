import MIpAddress from './MIpAddress.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MIpAddress> = {
  component: MIpAddress,
  argTypes: {
  }
}

export default meta
type Story = StoryObj<typeof MIpAddress>

export const Default: Story = {
  args: {
    ipAddress: '8.8.8.8',
  },
}

export const Localhost: Story = {
  args: {
    ipAddress: '::1',
  },
}
