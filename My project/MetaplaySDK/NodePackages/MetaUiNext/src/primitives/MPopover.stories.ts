import MPopover from './MPopover.vue'
import MList from './MList.vue'
import MListItem from './MListItem.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MPopover> = {
  component: MPopover,
  tags: ['autodocs'],
  argTypes: {
  }
}

export default meta
type Story = StoryObj<typeof MPopover>

export const Prop: Story = {
  render: (args) => ({
    components: {
      MPopover,
      MList,
      MListItem,
    },
    setup: () => ({ args }),
    template: `<MPopover v-bind="args">
      <MList>
        <MListItem class="tw-px-4" clickable>Item 1</MListItem>
        <MListItem class="tw-px-4" clickable>Item 2</MListItem>
      </MList>
    </MPopover>`
  }),
  args: {
    title: 'Link selection popover',
    subtitle: 'Add stuff to the default slot to see it here. tw-px-4 and tw-pb-4 are the right paddings.',
  },
}
