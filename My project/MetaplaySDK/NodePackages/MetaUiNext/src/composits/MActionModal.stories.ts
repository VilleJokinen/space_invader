import MActionModal from './MActionModal.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MActionModal> = {
  component: MActionModal,
  // tags: ['autodocs'],
  argTypes: {
    variant: {
      control: {
        type: 'select',
      },
      defaultValue: 'default',
      options: ['default', 'danger', 'warning'],
    },
    size: {
      control: {
        type: 'radio',
      },
      defaultValue: 'md',
      options: ['sm', 'md', 'lg']
    },
    open: {
      defaultValue: true,
    },
  }
}

export default meta
type Story = StoryObj<typeof MActionModal>

export const OneColumn: Story = {
  render: (args) => ({
    components: { MActionModal },
    setup: () => ({ args }),
    template: `
    <MActionModal v-bind="args">
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.</p>
    </MActionModal>
    `,
  }),
  args: {
    title: 'Fairly Normal Length Title'
  },
}

export const TwoColumns: Story = {
  render: (args) => ({
    components: { MActionModal },
    setup: () => ({ args }),
    template: `
    <MActionModal v-bind="args">
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.</p>
      <template #right-panel>
        <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.</p>
      </template>
    </MActionModal>
    `,
  }),
  args: {
    title: 'Fairly Normal Length Title'
  },
}

export const LongContent: Story = {
  render: (args) => ({
    components: { MActionModal },
    setup: () => ({ args }),
    template: `
    <MActionModal v-bind="args">
      <h3>H3 Header 1</h3>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.</p>
      <h3>H3 Header 2</h3>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.</p>
      <h3>H3 Header 3</h3>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.</p>
      <h3>H3 Header 4</h3>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.</p>
      <h3>H3 Header 5</h3>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.</p>
      <h3>H3 Header 6</h3>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.</p>
      <h3>H3 Header 7</h3>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.</p>
      <h3>H3 Header 8</h3>
      <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.</p>
    </MActionModal>
    `,
  }),
  args: {
    title: 'Long Title That Will Cause Overflow In The Header Area And Will Not Be Truncated With Ellipsis',
  },
}

export const Overflow: Story = {
  render: (args) => ({
    components: { MActionModal },
    setup: () => ({ args }),
    template: `
    <MActionModal v-bind="args">
      <h3>H3 Header</h3>
      <pre>
export const TwoColumns: Story = {
  render: (args) => ({
    components: { MActionModal },
    setup: () => ({ args }),
    template: 'Very long template string that will cause overflow in the modal content area and will make the body scrollable.',
  }),
  args: {},
}
      </pre>
    </MActionModal>
    `,
  }),
  args: {
    title: 'LongTitleThatWillCauseOverflowInTheHeaderAreaAndWillBeTruncatedWithEllipsis',
  },
}
