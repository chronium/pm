import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PollingCoordinator } from '../../core/polling-coordinator';
import { ProjectLinksService } from '../../core/project-links.service';
import type { TaskResponse } from '../task-api.service';
import { TaskWorkspace } from './task-workspace';

const settings = {
  projectName: 'PM',
  statuses: [
    { key: 'todo', name: 'To do' },
    { key: 'done', name: 'Done' },
  ],
  tracks: [
    { key: 'PM', name: 'Product' },
    { key: 'BUILD', name: 'Build' },
  ],
  milestones: [{ key: 'angular-web', title: 'Angular web', priority: 'high' }],
  priorityOptions: ['none', 'low', 'medium', 'high', 'urgent'],
  revision: 'settings-r1',
};

const task: TaskResponse = {
  id: 'PM-0060',
  title: 'Shared workspace',
  track: 'PM',
  milestone: 'angular-web',
  priority: 'high',
  prioritySource: 'milestone',
  prioritySelection: 'inherit',
  state: 'todo',
  dependencies: {
    ready: false,
    dependsOn: ['PM-0029'],
    waitingOn: ['PM-0029'],
    missing: [],
    summary: 'Waiting on PM-0029',
  },
  activation: {
    isEligible: false,
    milestoneLifecycle: 'inactive',
    requiredActivationTriggers: ['beta-entry'],
    unmetActivationTriggers: ['beta-entry'],
    summary:
      'Ineligible: milestone angular-web is inactive; unmet activation triggers: beta-entry.',
  },
  createdAt: '2026-07-16T00:00:00Z',
  modifiedAt: '2026-07-18T00:00:00Z',
  description: 'Original description',
  revision: 'task-r1',
  localMetadata: { filePath: '.pm/tasks/PM-0060.md' },
};

describe('TaskWorkspace', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TaskWorkspace],
      providers: [
        PollingCoordinator,
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
  });

  afterEach(() => {
    history.replaceState({}, '');
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  async function render(
    mode: 'detail' | 'create',
    presentation: 'dialog' | 'page' = 'page',
    response: TaskResponse = task,
  ) {
    const fixture = TestBed.createComponent(TaskWorkspace);
    fixture.componentRef.setInput('presentation', presentation);
    fixture.componentRef.setInput('mode', mode);
    if (mode === 'detail') fixture.componentRef.setInput('taskId', response.id);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/settings').flush(settings);
    if (mode === 'detail') {
      http
        .expectOne(`/api/v1/tasks/${response.id}`)
        .flush(response, { headers: { ETag: '"task-r1"' } });
    }
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, http };
  }

  function input(element: HTMLInputElement, value: string): void {
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  function textareaInput(element: HTMLTextAreaElement, value: string): void {
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  it('keeps initial task loading visual noise out of the workspace', async () => {
    const fixture = TestBed.createComponent(TaskWorkspace);
    fixture.componentRef.setInput('presentation', 'dialog');
    fixture.componentRef.setInput('mode', 'detail');
    fixture.componentRef.setInput('taskId', task.id);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('pm-loading-state')).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain(
      'Loading task workspace',
    );

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/settings').flush(settings);
    http.expectOne(`/api/v1/tasks/${task.id}`).flush(task, { headers: { ETag: '"task-r1"' } });
    await fixture.whenStable();
  });

  it('uses compact icon-only actions in dialog hosts', async () => {
    const { element } = await render('detail', 'dialog');
    const fullscreen = element.querySelector('[aria-label="Full screen"]') as HTMLButtonElement;
    const close = element.querySelector('[aria-label="Close task dialog"]') as HTMLButtonElement;
    expect(fullscreen.textContent?.trim()).toBe('');
    expect(fullscreen.title).toBe('Open task in full screen');
    expect(close.textContent?.trim()).toBe('');
    expect(close.title).toBe('Close');
  });

  it('offers runner launch only for a clean saved task and disables it during inline edits', async () => {
    const { fixture, element } = await render('detail');
    const launch = element.querySelector('.run-action') as HTMLButtonElement;
    expect(launch.textContent?.trim()).toBe('Run with Codex');
    expect(launch.disabled).toBe(false);

    (element.querySelector('[aria-label="Edit task title"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const title = element.querySelector('#workspace-title') as HTMLInputElement;
    input(title, 'Unsaved task title');
    fixture.detectChanges();
    expect(launch.disabled).toBe(true);
  });

  it('uses configured display names and activates one stable inline field at a time', async () => {
    const { fixture, element } = await render('detail');
    expect(element.textContent).toContain('To do');
    expect(element.textContent).toContain('Product');
    expect(element.textContent).toContain('Angular web');
    expect(element.textContent).toContain('Inherited (high)');

    (element.querySelector('[aria-label="Edit task title"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const title = element.querySelector('#workspace-title') as HTMLInputElement;
    input(title, 'Updated workspace');
    title.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    expect(element.querySelector('#workspace-title')).toBeNull();

    const trackButton = [
      ...element.querySelectorAll<HTMLButtonElement>('.property-field button'),
    ].find((button) => button.textContent?.includes('Product'))!;
    trackButton.click();
    fixture.detectChanges();
    expect(element.querySelectorAll('.property-row select')).toHaveLength(1);
    expect(element.textContent).toContain('Updated workspace');
    expect(element.textContent).toContain('Save and close');
  });

  it('computes dirty state from draft differences and cancel restores the accepted task', async () => {
    const { fixture, element } = await render('detail');
    expect(fixture.componentInstance.backdropDismissible()).toBe(true);
    (element.querySelector('[aria-label="Edit task title"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const title = element.querySelector('#workspace-title') as HTMLInputElement;
    input(title, 'Changed');
    fixture.detectChanges();
    expect(fixture.componentInstance.backdropDismissible()).toBe(false);
    expect(element.textContent).toContain('Save and close');

    input(title, task.title);
    fixture.detectChanges();
    expect(fixture.componentInstance.backdropDismissible()).toBe(true);
    expect(element.textContent).not.toContain('Save and close');

    input(title, 'Changed again');
    fixture.detectChanges();
    const cancel = [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Cancel',
    )!;
    cancel.click();
    fixture.detectChanges();
    expect(element.textContent).toContain(task.title);
    expect(element.textContent).not.toContain('Save and close');
  });

  it('saves the complete accumulated draft and returns every field to read mode', async () => {
    const { fixture, element, http } = await render('detail');
    (element.querySelector('[aria-label="Edit task title"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    input(element.querySelector('#workspace-title') as HTMLInputElement, 'Updated workspace');
    (element.querySelector('#workspace-title') as HTMLInputElement).dispatchEvent(
      new Event('blur'),
    );
    fixture.detectChanges();

    const status = element.querySelector('[aria-label="Edit task status"]') as HTMLButtonElement;
    status.click();
    fixture.detectChanges();
    const statusSelect = element.querySelector('#workspace-status') as HTMLSelectElement;
    statusSelect.value = 'done';
    statusSelect.dispatchEvent(new Event('input'));
    statusSelect.dispatchEvent(new Event('change'));
    statusSelect.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    const save = [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Save',
    )!;
    save.click();
    const request = http.expectOne(`/api/v1/tasks/${task.id}`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({
      title: 'Updated workspace',
      state: 'done',
      priority: 'inherit',
      description: task.description,
      placement: { track: 'PM', milestone: 'angular-web' },
    });
    request.flush(
      { ...task, title: 'Updated workspace', state: 'done', revision: 'task-r2' },
      { headers: { ETag: '"task-r2"' } },
    );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(element.querySelector('#workspace-title')).toBeNull();
    expect(element.querySelector('#workspace-status')).toBeNull();
    expect(element.textContent).toContain('Updated workspace');
    expect(element.textContent).not.toContain('Save and close');
  });

  it('creates with read-only defaults and transitions using the emitted task id', async () => {
    const { fixture, element, http } = await render('create');
    expect(element.textContent).toContain('New task');
    expect(element.textContent).toContain('Status: To do');
    expect(element.textContent).toContain('Priority: Inherited');
    const created: Array<{ id: string; close: boolean }> = [];
    fixture.componentInstance.created.subscribe((event) => created.push(event));

    input(element.querySelector('#workspace-title') as HTMLInputElement, 'New workspace task');
    fixture.detectChanges();
    const create = [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Create',
    )!;
    create.click();
    const request = http.expectOne('/api/v1/tasks');
    expect(request.request.body).toEqual({
      title: 'New workspace task',
      track: 'PM',
      milestone: null,
      description: '',
    });
    request.flush(task, { status: 201, statusText: 'Created' });
    await fixture.whenStable();
    expect(created).toEqual([{ id: task.id, close: false }]);
  });

  it('appends a compact note with the current revision and refreshes the rendered description', async () => {
    const { fixture, element, http } = await render('detail');
    (element.querySelector('[aria-label="Add task note"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const note = element.querySelector('#task-note') as HTMLTextAreaElement;
    expect(note.rows).toBe(4);
    textareaInput(note, 'Progress **note**');
    fixture.detectChanges();

    const add = [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Add note',
    )!;
    add.click();
    const request = http.expectOne(`/api/v1/tasks/${task.id}/notes`);
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('If-Match')).toBe('"task-r1"');
    expect(request.request.body).toEqual({ note: 'Progress **note**' });
    request.flush(
      {
        ...task,
        description: 'Original description\n\n## Notes\n\n- Progress **note**',
        revision: 'task-r2',
      },
      { headers: { ETag: '"task-r2"' } },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('#task-note')).toBeNull();
    expect(element.textContent).toContain('Progress note');
  });

  it('keeps the note composer compact and prevents competing description edits', async () => {
    const { fixture, element } = await render('detail');
    (element.querySelector('[aria-label="Add task note"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const note = element.querySelector('#task-note') as HTMLTextAreaElement;
    textareaInput(note, 'Unsent note');
    fixture.detectChanges();

    expect(
      (element.querySelector('[aria-label="Edit task description"]') as HTMLButtonElement).disabled,
    ).toBe(true);
    expect(fixture.componentInstance.canDeactivate()).toBeInstanceOf(Promise);

    const cancel = [...element.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === 'Cancel' && button.closest('.note-actions'),
    )!;
    cancel.click();
    fixture.detectChanges();
    expect(element.querySelector('#task-note')).toBeNull();
  });

  it('shows recommendation rationale only when navigation supplies it', async () => {
    history.replaceState({ recommendationReason: 'Selected high priority ready task.' }, '');
    const { element } = await render('detail');

    expect(element.textContent).toContain('Recommendation');
    expect(element.textContent).toContain('Selected high priority ready task.');
  });

  it('presents activation eligibility separately from dependency readiness', async () => {
    const { element } = await render('detail');
    const activation = element.querySelector('.activation-context') as HTMLElement;

    expect(activation.getAttribute('data-eligible')).toBe('false');
    expect(activation.textContent).toContain('Activation');
    expect(activation.textContent).toContain('milestone angular-web is inactive');
    expect(activation.querySelector('code')?.textContent).toBe('beta-entry');
    expect(element.querySelector('.dependencies')).not.toBeNull();
  });

  it('renders each dependency once with an explicit icon-backed state', async () => {
    const { element } = await render('detail');
    const dependency = element.querySelector('.dependency-item') as HTMLElement;
    const state = dependency.querySelector('.dependency-state') as HTMLElement;

    expect(dependency.querySelector('a')?.textContent?.trim()).toBe('PM-0029');
    expect(state.textContent?.trim()).toBe('Waiting');
    expect(state.classList).toContain('dependency-state--waiting');
    expect(state.querySelector('ng-icon')).not.toBeNull();
    expect(element.textContent).not.toContain('Waiting on PM-0029');
  });

  it('distinguishes waiting, missing, and ready dependencies with text and semantic color', async () => {
    const stateTask: TaskResponse = {
      ...task,
      dependencies: {
        ready: false,
        dependsOn: ['PM-0029', 'PM-0030', 'PM-0031'],
        waitingOn: ['PM-0029'],
        missing: ['PM-0030'],
        summary: 'Waiting on PM-0029; missing PM-0030',
      },
    };

    const { element } = await render('detail', 'page', stateTask);
    const states = [...element.querySelectorAll<HTMLElement>('.dependency-state')];

    expect(states.map((state) => state.textContent?.trim())).toEqual([
      'Waiting',
      'Missing',
      'Ready',
    ]);
    expect(states.map((state) => state.className)).toEqual([
      'dependency-state dependency-state--waiting',
      'dependency-state dependency-state--missing',
      'dependency-state dependency-state--ready',
    ]);
  });

  it('shortens canonical dependency references without discarding their link context', async () => {
    const reference = 'pm://project/prj_pm_link_starfall/task/STAR-0001';
    TestBed.overrideProvider(ProjectLinksService, {
      useValue: {
        resolve: () => ({
          kind: 'available',
          href: '/projects/prj_pm_link_starfall/tasks/STAR-0001',
          local: true,
        }),
      },
    });
    const linkedTask: TaskResponse = {
      ...task,
      dependencies: {
        ready: false,
        dependsOn: [reference],
        waitingOn: [reference],
        missing: [],
        summary: `Waiting on ${reference}`,
      },
    };

    const { element } = await render('detail', 'page', linkedTask);
    const dependency = element.querySelector('.dependency-item') as HTMLElement;
    const link = dependency.querySelector('a') as HTMLAnchorElement;

    expect(link.textContent?.trim()).toBe('STAR-0001');
    expect(link.title).toBe(reference);
    expect(link.getAttribute('href')).toBe('/projects/prj_pm_link_starfall/tasks/STAR-0001');
    expect(dependency.querySelector('.dependency-state')?.textContent?.trim()).toBe('Waiting');
    expect(element.textContent).not.toContain(reference);
  });
});
