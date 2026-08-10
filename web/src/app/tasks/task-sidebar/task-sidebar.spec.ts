import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { TaskSidebar } from './task-sidebar';
import { TaskSidebarStore, type BoardNavigationResponse } from './task-sidebar.store';

@Component({ template: '' })
class EmptyRoute {}

const navigation: BoardNavigationResponse = {
  remainingCount: 2,
  activationEligibleCount: 1,
  tracks: [{ key: 'OPS', name: 'Operations', remainingCount: 1, activationEligibleCount: 0 }],
  milestones: [
    {
      key: 'archive',
      name: 'Archive',
      remainingCount: 1,
      activationEligibleCount: 0,
      lifecycle: 'delivered',
      unmetActivationTriggers: [],
    },
  ],
  revision: 'navigation-r1',
};

function sidebarStore() {
  return {
    navigation: signal(navigation),
    loading: signal(false),
    error: signal<string | null>(null),
    recommendationPending: signal(false),
    recommendationMessage: signal<string | null>(null),
    recommendationError: signal<string | null>(null),
    recommend: async () => null,
    reload: () => true,
  };
}

describe('TaskSidebar', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [TaskSidebar],
      providers: [
        { provide: TaskSidebarStore, useFactory: sidebarStore },
        provideRouter([
          { path: 'tasks', component: EmptyRoute },
          { path: 'projects/:projectId/tasks', component: EmptyRoute },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }),
  );

  afterEach(() => TestBed.resetTestingModule());

  it('uses a pressed button to enable delivered work without dropping filters', async () => {
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/tasks?track=OPS&state=todo&view=dense');
    const fixture = TestBed.createComponent(TaskSidebar);
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('.delivered-toggle') as HTMLButtonElement;

    expect(button.textContent).toContain('Show delivered');
    expect(button.getAttribute('aria-pressed')).toBe('false');
    button.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(router.url).toBe('/tasks?track=OPS&state=todo&view=dense&includeDelivered=true');
    expect(button.getAttribute('aria-pressed')).toBe('true');
  });

  it('returns to the unscoped board when hiding a selected delivered milestone', async () => {
    const router = TestBed.inject(Router);
    await router.navigateByUrl(
      '/tasks?track=OPS&milestone=archive&state=todo&view=dense&includeDelivered=true',
    );
    const fixture = TestBed.createComponent(TaskSidebar);
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('.delivered-toggle') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(router.url).toBe('/tasks?state=todo&view=dense');
  });

  it('keeps the toggle available in a linked read-only task page', async () => {
    await TestBed.inject(Router).navigateByUrl('/projects/child/tasks');
    const fixture = TestBed.createComponent(TaskSidebar);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('.delivered-toggle')).not.toBeNull();
    expect(element.querySelector('.new-task-action')).toBeNull();
    expect(element.querySelector('.next-task-action')).toBeNull();
  });
});
