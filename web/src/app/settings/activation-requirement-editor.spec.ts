import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { provideRouter, Router } from '@angular/router';

import type { ActivationRequirementRequest } from './activation-api.service';
import { ActivationRequirementEditor } from './activation-requirement-editor';

@Component({ template: '' })
class RouteTarget {}

describe('ActivationRequirementEditor', () => {
  beforeEach(async () =>
    TestBed.configureTestingModule({
      imports: [ActivationRequirementEditor],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'tasks/settings', component: RouteTarget },
          { path: 'projects/:projectId/tasks/settings', component: RouteTarget },
        ]),
      ],
    }).compileComponents(),
  );
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('selects and removes milestones while excluding existing requirements', () => {
    const fixture = TestBed.createComponent(ActivationRequirementEditor);
    fixture.componentRef.setInput('requirements', [
      { kind: 'milestone', source: 'current' },
    ] satisfies ActivationRequirementRequest[]);
    fixture.componentRef.setInput('milestones', [
      { key: 'current', title: 'Current release' },
      { key: 'later', title: 'Later release' },
    ]);
    const changes: ActivationRequirementRequest[][] = [];
    fixture.componentInstance.requirementsChange.subscribe((value) => changes.push(value));
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const kind = element.querySelector('select') as HTMLSelectElement;
    kind.value = 'milestone';
    kind.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    const search = element.querySelector('input[type="search"]') as HTMLInputElement;
    search.focus();
    search.dispatchEvent(new Event('focus'));
    fixture.detectChanges();
    const options = [...element.querySelectorAll<HTMLElement>('[role="option"]')].map(
      (option) => option.textContent,
    );
    expect(options.some((option) => option?.includes('Current release'))).toBe(false);
    expect(options.some((option) => option?.includes('Later release'))).toBe(true);
    (element.querySelector('[role="option"]') as HTMLButtonElement).click();
    expect(changes.at(-1)).toEqual([
      { kind: 'milestone', source: 'current' },
      { kind: 'milestone', source: 'later' },
    ]);

    (element.querySelector('.remove-requirement') as HTMLButtonElement).click();
    expect(changes.at(-1)).toEqual([]);
  });

  it('searches tasks and excludes a task that is already selected', async () => {
    vi.useFakeTimers();
    try {
      const fixture = TestBed.createComponent(ActivationRequirementEditor);
      fixture.componentRef.setInput('requirements', [
        { kind: 'task', source: 'PM-0001' },
      ] satisfies ActivationRequirementRequest[]);
      const changes: ActivationRequirementRequest[][] = [];
      fixture.componentInstance.requirementsChange.subscribe((value) => changes.push(value));
      fixture.detectChanges();
      const element = fixture.nativeElement as HTMLElement;
      const search = element.querySelector('input[type="search"]') as HTMLInputElement;
      search.value = 'foundation';
      search.dispatchEvent(new Event('input'));
      await vi.advanceTimersByTimeAsync(250);
      const request = TestBed.inject(HttpTestingController).expectOne(
        (candidate) =>
          candidate.url === '/api/v1/tasks/search' &&
          candidate.params.get('query') === 'foundation' &&
          candidate.params.get('limit') === '20',
      );
      request.flush([
        {
          id: 'PM-0001',
          title: 'Existing foundation task',
          state: 'todo',
          track: 'PM',
          milestone: 'current',
          matchCount: 1,
          snippet: '',
        },
        {
          id: 'PM-0002',
          title: 'Selectable foundation task',
          state: 'todo',
          track: 'PM',
          milestone: 'current',
          matchCount: 1,
          snippet: '',
        },
      ]);
      search.focus();
      search.dispatchEvent(new Event('focus'));
      fixture.detectChanges();
      expect(element.textContent).not.toContain('Existing foundation task');
      expect(element.textContent).toContain('Selectable foundation task');
      (element.querySelector('[role="option"]') as HTMLButtonElement).click();
      expect(changes.at(-1)).toEqual([
        { kind: 'task', source: 'PM-0001' },
        { kind: 'task', source: 'PM-0002' },
      ]);
    } finally {
      vi.useRealTimers();
    }
  });

  it('opens milestone choices after selecting a task without moving focus', async () => {
    vi.useFakeTimers();
    try {
      const fixture = TestBed.createComponent(ActivationRequirementEditor);
      fixture.componentRef.setInput('requirements', [] satisfies ActivationRequirementRequest[]);
      fixture.componentRef.setInput('milestones', [{ key: 'later', title: 'Later release' }]);
      fixture.detectChanges();
      const element = fixture.nativeElement as HTMLElement;
      const search = element.querySelector('input[type="search"]') as HTMLInputElement;
      search.focus();
      search.value = 'foundation';
      search.dispatchEvent(new Event('input'));
      await vi.advanceTimersByTimeAsync(250);
      TestBed.inject(HttpTestingController)
        .expectOne((candidate) => candidate.url === '/api/v1/tasks/search')
        .flush([
          {
            id: 'PM-0002',
            title: 'Foundation task',
            state: 'todo',
            track: 'PM',
            milestone: 'current',
            matchCount: 1,
            snippet: '',
          },
        ]);
      fixture.detectChanges();
      (element.querySelector('[role="option"]') as HTMLButtonElement).click();
      await vi.runAllTimersAsync();
      fixture.detectChanges();

      const kind = element.querySelector('select') as HTMLSelectElement;
      kind.value = 'milestone';
      kind.dispatchEvent(new Event('change'));
      fixture.detectChanges();

      expect(element.querySelector('[role="listbox"]')?.textContent).toContain('Later release');
    } finally {
      vi.useRealTimers();
    }
  });

  it('searches tasks in the selected linked project', async () => {
    vi.useFakeTimers();
    try {
      await TestBed.inject(Router).navigateByUrl('/projects/child/tasks/settings');
      const fixture = TestBed.createComponent(ActivationRequirementEditor);
      fixture.componentRef.setInput('requirements', [] satisfies ActivationRequirementRequest[]);
      fixture.detectChanges();
      const search = fixture.nativeElement.querySelector(
        'input[type="search"]',
      ) as HTMLInputElement;
      search.value = 'foundation';
      search.dispatchEvent(new Event('input'));
      await vi.advanceTimersByTimeAsync(250);

      TestBed.inject(HttpTestingController)
        .expectOne(
          (candidate) =>
            candidate.url === '/api/v1/projects/child/tasks/search' &&
            candidate.params.get('query') === 'foundation',
        )
        .flush([]);
    } finally {
      vi.useRealTimers();
    }
  });
});
