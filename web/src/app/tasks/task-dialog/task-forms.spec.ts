import { TestBed } from '@angular/core/testing';

import type { TaskResponse } from '../task-api.service';
import { TaskCreateForm } from './task-create-form';
import { TaskEditForm } from './task-edit-form';

const options = [
  { key: 'PM', name: 'Product', priority: 'medium' },
  { key: 'BUILD', name: 'Build', priority: 'high' },
];
const task: TaskResponse = {
  id: 'PM-0050',
  title: 'Dialogs',
  track: 'PM',
  milestone: 'angular-web',
  priority: 'high',
  prioritySource: 'milestone',
  prioritySelection: 'inherit',
  state: 'todo',
  dependencies: { ready: true, dependsOn: [], waitingOn: [], missing: [], summary: 'ready' },
  createdAt: '2026-07-15T00:00:00Z',
  modifiedAt: '2026-07-15T00:00:00Z',
  description: 'Body',
  revision: 'r1',
  localMetadata: { filePath: '.pm/tasks/PM-0050.md' },
};

describe('task Signal Forms', () => {
  it('prefills active filters and requires a create title', async () => {
    const fixture = TestBed.createComponent(TaskCreateForm);
    fixture.componentRef.setInput('tracks', options);
    fixture.componentRef.setInput('milestones', [
      { key: 'angular-web', name: 'Angular web', priority: 'high' },
    ]);
    fixture.componentRef.setInput('initialTrack', 'BUILD');
    fixture.componentRef.setInput('initialMilestone', 'angular-web');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.componentInstance.model()).toMatchObject({
      track: 'BUILD',
      milestone: 'angular-web',
    });
    expect(fixture.componentInstance.taskForm().valid()).toBe(false);
    const title = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    title.value = 'A task';
    title.dispatchEvent(new Event('input'));
    expect(fixture.componentInstance.taskForm().valid()).toBe(true);
    expect(fixture.componentInstance.dirty()).toBe(true);
  });

  it('keeps track and milestone read-only while editing supported fields', async () => {
    const fixture = TestBed.createComponent(TaskEditForm);
    fixture.componentRef.setInput('task', task);
    fixture.componentRef.setInput(
      'states',
      options.map((item, index) => ({ ...item, key: index ? 'done' : 'todo' })),
    );
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('PM · angular-web');
    expect(fixture.nativeElement.querySelectorAll('select')).toHaveLength(2);
    const title = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    title.value = 'Changed';
    title.dispatchEvent(new Event('input'));
    expect(fixture.componentInstance.dirty()).toBe(true);
    fixture.componentRef.setInput('stale', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button[type="submit"]')?.disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Reload latest');
  });
});
