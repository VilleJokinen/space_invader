import MInputSwitch from './MInputSwitch.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MInputSwitch> = {
  component: MInputSwitch,
  tags: ['autodocs'],
  argTypes: {
    variant: {
      control: {
        type: 'select',
      },
      options: ['default', 'neutral', 'success', 'danger', 'warning'],
    },
    size: {
      control: {
        type: 'select',
      },
      options: ['xs', 'sm', 'md'],
    }
  }
}

export default meta
type Story = StoryObj<typeof MInputSwitch>

export const Default: Story = {
  render: (args) => ({
    components: { MInputSwitch },
    setup: () => ({ args }),
    template: `<div>
      <MInputSwitch v-bind="args"/>
    </div>`,
  }),
  args: {
    name: 'default switch'
  },
}

export const Small: Story = {
  args: {
    size: 'sm',
  },
}

export const ExtraSmall: Story = {
  args: {
    size: 'xs',
  },
}

export const Disabled: Story = {
  args: {
    disabled: true,
  },
}

export const PrimaryChecked: Story = {
  args: {
    modelValue: true,
  },
}

export const PrimaryDisabled: Story = {
  args: {
    modelValue: true,
    disabled: true,
  },
}

export const SuccessChecked: Story = {
  args: {
    variant: 'success',
    modelValue: true,
  },
}

export const SuccessDisabled: Story = {
  args: {
    variant: 'success',
    modelValue: true,
    disabled: true,
  },
}

export const DangerChecked: Story = {
  args: {
    variant: 'danger',
    modelValue: true,
  },
}

export const DangerDisabled: Story = {
  args: {
    variant: 'danger',
    modelValue: true,
    disabled: true,
  },
}

export const WarningChecked: Story = {
  args: {
    variant: 'warning',
    modelValue: true,
  },
}

export const WarningDisabled: Story = {
  args: {
    variant: 'warning',
    modelValue: true,
    disabled: true,
  },
}
