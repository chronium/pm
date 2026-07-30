import { Component, computed, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { cssChevronRight } from '@ng-icons/css.gg';

import { TaskStatusGroup } from '../task-status-group/task-status-group';
import type {
  BoardMilestoneGroup,
  BoardStateGroup,
  MilestoneOpenIntent,
  StatusOpenIntent,
} from '../tasks-board.store';

@Component({
  selector: 'details[pmTaskMilestone]',
  imports: [NgIcon, TaskStatusGroup],
  templateUrl: './task-milestone.html',
  styleUrl: './task-milestone.css',
  providers: [provideIcons({ cssChevronRight })],
  host: {
    '[open]': 'milestoneOpen()',
    '[class.completed]': 'completed()',
    '(toggle)': 'toggled($event)',
  },
})
export class TaskMilestone {
  readonly milestone = input.required<BoardMilestoneGroup>();
  readonly headingId = input.required<string>();
  readonly selectedTaskId = input.required<string | null>();
  readonly openStates = input.required<Readonly<Record<string, boolean>>>();
  readonly milestoneOpen = input.required<boolean>();
  readonly milestoneOpenChange = output<MilestoneOpenIntent>();
  readonly statusOpenChange = output<StatusOpenIntent>();

  protected readonly taskCount = computed(() =>
    this.milestone().states.reduce((total, state) => total + state.tasks.length, 0),
  );
  protected readonly completed = computed(
    () =>
      this.taskCount() > 0 &&
      this.milestone().states.every((state) => state.tasks.length === 0 || state.key === 'done'),
  );

  protected changed(state: BoardStateGroup, open: boolean): void {
    this.statusOpenChange.emit({ milestone: this.milestone(), state, open });
  }

  protected toggled(event: Event): void {
    if (event.currentTarget instanceof HTMLDetailsElement) {
      this.milestoneOpenChange.emit({
        milestone: this.milestone(),
        open: event.currentTarget.open,
      });
    }
  }
}
