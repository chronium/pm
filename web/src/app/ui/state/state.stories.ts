import type { Meta, StoryObj } from '@storybook/angular-vite';
import { moduleMetadata } from '@storybook/angular-vite';

import { PmEmptyState, PmErrorState, PmLoadingState } from './state';

const meta = {
  title: 'Design System/Status States',
  decorators: [moduleMetadata({ imports: [PmLoadingState, PmEmptyState, PmErrorState] })],
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Loading: Story = {
  render: () => ({ template: '<pm-loading-state>Loading tasks for the angular-web milestone…</pm-loading-state>' }),
};

export const Empty: Story = {
  render: () => ({ template: '<pm-empty-state>No tasks match the active track and milestone filters.</pm-empty-state>' }),
};

export const Error: Story = {
  render: () => ({ template: '<pm-error-state>Tasks could not be loaded. Check that the local PM server is running.</pm-error-state>' }),
};
