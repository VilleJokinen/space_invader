import MBadge from './MBadge.vue'
import MCallout from './MCallout.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MBadge> = {
  component: MBadge,
  tags: ['autodocs'],
  argTypes: {
    shape: {
      control: {
        type: 'select',
      },
      options: ['default', 'pill'],
    },
    variant: {
      control: {
        type: 'select',
      },
      options: ['neutral', 'primary', 'success', 'danger', 'warning'],
    },
  }
}

export default meta
type Story = StoryObj<typeof MBadge>

export const Default: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: 'Inbox <MBadge v-bind="args">45</MBadge>',
  }),
  args: {},
}

export const Icon: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: `Badges with icons <MBadge v-bind="args">
    <template #icon>
      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor">
        <path d="M2 3a1 1 0 00-1 1v1a1 1 0 001 1h16a1 1 0 001-1V4a1 1 0 00-1-1H2z" />
        <path fill-rule="evenodd" d="M2 7.5h16l-.811 7.71a2 2 0 01-1.99 1.79H4.802a2 2 0 01-1.99-1.79L2 7.5zM7 11a1 1 0 011-1h4a1 1 0 110 2H8a1 1 0 01-1-1z" clip-rule="evenodd" />
      </svg>
    </template>
    Should look OK
    </MBadge>
    even inside text.`,
  }),
  args: {},
}

export const TooltipBadge: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: 'Hover over the <MBadge v-bind="args">tooltip</MBadge>',
  }),
  args: {
    tooltip: 'This is a tooltip in a badge.'
  },
}

export const Primary: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: 'Lorem ipsum <MBadge v-bind="args">dolor</MBadge> sit amet.',
  }),
  args: {
    variant: 'primary',
  },
}

export const Success: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: 'Status <MBadge v-bind="args">On</MBadge>',
  }),
  args: {
    variant: 'success',
  },
}

export const Danger: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: 'Status <MBadge v-bind="args">Off</MBadge>',
  }),
  args: {
    variant: 'danger',
  },
}

export const DangerWithDangerBackground: Story = {
  render: (args) => ({
    components: { MBadge, MCallout },
    setup: () => ({ args }),
    template: `
    <MCallout title="Contrast check" v-bind="args">
      <MBadge v-bind="args">This only looks good</MBadge> if there's enough contrast.
    </MCallout>
    `,
  }),
  args: {
    variant: 'danger',
  },
}

export const Warning: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: 'Contract status <MBadge v-bind="args">Expired</MBadge>',
  }),
  args: {
    variant: 'warning',
  },
}

export const Pill: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: 'In-game mails <MBadge v-bind="args">300</MBadge>',
  }),
  args: {
    shape: 'pill',
  },
}

export const NarrowPill: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: 'In-game mails <MBadge v-bind="args">1</MBadge>',
  }),
  args: {
    shape: 'pill',
  },
}

export const TailwindXsText: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: '<span class="tw-text-xs">Sometimes <MBadge v-bind="args">extra small</MBadge> looks better?</span>',
  }),
  args: {
  },
}

export const TailwindSmText: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: '<span class="tw-text-sm">Sometimes <MBadge v-bind="args">small</MBadge> looks better?</span>',
  }),
  args: {
  },
}

export const TailwindLgText: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: '<span class="tw-text-lg">Sometimes <MBadge v-bind="args">large</MBadge> looks better?</span>',
  }),
  args: {
  },
}

export const TailwindXlText: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: '<span class="tw-text-xl">Sometimes <MBadge v-bind="args">extra large</MBadge> looks better?</span>',
  }),
  args: {
  },
}

export const WrappedAroundListOfBadges: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({
      args,
      items: [
        'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten',
        'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen', 'eighteen', 'nineteen', 'twenty'
      ]
    }),
    template: `
      <div>This is what it looks like in our dashboard in a few places right now.</div>
      <div class="tw-flex tw-flex-wrap" style="max-width: 300px;">
        <MBadge
          v-bind="args"
          class="tw-mr-1"
          v-for="item in items"
          :key="item"
        >
          {{ item }}
        </MBadge>
      </div>
    `,
  }),
  args: {
  },
}

export const BadgeOverflow: Story = {
  render: (args) => ({
    components: { MBadge },
    setup: () => ({ args }),
    template: '<MBadge v-bind="args">Thisisanexampleofalongerrormessagewithnowhitespaceandwillcauseoverflowintheheaderareaandwillbetrun.</MBadge>',
  }),
  args: {},
}
