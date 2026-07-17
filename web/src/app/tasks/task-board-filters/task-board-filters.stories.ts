import type { Meta, StoryObj } from '@storybook/angular-vite';

import { TaskBoardFilters } from './task-board-filters';

const meta = {
  title: 'Tasks/Board filters',
  component: TaskBoardFilters,
  parameters: { layout: 'fullscreen' },
} satisfies Meta<TaskBoardFilters>;
export default meta;
type Story = StoryObj<typeof meta>;
const options = {
  states: [
    { key: 'todo', name: 'To do' },
    { key: 'done', name: 'Done' },
  ],
};

export const Default: Story = { args: { ...options, filters: {} } };
export const ActiveFilters: Story = {
  args: { ...options, filters: { state: 'todo' } },
};
export const Mobile: Story = {
  args: { ...options, filters: { state: 'todo' } },
  globals: { viewport: 'mobile' },
};
