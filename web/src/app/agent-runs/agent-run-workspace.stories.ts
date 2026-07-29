import { HttpBackend, HttpRequest, HttpResponse, provideHttpClient } from '@angular/common/http';
import { Component, Injectable } from '@angular/core';
import { provideRouter } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { of } from 'rxjs';

import { AgentRunWorkspace } from './agent-run-workspace';
import { runArtifacts, runEvents, runInspection } from './agent-runs.fixtures';

@Component({ template: '' })
class RunWorkspaceStoryRoute {}

const completedInspection = {
  ...runInspection,
  run: {
    ...runInspection.run,
    state: 'completed' as const,
    terminalAt: '2026-07-29T08:10:00.000Z',
  },
};
const completedEvents = [
  ...runEvents,
  {
    ...runEvents[0]!,
    sequence: 9,
    type: 'run.state_changed',
    state: 'validating' as const,
    summary: 'Validating changes',
  },
  {
    ...runEvents[0]!,
    sequence: 10,
    type: 'run.state_changed',
    state: 'collecting_artifacts' as const,
    summary: 'Collecting artifacts',
  },
  {
    ...runEvents[0]!,
    sequence: 11,
    type: 'run.state_changed',
    state: 'completed' as const,
    summary: 'Run completed',
  },
];

@Injectable()
class RunWorkspaceStoryBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    if (request.url.endsWith('/artifacts'))
      return of(new HttpResponse({ status: 200, body: runArtifacts }));
    if (request.url.endsWith('/events'))
      return of(
        new HttpResponse({
          status: 200,
          body: {
            events: completedEvents,
            nextAfterSequence: 11,
            hasMore: false,
            terminal: false,
          },
        }),
      );
    if (request.url.endsWith('/run-01K123'))
      return of(new HttpResponse({ status: 200, body: completedInspection }));
    return of(new HttpResponse({ status: 404 }));
  }
}

const meta = {
  title: 'Agent runs/Workspace',
  component: AgentRunWorkspace,
  args: { runId: 'run-01K123' },
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([{ path: '**', component: RunWorkspaceStoryRoute }]),
        provideHttpClient(),
        { provide: HttpBackend, useClass: RunWorkspaceStoryBackend },
      ],
    }),
  ],
  parameters: { layout: 'fullscreen' },
} satisfies Meta<AgentRunWorkspace>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Completed: Story = {};
export const Mobile: Story = { globals: { viewport: 'mobile' } };
export const DarkMode: Story = { globals: { theme: 'dark' } };
