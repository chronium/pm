import { Component, input, output } from '@angular/core';

import { TaskStatusGroup } from '../task-status-group/task-status-group';
import type { BoardMilestoneGroup, BoardStateGroup, StatusOpenIntent } from '../tasks-board.store';

@Component({ selector: 'section[pmTaskMilestone]', imports: [TaskStatusGroup], templateUrl: './task-milestone.html', styleUrl: './task-milestone.css' })
export class TaskMilestone {
  readonly milestone = input.required<BoardMilestoneGroup>();
  readonly headingId = input.required<string>();
  readonly selectedTaskId = input.required<string | null>();
  readonly openStates = input.required<Readonly<Record<string, boolean>>>();
  readonly statusOpenChange = output<StatusOpenIntent>();

  protected changed(state: BoardStateGroup, open: boolean): void {
    this.statusOpenChange.emit({ milestone: this.milestone(), state, open });
  }
}
