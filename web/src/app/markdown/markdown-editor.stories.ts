import type { Meta, StoryObj } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';

import { MarkdownEditor } from './markdown-editor';

const meta = {
  title: 'Markdown/Editor',
  component: MarkdownEditor,
  args: { value: '# Task description\n\nUse **Markdown** for implementation notes.', disabled: false, label: 'Task description' },
  parameters: { layout: 'padded' },
} satisfies Meta<MarkdownEditor>;

export default meta;
type Story = StoryObj<typeof meta>;
export const Default: Story = {};
export const Disabled: Story = { args: { disabled: true } };
export const LongContent: Story = { args: { value: Array.from({ length: 30 }, (_, index) => `## Section ${index + 1}\n\nScrollable editor content.`).join('\n\n') } };
export const KeyboardEntry: Story = { play: async ({ canvasElement }) => { const canvas = within(canvasElement); const textbox = canvas.getByRole('textbox', { name: 'Task description' }); await userEvent.click(textbox); await userEvent.keyboard('{Control>}a{/Control}Keyboard text'); await expect(textbox).toHaveValue('Keyboard text'); } };
