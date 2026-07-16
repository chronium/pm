import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { TaskBoardFilters } from './task-board-filters/task-board-filters';
import { TaskMilestone } from './task-milestone/task-milestone';
import { TaskRow } from './task-row/task-row';
import { TaskStatusGroup } from './task-status-group/task-status-group';
import type { BoardMilestoneGroup, BoardStateGroup, BoardTask } from './tasks-board.store';

const readyTask: BoardTask = {
  id: 'PM-0055',
  title: 'Decompose the Angular task board',
  track: 'PM',
  milestone: 'angular-web',
  priority: 'high',
  prioritySource: 'milestone',
  state: 'todo',
  dependencies: {
    ready: true,
    dependsOn: ['PM-0049'],
    waitingOn: [],
    missing: [],
    summary: 'ready',
  },
  descriptionPreview: 'Keep a deliberately useful preview visible for dense scanning.',
  modifiedAt: '2026-07-15T07:48:04Z',
};
const blockedTask: BoardTask = {
  ...readyTask,
  id: 'PM-0056',
  title: 'A very long blocked task title that remains fully available to assistive technology',
  priority: 'critical',
  dependencies: {
    ready: false,
    dependsOn: ['PM-9999'],
    waitingOn: [],
    missing: ['PM-9999'],
    summary: 'missing PM-9999',
  },
};
const todoState: BoardStateGroup = { key: 'todo', name: 'To do', tasks: [readyTask, blockedTask] };
const milestone: BoardMilestoneGroup = {
  key: 'angular-web',
  name: 'Angular web',
  states: [todoState, { key: 'review', name: 'Review', tasks: [] }],
};

@Component({
  imports: [TaskStatusGroup],
  template:
    '<details pmTaskStatusGroup [state]="state" [selectedTaskId]="null" [open]="open" (openChange)="changes.push($event)"></details>',
})
class StatusGroupHost {
  readonly state = todoState;
  readonly changes: boolean[] = [];
  open = false;
}

describe('Task board components', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

  it('emits typed filter changes and clear intent while reflecting active filters', () => {
    const fixture = TestBed.createComponent(TaskBoardFilters);
    fixture.componentRef.setInput('tracks', [{ key: 'PM', name: 'Product' }]);
    fixture.componentRef.setInput('milestones', [{ key: 'angular-web', name: 'Angular web' }]);
    fixture.componentRef.setInput('states', [{ key: 'todo', name: 'To do' }]);
    fixture.componentRef.setInput('filters', { track: 'PM' });
    const changes: unknown[] = [];
    let clears = 0;
    fixture.componentInstance.filterChange.subscribe((change) => changes.push(change));
    fixture.componentInstance.clearIntent.subscribe(() => clears++);
    fixture.detectChanges();

    const selects = fixture.nativeElement.querySelectorAll(
      'select',
    ) as NodeListOf<HTMLSelectElement>;
    expect(selects[0]?.value).toBe('PM');
    selects[2]!.value = 'todo';
    selects[2]!.dispatchEvent(new Event('change'));
    fixture.nativeElement.querySelector('button').click();
    expect(changes).toEqual([{ filter: 'state', value: 'todo' }]);
    expect(clears).toBe(1);
  });

  it('suppresses empty statuses and emits milestone-scoped collapse intent', () => {
    const fixture = TestBed.createComponent(TaskMilestone);
    fixture.componentRef.setInput('milestone', milestone);
    fixture.componentRef.setInput('headingId', 'milestone-angular');
    fixture.componentRef.setInput('selectedTaskId', null);
    fixture.componentRef.setInput('openStates', { todo: true, review: true });
    const intents: unknown[] = [];
    fixture.componentInstance.statusOpenChange.subscribe((intent) => intents.push(intent));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('h2')?.id).toBe('milestone-angular');
    expect(fixture.nativeElement.querySelectorAll('details')).toHaveLength(1);
    expect(fixture.nativeElement.textContent).not.toContain('Review');
    const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
    details.open = false;
    details.dispatchEvent(new Event('toggle'));
    expect(intents).toEqual([{ milestone, state: todoState, open: false }]);
  });

  it('renders native group counts and emits boolean open state', () => {
    const fixture = TestBed.createComponent(StatusGroupHost);
    fixture.detectChanges();

    const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    expect(fixture.nativeElement.querySelector('.task-count')?.getAttribute('aria-label')).toBe(
      '2 tasks',
    );
    details.open = true;
    details.dispatchEvent(new Event('toggle'));
    expect(fixture.componentInstance.changes.at(-1)).toBe(true);
  });

  it('renders semantic selected links, metadata, and ready and missing badge variants', () => {
    const readyFixture = TestBed.createComponent(TaskRow);
    readyFixture.componentRef.setInput('task', readyTask);
    readyFixture.componentRef.setInput('selected', true);
    readyFixture.detectChanges();
    const link = readyFixture.nativeElement.querySelector('a') as HTMLAnchorElement;
    expect(readyFixture.nativeElement.classList.contains('selected')).toBe(true);
    expect(link.getAttribute('aria-current')).toBe('true');
    expect(link.getAttribute('href')).toBe('/PM-0055');
    expect(link.textContent).toContain('PM-0055');
    expect(link.textContent).toContain('angular-web');
    expect(link.textContent).toContain('Priority: high');
    expect(link.textContent).toContain('Ready');
    expect(link.textContent).toContain('Keep a deliberately useful preview');

    const blockedFixture = TestBed.createComponent(TaskRow);
    blockedFixture.componentRef.setInput('task', blockedTask);
    blockedFixture.componentRef.setInput('selected', false);
    blockedFixture.detectChanges();
    const badges = blockedFixture.nativeElement.querySelectorAll('pm-badge');
    expect(badges[0]?.querySelector('.badge--danger')).toBeTruthy();
    expect(badges[1]?.querySelector('.badge--danger')).toBeTruthy();
    expect(badges[1]?.getAttribute('title')).toBe('missing PM-9999');
    expect(badges[1]?.textContent).toContain('Blocked');
    expect(blockedFixture.nativeElement.textContent).toContain('A very long blocked task title');
  });
});
