import MInputTextArea from './MInputTextArea.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MInputTextArea> = {
  component: MInputTextArea,
  tags: ['autodocs'],
}

export default meta
type Story = StoryObj<typeof MInputTextArea>

export const Default: Story = {
  render: (args) => ({
    components: { MInputTextArea },
    setup: () => ({ args }),
    data: () => ({ value: 'It was a dark and stormy night. Our ship was tossed on the waves like a toy boat. The crew was terrified. The captain was terrified. Everyone but the ship\'s cat was terrified. I was too hungry to be scared. I was a cat, after all.' }),
    template: `<div>
      <MInputTextArea v-bind="args" v-model="value" />
      <pre class="tw-mt-2">Output: {{ value }}</pre>
    </div>`,
  }),
  args: {
    label: 'Chapter 1',
    hintMessage: 'The text field can never be undefined and only supports strings.',
  },
}

export const Placeholder: Story = {
  args: {
    label: 'Open feedback',
    placeholder: 'Enter your feedback here',
  },
}

export const Large: Story = {
  args: {
    label: 'Input a player list',
    placeholder: 'Expecting a long list...',
    rows: 10,
  },
}

export const Disabled: Story = {
  args: {
    label: 'Disabled needs to look good',
    modelValue: 'Even when it has text inside.',
    disabled: true,
  },
}

export const Danger: Story = {
  args: {
    label: 'Input can be false',
    modelValue: 'Commit and run',
    variant: 'danger',
    hintMessage: 'Hints turn red when the variant is danger.',
  },
}

export const Success: Story = {
  args: {
    label: 'Input can be valid',
    modelValue: 'Lint, test and build',
    variant: 'success',
    hintMessage: 'Hints are still neutral when the variant is success.',
  },
}

export const Loading: Story = {
  args: {
    label: 'Input can be pending for validation',
    modelValue: 'Someting that needs server-side validation...',
    variant: 'loading',
    hintMessage: 'Hints are still neutral when the variant is loading.',
  },
}

export const NoLabel: Story = {
  args: {},
}
