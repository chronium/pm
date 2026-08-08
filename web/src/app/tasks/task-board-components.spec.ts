import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

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
  activation: {
    isEligible: true,
    milestoneLifecycle: 'active',
    requiredActivationTriggers: ['entry'],
    unmetActivationTriggers: [],
    summary: 'Eligible: milestone angular-web is active.',
  },
  descriptionPreview: 'Keep a deliberately useful preview visible for dense scanning.',
  modifiedAt: '2026-07-15T07:48:04Z',
};
const blockedTask: BoardTask = {
  ...readyTask,
  id: 'PM-0056',
  title: 'A very long blocked task title that remains fully available to assistive technology',
  priority: 'urgent',
  dependencies: {
    ready: false,
    dependsOn: ['PM-9999'],
    waitingOn: [],
    missing: ['PM-9999'],
    summary: 'missing PM-9999',
  },
  activation: {
    isEligible: false,
    milestoneLifecycle: 'inactive',
    requiredActivationTriggers: ['entry'],
    unmetActivationTriggers: ['entry'],
    summary: 'Ineligible: milestone angular-web is inactive; unmet activation triggers: entry.',
  },
};
const todoState: BoardStateGroup = { key: 'todo', name: 'To do', tasks: [readyTask, blockedTask] };
const milestone: BoardMilestoneGroup = {
  key: 'angular-web',
  name: 'Angular web',
  description: 'Deliver the **Angular board**.',
  lifecycle: 'inactive',
  requiredActivationTriggers: ['entry'],
  unmetActivationTriggers: ['entry'],
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

  it('suppresses empty statuses and emits milestone-scoped collapse intent', () => {
    const fixture = TestBed.createComponent(TaskMilestone);
    fixture.componentRef.setInput('milestone', milestone);
    fixture.componentRef.setInput('headingId', 'milestone-angular');
    fixture.componentRef.setInput('selectedTaskId', null);
    fixture.componentRef.setInput('openStates', { todo: true, review: true });
    fixture.componentRef.setInput('milestoneOpen', true);
    const intents: unknown[] = [];
    fixture.componentInstance.statusOpenChange.subscribe((intent) => intents.push(intent));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.milestone-summary')?.id).toBe('milestone-angular');
    expect(fixture.nativeElement.querySelectorAll('details.status-group')).toHaveLength(1);
    expect(fixture.nativeElement.textContent).toContain('Inactive');
    expect(fixture.nativeElement.textContent).toContain('Waiting on: entry');
    const deliverable = fixture.nativeElement.querySelector(
      'details.deliverable-description',
    ) as HTMLDetailsElement;
    expect(deliverable.open).toBe(false);
    expect(deliverable.querySelector('.deliverable-disclosure')?.getAttribute('name')).toBe(
      'cssChevronRight',
    );
    deliverable.open = true;
    deliverable.dispatchEvent(new Event('toggle'));
    fixture.detectChanges();
    expect(deliverable.textContent).toContain('Angular board');
    expect(fixture.nativeElement.textContent).not.toContain('Review');
    const details = fixture.nativeElement.querySelector(
      'details.status-group',
    ) as HTMLDetailsElement;
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
    expect(link.getAttribute('href')).toBe('/tasks/PM-0055');
    expect(link.textContent).toContain('PM-0055');
    expect(link.textContent).toContain('angular-web');
    const priority = link.querySelector('pm-priority-indicator') as HTMLElement;
    expect(priority.getAttribute('aria-label')).toBe('Priority: high');
    expect(priority.getAttribute('title')).toBe(
      'Priority: high — effective priority from milestone',
    );
    const readyStatuses = [...link.querySelectorAll<HTMLElement>('.task-status')];
    expect(readyStatuses.map((status) => status.dataset['icon'])).toEqual([
      'cssUnblock',
      'cssLockUnlock',
    ]);
    expect(readyStatuses.map((status) => status.getAttribute('aria-label'))).toEqual([
      'Dependencies: ready',
      'Activation: eligible',
    ]);
    expect(link.textContent).toContain('Keep a deliberately useful preview');
    expect(link.querySelector('pm-badge')).toBeNull();

    const blockedFixture = TestBed.createComponent(TaskRow);
    blockedFixture.componentRef.setInput('task', blockedTask);
    blockedFixture.componentRef.setInput('selected', false);
    blockedFixture.detectChanges();
    const blockedPriority = blockedFixture.nativeElement.querySelector(
      'pm-priority-indicator',
    ) as HTMLElement;
    const blockedStatuses = [
      ...(blockedFixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>(
        '.task-status',
      ),
    ];
    expect(blockedPriority.dataset['priority']).toBe('urgent');
    expect(blockedStatuses.map((status) => status.dataset['icon'])).toEqual([
      'cssBlock',
      'cssLock',
    ]);
    expect(blockedStatuses.map((status) => status.dataset['tone'])).toEqual(['danger', 'warning']);
    expect(blockedStatuses[0]?.getAttribute('title')).toBe(
      'Dependencies: blocked — missing PM-9999',
    );
    expect(blockedStatuses[1]?.getAttribute('aria-label')).toBe('Activation: inactive');
    expect(blockedFixture.nativeElement.textContent).toContain('A very long blocked task title');
  });
});
