import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { PmEmptyState, PmErrorState, PmLoadingState } from '../ui/state/state';
import { TaskMilestone } from './task-milestone/task-milestone';
import { TasksBoardStore } from './tasks-board.store';
import { PollingCoordinator } from '../core/polling-coordinator';

@Component({
  selector: 'pm-tasks-board',
  imports: [PmEmptyState, PmErrorState, PmLoadingState, RouterOutlet, TaskMilestone],
  providers: [TasksBoardStore, PollingCoordinator],
  templateUrl: './tasks-board.html',
  styleUrl: './tasks-board.css',
})
export class TasksBoard {
  protected readonly board = inject(TasksBoardStore);
}
