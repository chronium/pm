import { Component, input } from '@angular/core';

import { TaskRow } from '../tasks/task-row/task-row';
import type { BoardTask } from '../tasks/tasks-board.store';
import { OverviewSection } from './overview-section';

@Component({
  selector: 'pm-overview-tasks',
  imports: [OverviewSection, TaskRow],
  templateUrl: './overview-tasks.html',
  styleUrl: './overview-tasks.css',
})
export class OverviewTasks {
  readonly headingId = input.required<string>();
  readonly title = input.required<string>();
  readonly tasks = input.required<readonly BoardTask[]>();
}
