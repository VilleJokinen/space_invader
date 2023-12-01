import MCard from '../primitives/MCard.vue'
import MBadge from '../primitives/MBadge.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

import { DisplayError } from '../utils/DisplayErrorHandler'

const meta: Meta<typeof MCard> = {
  component: MCard,
  tags: ['autodocs'],
}

export default meta
type Story = StoryObj<typeof MCard>

export const Default: Story = {
  render: (args) => ({
    components: { MCard },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'Short Title',
  },
}

export const Subtitle: Story = {
  render: (args) => ({
    components: { MCard },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'Subtitle Card',
    subtitle: 'In some cases a subtitle is needed to provide more context to the card.',
  },
}

export const EmptyPill: Story = {
  render: (args) => ({
    components: { MCard },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'List of Things',
    badge: '0',
  },
}

export const Pill: Story = {
  render: (args) => ({
    components: { MCard },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'List of Things',
    badge: '10/35',
  },
}

export const HeaderRightContent: Story = {
  render: (args) => ({
    components: { MCard, MBadge },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      <template #header-right>
        <MBadge variant="primary">Look at me!</MBadge>
      </template>

      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'Card With Header Right Content',
  },
}

export const ClickableHeader: Story = {
  render: (args) => ({
    components: { MCard, MBadge },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'Card With a Clickable Header',
    subtitle: 'Click the header to invoke a "headerClick" event.',
    clickableHeader: true,
  },
}

export const TextWrap: Story = {
  render: (args) => ({
    components: { MCard },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      <template #subtitle>
        Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed euismod, nisl nec ultricies aliquam, nisl nisl aliquet nisl, eget aliquet nisl.
      </template>

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
    </MCard>
    `,
  }),
  args: {
    title: 'Card Headers Should Deal With Very Long Text by Wrapping to The Next Line',
    badge: '1337',
  },
}

export const Overflow: Story = {
  render: (args) => ({
    components: { MCard },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'WhatHappensWhenTheTitleIsAllOneWordAndStillLongTruncatedOverflow',
    badge: '1337',
  },
}

export const Error: Story = {
  render: (args) => ({
    components: { MCard },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args">
      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'French military victories',
    error: new DisplayError(
      'No victories found',
      'Oh no, something went wrong while loading data for this card!',
      500,
      [{
        title: 'Example stack trace',
        content: 'Some long stack trace here'
      }],
    ),
  },
}

export const Warning: Story = {
  render: (args) => ({
    components: { MCard },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'Warning Card (TODO)',
    variant: 'warning',
  },
}

export const Danger: Story = {
  render: (args) => ({
    components: { MCard },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'Dangerous Card (TODO)',
    variant: 'danger',
  },
}

export const Neutral: Story = {
  render: (args) => ({
    components: { MCard },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'Neutral Card (TODO)',
    variant: 'neutral',
  },
}

export const Success: Story = {
  render: (args) => ({
    components: { MCard },
    setup: () => ({ args }),
    template: `
    <MCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MCard>
    `,
  }),
  args: {
    title: 'Successful Card (TODO)',
    variant: 'success',
  },
}
