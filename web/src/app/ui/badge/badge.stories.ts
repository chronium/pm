import type { Meta, StoryObj } from '@storybook/angular-vite';
import { moduleMetadata } from '@storybook/angular-vite';

import { PmBadge } from './badge';

const meta = {
  title: 'Design System/Badge',
  decorators: [moduleMetadata({ imports: [PmBadge] })],
  parameters: { layout: 'centered' },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Tones: Story = {
  render: () => ({
    template: `
      <div style="display: flex; flex-wrap: wrap; gap: var(--pm-space-2); align-items: center">
        <pm-badge tone="neutral">Backlog</pm-badge>
        <pm-badge tone="accent">In progress</pm-badge>
        <pm-badge tone="success">Done</pm-badge>
        <pm-badge tone="warning">Blocked</pm-badge>
        <pm-badge tone="danger">Failed</pm-badge>
      </div>
    `,
  }),
};

export const LongContent: Story = {
  render: () => ({
    template: '<pm-badge tone="warning">Waiting for architecture review from the platform team</pm-badge>',
  }),
  globals: { viewport: 'mobile' },
};
