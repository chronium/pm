import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import type { BoardResponse } from './tasks-board.store';
import { TasksBoardStore } from './tasks-board.store';

const emptyBoard: BoardResponse = {
  projectName: 'Atlas',
  filters: { track: null, milestone: null, state: null },
  tracks: [{ key: 'PM', name: 'Product', priority: 'medium' }],
  milestones: [{ key: 'm1', name: 'Milestone One', priority: 'high' }],
  states: [{ key: 'todo', name: 'To do', priority: 'medium' }],
  milestoneGroups: [],
  revision: 'board-revision',
};

describe('TasksBoardStore', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        TasksBoardStore,
        provideRouter([{ path: 'tasks', children: [{ path: ':taskId', children: [] }] }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }),
  );

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  async function createAt(url: string) {
    const router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    const store = TestBed.inject(TasksBoardStore);
    TestBed.tick();
    return { router, store, http: TestBed.inject(HttpTestingController) };
  }

  it('serializes typed URL filters into the board request', async () => {
    const { store, http } = await createAt('/tasks?track=PM&milestone=m1&state=todo');
    const request = http.expectOne((candidate) => candidate.url === '/api/v1/board');

    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('track')).toBe('PM');
    expect(request.request.params.get('milestone')).toBe('m1');
    expect(request.request.params.get('state')).toBe('todo');
    request.flush({ ...emptyBoard, filters: { track: 'PM', milestone: 'm1', state: 'todo' } });
    await TestBed.tick();

    expect(store.revision()).toBe('board-revision');
  });

  it('writes filter changes to history, preserves selected routes, and clears only board filters', async () => {
    const { router, store, http } = await createAt('/tasks/PM-0049?track=PM&view=dense');
    http.expectOne((candidate) => candidate.url === '/api/v1/board').flush(emptyBoard);
    await TestBed.tick();

    await store.setFilter('state', 'todo');
    expect(router.url).toBe('/tasks/PM-0049?track=PM&view=dense&state=todo');
    TestBed.tick();
    http.expectOne((candidate) => candidate.params.get('state') === 'todo').flush(emptyBoard);
    await TestBed.tick();

    await store.clearFilters();
    expect(router.url).toBe('/tasks/PM-0049?view=dense');
    TestBed.tick();
    http.expectOne((candidate) => candidate.url === '/api/v1/board').flush(emptyBoard);
    await TestBed.tick();
  });

  it('synchronizes filters when browser history navigation changes the URL', async () => {
    const { router, store, http } = await createAt('/tasks?track=PM');
    http.expectOne((candidate) => candidate.params.get('track') === 'PM').flush(emptyBoard);
    await TestBed.tick();

    await router.navigateByUrl('/tasks?state=todo');
    TestBed.tick();
    const request = http.expectOne((candidate) => candidate.params.get('state') === 'todo');
    expect(request.request.params.has('track')).toBe(false);
    request.flush(emptyBoard);
    await TestBed.tick();

    expect(store.filters()).toEqual({ state: 'todo' });
  });

  it('retains loaded content during reload and exposes readable API errors and retry', async () => {
    const { store, http } = await createAt('/tasks');
    http.expectOne('/api/v1/board').flush(emptyBoard);
    await TestBed.tick();

    expect(store.reload()).toBe(true);
    await TestBed.tick();
    expect(store.refreshing()).toBe(true);
    expect(store.board()?.projectName).toBe('Atlas');
    http.expectOne('/api/v1/board').flush(
      {
        title: 'Invalid filter',
        detail: 'State archived not found.',
        errorCode: 'invalid_state',
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await TestBed.tick();

    expect(store.error()).toBe('State archived not found.');
    expect(store.board()?.projectName).toBe('Atlas');
    expect(store.reload()).toBe(true);
    await TestBed.tick();
    http.expectOne('/api/v1/board').flush(emptyBoard);
    await TestBed.tick();
    expect(store.error()).toBeNull();
  });

  it('exposes group open-state data and persists typed collapse intent', async () => {
    sessionStorage.clear();
    const { store, http } = await createAt('/tasks');
    const milestone = {
      key: 'm1',
      name: 'Milestone One',
      states: [
        { key: 'todo', name: 'To do', tasks: [] },
        { key: 'done', name: 'Done', tasks: [] },
      ],
    };
    http.expectOne('/api/v1/board').flush({ ...emptyBoard, milestoneGroups: [milestone] });
    await TestBed.tick();

    expect(store.groupOpenStates(milestone)).toEqual({ todo: true, done: false });
    store.rememberGroupOpen({ milestone, state: milestone.states[1]!, open: true });
    expect(store.groupOpenStates(milestone)).toEqual({ todo: true, done: true });
  });
});
