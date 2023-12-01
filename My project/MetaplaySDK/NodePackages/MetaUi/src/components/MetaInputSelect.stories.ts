import MetaInputSelect from './MetaInputSelect.vue'
import type { Meta, StoryObj } from '@storybook/vue3'

import { ref } from 'vue'

export default {
  // @ts-expect-error Storybook doesn't seem to like generics?
  component: MetaInputSelect,
  tags: ['autodocs'],
  render: (args) => ({
    components: { MetaInputSelect },
    setup: () => {
      const selection = ref()
      return { selection, args }
    },
    template: `<div>
      <MetaInputSelect v-bind="args" :value="selection" @input="selection = $event"/>
      <pre class="tw-mt-2">Output: {{ selection }}</pre>
    </div>`,
  }),
} satisfies Meta<typeof MetaInputSelect>

type Story = StoryObj<typeof MetaInputSelect>

export const Strings: Story = {
  args: {
    options: [
      { id: 'id 1', value: 'value one' },
      { id: 'id 2', value: 'value two' },
      { id: 'id 3', value: 'value three', disabled: true },
      { id: 'id 4', value: 'value four' },
      { id: 'id 5', value: 'value five' },
    ],
  },
}

export const Numbers: Story = {
  args: {
    options: [
      { id: 'id 1', value: 111 },
      { id: 'id 2', value: 222 },
      { id: 'id 3', value: 333, disabled: true },
      { id: 'id 4', value: 444 },
      { id: 'id 5', value: 555 },
    ],
  },
}

export const Objects: Story = {
  args: {
    options: [
      { id: 'id 1', value: { displayName: 'value one' } },
      { id: 'id 2', value: { displayName: 'value two' } },
      { id: 'id 3', value: { displayName: 'value three' }, disabled: true },
      { id: 'id 4', value: { displayName: 'value four' } },
      { id: 'id 5', value: { displayName: 'value five' } },
    ],
  },
}

export const SearchableObjects: Story = {
  args: {
    options: [
      { id: 'id 1', value: { displayName: 'value one' } },
      { id: 'id 2', value: { displayName: 'value two' } },
      { id: 'id 3', value: { displayName: 'value three' }, disabled: true },
      { id: 'id 4', value: { displayName: 'value four' } },
      { id: 'id 5', value: { displayName: 'value five' } },
    ],
    searchFields: ['displayName'],
  },
}

// TODO: Disabled because of TS errors.
// export const Function: Story = {
//   args: {
//     options: (searchQuery: string) => {
//       return [
//         { id: 'id 1', value: { displayName: 'value one' } },
//         { id: 'id 2', value: { displayName: 'value two' } },
//         { id: 'id 3', value: { displayName: 'value three' }, disabled: true },
//         { id: 'id 4', value: { displayName: 'value four' } },
//         { id: 'id 5', value: { displayName: 'value five' } },
//       ].filter((option) => option.value.displayName.includes(searchQuery))
//     },
//   },
// }

export const MultiselectStrings: Story = {
  args: {
    options: [
      { id: 'id 1', value: 'value one' },
      { id: 'id 2', value: 'value two' },
      { id: 'id 3', value: 'value three', disabled: true },
      { id: 'id 4', value: 'value four' },
      { id: 'id 5', value: 'value five' },
    ],
    multiselect: true,
  },
}

// export const MultiselectFunction: Story = {
//   args: {
//     options: (searchQuery: string) => {
//       return [
//         { id: 'id 1', value: { displayName: 'value one' } },
//         { id: 'id 2', value: { displayName: 'value two' } },
//         { id: 'id 3', value: { displayName: 'value three' }, disabled: true },
//         { id: 'id 4', value: { displayName: 'value four' } },
//         { id: 'id 5', value: { displayName: 'value five' } },
//       ].filter((option) => option.value.displayName.includes(searchQuery))
//     },
//     multiselect: true,
//   },
// }
