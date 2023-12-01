import MCollapseCard from './MCollapseCard.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MCollapseCard> = {
  component: MCollapseCard,
  tags: ['autodocs'],
  argTypes: {
  }
}

export default meta
type Story = StoryObj<typeof MCollapseCard>

export const Default: Story = {
  args: {
    title: 'Short Title',
  },
}

export const Pill: Story = {
  args: {
    title: 'List of Things',
    badge: '10/35',
  },
}

export const HeaderRightContent: Story = {
  render: (args) => ({
    components: { MCollapseCard },
    setup: () => ({ args }),
    template: `
    <MCollapseCard v-bind="args">
      <template #header-right>
        Right side content
      </template>

      Lorem ipsum dolor sit amet.
    </MCollapseCard>
    `,
  }),
  args: {
    title: 'Passing content in the header-right slot',
  },
}
