import {
  HttpBackend,
  HttpErrorResponse,
  HttpRequest,
  HttpResponse,
  provideHttpClient,
} from '@angular/common/http';
import { Injectable } from '@angular/core';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';
import { NEVER, of, throwError } from 'rxjs';

import type { SettingsResponse } from './settings-api.service';
import { SettingsPage } from './settings-page';

const settings: SettingsResponse = {
  projectName: 'Atlas workspace',
  accent: 'teal',
  statuses: [
    { key: 'todo', name: 'To do' },
    { key: 'review', name: 'Review' },
    { key: 'done', name: 'Done' },
  ],
  milestones: [
    {
      key: 'angular-web-with-a-long-key',
      title: 'Angular web replacement with a long title that demonstrates wrapping',
      priority: 'high',
      description:
        'Deliver the complete local workflow with documented exclusions and acceptance evidence.',
      requiredActivationTriggers: [],
    },
  ],
  activationTriggers: [
    {
      key: 'beta-entry',
      title: 'Beta entry criteria',
      requirements: [
        { kind: 'task', source: 'PM-0089' },
        { kind: 'milestone', source: 'foundation' },
      ],
    },
    { key: 'launch-authorized', title: 'Launch authorized', requirements: [] },
  ],
  tracks: [
    { key: 'PM', name: 'Product management' },
    { key: 'BUILD-AND-INTERFACE', name: 'Build and interface engineering' },
  ],
  priorityOptions: ['none', 'low', 'medium', 'high', 'urgent'],
  revision: 'story-r1',
};

@Injectable()
class SettingsStoryBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    if (request.url === '/api/v1/settings')
      return of(new HttpResponse({ status: 200, body: settings }));
    if (request.url === '/api/v1/validation')
      return of(new HttpResponse({ status: 200, body: { valid: true, issues: [] } }));
    if (request.url === '/api/v1/project/identity')
      return of(new HttpResponse({ status: 200, body: localIdentity }));
    if (request.url === '/api/v1/project/members')
      return of(new HttpResponse({ status: 200, body: projectMembers }));
    if (request.url === '/api/v1/project/invitations')
      return of(new HttpResponse({ status: 200, body: { invitations: [] } }));
    if (request.url === '/api/v1/runners') return of(new HttpResponse({ status: 200, body: [] }));
    if ((request.body as { key?: string } | null)?.key === 'pending') return NEVER;
    if ((request.body as { key?: string } | null)?.key === 'duplicate') {
      return throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: {
              title: 'Already exists',
              detail: 'That key is already configured.',
              errorCode: 'duplicate_key',
            },
          }),
      );
    }
    return of(new HttpResponse({ status: 200, body: { ...settings, revision: 'story-r2' } }));
  }
}

const localIdentity = {
  userId: 'usr_story_local',
  displayName: 'Story admin',
  publicKey: 'story-public-key',
  fingerprint: 'ab'.repeat(32),
};

const projectMembers = {
  projectId: 'prj_story',
  currentUserId: localIdentity.userId,
  currentRole: 'admin',
  authenticated: true,
  members: [{ ...localIdentity, role: 'admin', isLocal: true }],
};

const meta = {
  title: 'Settings/Workspace',
  component: SettingsPage,
  decorators: [
    applicationConfig({
      providers: [provideHttpClient(), { provide: HttpBackend, useClass: SettingsStoryBackend }],
    }),
  ],
  parameters: { layout: 'fullscreen' },
} satisfies Meta<SettingsPage>;
export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
export const Create: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Statuses' }));
    await userEvent.click(await canvas.findByRole('button', { name: 'Add status' }));
    await expect(canvas.getByLabelText('Key')).toBeVisible();
  },
};
export const Edit: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Milestones' }));
    await userEvent.click(await canvas.findByRole('button', { name: 'Edit deliverable' }));
    await userEvent.click(await canvas.findByRole('button', { name: 'Edit milestone title' }));
    await expect(canvas.getByLabelText('Milestone title')).toHaveValue(
      settings.milestones[0]!.title,
    );
  },
};
export const Pending: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Statuses' }));
    await userEvent.click(await canvas.findByRole('button', { name: 'Add status' }));
    await userEvent.type(canvas.getByLabelText('Key'), 'pending');
    await userEvent.type(canvas.getByLabelText('Name'), 'Pending state');
    await userEvent.click(canvas.getAllByRole('button', { name: 'Add status' })[1]!);
    await expect(canvas.getByRole('button', { name: 'Adding…' })).toBeDisabled();
  },
};
export const Error: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Statuses' }));
    await userEvent.click(await canvas.findByRole('button', { name: 'Add status' }));
    await userEvent.type(canvas.getByLabelText('Key'), 'duplicate');
    await userEvent.type(canvas.getByLabelText('Name'), 'Duplicate state');
    await userEvent.click(canvas.getAllByRole('button', { name: 'Add status' })[1]!);
    await expect(await canvas.findByRole('alert')).toHaveTextContent('already configured');
  },
};
export const LongContentMobile: Story = { globals: { viewport: 'mobile' } };
