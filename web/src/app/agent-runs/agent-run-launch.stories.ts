import { HttpBackend, HttpRequest, HttpResponse, provideHttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { provideRouter } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';
import { of } from 'rxjs';

import {
  acceptedRun,
  readyPreflight,
  runnerRegistration,
  runnerStatus,
} from './agent-runs.fixtures';
import { AgentRunLaunch } from './agent-run-launch';

@Injectable()
class LaunchStoryBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    if (request.url === '/api/v1/runners')
      return of(new HttpResponse({ status: 200, body: [runnerRegistration] }));
    if (request.url.endsWith('/status'))
      return of(new HttpResponse({ status: 200, body: runnerStatus }));
    if (request.url === '/api/v1/runs/preflight')
      return of(
        new HttpResponse({
          status: 200,
          body: readyPreflight,
          headers: request.headers.set('ETag', '"draft-r1"'),
        }),
      );
    if (request.url.endsWith('/start'))
      return of(new HttpResponse({ status: 202, body: acceptedRun }));
    return of(new HttpResponse({ status: 404 }));
  }
}

const meta = {
  title: 'Agent runs/Launch',
  component: AgentRunLaunch,
  args: {
    open: true,
    taskId: 'AGENT-0010',
    taskTitle: 'Add Angular runner settings and task launch flow',
  },
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: HttpBackend, useClass: LaunchStoryBackend },
      ],
    }),
  ],
  parameters: { layout: 'fullscreen' },
} satisfies Meta<AgentRunLaunch>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Selection: Story = {};
export const DarkMode: Story = { globals: { theme: 'dark' } };
export const Mobile: Story = { globals: { viewport: 'mobile' } };
export const Ready: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Check readiness' }));
    await expect(await canvas.findByText('Ready to start.')).toBeInTheDocument();
    await expect(canvas.getByRole('button', { name: 'Start run' })).toBeEnabled();
  },
};
export const Accepted: Story = {
  play: async ({ canvasElement }) => {
    const canvas = within(canvasElement);
    await userEvent.click(await canvas.findByRole('button', { name: 'Check readiness' }));
    await userEvent.click(await canvas.findByRole('button', { name: 'Start run' }));
    await expect(await canvas.findByText('Run accepted')).toBeInTheDocument();
  },
};
