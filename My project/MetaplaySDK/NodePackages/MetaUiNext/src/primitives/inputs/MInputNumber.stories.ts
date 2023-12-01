import MInputNumber from './MInputNumber.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MInputNumber> = {
  component: MInputNumber,
  tags: ['autodocs'],
}

export default meta
type Story = StoryObj<typeof MInputNumber>

export const Default: Story = {
  render: (args) => ({
    components: { MInputNumber },
    setup: () => ({ args }),
    data: () => ({ number: undefined }),
    template: `<div>
      <MInputNumber v-bind="args" v-model="number"/>
      <pre class="tw-mt-2">Output: {{ number }}</pre>
    </div>`,
  }),
  args: {
    label: 'Number',
    hintMessage: 'Fractions are not allowed by default.',
  },
}

export const Min3: Story = {
  args: {
    label: 'Number >= 3',
    modelValue: 5,
    min: 3,
  },
}

export const Max6: Story = {
  args: {
    label: 'Number <= 6',
    modelValue: 5,
    max: 6,
  },
}

export const Min3Max6: Story = {
  args: {
    label: 'Number >= 3 && <= 6',
    modelValue: 5,
    min: 3,
    max: 6,
  },
}

export const Disabled: Story = {
  args: {
    label: 'Number (disabled)',
    modelValue: 3,
    disabled: true,
  },
}

export const Danger: Story = {
  args: {
    label: 'Number (invalid)',
    modelValue: 666,
    variant: 'danger',
    hintMessage: 'Hints turn red when the variant is danger.',
  },
}

export const Success: Story = {
  args: {
    label: 'Duration',
    modelValue: 4,
    variant: 'success',
  },
}

export const NoLabel: Story = {
  args: {
    modelValue: 5,
  },
}
