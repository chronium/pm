import { Component, input, output } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { cssChevronRight } from '@ng-icons/css.gg';

import { TaskRow } from '../task-row/task-row';
import type { BoardStateGroup } from '../tasks-board.store';

@Component({
  selector: 'details[pmTaskStatusGroup]',
  imports: [NgIcon, TaskRow],
  templateUrl: './task-status-group.html',
  styleUrl: './task-status-group.css',
  providers: [provideIcons({ cssChevronRight })],
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
