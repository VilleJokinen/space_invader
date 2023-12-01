import MCallout from './MCallout.vue'
import MButton from './MButton.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

const meta: Meta<typeof MCallout> = {
  component: MCallout,
  tags: ['autodocs'],
  argTypes: {
  }
}

export default meta
type Story = StoryObj<typeof MCallout>

export const Warning: Story = {
  render: (args) => ({
    components: { MCallout },
    setup: () => ({ args }),
    template: `
    <MCallout v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCallout>
    `,
  }),
  args: {
    title: 'This is a warning!',
  },
}

export const Danger: Story = {
  render: (args) => ({
    components: { MCallout },
    setup: () => ({ args }),
    template: `
    <MCallout v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCallout>
    `,
  }),
  args: {
    title: 'Maybe you should not do this...',
    variant: 'danger',
  },
}

export const Success: Story = {
  render: (args) => ({
    components: { MCallout },
    setup: () => ({ args }),
    template: `
    <MCallout v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCallout>
    `,
  }),
  args: {
    title: 'Looking good, boss!',
    variant: 'success',
  },
}

export const Primary: Story = {
  render: (args) => ({
    components: { MCallout },
    setup: () => ({ args }),
    template: `
    <MCallout v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCallout>
    `,
  }),
  args: {
    title: 'Looking good, boss!',
    variant: 'primary',
  },
}

export const OneButton: Story = {
  render: (args) => ({
    components: { MCallout, MButton },
    setup: () => ({ args }),
    template: `
    <MCallout v-bind="args" style="width: 600px">
      Buttons should be on the right.
      <template #buttons>
        <MButton>OK</button>
      </template>
    </MCallout>
    `,
  }),
  args: {
    title: 'Interactive Callout',
    variant: 'primary',
  },
}

export const ManyButtons: Story = {
  render: (args) => ({
    components: { MCallout, MButton },
    setup: () => ({ args }),
    template: `
    <MCallout v-bind="args" style="width: 600px">
      Callout should deal with lots of buttons in a reasonable way.
      <template #buttons>
        <MButton>OK</MButton>
        <MButton>Cancel</MButton>
        <MButton>Help</MButton>
        <MButton>More</MButton>
        <MButton>Less</MButton>
        <MButton>Close</MButton>
        <MButton>Super long text in a button</MButton>
        <MButton>Superunlikelytextinabutton</MButton>
      </template>
    </MCallout>
    `,
  }),
  args: {
    title: 'Interactive Callout',
    variant: 'primary',
  },
}

export const TitleOverflow: Story = {
  render: (args) => ({
    components: { MCallout },
    setup: () => ({ args }),
    template: `
    <MCallout v-bind="args" title="Long Title That Will Cause Overflow In The Header Area And Will Not Be Truncated With Ellipsis" style="width: 600px">
      Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.
    </MCallout>
    `,
  }),
  args: {
  },
}

// TODO: Reevaluate need for props when reviewing and implementing MCallout Component.
/* export const TitleAndBodyTruncateUsingProps: Story = {
  render: (args) => ({
    components: { MCallout },
    setup: () => ({ args }),
    template: `
    <MCallout v-bind="args" style="width: 600px">
    </MCallout>
    `,
  }),
  args: {
    title: 'LongTitleThatWillCauseOverflowInTheHeaderAreaAndWillBeTruncatedWithEllipsis',
    body: 'Thisisanexampleofalongerrormessagewithnowhitespaceandwillcauseoverflowintheheaderareaandwillbetruncatedwithellipsis.',
    variant: 'danger',
  },
} */

export const TitleAndBodyTruncateUsingSlots: Story = {
  render: (args) => ({
    components: { MCallout },
    setup: () => ({ args }),
    template: `
    <MCallout v-bind="args" title="LongTitleThatWillCauseOverflowInTheHeaderAreaAndWillBeTruncatedWithEllipsis" style="width: 600px">
      <p>Thisisanexampleofalongerrormessagewithnowhitespaceandwillcauseoverflowintheheaderareaandwillbetruncatedwithellipsis..</p>
      <p>Slots checking currently does not work. Passing it through helper function doesn't do anything since slots take in a span/div.</p>
    </MCallout>
    `,
  }),
  args: {
    variant: 'danger',
  },
}

export const BodyOverflow: Story = {
  render: (args) => ({
    components: { MCallout },
    setup: () => ({ args }),
    template: `
    <MCallout v-bind="args" style="width: 600px">
      <p class="mb-2">Lorem ipsum dolor sit amet.</p>
      <pre class="text-sm">
export const TwoColumns: Story = {
  render: (args) => ({
    components: { MActionModal },
    setup: () => ({ args }),
    template: 'Very long template string that will cause overflow in the content area and will make the body scrollable.',
  }),
  args: {},
}
      </pre>
    </MCallout>
    `,
  }),
  args: {
    title: 'Maybe you should not do this...',
    variant: 'danger',
  },
}
