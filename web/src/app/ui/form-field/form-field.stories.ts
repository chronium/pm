import type { Meta, StoryObj } from '@storybook/angular-vite';
import { moduleMetadata } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';

import { PmFormField } from './form-field';

const meta = {
  title: 'Design System/Form Field',
  decorators: [moduleMetadata({ imports: [PmFormField] })],
  parameters: { layout: 'centered' },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {
  render: () => ({
    template: `
      <pm-form-field style="display: block; width: min(360px, calc(100vw - 48px))">
        <label for="task-title-default">Task title</label>
        <input pmControl id="task-title-default" autocomplete="off" />
      </pm-form-field>
    `,
  }),
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const input = canvas.getByLabelText('Task title');
    await userEvent.click(input);
    await expect(input).toHaveFocus();
    await userEvent.type(input, 'Add keyboard shortcuts to the board');
    await expect(input).toHaveValue('Add keyboard shortcuts to the board');
  },
};

export const WithHint: Story = {
  render: () => ({
    template: `
      <pm-form-field style="display: block; width: min(360px, calc(100vw - 48px))">
        <label for="task-id">Task ID</label>
        <input pmControl id="task-id" aria-describedby="task-id-hint" value="PM-0048" />
        <p pmFieldMessage id="task-id-hint">IDs are assigned when the task is created.</p>
      </pm-form-field>
    `,
  }),
};

export const ValidationError: Story = {
  render: () => ({
    template: `
      <pm-form-field style="display: block; width: min(360px, calc(100vw - 48px))">
        <label for="task-title-error">Task title</label>
        <input pmControl id="task-title-error" aria-invalid="true" aria-describedby="task-title-error-message" />
        <p pmFieldMessage id="task-title-error-message" role="alert">Enter a title before saving the task.</p>
      </pm-form-field>
    `,
  }),
};

export const Disabled: Story = {
  render: () => ({
    template: `
      <pm-form-field style="display: block; width: min(360px, calc(100vw - 48px))">
        <label for="task-track-disabled">Track</label>
        <input pmControl id="task-track-disabled" value="PM" disabled />
        <p pmFieldMessage>Track cannot be changed after creation.</p>
      </pm-form-field>
    `,
  }),
};

export const LongLabel: Story = {
  render: () => ({
    template: `
      <pm-form-field style="display: block; width: min(360px, calc(100vw - 48px))">
        <label for="acceptance-long">Acceptance criteria visible to everyone reviewing this project milestone</label>
        <textarea pmControl id="acceptance-long" rows="4">Storybook builds locally and accessibility checks pass.</textarea>
      </pm-form-field>
    `,
  }),
  globals: { viewport: 'mobile' },
};
