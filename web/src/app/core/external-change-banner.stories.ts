import type { Meta, StoryObj } from '@storybook/angular';

import { ExternalChangeBanner } from './external-change-banner';

const meta: Meta<ExternalChangeBanner> = {
  title: 'Core/External change',
  component: ExternalChangeBanner,
  args: {
    heading: 'This task changed elsewhere.',
    message: 'Review the latest version without losing your local draft.',
  },
};

export default meta;
type Story = StoryObj<ExternalChangeBanner>;

export const ExternalChange: Story = { args: { phase: 'pending' } };

export const ReviewingLatest: Story = { args: { phase: 'reviewing' } };

export const PreservedDraft: Story = {
  args: {
    phase: 'preserved',
    heading: 'Your draft is restored.',
    message: 'Reconcile it with the latest version before saving.',
  },
};

export const UnavailableResource: Story = {
  render: () => ({
    template: `<div role="alert"><strong>Resource unavailable</strong><p>This item was removed or renamed outside this view.</p></div>`,
  }),
};

export const LiveUpdateFailure: Story = {
  render: () => ({
    template: `<p role="status">Live updates unavailable; retrying</p>`,
  }),
};
