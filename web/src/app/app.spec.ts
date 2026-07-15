import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { App } from './app';
import { AppShell, routes } from './app.routes';
import { LayoutService } from './core/layout.service';
import { TaskNavigationService } from './tasks/task-navigation.service';

describe('application shell', () => {
  beforeEach(async () => {
    document.documentElement.removeAttribute('data-theme');
    document.documentElement.removeAttribute('data-theme-preference');
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  afterEach(() => TestBed.resetTestingModule());

  async function renderAt(url: string, projectName = 'Test Project') {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/project').flush({
      name: projectName,
      revision: 'project-revision',
    });
    const boardRequest = TestBed.inject(HttpTestingController).match(
      (request) => request.url === '/api/v1/board',
    );
    for (const request of boardRequest) {
      request.flush({
        projectName,
        filters: { track: null, milestone: null, state: null },
        tracks: [],
        milestones: [],
        states: [],
        milestoneGroups: [],
        revision: 'board-revision',
      });
    }
    const taskRequests = TestBed.inject(HttpTestingController).match(
      (request) => request.url.startsWith('/api/v1/tasks/'),
    );
    for (const request of taskRequests) {
      request.flush({
        id: 'PM-0049', title: 'Task details', track: 'PM', milestone: null,
        priority: 'medium', prioritySource: 'track', prioritySelection: 'inherit', state: 'todo',
        dependencies: { ready: true, dependsOn: [], waitingOn: [], missing: [], summary: 'ready' },
        createdAt: '2026-07-15T00:00:00Z', modifiedAt: '2026-07-15T00:00:00Z',
        description: '', revision: 'task-revision', localMetadata: { filePath: '.pm/tasks/PM-0049.md' },
      }, { headers: { ETag: '"task-revision"' } });
    }
    for (const request of TestBed.inject(HttpTestingController).match('/api/v1/settings')) {
      request.flush({ projectName, statuses: [], tracks: [], milestones: [], priorityOptions: ['none'], revision: 'settings-revision' });
    }
    for (const request of TestBed.inject(HttpTestingController).match('/api/v1/validation')) {
      request.flush({ valid: true, issues: [] });
    }
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, router };
  }

  it('shows the loaded project name in the top bar', async () => {
    const { fixture } = await renderAt('/tasks', 'Project Atlas');
    expect(fixture.nativeElement.querySelector('.brand')?.textContent).toBe('Project Atlas');
  });

  it('shows the remaining task count in the Tasks mode header', async () => {
    TestBed.inject(TaskNavigationService).setRemainingCount(8);
    const { fixture } = await renderAt('/wiki');
    const tasksLink = fixture.nativeElement.querySelector('.mode-navigation a[href="/tasks"]');
    expect(tasksLink?.querySelector('span:first-child')?.textContent).toBe('Tasks');
    expect(tasksLink?.querySelector('.mode-count')?.textContent).toBe('8 left');
    expect(tasksLink?.querySelector('.mode-count')?.getAttribute('aria-label')).toBe('8 tasks left');
  });

  it('keeps PM as the loading and error fallback', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.brand')?.textContent).toBe('PM');

    TestBed.inject(HttpTestingController).expectOne('/api/v1/project').flush(
      { title: 'Unavailable' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.brand')?.textContent).toBe('PM');
  });

  it.each([
    ['/', '/tasks', 'Tasks'],
    ['/tasks', '/tasks', 'Tasks'],
    ['/wiki', '/wiki', 'Wiki'],
    ['/settings', '/tasks/settings', 'Project settings'],
    ['/not-a-route', '/tasks', 'Tasks'],
  ])('routes %s to the correct child shell', async (requested, expectedUrl, heading) => {
    const { fixture, router } = await renderAt(requested);
    expect(router.url).toBe(expectedUrl);
    expect(fixture.nativeElement.querySelector('main h1')?.textContent).toBe(heading);
  });

  it('derives the active shell from the deepest activated route and marks the mode link', async () => {
    const { fixture, router } = await renderAt('/tasks');
    const layout = TestBed.inject(LayoutService);
    expect(layout.activeShell()).toBe(AppShell.Tasks);
    expect(fixture.nativeElement.querySelector('a[href="/tasks"][aria-current="page"]')).toBeTruthy();

    await router.navigateByUrl('/wiki');
    fixture.detectChanges();
    expect(layout.activeShell()).toBe(AppShell.Wiki);
    expect(fixture.nativeElement.querySelector('a[href="/wiki"][aria-current="page"]')).toBeTruthy();
  });

  it('keeps All tasks exact while a nested task remains in the board workspace', async () => {
    const { fixture } = await renderAt('/tasks/PM-0049?track=PM');
    const allTasks = [...fixture.nativeElement.querySelectorAll('aside a')]
      .find((link: HTMLAnchorElement) => link.textContent?.trim() === 'All tasks');
    expect(allTasks?.classList.contains('active')).toBe(false);
    expect(fixture.nativeElement.querySelector('main h1')?.textContent).toBe('Tasks');
  });

  it('selects only Settings in the task sidebar on the settings route', async () => {
    const { fixture } = await renderAt('/tasks/settings');
    const allTasks = fixture.nativeElement.querySelector('aside a[href="/tasks"]');
    const settingsLink = fixture.nativeElement.querySelector('aside a[href="/tasks/settings"]');
    expect(allTasks?.classList.contains('active')).toBe(false);
    expect(settingsLink?.classList.contains('active')).toBe(true);
    expect(settingsLink?.querySelector('ng-icon')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('uses semantic navigation and hides decorative icons from assistive technology', async () => {
    const { fixture } = await renderAt('/tasks');
    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('header nav[aria-label="Workspace"]')).toBeTruthy();
    expect(element.querySelector('aside[aria-label="Tasks navigation"]')).toBeTruthy();
    expect([...element.querySelectorAll('ng-icon')].every((icon) => icon.getAttribute('aria-hidden') === 'true')).toBe(true);
  });

  it('closes the mobile drawer with Escape and restores focus to its trigger', async () => {
    const { fixture } = await renderAt('/tasks');
    const menu = fixture.nativeElement.querySelector('.menu-button') as HTMLButtonElement;
    menu.click();
    fixture.detectChanges();

    expect(menu.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.querySelector('main')?.hasAttribute('inert')).toBe(true);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    await Promise.resolve();
    expect(menu.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(menu);
  });

  it('closes the mobile drawer from the backdrop and after navigation', async () => {
    const { fixture, router } = await renderAt('/tasks');
    const menu = fixture.nativeElement.querySelector('.menu-button') as HTMLButtonElement;
    menu.click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.backdrop') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(menu.getAttribute('aria-expanded')).toBe('false');

    menu.click();
    await router.navigateByUrl('/wiki');
    fixture.detectChanges();
    expect(menu.getAttribute('aria-expanded')).toBe('false');
  });
});
