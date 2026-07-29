import { HttpBackend, HttpRequest, HttpResponse, provideHttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { of, throwError } from 'rxjs';

import { runnerRegistration, runnerStatus } from '../agent-runs/agent-runs.fixtures';
import { AgentRunners } from './agent-runners';

@Injectable()
class OnlineRunnerBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    if (request.url === '/api/v1/runners')
      return of(new HttpResponse({ status: 200, body: [runnerRegistration] }));
    if (request.url.endsWith('/status'))
      return of(new HttpResponse({ status: 200, body: runnerStatus }));
    return of(new HttpResponse({ status: 204 }));
  }
}

@Injectable()
class EmptyRunnerBackend extends HttpBackend {
  handle() {
    return of(new HttpResponse({ status: 200, body: [] }));
  }
}

@Injectable()
class OfflineRunnerBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    if (request.url === '/api/v1/runners')
      return of(new HttpResponse({ status: 200, body: [runnerRegistration] }));
    return throwError(
      () =>
        new Error(
          'Runner status is unavailable while the execution host is disconnected from Tailscale.',
        ),
    );
  }
}

const meta = {
  title: 'Settings/Agent runners',
  component: AgentRunners,
  decorators: [
    applicationConfig({
      providers: [provideHttpClient(), { provide: HttpBackend, useClass: OnlineRunnerBackend }],
    }),
  ],
} satisfies Meta<AgentRunners>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Online: Story = {};
export const DarkMode: Story = { globals: { theme: 'dark' } };
export const Mobile: Story = { globals: { viewport: 'mobile' } };
export const Empty: Story = {
  decorators: [
    applicationConfig({
      providers: [{ provide: HttpBackend, useClass: EmptyRunnerBackend }],
    }),
  ],
};
export const Offline: Story = {
  decorators: [
    applicationConfig({
      providers: [{ provide: HttpBackend, useClass: OfflineRunnerBackend }],
    }),
  ],
};
