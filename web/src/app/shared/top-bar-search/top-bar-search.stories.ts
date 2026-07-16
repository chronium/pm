import type { Meta, StoryObj } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';

import { TopBarSearch } from './top-bar-search';

const results = [
  {
    id: 'guide',
    primary: 'Rendering guide',
    secondary: 'guides/rendering · Jul 16, 2026',
    snippet: 'The canvas rendering pipeline starts with…',
  },
];

const meta = {
  title: 'Shared/Top bar search',
  component: TopBarSearch,
  args: {
    ariaLabel: 'Search examples',
    listboxLabel: 'Example results',
    placeholder: 'Search examples',
    emptyMessage: 'Nothing found.',
    query: 'render',
    options: results,
    loading: false,
    error: null,
  },
  parameters: { layout: 'centered' },
} satisfies Meta<TopBarSearch>;
export default meta;
type Story = StoryObj<typeof meta>;

async function open(canvasElement: HTMLElement) {
  const input = within(canvasElement).getByRole('combobox');
  await userEvent.click(input);
  return input;
}

export const Results: Story = {
  play: async ({ canvasElement }) => void (await open(canvasElement)),
};
export const Loading: Story = { args: { loading: true, options: [] }, play: Results.play };
export const Empty: Story = { args: { options: [] }, play: Results.play };
export const Error: Story = { args: { error: 'Search failed.', options: [] }, play: Results.play };
export const Keyboard: Story = {
  play: async ({ canvasElement }) => {
    const input = await open(canvasElement);
    await userEvent.keyboard('{ArrowDown}');
    await expect(input).toHaveAttribute('aria-activedescendant');
  },
};
export const Mobile: Story = { ...Results, globals: { viewport: 'mobile' } };
