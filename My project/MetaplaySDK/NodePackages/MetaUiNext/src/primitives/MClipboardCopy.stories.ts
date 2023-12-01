import MClipboardCopy from '../primitives/MClipboardCopy.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MClipboardCopy> = {
  component: MClipboardCopy,
  tags: ['autodocs'],
  argTypes: {
  }
}

export default meta
type Story = StoryObj<typeof MClipboardCopy>

export const Default: Story = {
  render: (args) => ({
    components: { MClipboardCopy },
    setup: () => ({ args }),
    template: `
    Small inline button: <MClipboardCopy v-bind="args"></MClipboardCopy>
    `,
  }),
  args: {
    contents: 'Contents of the clipboard',
  },
}

export const FullSize: Story = {
  render: (args) => ({
    components: { MClipboardCopy },
    setup: () => ({ args }),
    template: `
    <MClipboardCopy v-bind="args">Copy to Clipboard</MClipboardCopy>
    `,
  }),
  args: {
    contents: 'Contents of the clipboard',
    fullSize: true,
  },
}
