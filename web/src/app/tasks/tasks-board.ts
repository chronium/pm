import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { PmEmptyState, PmErrorState, PmLoadingState } from '../ui/state/state';
import { TaskBoardFilters } from './task-board-filters/task-board-filters';
import { TaskMilestone } from './task-milestone/task-milestone';
import { TasksBoardStore } from './tasks-board.store';

@Component({
  selector: 'pm-tasks-board',
  imports: [
    PmEmptyState,
    PmErrorState,
    PmLoadingState,
    RouterOutlet,
    TaskBoardFilters,
    TaskMilestone,
  ],
  providers: [TasksBoardStore],
  templateUrl: './tasks-board.html',
  styleUrl: './tasks-board.css',
})
export class TasksBoard {
  protected readonly board = inject(TasksBoardStore);
}
