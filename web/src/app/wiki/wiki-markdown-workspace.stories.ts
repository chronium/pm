import type { Meta, StoryObj } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';

import { WikiMarkdownWorkspace } from './wiki-markdown-workspace';

const populated = `# Editing workspace

The preview follows the local **Markdown draft** while both panes scroll independently.

- Keyboard accessible
- Sanitized rendering
- Responsive tabs`;

const meta = {
  title: 'Wiki/Markdown workspace',
  component: WikiMarkdownWorkspace,
  args: { value: populated, disabled: false, label: 'Wiki page Markdown body' },
  parameters: { layout: 'padded' },
} satisfies Meta<WikiMarkdownWorkspace>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Populated: Story = {};
export const Empty: Story = { args: { value: '' } };
export const Disabled: Story = { args: { disabled: true } };
export const LongContent: Story = {
  args: {
    value: Array.from(
      { length: 24 },
      (_, index) =>
        `## Section ${index + 1}\n\nLong-form wiki content remains independently scrollable.`,
    ).join('\n\n'),
  },
};
export const Dirty: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const editor = canvas.getByRole('textbox', { name: 'Wiki page Markdown body' });
    await userEvent.click(editor);
    await userEvent.keyboard('{End}\n\nLocal draft');
    await expect(editor).toHaveValue('Local draft');
  },
};
export const MobileEditor: Story = {
  parameters: { viewport: { defaultViewport: 'mobile1' } },
};
export const MobilePreview: Story = {
  parameters: { viewport: { defaultViewport: 'mobile1' } },
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByRole('tab', { name: 'Preview' }));
    await expect(canvas.getByRole('tab', { name: 'Preview' })).toHaveAttribute(
      'aria-selected',
      'true',
    );
  },
};
