import MErrorCallout from './MErrorCallout.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

import { DisplayError } from '../utils/DisplayErrorHandler'

const meta: Meta<typeof MErrorCallout> = {
  component: MErrorCallout,
  tags: ['autodocs'],
  argTypes: {
  }
}

export default meta
type Story = StoryObj<typeof MErrorCallout>

export const Default: Story = {
  args: {
    error: new DisplayError(
      'Example Metaplay API Error',
      'This is an example of an error with added details.',
      500,
      [
        {
          title: 'API Request',
          content: { method: 'GET', path: '/api/v1/endpoint', body: { foo: 'bar' } }
        },
        {
          title: 'Stack Trace',
          content: 'Exception in thread "main" sample.InputMismatchException at sample.base/sample.MPlay.throwFor(MPlay.sample:939) at sample.base/sample.MPlay.next(MPlay.sample:1594) at sample.base/sample.MPlay.nextFloat(MPlay.sample:2496) at com.example.mySampleProject.hello.main(hello.sample:12)'
        },
      ]
    ),
  },
}

export const TitleAndMessage: Story = {
  args: {
    error: new DisplayError(
      'Example Error',
      'This is an example of an error with and added message.',
    ),
  },
}

export const JavascriptError: Story = {
  args: {
    error: new DisplayError(
      'Example Javascript Error',
      'Unexpected token',
      'SyntaxError',
      [
        {
          title: 'Syntax Error',
          content: 'Unexpected token < in JSON at position 0'
        },
      ]
    )
  },
}

export const MetaplayInternalError: Story = {
  args: {
    error: new DisplayError(
      'Example Metaplay Internal Error',
      'This is an example of an error with added details.',
      500,
      [
        {
          title: 'Internal Error',
          content: 'Example internal error message'
        },
      ]
    ),
  },
}

export const LongErrorExample: Story = {
  render: (args) => ({
    components: { MErrorCallout },
    setup: () => ({ args }),
    template: `
    <MErrorCallout v-bind="args" style="width: 576px">
      Lorem ipsum dolor sit amet.
    </MErrorCallout>
    `,
  }),
  args: {
    error: new DisplayError(
      'Example of a very long error message that will cause overflow in the content area and will make the body scrollable.',
      'Very long template string that will cause overflow in the content area and will make the body scrollable. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed non risus. Suspendisse lectus tortor, dignissim sit amet, adipiscing nec, ultricies sed, dolor.',
    ),
  },
}

export const LongErrorExampleWithBadge: Story = {
  render: (args) => ({
    components: { MErrorCallout },
    setup: () => ({ args }),
    template: `
    <MErrorCallout v-bind="args" style="width: 576px">
      Lorem ipsum dolor sit amet.
    </MErrorCallout>
    `,
  }),
  args: {
    error: new DisplayError(
      'Example of a very long error message that will cause overflow in the content area and will make the body scrollable.',
      'Very long template string that will cause overflow in the content area and will make the body scrollable. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed non risus. Suspendisse lectus tortor, dignissim sit amet, adipiscing nec, ultricies sed, dolor.',
      'Overflow',
    ),
  },
}

export const LongContentErrorExample: Story = {
  render: (args) => ({
    components: { MErrorCallout },
    setup: () => ({ args }),
    template: `
    <MErrorCallout v-bind="args" style="width: 576px">
      Lorem ipsum dolor sit amet.
    </MErrorCallout>
    `,
  }),
  args: {
    error: new DisplayError(
      'Example Javascript Error',
      'Unexpected token',
      'Overflow',
      [
        {
          title: 'Overflow in content',
          content: 'Very long template string that will cause overflow in the content area and will make the body scrollable. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed non risus. Suspendisse lectus tortor, dignissim sit amet, adipiscing nec, ultricies sed, dolor.'
        },
      ]
    )
  },
}

export const ErrorOverflowNoWhiteSpace: Story = {
  render: (args) => ({
    components: { MErrorCallout },
    setup: () => ({ args }),
    template: `
    <MErrorCallout v-bind="args" style="width: 576px">
      Lorem ipsum dolor sit amet.
    </MErrorCallout>
    `,
  }),
  args: {
    error: new DisplayError(
      'LongTitleThatWillCauseOverflowInTheHeaderAreaAndWillBeTruncatedWithEllipsis',
      'Thisisanexampleofalongerrormessagewithnowhitespaceandwillcauseoverflowintheheaderareaandwillbetruncatedwithellipsis.'
    ),
  },
}

export const ErrorOverflowNoWhiteSpaceWithBadge: Story = {
  render: (args) => ({
    components: { MErrorCallout },
    setup: () => ({ args }),
    template: `
    <MErrorCallout v-bind="args" style="width: 576px">
      Lorem ipsum dolor sit amet.
    </MErrorCallout>
    `,
  }),
  args: {
    error: new DisplayError(
      'LongTitleThatWillCauseOverflowInTheHeaderAreaAndWillBeTruncatedWithEllipsis',
      'Thisisanexampleofalongerrormessagewithnowhitespaceandwillcauseoverflowintheheaderareaandwillbetruncatedwithellipsis.',
      'Overflow'
    ),
  },
}

export const ErrorOverflowNoWhiteSpaceWithOverflowBadge: Story = {
  render: (args) => ({
    components: { MErrorCallout },
    setup: () => ({ args }),
    template: `
    <MErrorCallout v-bind="args" style="width: 576px">
      Lorem ipsum dolor sit amet.
    </MErrorCallout>
    `,
  }),
  args: {
    error: new DisplayError(
      'LongTitleThatWillCauseOverflowInTheHeaderAreaAndWillBeTruncatedWithEllipsis',
      'Thisisanexampleofalongerrormessagewithnowhitespaceandwillcauseoverflowintheheaderareaandwillbetruncatedwithellipsis.',
      'Thisisanexampleofalongerrormessagewithnowhitespaceandwillcauseoverflowintheheaderareaandwillbetruncatedwithellipsis.'
    ),
  },
}
