import { HttpBackend, HttpRequest, HttpResponse, provideHttpClient } from '@angular/common/http';
import { Component, Injectable } from '@angular/core';
import { provideRouter, withDisabledInitialNavigation } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig, moduleMetadata } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';
import { of } from 'rxjs';

import { PollingCoordinator } from '../../core/polling-coordinator';
import type { TaskResponse } from '../task-api.service';
import { TaskDialogShell } from '../task-dialog/task-dialog-shell';
import { TaskWorkspace } from './task-workspace';

const settings = {
  projectName: 'PM',
  statuses: [
    { key: 'todo', name: 'To do' },
    { key: 'review', name: 'Ready for review with a long display name' },
    { key: 'done', name: 'Done' },
  ],
  tracks: [
    { key: 'PM', name: 'Product management' },
    { key: 'BUILD', name: 'Build and interface engineering' },
  ],
  milestones: [
    { key: 'angular-web', title: 'Angular web replacement', priority: 'high' },
    {
      key: 'long',
      title: 'A milestone with a deliberately long display name for layout checks',
      priority: 'medium',
    },
  ],
  priorityOptions: ['none', 'low', 'medium', 'high', 'urgent'],
  revision: 'settings-r1',
};

const task: TaskResponse = {
  id: 'PM-0060',
  title: 'Build the shared inline task workspace',
  track: 'PM',
  milestone: 'angular-web',
  priority: 'high',
  prioritySource: 'milestone',
  prioritySelection: 'inherit',
  state: 'todo',
  dependencies: {
    ready: false,
    dependsOn: ['PM-0029'],
    waitingOn: ['PM-0029'],
    missing: [],
    summary: 'Waiting on PM-0029',
  },
  activation: {
    isEligible: false,
    milestoneLifecycle: 'inactive',
    requiredActivationTriggers: ['beta-entry'],
    unmetActivationTriggers: ['beta-entry'],
    summary:
      'Ineligible: milestone angular-web is inactive; unmet activation triggers: beta-entry.',
  },
  createdAt: '2026-07-16T17:02:06Z',
  modifiedAt: '2026-07-18T14:30:00Z',
  description:
    '## Goal\n\nUse one **inline workspace** for dialogs and canonical pages.\n\n- Preserve board context\n- Keep every field keyboard accessible',
  revision: 'task-r1',
  localMetadata: { filePath: '.pm/tasks/PM-0060.md' },
};

const longTask: TaskResponse = {
  ...task,
  id: 'PM-LONG',
  title:
    'A long task title that demonstrates stable wrapping without moving the surrounding actions',
  milestone: 'long',
  description: Array.from(
    { length: 12 },
    (_, index) =>
      `### Section ${index + 1}\n\nRealistic long-form task content remains contained and readable.`,
  ).join('\n\n'),
};
const emptyTask: TaskResponse = { ...task, id: 'PM-EMPTY', description: '' };

@Injectable()
class TaskWorkspaceStoryBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    if (request.url === '/api/v1/settings')
      return of(new HttpResponse({ status: 200, body: settings }));
    if (request.method === 'POST')
      return of(new HttpResponse({ status: 201, body: { ...task, id: 'PM-0063' } }));
    const responseTask = request.url.endsWith('PM-LONG')
      ? longTask
      : request.url.endsWith('PM-EMPTY')
        ? emptyTask
        : task;
    return of(
      new HttpResponse({
        status: 200,
        body:
          request.method === 'PUT'
            ? { ...responseTask, ...(request.body as object) }
            : responseTask,
        headers: undefined,
      }),
    );
  }
}

@Component({ template: '' })
class StoryRoute {}

const meta = {
  title: 'Tasks/Shared workspace',
  component: TaskWorkspace,
  decorators: [
    moduleMetadata({ imports: [TaskDialogShell] }),
    applicationConfig({
      providers: [
        PollingCoordinator,
        provideHttpClient(),
        provideRouter(
          [
            { path: 'tasks/:taskId', component: StoryRoute },
            { path: 'tasks', component: StoryRoute },
          ],
          withDisabledInitialNavigation(),
        ),
        { provide: HttpBackend, useClass: TaskWorkspaceStoryBackend },
      ],
    }),
  ],
  args: { presentation: 'page', mode: 'detail', taskId: task.id },
  parameters: { layout: 'fullscreen' },
} satisfies Meta<TaskWorkspace>;
export default meta;
type Story = StoryObj<typeof meta>;

export const PageRead: Story = {};

export const DialogRead: Story = {
  render: (args) => ({
    props: args,
    template: `
      <pm-task-dialog-shell dialogTitle="Task workspace" [chrome]="false">
        <pm-task-workspace presentation="dialog" [mode]="mode" [taskId]="taskId" />
      </pm-task-dialog-shell>
    `,
  }),
};

export const ActiveTitle: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const value = await canvas.findByRole('button', { name: 'Edit task title' });
    const before = value.getBoundingClientRect();
    await userEvent.click(value);
    const editor = canvas.getByLabelText('Title');
    const after = editor.getBoundingClientRect();
    await expect(Math.abs(before.width - after.width)).toBeLessThanOrEqual(1);
    await expect(Math.abs(before.height - after.height)).toBeLessThanOrEqual(1);
  },
};

export const ActiveStatus: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Edit task status' }));
    await expect(canvas.getByLabelText('Task status')).toBeVisible();
  },
};

export const ActiveProperties: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    const track = await canvas.findByRole('button', { name: 'Edit task track' });
    const before = track.getBoundingClientRect();
    await userEvent.click(track);
    const editor = canvas.getByLabelText('Track');
    const after = editor.getBoundingClientRect();
    await expect(Math.abs(before.width - after.width)).toBeLessThanOrEqual(1);
    await expect(Math.abs(before.height - after.height)).toBeLessThanOrEqual(1);
  },
};

export const ActiveDescription: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Edit task description' }));
    await expect(
      canvas
        .getAllByLabelText('Description')
        .find((element) => element.closest('.CodeMirror') !== null),
    ).toBeVisible();
  },
};

export const ActiveNoteComposer: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Add task note' }));
    const note = canvas.getByLabelText('Add note');
    await userEvent.type(note, 'A compact progress note.');
    await expect(note).toBeVisible();
    await expect(canvas.getByRole('button', { name: 'Add note', exact: true })).toBeEnabled();
  },
};

export const Dirty: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Edit task title' }));
    await userEvent.type(canvas.getByLabelText('Title'), ' — revised');
    await expect(canvas.getByRole('button', { name: 'Save and close' })).toBeVisible();
  },
};

export const EmptyDescription: Story = { args: { taskId: emptyTask.id } };

export const Creation: Story = { args: { mode: 'create', taskId: null } };
export const LongContent: Story = { args: { taskId: longTask.id } };
export const DarkMode: Story = { globals: { theme: 'dark' } };
export const Mobile: Story = { globals: { viewport: 'mobile' } };
export const MobileLongContent: Story = {
  args: { taskId: longTask.id },
  globals: { viewport: 'mobile' },
};
