import MCard from '../../primitives/MCard.vue'
import MErrorCallout from '../../primitives/MErrorCallout.vue'
import MRootLayout from './MRootLayout.vue'
import MViewContainer from './MViewContainer.vue'
import MSidebarSection from './MSidebarSection.vue'
import MSidebarLink from './MSidebarLink.vue'
import type { Meta, StoryObj } from '@storybook/vue3'
import { usePermissions } from '../../utils/permissions'

const meta: Meta<typeof MRootLayout> = {
  component: MRootLayout,
  parameters: {
    layout: 'fullscreen',
  }
}

export default meta
type Story = StoryObj<typeof MRootLayout>

export const Default: Story = {
  render: (args) => ({
    components: {
      MRootLayout,
      MViewContainer,
      MCard,
      MSidebarSection,
      MSidebarLink,
    },
    setup: () => {
      usePermissions().setPermissions(['example-permission'])

      return {
        args
      }
    },
    template: `
      <MRootLayout v-bind="args">
        <template #sidebar>
          <div>
            <MSidebarSection title="Section 1">
              <MSidebarLink label="Link 1"></MSidebarLink>
              <MSidebarLink label="Link 2"></MSidebarLink>
            </MSidebarSection>
            <MSidebarSection title="Section 2">
              <MSidebarLink label="Link 1"></MSidebarLink>
              <MSidebarLink label="Link 2" permission="example-permission2"></MSidebarLink>
              <MSidebarLink label="Link 3"></MSidebarLink>
            </MSidebarSection>
            <MSidebarSection title="Section 3">
              <MSidebarLink label="Link 1"></MSidebarLink>
            </MSidebarSection>
          </div>
        </template>

        <MViewContainer>
          <template #overview>
            <MCard title="Overview card placeholder" class="tw-h-52 tw-w-96">
              <p>Lorem ipsum dolor sit amet.</p>
            </MCard>
          </template>

          <MCard title="Example content">
            <p>Lorem ipsum dolor sit amet.</p>
          </MCard>
        </MViewContainer>
      </MRootLayout>
    `,
  }),
  args: {
    projectName: 'Project Name',
    headerBadgeLabel: 'Anonymous Moomin',
  },
}

export const RootLayoutWithLoadingError: Story = {
  render: (args) => ({
    components: {
      MRootLayout,
      MViewContainer,
      MCard,
      MErrorCallout,
      MSidebarSection,
      MSidebarLink,
    },
    setup: () => {
      usePermissions().setPermissions(['example-permission'])
      return {
        args
      }
    },
    template: `
      <MRootLayout v-bind="args">
        <template #sidebar>
          <MSidebarSection title="Section 1">
            <MSidebarLink label="Link 1"></MSidebarLink>
            <MSidebarLink label="Link 2"></MSidebarLink>
          </MSidebarSection>
          <MSidebarSection title="Section 2">
            <MSidebarLink label="Link 1"></MSidebarLink>
            <MSidebarLink label="Link 2" permission="example-permission2"></MSidebarLink>
            <MSidebarLink label="Link 3"></MSidebarLink>
          </MSidebarSection>
          <MSidebarSection title="Section 3">
            <MSidebarLink label="Link 1"></MSidebarLink>
          </MSidebarSection>
        </template>

        <MViewContainer isLoading :errorPayload="{ title: 'Loading error', message: 'This is an example loading error message.'}">
          <template #overview>
            <MCard title="Overview card placeholder" class="tw-h-52 tw-w-96">
              <p>Lorem ipsum dolor sit amet.</p>
            </MCard>
          </template>
        </MViewContainer>
      </MRootLayout>
    `,
  }),
  args: {
    projectName: 'Project Name',
    headerBadgeLabel: 'Anonymous Moomin',
  },
}
