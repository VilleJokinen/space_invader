import MInputSingleCheckbox from './MInputSingleCheckbox.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MInputSingleCheckbox> = {
  component: MInputSingleCheckbox,
  tags: ['autodocs'],
}

export default meta
type Story = StoryObj<typeof MInputSingleCheckbox>

export const Default: Story = {
  render: (args) => ({
    components: { MInputSingleCheckbox },
    setup: () => ({ args }),
    data: () => ({ value: false }),
    template: `<div>
      <MInputSingleCheckbox v-bind="args" v-model="value">I will use this default slot <a target="_blank" class="tw-text-blue-500 hover:tw-underline hover:tw-text-blue-600 active:tw-text-blue-700" href="https://www.youtube.com/watch?v=dQw4w9WgXcQ&ab_channel=RickAstley">responsibly.</a></MInputSingleCheckbox>
      <pre class="tw-mt-2">Output: {{ value }}</pre>
    </div>`,
  }),
  args: {
    label: 'Accept everything',
    hintMessage: 'Checkboxes return booleans.',
  },
}

export const Checked: Story = {
  args: {
    label: 'Accept everything',
    modelValue: true,
    description: 'This checkbox is checked by default.',
  },
}

export const Disabled: Story = {
  args: {
    label: 'Format C:/ ?',
    modelValue: true,
    description: 'This action can not be undone.',
    disabled: true,
  },
}

export const Success: Story = {
  args: {
    label: 'Ok?',
    modelValue: true,
    variant: 'success',
    description: 'This mostly makes sense when checked.',
    hintMessage: 'Success hint message.',
  },
}

export const Danger: Story = {
  args: {
    label: 'Ok?',
    modelValue: false,
    variant: 'danger',
    description: 'This mostly makes sense when not checked.',
    hintMessage: 'Danger hint message.',
  },
}

export const LongDescription: Story = {
  args: {
    label: 'Accept everything',
    modelValue: true,
    description: 'This is a very long description that should wrap to multiple lines. It really should be shorter. I mean, who needs this much description for a checkbox?',
  },
}

export const DescriptionOverflow: Story = {
  args: {
    label: 'Accept everything',
    modelValue: true,
    description: 'Thisisastringofwordsthatislongerthanthecheckboxitselfandwilloverflowthecontainer.Whatonearthcouldpossiblybesolongthatitneedsthismanycharacters?Iguesswewillfindout.',
  },
}

export const NoLabelOrDescription: Story = {
  args: {
    modelValue: false,
  },
}
