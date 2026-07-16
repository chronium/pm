import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { TaskApiService } from './task-api.service';

const task = {
  id: 'PM-0050',
  title: 'Dialog task',
  track: 'PM',
  milestone: 'angular-web',
  priority: 'high',
  prioritySource: 'milestone',
  prioritySelection: 'inherit',
  state: 'todo',
  dependencies: { ready: true, dependsOn: [], waitingOn: [], missing: [], summary: 'ready' },
  createdAt: '2026-07-15T00:00:00Z',
  modifiedAt: '2026-07-15T00:00:00Z',
  description: '# Body',
  revision: 'revision-1',
  localMetadata: { filePath: '.pm/tasks/PM-0050.md' },
};

describe('TaskApiService', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends typed create payloads and captures response ETags', () => {
    const api = TestBed.inject(TaskApiService);
    let etag = '';
    api
      .create({ title: 'New task', track: 'PM', milestone: null, description: 'Body' })
      .subscribe((response) => (etag = api.etag(response)));
    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/tasks');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-PM-Client')).toBe('angular-web');
    expect(request.request.body).toEqual({
      title: 'New task',
      track: 'PM',
      milestone: null,
      description: 'Body',
    });
    request.flush(task, { status: 201, statusText: 'Created', headers: { ETag: '"revision-1"' } });
    expect(etag).toBe('"revision-1"');
  });

  it('requires the exact current ETag for update, state, and remove mutations', () => {
    const api = TestBed.inject(TaskApiService);
    api
      .update(
        task.id,
        { title: task.title, state: 'review', priority: 'high', description: task.description },
        '"revision-1"',
      )
      .subscribe();
    api.updateState(task.id, { state: 'done' }, '"revision-2"').subscribe();
    api.remove(task.id, '"revision-3"').subscribe();
    const http = TestBed.inject(HttpTestingController);
    const state = http.expectOne('/api/v1/tasks/PM-0050/state');
    const taskRequests = http.match('/api/v1/tasks/PM-0050');
    const update = taskRequests.find((item) => item.request.method === 'PUT')!;
    const remove = taskRequests.find((item) => item.request.method === 'DELETE')!;
    expect(update.request.headers.get('If-Match')).toBe('"revision-1"');
    expect(state.request.headers.get('If-Match')).toBe('"revision-2"');
    expect(remove.request.headers.get('If-Match')).toBe('"revision-3"');
    expect(
      [update, state, remove].every((item) => item.request.headers.get('If-Match') !== '*'),
    ).toBe(true);
    update.flush(task);
    state.flush(task);
    remove.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('maps problem details, conflicts, not-found, and unavailable responses', () => {
    const api = TestBed.inject(TaskApiService);
    for (const [status, expected] of [
      [412, true],
      [404, false],
      [503, false],
    ] as const) {
      const error = new HttpErrorResponse({
        status,
        error: { title: 'Request failed', detail: `Failure ${status}`, errorCode: 'failure' },
      });
      expect(api.error(error, 'Fallback')).toEqual({
        status,
        message: `Failure ${status}`,
        conflict: expected,
      });
    }
    expect(api.error(new HttpErrorResponse({ status: 0 }), 'Fallback').message).toContain(
      'could not be reached',
    );
  });
});
