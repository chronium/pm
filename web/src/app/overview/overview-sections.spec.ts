import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import type { BoardTask } from '../tasks/tasks-board.store';
import { OverviewMilestone, type OverviewMilestoneData } from './overview-milestone';
import { OverviewTasks } from './overview-tasks';

const milestone: OverviewMilestoneData = {
  key: 'public-beta',
  title: 'Public beta',
  description: 'Deliver an **installable beta** for the complete local workflow.',
  priority: 'high',
  lifecycle: 'active',
  assignedTaskCount: 4,
  doneTaskCount: 3,
  requiredActivationTriggers: ['beta-entry'],
  unmetActivationTriggers: [],
};

const task: BoardTask = {
  id: 'PM-0128',
  title: 'Publish static project Overview pages',
  track: 'PM',
  milestone: 'public-beta',
  priority: 'high',
  prioritySource: 'milestone',
  state: 'in-progress',
  dependencies: {
    ready: true,
    dependsOn: ['PM-0127'],
    waitingOn: [],
    missing: [],
    summary: 'all dependencies complete',
  },
  activation: {
    isEligible: true,
    milestoneLifecycle: 'active',
    requiredActivationTriggers: ['beta-entry'],
    unmetActivationTriggers: [],
    summary: 'Eligible: milestone public-beta is active.',
  },
  descriptionPreview: 'Publish the resolved Overview through the existing static snapshot.',
  modifiedAt: '2026-08-09T08:00:00Z',
};

describe('Overview milestone and task sections', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

  it('presents milestone lifecycle, Markdown, completion, and accessible progress', () => {
    const fixture = TestBed.createComponent(OverviewMilestone);
    fixture.componentRef.setInput('headingId', 'current-milestone');
    fixture.componentRef.setInput('title', 'Current milestone');
    fixture.componentRef.setInput('milestone', milestone);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const progress = element.querySelector('progress') as HTMLProgressElement;
    expect(element.querySelector('section')?.getAttribute('aria-labelledby')).toBe(
      'current-milestone',
    );
    expect(element.textContent).toContain('Public beta');
    expect(element.textContent).toContain('Active');
    expect(element.querySelector('.milestone-description strong')?.textContent).toBe(
      'installable beta',
    );
    expect(element.textContent).toContain('3 of 4 tasks complete');
    expect(element.textContent).toContain('75%');
    expect(progress.value).toBe(3);
    expect(progress.max).toBe(4);
    expect(progress.getAttribute('aria-label')).toBe('3 of 4 tasks complete');
  });

  it('keeps empty automatic selection and zero-task progress non-vacuous', () => {
    const emptyFixture = TestBed.createComponent(OverviewMilestone);
    emptyFixture.componentRef.setInput('headingId', 'empty-milestone');
    emptyFixture.componentRef.setInput('title', 'Current milestone');
    emptyFixture.componentRef.setInput('milestone', null);
    emptyFixture.detectChanges();
    expect((emptyFixture.nativeElement as HTMLElement).textContent).toContain(
      'No active milestone is available.',
    );

    const zeroFixture = TestBed.createComponent(OverviewMilestone);
    zeroFixture.componentRef.setInput('headingId', 'zero-milestone');
    zeroFixture.componentRef.setInput('title', 'Current milestone');
    zeroFixture.componentRef.setInput('milestone', {
      ...milestone,
      assignedTaskCount: 0,
      doneTaskCount: 0,
    });
    zeroFixture.detectChanges();
    const zero = zeroFixture.nativeElement as HTMLElement;
    expect(zero.textContent).toContain('No assigned tasks');
    expect(zero.querySelector('progress')).toBeNull();
    expect(zero.textContent).not.toContain('100%');
  });

  it('renders ordered compact tasks with visible state and an honest empty state', () => {
    const fixture = TestBed.createComponent(OverviewTasks);
    fixture.componentRef.setInput('headingId', 'current-work');
    fixture.componentRef.setInput('title', 'What is being worked on');
    fixture.componentRef.setInput('tasks', [task]);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const row = element.querySelector('li[pmTaskRow]') as HTMLElement;
    const link = element.querySelector('li[pmTaskRow] a') as HTMLAnchorElement;
    expect(element.querySelector('section')?.getAttribute('aria-labelledby')).toBe('current-work');
    expect(link.getAttribute('href')).toBe('/tasks/PM-0128');
    expect(row.dataset['layout']).toBe('overview');
    expect(link.textContent).toContain('PM-0128');
    expect(link.querySelector('pm-badge')?.textContent).toContain('in-progress');
    expect(link.querySelectorAll('.task-status')).toHaveLength(2);

    fixture.componentRef.setInput('tasks', []);
    fixture.detectChanges();
    expect(element.querySelector('li[pmTaskRow]')).toBeNull();
    expect(element.textContent).toContain('No tasks match this section.');
  });
});
