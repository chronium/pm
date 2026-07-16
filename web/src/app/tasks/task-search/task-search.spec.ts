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
        expect.stringContaining('state:'),
        expect.stringContaining('milestone:'),
      ]),
    );

    (element.querySelector('[role="option"]') as HTMLButtonElement).click();
    await Promise.resolve();
    fixture.detectChanges();
    expect(input.value).toBe('state:');
    expect(element.textContent).toContain('todo');
    expect(element.textContent).toContain('To do');

    enter(element, 'milestone:M');
    fixture.detectChanges();
    expect(element.textContent).toContain('First milestone');
  });

  it('debounces searches, forwards board filters, and cancels stale requests', async () => {
    const { fixture, element } = render();
    await TestBed.inject(Router).navigateByUrl('/tasks?track=BUILD&milestone=M1&state=todo');
    const input = enter(element, 'first');
    input.focus();
    await debounce();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne((request) => request.url === '/api/v1/tasks/search');
    expect(first.request.params.get('track')).toBe('BUILD');
    expect(first.request.params.get('milestone')).toBe('M1');
    expect(first.request.params.get('state')).toBe('todo');

    enter(element, 'second');
    await debounce();
    expect(first.cancelled).toBe(true);
    http.expectOne((request) => request.params.get('query') === 'second').flush([]);
    fixture.detectChanges();
    expect(element.textContent).toContain('No matching tasks.');
  });

  it('supports keyboard selection and opens a result without dropping query parameters', async () => {
    const { fixture, element } = render();
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/tasks?track=BUILD');
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
    expect(router.url).toBe('/tasks/BUILD-0001?track=BUILD');
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
