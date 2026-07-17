import {
  HttpBackend,
  HttpErrorResponse,
  HttpRequest,
  HttpResponse,
  provideHttpClient,
} from '@angular/common/http';
import { Injectable } from '@angular/core';
import { provideRouter } from '@angular/router';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { applicationConfig } from '@storybook/angular-vite';
import { expect, userEvent, within } from 'storybook/test';
import { NEVER, of, throwError } from 'rxjs';

import { TaskSearch } from './task-search';

const settings = {
  projectName: 'Atlas',
  statuses: [
    { key: 'todo', name: 'To do' },
    { key: 'review', name: 'Review' },
  ],
  tracks: [{ key: 'BUILD', name: 'Build and interface' }],
  milestones: [{ key: 'M1', title: 'First milestone', priority: 'high' }],
  priorityOptions: ['none'],
  revision: 'r1',
};

@Injectable()
class SearchStoryBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    if (request.url === '/api/v1/settings')
      return of(new HttpResponse({ status: 200, body: settings }));
    const query = request.params.get('query');
    if (query === 'loading') return NEVER;
    if (query === 'error')
      return throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server error' }));
    if (query === 'empty') return of(new HttpResponse({ status: 200, body: [] }));
    return of(
      new HttpResponse({
        status: 200,
        body: [
          {
            id: 'BUILD-0042',
            title:
              'A deliberately long task title that demonstrates compact wrapping in search results',
            state: 'review',
            track: 'BUILD',
            milestone: 'M1',
            matchCount: 3,
            snippet:
              'Description: Preserve the current board context while opening this matching task.',
          },
        ],
      }),
    );
  }
}

const meta = {
  title: 'Tasks/Task search',
  component: TaskSearch,
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: HttpBackend, useClass: SearchStoryBackend },
      ],
    }),
  ],
  parameters: { layout: 'centered' },
} satisfies Meta<TaskSearch>;
export default meta;
type Story = StoryObj<typeof meta>;

async function search(canvasElement: HTMLElement, query: string) {
  const input = within(canvasElement).getByRole('combobox');
  await userEvent.click(input);
  await userEvent.type(input, query);
  return input;
}

export const Default: Story = {
  play: async ({ canvasElement }) => {
    await userEvent.click(within(canvasElement).getByRole('combobox'));
    await expect(within(canvasElement).getByText('Filter by task state')).toBeVisible();
  },
};
export const Suggestions: Story = {
  play: async ({ canvasElement }) => void (await search(canvasElement, 'state:')),
};
export const ScopeSuggestions: Story = {
  play: async ({ canvasElement }) => void (await search(canvasElement, 'in:')),
};
export const ProjectWide: Story = {
  play: async ({ canvasElement }) => void (await search(canvasElement, 'render in:all')),
};
export const Results: Story = {
  play: async ({ canvasElement }) => void (await search(canvasElement, 'render')),
};
export const Loading: Story = {
  play: async ({ canvasElement }) => void (await search(canvasElement, 'loading')),
};
export const Empty: Story = {
  play: async ({ canvasElement }) => void (await search(canvasElement, 'empty')),
};
export const Error: Story = {
  play: async ({ canvasElement }) => void (await search(canvasElement, 'error')),
};
export const LongContent: Story = Results;
export const KeyboardFocus: Story = {
  play: async ({ canvasElement }) => {
    const input = await search(canvasElement, 'render');
    await within(canvasElement).findByText('BUILD-0042');
    await userEvent.keyboard('{ArrowDown}');
    await expect(input).toHaveAttribute('aria-activedescendant');
  },
};
export const Mobile: Story = { ...Results, globals: { viewport: 'mobile' } };
