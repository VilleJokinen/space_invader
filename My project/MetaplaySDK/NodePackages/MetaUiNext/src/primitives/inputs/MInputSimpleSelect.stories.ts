import MInputSimpleSelect from './MInputSimpleSelect.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MInputSimpleSelect> = {
  component: MInputSimpleSelect,
  tags: ['autodocs'],
}

export default meta
type Story = StoryObj<typeof MInputSimpleSelect>

export const Default: Story = {
  render: (args) => ({
    components: { MInputSimpleSelect },
    setup: () => ({ args }),
    data: () => ({ role: 'admin' }),
    template: `<div>
      <MInputSimpleSelect v-bind="args" v-model="role"/>
      <pre class="tw-mt-2">Output: {{ role }}</pre>
    </div>`,
  }),
  args: {
    label: 'Role',
    options: [
      { label: 'Admin', value: 'admin' },
      { label: 'User', value: 'user' },
      { label: 'Guest', value: 'guest' },
    ],
    placeholder: 'Choose a role',
    hintMessage: 'This element only supports string values.',
  },
}

export const Disabled: Story = {
  args: {
    label: 'Role',
    options: [
      { label: 'Admin', value: 'admin' },
      { label: 'User', value: 'user' },
      { label: 'Guest', value: 'guest' },
    ],
    modelValue: 'admin',
    placeholder: 'Choose a role',
    disabled: true,
  },
}

export const Placeholder: Story = {
  args: {
    label: 'Role',
    options: [
      { label: 'Admin', value: 'admin' },
      { label: 'User', value: 'user' },
      { label: 'Guest', value: 'guest' },
    ],
    placeholder: 'Choose a role',
  },
}

export const Success: Story = {
  args: {
    label: 'Role',
    options: [
      { label: 'Admin', value: 'admin' },
      { label: 'User', value: 'user' },
      { label: 'Guest', value: 'guest' },
    ],
    modelValue: 'admin',
    variant: 'success',
    hintMessage: 'Success hint message',
  },
}

export const Danger: Story = {
  args: {
    label: 'Role',
    options: [
      { label: 'Admin', value: 'admin' },
      { label: 'User', value: 'user' },
      { label: 'Guest', value: 'guest' },
    ],
    modelValue: 'admin',
    variant: 'danger',
    hintMessage: 'Danger hint message',
  },
}

export const NoLabel: Story = {
  args: {
    options: [
      { label: 'Admin', value: 'admin' },
      { label: 'User', value: 'user' },
      { label: 'Guest', value: 'guest' },
    ],
    modelValue: 'admin',
  },
}
