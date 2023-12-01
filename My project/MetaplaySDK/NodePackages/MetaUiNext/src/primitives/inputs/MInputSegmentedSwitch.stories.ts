import MInputSegmentedSwitch from './MInputSegmentedSwitch.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MInputSegmentedSwitch> = {
  // @ts-expect-error Storybook doesn't seem to like generics?
  component: MInputSegmentedSwitch,
  tags: ['autodocs'],
  argTypes: {
    size: {
      control: {
        type: 'select',
      },
      options: ['sm', 'md'],
    }
  }
}

export default meta
type Story = StoryObj<typeof MInputSegmentedSwitch>

export const Default: Story = {
  args: {
    label: 'Segmented switch',
    modelValue: 'option1',
    options: [
      { label: 'Option 1', value: 'option1' },
      { label: 'Option 2', value: 'option2' },
      { label: 'Option 3', value: 'option3' },
      { label: 'Option 4', value: 'option4' },
    ],
    hintMessage: 'Zag only supports selecting strings as values.',
  },
}

export const Small: Story = {
  args: {
    size: 'sm',
    modelValue: 'option1',
    options: [
      { label: 'All', value: 'option1' },
      { label: 'Any', value: 'option2' },
    ]
  },
}

export const Disabled: Story = {
  args: {
    disabled: true,
    modelValue: 'option1',
    options: [
      { label: 'Chill', value: 'option1' },
      { label: 'Try hard', value: 'option2' },
    ],
  },
}

export const Success: Story = {
  args: {
    variant: 'success',
    modelValue: 'option1',
    options: [
      { label: 'Option 1', value: 'option1' },
      { label: 'Option 2', value: 'option2' },
    ],
  },
}

export const SuccessDisabled: Story = {
  args: {
    variant: 'success',
    modelValue: 'option1',
    options: [
      { label: 'Option 1', value: 'option1' },
      { label: 'Option 2', value: 'option2' },
    ],
    disabled: true,
  },
}

export const Danger: Story = {
  args: {
    variant: 'danger',
    modelValue: 'option1',
    options: [
      { label: 'Option 1', value: 'option1' },
      { label: 'Option 2', value: 'option2' },
    ],
  },
}

export const DangerDisabled: Story = {
  args: {
    variant: 'danger',
    modelValue: 'option1',
    options: [
      { label: 'Option 1', value: 'option1' },
      { label: 'Option 2', value: 'option2' },
    ],
    disabled: true,
  },
}

export const Warning: Story = {
  args: {
    variant: 'warning',
    modelValue: 'option1',
    options: [
      { label: 'Option 1', value: 'option1' },
      { label: 'Option 2', value: 'option2' },
    ],
  },
}

export const WarningDisabled: Story = {
  args: {
    variant: 'warning',
    modelValue: 'option1',
    options: [
      { label: 'Option 1', value: 'option1' },
      { label: 'Option 2', value: 'option2' },
    ],
    disabled: true,
  },
}

export const ResponsiveLabel: Story = {
  args: {
    modelValue: 'option1',
    options: [
      { label: 'Option 1', value: 'option1' },
      { label: 'Option 2', value: 'option2' },
      { label: 'ThisLabelHasNoWhiteSpace', value: 'option3' },
      { label: 'This is a very long label name', value: 'option4' },
    ],
    hintMessage: 'We do not support wide use-cases. You should use a dropdown instead.',
  },
}

export const ResponsiveSwitch: Story = {
  args: {
    modelValue: 'option1',
    options: [
      { label: 'Option 1', value: 'option1' },
      { label: 'Option 2', value: 'option2' },
      { label: 'Option 3', value: 'option3' },
      { label: 'Option 4', value: 'option4' },
      { label: 'Option 5', value: 'option5' },
      { label: 'Option 6', value: 'option6' },
      { label: 'Option 7', value: 'option7' },
      { label: 'Option 8', value: 'option8' },
    ],
    hintMessage: 'We do not support wide use-cases. You should use a dropdown instead.',
  },
}
