import type { Meta, StoryObj } from '@storybook/angular-vite';
import { moduleMetadata } from '@storybook/angular-vite';

import { PmButton } from './button.directive';

const meta = {
  title: 'Design System/Button',
  decorators: [moduleMetadata({ imports: [PmButton] })],
  parameters: { layout: 'centered' },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Variants: Story = {
  render: () => ({
    template: `
      <div style="display: flex; flex-wrap: wrap; gap: var(--pm-space-2); align-items: center">
        <button type="button" pmButton="primary">Create task</button>
        <button type="button" pmButton="secondary">Edit details</button>
        <button type="button" pmButton="ghost">Cancel</button>
        <button type="button" pmButton="danger">Remove task</button>
      </div>
    `,
  }),
};

export const Disabled: Story = {
  render: () => ({
    template: `
      <div style="display: flex; flex-wrap: wrap; gap: var(--pm-space-2)">
        <button type="button" pmButton="primary" disabled>Create task</button>
        <button type="button" pmButton="secondary" disabled>Edit details</button>
        <button type="button" pmButton="danger" disabled>Remove task</button>
      </div>
    `,
  }),
};

export const Links: Story = {
  render: () => ({
    template: `
      <div style="display: flex; flex-wrap: wrap; gap: var(--pm-space-2)">
        <a pmButton="primary" href="/?path=/story/design-system-foundation--light">Open foundation</a>
        <a pmButton="secondary" href="https://storybook.js.org/">Storybook documentation</a>
      </div>
    `,
  }),
};

export const LongLabel: Story = {
  render: () => ({
    template:
      '<button type="button" pmButton="primary">Create a task and continue editing all project details</button>',
  }),
  globals: { viewport: 'mobile' },
};
