import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { TaskSearch } from './task-search';

@Component({ template: '' })
class EmptyRoute {}

const settings = {
  projectName: 'Atlas',
  statuses: [
    { key: 'todo', name: 'To do' },
    { key: 'review', name: 'Review' },
  ],
  tracks: [{ key: 'BUILD', name: 'Build' }],
  milestones: [{ key: 'M1', title: 'First milestone', priority: 'high' }],
  priorityOptions: ['none'],
  revision: 'r1',
};

describe('TaskSearch', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TaskSearch],
      providers: [
        provideRouter([
          { path: 'tasks', component: EmptyRoute },
          { path: 'tasks/:taskId', component: EmptyRoute },
          { path: 'tasks/dialog/:taskId', component: EmptyRoute },
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

  function render() {
    const fixture = TestBed.createComponent(TaskSearch);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/settings').flush(settings);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function enter(element: HTMLElement, value: string): HTMLInputElement {
    const input = element.querySelector('input')!;
    input.value = value;
    input.setSelectionRange(value.length, value.length);
    input.dispatchEvent(new Event('input'));
    return input;
  }

  async function debounce(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, 275));
  }

  it('shows field patterns and inserts configured value suggestions', async () => {
    const { fixture, element } = render();
    const input = element.querySelector('input')!;
    input.focus();
    input.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    expect(
      [...element.querySelectorAll('[role="option"]')].map((option) => option.textContent),
    ).toEqual(
      expect.arrayContaining([
        expect.stringContaining('in:'),
        expect.stringContaining('state:'),
        expect.stringContaining('milestone:'),
      ]),
    );

    [...element.querySelectorAll<HTMLButtonElement>('[role="option"]')]
      .find((option) => option.textContent?.includes('state:'))!
      .click();
    await Promise.resolve();
    fixture.detectChanges();
    expect(input.value).toBe('state:');
    expect(element.textContent).toContain('todo');
    expect(element.textContent).toContain('To do');

    enter(element, 'milestone:M');
    fixture.detectChanges();
    expect(element.textContent).toContain('First milestone');

    enter(element, 'in:');
    fixture.detectChanges();
    expect(element.textContent).toContain('selection');
    expect(element.textContent).toContain('Whole project');
  });

  it('debounces searches, forwards sidebar scope but not legacy state, and cancels stale requests', async () => {
    const { fixture, element } = render();
    await TestBed.inject(Router).navigateByUrl('/tasks?track=BUILD&milestone=M1&state=todo');
    const input = enter(element, 'first');
    input.focus();
    await debounce();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne((request) => request.url === '/api/v1/tasks/search');
    expect(first.request.params.get('track')).toBe('BUILD');
    expect(first.request.params.get('milestone')).toBe('M1');
    expect(first.request.params.has('state')).toBe(false);
    expect(first.request.params.has('includeDelivered')).toBe(false);

    enter(element, 'second');
    await debounce();
    expect(first.cancelled).toBe(true);
    http.expectOne((request) => request.params.get('query') === 'second').flush([]);
    fixture.detectChanges();
    expect(element.textContent).toContain('No matching tasks.');
  });

  it('sends explicit selection and all predicates from scoped and whole-project routes', async () => {
    const { element } = render();
    const router = TestBed.inject(Router);
    const http = TestBed.inject(HttpTestingController);
    await router.navigateByUrl('/tasks?milestone=M1');
    enter(element, 'in:selection state:todo');
    await debounce();
    const selected = http.expectOne((request) => request.url === '/api/v1/tasks/search');
    expect(selected.request.params.get('query')).toBe('in:selection state:todo');
    expect(selected.request.params.get('milestone')).toBe('M1');
    selected.flush([]);

    await router.navigateByUrl('/tasks');
    enter(element, 'in:all');
    await debounce();
    const all = http.expectOne((request) => request.url === '/api/v1/tasks/search');
    expect(all.request.params.get('query')).toBe('in:all');
    expect(all.request.params.has('track')).toBe(false);
    expect(all.request.params.has('milestone')).toBe(false);
    all.flush([]);
  });

  it('refreshes an open whole-project search when delivered visibility changes', async () => {
    const { fixture, element } = render();
    const router = TestBed.inject(Router);
    const http = TestBed.inject(HttpTestingController);
    await router.navigateByUrl('/tasks');
    enter(element, 'in:all');
    await debounce();
    const hidden = http.expectOne((request) => request.url === '/api/v1/tasks/search');
    expect(hidden.request.params.has('includeDelivered')).toBe(false);
    hidden.flush([]);

    await router.navigateByUrl('/tasks?includeDelivered=true');
    fixture.detectChanges();
    await debounce();
    const included = http.expectOne((request) => request.url === '/api/v1/tasks/search');
    expect(included.request.params.get('query')).toBe('in:all');
    expect(included.request.params.get('includeDelivered')).toBe('true');
    included.flush([]);
  });

  it('supports keyboard selection and opens a result without dropping query parameters', async () => {
    const { fixture, element } = render();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/tasks?track=BUILD&state=todo&view=dense');
    const input = enter(element, 'render');
    input.focus();
    await debounce();
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.params.get('query') === 'render')
      .flush([
        {
          id: 'BUILD-0001',
          title: 'Render task',
          state: 'todo',
          track: 'BUILD',
          milestone: null,
          matchCount: 2,
          snippet: 'Title: Render task',
        },
      ]);
    fixture.detectChanges();
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(router.url).toBe('/tasks/dialog/BUILD-0001?track=BUILD&state=todo&view=dense');
    expect(input.value).toBe('');
  });

  it('renders compact loading and error states and closes with Escape', async () => {
    const { fixture, element } = render();
    const input = enter(element, 'render');
    input.focus();
    await debounce();
    const request = TestBed.inject(HttpTestingController).expectOne(
      '/api/v1/tasks/search?query=render&limit=20',
    );
    fixture.detectChanges();
    expect(element.textContent).toContain('Searching…');
    request.flush({ detail: 'Search syntax failed.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(element.querySelector('[role="alert"]')?.textContent).toContain('Search syntax failed.');
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(input.getAttribute('aria-expanded')).toBe('false');
  });
});
