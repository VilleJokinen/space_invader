import MListItem from '../primitives/MListItem.vue'
import MBadge from '../primitives/MBadge.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MListItem> = {
  component: MListItem,
  tags: ['autodocs'],
  argTypes: {}
}

export default meta
type Story = StoryObj<typeof MListItem>

export const Default: Story = {
  render: (args) => ({
    components: {
      MListItem,
    },
    setup: () => ({ args }),
    template: `
    <MListItem v-bind="args" style="width: 400px">
      <p>Lorem Ipsum</p>
      <template #top-right>Player:123456789</template>
      <template #bottom-left>Usually item descriptions are short.</template>
      <template #bottom-right>Link here?</template>
    </MListItem>
    `,
  }),
}

export const Clickable: Story = {
  render: (args) => ({
    components: {
      MListItem,
    },
    setup: () => ({ args }),
    template: `
    <MListItem v-bind="args" style="width: 400px">
      <p>Lorem Ipsum</p>
      <template #top-right>Player:123456789</template>
      <template #bottom-left>Usually item descriptions are short.</template>
      <template #bottom-right>Link here?</template>
    </MListItem>
    `,
  }),
  args: {
    clickable: true
  }
}

export const WithBadge: Story = {
  render: (args) => ({
    components: {
      MListItem,
      MBadge
    },
    setup: () => ({ args }),
    template: `
    <MListItem v-bind="args" style="width: 400px">
      <p>Lorem Ipsum</p>
      <template #badge><MBadge>asd</MBadge></template>
      <template #top-right>Player:123456789</template>
      <template #bottom-left>Usually item descriptions are short.</template>
      <template #bottom-right>Link here?</template>
    </MListItem>
    `,
  }),
}

export const WithMultipleBadges: Story = {
  render: (args) => ({
    components: {
      MListItem,
      MBadge
    },
    setup: () => ({ args }),
    template: `
    <MListItem v-bind="args" style="width: 400px">
      <p>Lorem Ipsum</p>
      <template #badge><MBadge>one</MBadge><MBadge>two</MBadge></template>
      <template #top-right>Player:123456789</template>
      <template #bottom-left>Usually item descriptions are short.</template>
      <template #bottom-right>Link here?</template>
    </MListItem>
    `,
  }),
}

export const TextWrap: Story = {
  render: (args) => ({
    components: {
      MListItem,
    },
    setup: () => ({ args }),
    template: `
    <MListItem v-bind="args" style="width: 400px">
      <p>Some Items Can Have Very Long Titles That Will Wrap Onto Multiple Lines</p>
      <template #top-right>Player:123456789</template>
      <template #bottom-left>It's very common for item descriptions to be quite long and need to wrap into multiple lines. This should look great in all scenarios.</template>
      <template #bottom-right>Prop: Some value</template>
    </MListItem>
    `,
  }),
}

export const LongDescription508pxWidth: Story = {
  render: (args) => ({
    components: {
      MListItem,
      MBadge
    },
    setup: () => ({ args }),
    template: `
    <MListItem v-bind="args" style="width: 508px">
      <p>Initial game resources</p>
      <template #top-right><MBadge>Testing</MBadge> since 6 days ago</template>
      <template #bottom-left>If we tweak the early game funnel, then we can discover which settings work best to retain players, because players will either prefer a slower or faster early game experience.</template>
      <template #bottom-right><p>Total runtime: 12 days</p><p>54 participants</p>View experiment</template>
    </MListItem>
    `,
  }),
}

export const LongDescription: Story = {
  render: (args) => ({
    components: {
      MListItem,
      MBadge
    },
    setup: () => ({ args }),
    template: `
    <MListItem v-bind="args" style="width: 300px">
      <p>Initial game resources</p>
      <template #top-right><MBadge>Testing</MBadge> since 6 days ago</template>
      <template #bottom-left>If we tweak the early game funnel, then we can discover which settings work best to retain players, because players will either prefer a slower or faster early game experience.</template>
      <template #bottom-right><p>Total runtime: 12 days</p><p>54 participants</p>View experiment</template>
    </MListItem>
    `,
  }),
}
