import { TestBed } from '@angular/core/testing';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { App } from './app';
import { AppShell, routes } from './app.routes';
import { LayoutService } from './core/layout.service';
import { TaskNavigationService } from './tasks/task-navigation.service';
import { SyncStatusService } from './core/sync-status.service';

describe('application shell', () => {
  beforeEach(async () => {
    document.documentElement.removeAttribute('data-theme');
    document.documentElement.removeAttribute('data-theme-preference');
    document.documentElement.removeAttribute('data-accent');
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes, withComponentInputBinding()),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
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
      accent: 'purple',
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
    for (const request of TestBed.inject(HttpTestingController).match('/api/v1/board/navigation')) {
      request.flush({
        remainingCount: 3,
        tracks: [
          { key: 'PM', name: 'Product', remainingCount: 2 },
          { key: 'BUILD', name: 'Build', remainingCount: 1 },
        ],
        milestones: [
          { key: 'm1', name: 'First milestone', remainingCount: 1 },
          { key: 'empty', name: 'A very long empty milestone name', remainingCount: 0 },
        ],
        revision: 'navigation-revision',
      });
    }
    const taskRequests = TestBed.inject(HttpTestingController).match((request) =>
      request.url.startsWith('/api/v1/tasks/'),
    );
    for (const request of taskRequests) {
      request.flush(
        {
          id: 'PM-0049',
          title: 'Task details',
          track: 'PM',
          milestone: null,
          priority: 'medium',
          prioritySource: 'track',
          prioritySelection: 'inherit',
          state: 'todo',
          dependencies: {
            ready: true,
            dependsOn: [],
            waitingOn: [],
            missing: [],
            summary: 'ready',
          },
          createdAt: '2026-07-15T00:00:00Z',
          modifiedAt: '2026-07-15T00:00:00Z',
          description: '',
          revision: 'task-revision',
          localMetadata: { filePath: '.pm/tasks/PM-0049.md' },
        },
        { headers: { ETag: '"task-revision"' } },
      );
    }
    for (const request of TestBed.inject(HttpTestingController).match('/api/v1/settings')) {
      request.flush({
        projectName,
        statuses: [],
        tracks: [],
        milestones: [],
        priorityOptions: ['none'],
        revision: 'settings-revision',
      });
    }
    for (const request of TestBed.inject(HttpTestingController).match('/api/v1/validation')) {
      request.flush({ valid: true, issues: [] });
    }
    for (const request of TestBed.inject(HttpTestingController).match('/api/v1/project/identity')) {
      request.flush({
        userId: 'usr_local',
        displayName: 'Local user',
        publicKey: 'public-key',
        fingerprint: 'ab'.repeat(32),
      });
    }
    for (const request of TestBed.inject(HttpTestingController).match('/api/v1/project/members')) {
      request.flush({
        projectId: 'project-1',
        currentUserId: 'usr_local',
        currentRole: 'user',
        authenticated: true,
        members: [
          {
            userId: 'usr_local',
            displayName: 'Local user',
            publicKey: 'public-key',
            fingerprint: 'ab'.repeat(32),
            role: 'user',
            isLocal: true,
          },
        ],
      });
    }
    const wikiSegments = url.split('?')[0]!.split('/').filter(Boolean);
    const wikiPath =
      wikiSegments[0] === 'wiki' && wikiSegments.length > 1 && wikiSegments[1] !== 'new'
        ? wikiSegments
            .slice(wikiSegments[1] === 'edit' || wikiSegments[1] === 'meta' ? 2 : 1)
            .join('/')
        : null;
    const wikiPages = wikiPath
      ? [
          { path: wikiPath, title: 'Guide', modifiedAt: '2026-07-15T00:00:00Z' },
          ...(wikiPath === 'architecture'
            ? [
                {
                  path: 'architecture/child',
                  title: 'Child page',
                  modifiedAt: '2026-07-15T00:00:00Z',
                },
              ]
            : []),
        ]
      : [];
    for (const request of TestBed.inject(HttpTestingController).match('/api/v1/wiki/pages')) {
      request.flush(wikiPages);
    }
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
    await Promise.resolve();
    fixture.detectChanges();
    await Promise.resolve();
    for (const request of TestBed.inject(HttpTestingController).match((request) =>
      request.url.startsWith('/api/v1/wiki/pages/'),
    )) {
      request.flush(
        {
          path: wikiPath,
          title: 'Guide',
          createdAt: '2026-07-14T00:00:00Z',
          modifiedAt: '2026-07-15T00:00:00Z',
          body: '# Guide',
          revision: 'wiki-revision',
          localMetadata: { filePath: `.pm/wiki/${wikiPath}.md` },
        },
        { headers: { ETag: '"wiki-revision"' } },
      );
    }
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, router };
  }

  it('shows the loaded project name in the top bar', async () => {
    const { fixture } = await renderAt('/tasks', 'Project Atlas');
    expect(fixture.nativeElement.querySelector('.brand')?.textContent).toBe('Project Atlas');
  });

  it('reports shared sync activity from the top bar without shifting the page', async () => {
    const { fixture } = await renderAt('/tasks');
    const syncStatus = TestBed.inject(SyncStatusService);
    const indicator = () => fixture.nativeElement.querySelector('.sync-status') as HTMLElement;

    expect(indicator().getAttribute('aria-label')).toBe('Project data synced');
    const finish = syncStatus.begin();
    fixture.detectChanges();
    expect(indicator().getAttribute('aria-label')).toBe('Syncing project data');
    expect(indicator().classList).toContain('sync-status--active');

    finish();
    fixture.detectChanges();
    expect(indicator().getAttribute('aria-label')).toBe('Project data synced');
  });

  it('applies the project accent without showing a top-bar picker', async () => {
    const { fixture } = await renderAt('/tasks');

    expect(fixture.nativeElement.querySelector('pm-accent-picker')).toBeNull();
    expect(document.documentElement.dataset['accent']).toBe('purple');
  });

  it('shows the remaining task count in the Tasks mode header', async () => {
    TestBed.inject(TaskNavigationService).setRemainingCount(8);
    const { fixture } = await renderAt('/wiki');
    const tasksLink = fixture.nativeElement.querySelector('.mode-navigation a[href="/tasks"]');
    expect(tasksLink?.querySelector('span:first-child')?.textContent).toBe('Tasks');
    expect(tasksLink?.querySelector('.mode-count')?.textContent).toBe('8 left');
    expect(tasksLink?.querySelector('.mode-count')?.getAttribute('aria-label')).toBe(
      '8 tasks left',
    );
  });

  it('keeps PM as the loading and error fallback', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.brand')?.textContent).toBe('PM');

    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/project')
      .flush({ title: 'Unavailable' }, { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.brand')?.textContent).toBe('PM');
  });

  it.each([
    ['/', '/tasks', 'Tasks'],
    ['/tasks', '/tasks', 'Tasks'],
    ['/wiki', '/wiki', 'Wiki'],
    ['/wiki/new', '/wiki/new', 'New page'],
    ['/wiki/guides/start', '/wiki/guides/start', 'Guide'],
    ['/wiki/architecture', '/wiki/architecture', 'Guide'],
    ['/wiki/edit/guides/start', '/wiki/edit/guides/start', 'Edit Guide'],
    ['/wiki/meta/guides/start', '/wiki/meta/guides/start', 'Metadata'],
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
    expect(
      fixture.nativeElement.querySelector('a[href="/tasks"][aria-current="page"]'),
    ).toBeTruthy();

    await router.navigateByUrl('/wiki');
    fixture.detectChanges();
    expect(layout.activeShell()).toBe(AppShell.Wiki);
    expect(
      fixture.nativeElement.querySelector('a[href="/wiki"][aria-current="page"]'),
    ).toBeTruthy();
  });

  it('shows task search in task mode and wiki search in wiki mode', async () => {
    const { fixture, router } = await renderAt('/tasks');
    expect(fixture.nativeElement.querySelector('pm-task-search')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('pm-wiki-search')).toBeNull();

    await router.navigateByUrl('/wiki');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('pm-task-search')).toBeNull();
    expect(fixture.nativeElement.querySelector('pm-wiki-search')).toBeTruthy();
  });

  it('renders wiki primary actions as stacked sidebar navigation', async () => {
    const { fixture } = await renderAt('/wiki');
    const nav = fixture.nativeElement.querySelector('.wiki-primary-nav') as HTMLElement;
    const links = [...nav.querySelectorAll<HTMLAnchorElement>('a')];

    expect(links.map((link) => link.textContent?.trim())).toEqual(['Wiki home', 'New page']);
    expect(getComputedStyle(nav).flexDirection).toBe('column');
    expect(links.map((link) => getComputedStyle(link).display)).toEqual(['flex', 'flex']);
    expect(links.map((link) => getComputedStyle(link).minHeight)).toEqual(['32px', '32px']);
    expect(links[0]?.classList.contains('active')).toBe(true);
  });

  it('keeps All tasks exact while a nested task remains in the board workspace', async () => {
    const { fixture } = await renderAt('/tasks/dialog/PM-0049?track=PM');
    const allTasks = fixture.nativeElement.querySelector('aside a[href^="/tasks?"]');
    expect(allTasks?.classList.contains('active')).toBe(false);
    expect(fixture.nativeElement.querySelector('main h1')?.textContent).toBe('Tasks');
  });

  it('renders task scopes with counts and keeps nested filtered scopes active', async () => {
    const { fixture } = await renderAt('/tasks/PM-0049?track=PM&state=todo&view=dense');
    const product = [...fixture.nativeElement.querySelectorAll('aside a')].find(
      (link: HTMLAnchorElement) => link.textContent?.includes('Product'),
    );
    expect(product?.classList.contains('active')).toBe(true);
    expect(product?.getAttribute('aria-current')).toBe('page');
    expect(product?.textContent.replace(/\s+/g, ' ').trim()).toBe('Product2');
    expect(fixture.nativeElement.querySelectorAll('aside .scope-count')).toHaveLength(5);
    const milestone = [...fixture.nativeElement.querySelectorAll('aside a')].find(
      (link: HTMLAnchorElement) => link.textContent?.includes('First milestone'),
    ) as HTMLAnchorElement;
    const milestoneUrl = new URL(milestone.href);
    expect(milestoneUrl.pathname).toBe('/tasks');
    expect(milestoneUrl.searchParams.get('milestone')).toBe('m1');
    expect(milestoneUrl.searchParams.get('track')).toBeNull();
    expect(milestoneUrl.searchParams.get('state')).toBe('todo');
    expect(milestoneUrl.searchParams.get('view')).toBe('dense');
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
    expect(
      [...element.querySelectorAll('ng-icon')].every(
        (icon) => icon.getAttribute('aria-hidden') === 'true',
      ),
    ).toBe(true);
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
