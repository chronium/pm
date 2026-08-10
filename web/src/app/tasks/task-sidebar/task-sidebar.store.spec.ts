import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { PollingCoordinator } from '../../core/polling-coordinator';
import { TaskSidebarStore } from './task-sidebar.store';

@Component({ template: '' })
class EmptyRoute {}

describe('TaskSidebarStore', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        TaskSidebarStore,
        PollingCoordinator,
        provideRouter([{ path: 'tasks', component: EmptyRoute }]),
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

  it('forwards delivered visibility to navigation reads and polling', async () => {
    vi.useFakeTimers();
    try {
      await TestBed.inject(Router).navigateByUrl('/tasks?includeDelivered=true');
      const store = TestBed.inject(TaskSidebarStore);
      const http = TestBed.inject(HttpTestingController);
      await TestBed.tick();
      const initial = http.expectOne((candidate) => candidate.url === '/api/v1/board/navigation');
      expect(initial.request.params.get('includeDelivered')).toBe('true');
      initial.flush({
        remainingCount: 1,
        activationEligibleCount: 0,
        tracks: [],
        milestones: [],
        revision: 'navigation-r1',
      });
      await TestBed.tick();

      store.pollStatus.start(true);
      vi.advanceTimersByTime(0);
      const poll = http.expectOne((candidate) => candidate.url === '/api/v1/board/navigation');
      expect(poll.request.params.get('includeDelivered')).toBe('true');
      poll.flush(null, { status: 304, statusText: 'Not Modified' });
      store.pollStatus.stop();
    } finally {
      vi.useRealTimers();
    }
  });
});
