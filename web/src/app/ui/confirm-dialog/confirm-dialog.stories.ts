import type { Meta, StoryObj } from '@storybook/angular-vite';
import { moduleMetadata } from '@storybook/angular-vite';
import { expect, fn, userEvent, within } from 'storybook/test';

import { PmConfirmDialog } from './confirm-dialog';

interface DialogStoryArgs {
  open: boolean;
  pending: boolean;
  heading: string;
  message: string;
  confirmLabel: string;
  cancelLabel: string;
  confirmed: () => void;
  cancelled: () => void;
}

const meta = {
  title: 'Design System/Confirmation Dialog',
  decorators: [moduleMetadata({ imports: [PmConfirmDialog] })],
  argTypes: {
    confirmed: { action: 'confirmed' },
    cancelled: { action: 'cancelled' },
  },
  args: {
    open: true,
    pending: false,
    heading: 'Remove PM-0048?',
    message: 'The task will be removed from the project. This action cannot be undone.',
    confirmLabel: 'Remove task',
    cancelLabel: 'Cancel',
    confirmed: fn(),
    cancelled: fn(),
  },
  render: (args) => ({
    props: args,
    template: `
      <pm-confirm-dialog
        [(open)]="open"
        [pending]="pending"
        [heading]="heading"
        [message]="message"
        [confirmLabel]="confirmLabel"
        [cancelLabel]="cancelLabel"
        (confirmed)="confirmed()"
        (cancelled)="cancelled()"
      />
    `,
  }),
} satisfies Meta<DialogStoryArgs>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Pending: Story = {
  args: { pending: true },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('button', { name: 'Cancel' })).toBeDisabled();
    await expect(canvas.getByRole('button', { name: 'Working…' })).toBeDisabled();
  },
};

export const LongContent: Story = {
  args: {
    heading: 'Remove the task with a deliberately long title from the angular-web milestone?',
    message:
      'PM-0048 documents the component workshop, its browser checks, and the accessibility baseline used by future reusable visual components. Removing it also removes this context from the local project history.',
  },
  globals: { viewport: 'mobile' },
};

export const CancelAction: Story = {
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);
    const dialog = canvas.getByRole('dialog');
    const cancel = canvas.getByRole('button', { name: 'Cancel' });
    await expect(cancel).toHaveFocus();
    await userEvent.click(cancel);
    await expect(args.cancelled).toHaveBeenCalledOnce();
    await expect(dialog).not.toHaveAttribute('open');
  },
};

export const ConfirmAction: Story = {
  play: async ({ args, canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByRole('button', { name: 'Remove task' }));
    await expect(args.confirmed).toHaveBeenCalledOnce();
  },
};
