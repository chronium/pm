import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PollingCoordinator } from '../../core/polling-coordinator';
import { TaskSidebarStore } from './task-sidebar.store';

describe('TaskSidebarStore', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        TaskSidebarStore,
        PollingCoordinator,
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }),
  );

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('requests a scoped ready recommendation and returns it to the sidebar', async () => {
    const store = TestBed.inject(TaskSidebarStore);
    const http = TestBed.inject(HttpTestingController);

    const pending = store.recommend('BUILD', 'm1');
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/tasks/next');
    expect(request.request.params.get('track')).toBe('BUILD');
    expect(request.request.params.get('milestone')).toBe('m1');
    expect(request.request.params.get('readyOnly')).toBe('true');
    request.flush({
      found: true,
      task: { id: 'BUILD-0001' },
      reason: 'Selected scoped task.',
    });

    expect((await pending)?.task?.id).toBe('BUILD-0001');
    expect(store.recommendationPending()).toBe(false);
  });

  it('keeps no-result and API failure feedback inline', async () => {
    const store = TestBed.inject(TaskSidebarStore);
    const http = TestBed.inject(HttpTestingController);

    const empty = store.recommend(null, null);
    http
      .expectOne((candidate) => candidate.url === '/api/v1/tasks/next')
      .flush({
        found: false,
        task: null,
        reason: 'No dependency-ready actionable task found.',
      });
    await empty;
    expect(store.recommendationMessage()).toBe('No dependency-ready actionable task found.');

    const failed = store.recommend(null, null);
    http
      .expectOne((candidate) => candidate.url === '/api/v1/tasks/next')
      .flush(
        { title: 'Failed', detail: 'Recommendation unavailable.', errorCode: 'failed' },
        { status: 503, statusText: 'Unavailable' },
      );
    await failed;
    expect(store.recommendationError()).toBe('Recommendation unavailable.');
  });
});
