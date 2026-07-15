import type { Meta, StoryObj } from '@storybook/angular-vite';

import { TaskBoardFilters } from './task-board-filters';

const meta = { title: 'Tasks/Board filters', component: TaskBoardFilters, parameters: { layout: 'fullscreen' } } satisfies Meta<TaskBoardFilters>;
export default meta;
type Story = StoryObj<typeof meta>;
const options = {
  tracks: [{ key: 'PM', name: 'Product' }, { key: 'BUILD', name: 'Build' }],
  milestones: [{ key: 'angular-web', name: 'Angular web' }, { key: 'release', name: 'Release' }],
  states: [{ key: 'todo', name: 'To do' }, { key: 'done', name: 'Done' }],
};

export const Default: Story = { args: { ...options, filters: {} } };
export const ActiveFilters: Story = { args: { ...options, filters: { track: 'PM', milestone: 'angular-web', state: 'todo' } } };
export const Mobile: Story = { args: { ...options, filters: { state: 'todo' } }, globals: { viewport: 'mobile' } };
