import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

import { PmBadge, type BadgeTone } from '../ui/badge/badge';
import { PmEmptyState, PmErrorState, PmLoadingState } from '../ui/state/state';
import { TasksBoardStore, type BoardTask } from './tasks-board.store';

@Component({
  selector: 'pm-tasks-board',
  imports: [PmBadge, PmEmptyState, PmErrorState, PmLoadingState, RouterLink, RouterOutlet],
  providers: [TasksBoardStore],
  templateUrl: './tasks-board.html',
  styleUrl: './tasks-board.css',
})
export class TasksBoard {
  protected readonly board = inject(TasksBoardStore);

  protected priorityTone(priority: string): BadgeTone {
    switch (priority.toLowerCase()) {
      case 'critical':
      case 'urgent':
      case 'high':
        return 'danger';
      case 'medium':
        return 'warning';
      case 'low':
        return 'neutral';
      default:
        return 'accent';
    }
  }

  protected dependencyTone(task: BoardTask): BadgeTone {
    if (task.dependencies.missing.length > 0) return 'danger';
    return task.dependencies.ready ? 'success' : 'warning';
  }

  protected dependencyLabel(task: BoardTask): string {
    return task.dependencies.ready ? 'Ready' : 'Blocked';
  }
}
