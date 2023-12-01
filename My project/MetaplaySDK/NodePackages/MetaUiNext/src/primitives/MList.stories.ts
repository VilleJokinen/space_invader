import MList from '../primitives/MList.vue'
import MListItem from '../primitives/MListItem.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MList> = {
  component: MList,
  tags: ['autodocs'],
  argTypes: {}
}

export default meta
type Story = StoryObj<typeof MList>

export const Default: Story = {
  render: (args) => ({
    components: {
      MList,
      MListItem,
    },
    setup: () => ({ args }),
    template: `
    <MList v-bind="args" style="width: 600px">
      <MListItem>
        Item 1
        <template #top-right>Something small</template>
        <template #bottom-left>Lorem ipsum dolor sit amet.</template>
        <template #bottom-right>Link here?</template>
      </MListItem>
      <MListItem>
        Item 2
        <template #top-right>Something small</template>
        <template #bottom-left>Lorem ipsum dolor sit amet.</template>
        <template #bottom-right>Link here?</template>
      </MListItem>
      <MListItem>
        Item 3
        <template #top-right>Something small</template>
        <template #bottom-left>Lorem ipsum dolor sit amet.</template>
        <template #bottom-right>Link here?</template>
      </MListItem>
      <MListItem>
        Item 4
        <template #top-right>Something small</template>
        <template #bottom-left>Lorem ipsum dolor sit amet.</template>
        <template #bottom-right>Link here?</template>
      </MListItem>
    </MList>
    `,
  }),
}

export const WithBorder: Story = {
  render: (args) => ({
    components: {
      MList,
      MListItem,
    },
    setup: () => ({ args }),
    template: `
    <MList v-bind="args" style="width: 600px">
      <MListItem>
        Item 1
        <template #top-right>Something small</template>
        <template #bottom-left>Lorem ipsum dolor sit amet.</template>
        <template #bottom-right>Link here?</template>
      </MListItem>
      <MListItem>
        Item 2
        <template #top-right>Something small</template>
        <template #bottom-left>Lorem ipsum dolor sit amet.</template>
        <template #bottom-right>Link here?</template>
      </MListItem>
      <MListItem>
        Item 3
        <template #top-right>Something small</template>
        <template #bottom-left>Lorem ipsum dolor sit amet.</template>
        <template #bottom-right>Link here?</template>
      </MListItem>
      <MListItem>
        Item 4
        <template #top-right>Something small</template>
        <template #bottom-left>Lorem ipsum dolor sit amet.</template>
        <template #bottom-right>Link here?</template>
      </MListItem>
    </MList>
    `,
  }),
  args: {
    showBorder: true
  }
}
