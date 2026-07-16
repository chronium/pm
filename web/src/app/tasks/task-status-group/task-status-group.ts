import { Component, input, output } from '@angular/core';

import { TaskRow } from '../task-row/task-row';
import type { BoardStateGroup } from '../tasks-board.store';

@Component({
  selector: 'details[pmTaskStatusGroup]',
  imports: [TaskRow],
  templateUrl: './task-status-group.html',
  styleUrl: './task-status-group.css',
  host: { '[open]': 'open()', '(toggle)': 'toggled($event)' },
})
export class TaskStatusGroup {
  readonly state = input.required<BoardStateGroup>();
  readonly selectedTaskId = input.required<string | null>();
  readonly open = input.required<boolean>();
  readonly openChange = output<boolean>();

  protected toggled(event: Event): void {
    if (event.currentTarget instanceof HTMLDetailsElement)
      this.openChange.emit(event.currentTarget.open);
  }
}
