import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, RouterOutlet } from '@angular/router';

import { TasksBoard } from './tasks-board';
import type { BoardResponse } from './tasks-board.store';

@Component({ imports: [RouterOutlet], template: '<router-outlet />' })
class RouterHost {}

@Component({ template: '' })
class NestedTaskHost {}

const boardResponse: BoardResponse = {
  projectName: 'Atlas Project',
  filters: { track: null, milestone: null, state: null },
  tracks: [
    { key: 'PM', name: 'Product', priority: 'medium' },
    { key: 'BUILD', name: 'Build', priority: 'high' },
  ],
  milestones: [
    { key: 'first', name: 'First milestone', priority: 'high' },
    { key: 'second', name: 'Second milestone', priority: 'medium' },
  ],
  states: [
    { key: 'todo', name: 'To do', priority: 'medium' },
    { key: 'review', name: 'Review', priority: 'medium' },
    { key: 'done', name: 'Done', priority: 'low' },
  ],
  milestoneGroups: [
    {
      key: 'second',
      name: 'Second milestone',
      states: [
        {
          key: 'todo',
          name: 'To do',
          tasks: [
            {
              id: 'PM-0002',
              title:
                'A deliberately long task title that remains available to assistive technology and wraps on narrow screens',
              track: 'BUILD',
              milestone: 'second',
              priority: 'high',
              prioritySource: 'milestone',
              state: 'todo',
              dependencies: {
                ready: false,
                dependsOn: ['PM-0001'],
                waitingOn: ['PM-0001'],
                missing: [],
                summary: 'waiting on PM-0001',
              },
              descriptionPreview:
                'A useful and intentionally long description preview for dense board scanning.',
              modifiedAt: '2026-07-14T12:00:00Z',
            },
          ],
        },
        { key: 'review', name: 'Review', tasks: [] },
        {
          key: 'done',
          name: 'Done',
          tasks: [
            {
              id: 'PM-0001',
              title: 'Completed dependency',
              track: 'PM',
              milestone: 'second',
              priority: 'low',
              prioritySource: 'task',
              state: 'done',
              dependencies: {
                ready: true,
                dependsOn: [],
                waitingOn: [],
                missing: [],
                summary: 'ready',
              },
              descriptionPreview: '',
              modifiedAt: '2026-07-13T12:00:00Z',
            },
          ],
        },
      ],
    },
    {
      key: 'first',
      name: 'First milestone',
      states: [
        { key: 'todo', name: 'To do', tasks: [] },
        { key: 'review', name: 'Review', tasks: [] },
        { key: 'done', name: 'Done', tasks: [] },
      ],
    },
  ],
  revision: 'board-1',
};

describe('TasksBoard', () => {
  beforeEach(async () => {
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [RouterHost],
      providers: [
        provideRouter([
          {
            path: 'tasks',
            component: TasksBoard,
            children: [{ path: ':taskId', component: NestedTaskHost }],
          },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  async function render(url = '/tasks', response = boardResponse) {
    const fixture = TestBed.createComponent(RouterHost);
    const router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/v1/board')
      .flush(response);
    await fixture.whenStable();
    fixture.detectChanges();
    return { element: fixture.nativeElement as HTMLElement, fixture, router };
  }

  it('renders server milestone/status order, counts, and hides empty groups and sections', async () => {
    const { element } = await render();
    expect(
      [...element.querySelectorAll('.milestone-section h2')].map((item) => item.textContent),
    ).toEqual(['Second milestone']);
    expect(
      [...element.querySelectorAll('.status-group summary')].map((item) =>
        item.textContent?.trim().replace(/\s+/g, ' '),
      ),
    ).toEqual(['To do1', 'Done1']);
    expect(
      [...element.querySelectorAll('.status-group summary')].some((item) =>
        item.textContent?.includes('Review'),
      ),
    ).toBe(false);
    expect(
      [...element.querySelectorAll('.milestone-section h2')].some((item) =>
        item.textContent?.includes('First milestone'),
      ),
    ).toBe(false);
  });

  it('uses native collapse controls, defaults done closed, and restores project-scoped session choices', async () => {
    sessionStorage.setItem('pm.tasks-board.v1.Atlas%20Project.second.todo.open', 'false');
    const { element } = await render();
    const groups = [...element.querySelectorAll<HTMLDetailsElement>('details.status-group')];
    expect(groups[0]?.open).toBe(false);
    expect(groups[1]?.open).toBe(false);
    expect(groups.every((group) => group.querySelector('summary') !== null)).toBe(true);

    groups[1]!.open = true;
    groups[1]!.dispatchEvent(new Event('toggle'));
    expect(sessionStorage.getItem('pm.tasks-board.v1.Atlas%20Project.second.done.open')).toBe(
      'true',
    );
  });

  it('renders semantic task links with textual priority and dependency meaning and long content', async () => {
    const { element } = await render();
    const link = element.querySelector<HTMLAnchorElement>('a[href="/tasks/PM-0002"]');
    expect(link).toBeTruthy();
    expect(link?.textContent).toContain('Priority: high');
    expect(link?.textContent).toContain('Blocked');
    expect(link?.textContent).not.toContain('waiting on PM-0001');
    expect(link?.textContent).toContain('A deliberately long task title');
    expect(link?.textContent).toContain('A useful and intentionally long description preview');
    link?.focus();
    expect(document.activeElement).toBe(link);
    expect(element.querySelector('form[aria-label="Board filters"]')).toBeNull();
  });

  it('shows one board-level empty state and offers clear filters', async () => {
    const response = { ...boardResponse, milestoneGroups: [] };
    const { element } = await render('/tasks?track=PM', response);
    expect(element.querySelectorAll('pm-empty-state')).toHaveLength(1);
    expect(element.textContent).toContain('No tasks match these filters');
    expect(
      [...element.querySelectorAll('button')].some((button) =>
        button.textContent?.includes('View whole project'),
      ),
    ).toBe(true);
  });

  it('shows initial loading and readable errors with retry and clear-filter actions', async () => {
    const fixture = TestBed.createComponent(RouterHost);
    await TestBed.inject(Router).navigateByUrl('/tasks?state=archived');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Loading task board');

    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/v1/board')
      .flush(
        {
          title: 'Invalid status',
          detail: 'State archived not found.',
          errorCode: 'invalid_state',
        },
        { status: 400, statusText: 'Bad Request' },
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('State archived not found.');
    expect(
      [...fixture.nativeElement.querySelectorAll('button')].map((button: HTMLButtonElement) =>
        button.textContent?.trim(),
      ),
    ).toEqual(['Retry', 'Clear status']);
  });

  it('keeps the board mounted, preserves filters, and visibly selects a nested task route', async () => {
    const { element, router } = await render('/tasks/PM-0002?track=BUILD');
    expect(router.url).toBe('/tasks/PM-0002?track=BUILD');
    expect(
      element.querySelector('.task-list li.selected a[aria-current="true"]')?.textContent,
    ).toContain('PM-0002');
    expect(element.querySelector('h1.visually-hidden')?.textContent).toBe('Tasks');
    expect(element.querySelector('form[aria-label="Board filters"]')).toBeNull();
  });
});
