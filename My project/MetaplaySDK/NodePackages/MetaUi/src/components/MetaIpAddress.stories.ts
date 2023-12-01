import MetaIpAddress from './MetaIpAddress.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta = {
  component: MetaIpAddress,
  tags: ['autodocs'],
  argTypes: {
  },
  args: { ipAddress: '192.168.1.1' },
} satisfies Meta<typeof MetaIpAddress>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = {
  args: {
  },
}

export const Localhost: Story = {
  args: {
    ipAddress: '::1',
  },
}
