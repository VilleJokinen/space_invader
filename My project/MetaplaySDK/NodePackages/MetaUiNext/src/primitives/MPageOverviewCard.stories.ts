import MPageOverviewCard from './MPageOverviewCard.vue'
import MButton from './MButton.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

import { DisplayError } from '../utils/DisplayErrorHandler'

const meta: Meta<typeof MPageOverviewCard> = {
  component: MPageOverviewCard,
  tags: ['autodocs'],
}

export default meta
type Story = StoryObj<typeof MPageOverviewCard>

export const Default: Story = {
  render: (args) => ({
    components: { MPageOverviewCard },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MPageOverviewCard>
    `,
  }),
  args: {
    title: 'Short Title',
  },
}

export const Subtitle: Story = {
  render: (args) => ({
    components: { MPageOverviewCard },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MPageOverviewCard>
    `,
  }),
  args: {
    title: 'Subtitle Card',
    subtitle: 'In some cases a subtitle is needed to provide more context to the card.',
  },
}

export const ID: Story = {
  render: (args) => ({
    components: { MPageOverviewCard },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MPageOverviewCard>
    `,
  }),
  args: {
    title: 'Card With an ID',
    id: 'Player:ZArvpuPqNL',
  },
}

export const Button: Story = {
  render: (args) => ({
    components: { MPageOverviewCard, MButton },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
      <template #buttons>
        <MButton>Some Action</MButton>
      </template>
    </MPageOverviewCard>
    `,
  }),
  args: {
    title: 'Card With a button',
  },
}

export const ManyButtons: Story = {
  render: (args) => ({
    components: { MPageOverviewCard, MButton },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
      <template #buttons>
        <MButton>Button 1</MButton>
        <MButton>Button 2</MButton>
        <MButton>Button 3</MButton>
        <MButton>Button 4</MButton>
        <MButton>Button 5</MButton>
        <MButton>Button 6</MButton>
        <MButton>Button 7</MButton>
        <MButton>Button 8</MButton>
        <MButton>Button 9</MButton>
      </template>
    </MPageOverviewCard>
    `,
  }),
  args: {
    title: 'Card With too many buttons',
    id: 'Player:ZArvpuPqNL',
  },
}

export const TextWrap: Story = {
  render: (args) => ({
    components: { MPageOverviewCard },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args" style="width: 600px">
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
    </MPageOverviewCard>
    `,
  }),
  args: {
    title: 'Card Headers Should Deal With Very Long Text by Wrapping to The Next Line',
    id: 'Player:ZArvpuPqNL',
  },
}

export const Overflow: Story = {
  render: (args) => ({
    components: { MPageOverviewCard },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MPageOverviewCard>
    `,
  }),
  args: {
    title: 'WhatHappensWhenTheTitleIsAllOneWordAndStillLongTruncatedOverflow',
    id: 'Player:ZArvpuPqNL',
  },
}

export const Error: Story = {
  render: (args) => ({
    components: { MPageOverviewCard },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args">
      Lorem ipsum dolor sit amet.
    </MPageOverviewCard>
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
    components: { MPageOverviewCard },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MPageOverviewCard>
    `,
  }),
  args: {
    title: 'Warning Card (TODO)',
    variant: 'warning',
  },
}

export const Danger: Story = {
  render: (args) => ({
    components: { MPageOverviewCard },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MPageOverviewCard>
    `,
  }),
  args: {
    title: 'Dangerous Card (TODO)',
    variant: 'danger',
  },
}

export const Neutral: Story = {
  render: (args) => ({
    components: { MPageOverviewCard },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MPageOverviewCard>
    `,
  }),
  args: {
    title: 'Neutral Card (TODO)',
    variant: 'neutral',
  },
}

export const Success: Story = {
  render: (args) => ({
    components: { MPageOverviewCard },
    setup: () => ({ args }),
    template: `
    <MPageOverviewCard v-bind="args" style="width: 600px">
      Lorem ipsum dolor sit amet.
    </MPageOverviewCard>
    `,
  }),
  args: {
    title: 'Successful Card (TODO)',
    variant: 'success',
  },
}
