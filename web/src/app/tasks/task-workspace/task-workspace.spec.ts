import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PollingCoordinator } from '../../core/polling-coordinator';
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
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  async function render(mode: 'detail' | 'create', presentation: 'dialog' | 'page' = 'page') {
    const fixture = TestBed.createComponent(TaskWorkspace);
    fixture.componentRef.setInput('presentation', presentation);
    fixture.componentRef.setInput('mode', mode);
    if (mode === 'detail') fixture.componentRef.setInput('taskId', task.id);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/settings').flush(settings);
    if (mode === 'detail') {
      http.expectOne(`/api/v1/tasks/${task.id}`).flush(task, { headers: { ETag: '"task-r1"' } });
    }
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, http };
  }

  function input(element: HTMLInputElement, value: string): void {
    element.value = value;
    element.dispatchEvent(new Event('input'));
  }

  it('uses compact icon-only actions in dialog hosts', async () => {
    const { element } = await render('detail', 'dialog');
    const fullscreen = element.querySelector('[aria-label="Full screen"]') as HTMLButtonElement;
    const close = element.querySelector('[aria-label="Close task dialog"]') as HTMLButtonElement;
    expect(fullscreen.textContent?.trim()).toBe('');
    expect(fullscreen.title).toBe('Open task in full screen');
    expect(close.textContent?.trim()).toBe('');
    expect(close.title).toBe('Close');
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
    (element.querySelector('[aria-label="Edit task title"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const title = element.querySelector('#workspace-title') as HTMLInputElement;
    input(title, 'Changed');
    fixture.detectChanges();
    expect(element.textContent).toContain('Save and close');

    input(title, task.title);
    fixture.detectChanges();
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
});
