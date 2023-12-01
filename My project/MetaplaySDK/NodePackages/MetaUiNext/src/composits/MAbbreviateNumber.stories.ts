import MAbbreviateNumber from './MAbbreviateNumber.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MAbbreviateNumber> = {
  component: MAbbreviateNumber,
  tags: ['autodocs'],
  argTypes: {
  }
}

export default meta
type Story = StoryObj<typeof MAbbreviateNumber>

export const Default: Story = {
  args: {
    number: 123456789,
  },
}

export const NoTooltip: Story = {
  args: {
    number: 123456789,
    disableTooltip: true
  },
}

export const SmallNumber: Story = {
  args: {
    number: 100,
  },
}

export const Rounded: Story = {
  args: {
    number: 300000.14159,
    roundDown: true
  },
}

export const OneUnit: Story = {
  args: {
    number: 1,
    unit: 'lemming'
  },
}

export const ManyUnits: Story = {
  args: {
    number: 987654321,
    unit: 'lemming'
  },
}
