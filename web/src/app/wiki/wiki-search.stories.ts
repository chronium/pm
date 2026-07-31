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
import { userEvent, within } from 'storybook/test';
import { NEVER, of, throwError } from 'rxjs';

import { WikiSearch } from './wiki-search';

@Injectable()
class WikiSearchStoryBackend extends HttpBackend {
  handle(request: HttpRequest<unknown>) {
    if (request.url === '/api/v1/project/links') {
      return of(
        new HttpResponse({
          status: 200,
          body: {
            activeProjectId: 'storybook',
            members: [
              {
                projectId: 'storybook',
                name: 'Storybook project',
                alias: null,
                relationship: 'current',
                status: 'resolved',
                source: 'current',
                readable: true,
                writeTrusted: true,
              },
            ],
            warnings: [],
          },
        }),
      );
    }
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
            path: 'guides/rendering/canvas',
            title: 'Canvas rendering guide',
            modifiedAt: '2026-07-16T10:00:00Z',
            matchCount: 3,
            snippet: 'The rendering pipeline prepares canvas commands before presenting the frame.',
          },
        ],
      }),
    );
  }
}

const meta = {
  title: 'Wiki/Wiki search',
  component: WikiSearch,
  decorators: [
    applicationConfig({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: HttpBackend, useClass: WikiSearchStoryBackend },
      ],
    }),
  ],
  parameters: { layout: 'centered' },
} satisfies Meta<WikiSearch>;
export default meta;
type Story = StoryObj<typeof meta>;

async function search(canvasElement: HTMLElement, query: string) {
  const input = within(canvasElement).getByRole('combobox');
  await userEvent.click(input);
  await userEvent.type(input, query);
}

export const Results: Story = {
  play: async ({ canvasElement }) => search(canvasElement, 'render'),
};
export const Loading: Story = {
  play: async ({ canvasElement }) => search(canvasElement, 'loading'),
};
export const Empty: Story = { play: async ({ canvasElement }) => search(canvasElement, 'empty') };
export const Error: Story = { play: async ({ canvasElement }) => search(canvasElement, 'error') };
export const Mobile: Story = { ...Results, globals: { viewport: 'mobile' } };
