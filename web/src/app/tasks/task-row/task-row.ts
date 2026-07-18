import { Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';

import { PmBadge, type BadgeTone } from '../../ui/badge/badge';
import type { BoardTask } from '../tasks-board.store';
import { TaskNavigationService } from '../task-navigation.service';

@Component({
  selector: 'li[pmTaskRow]',
  imports: [PmBadge],
  templateUrl: './task-row.html',
  styleUrl: './task-row.css',
  host: { '[class.selected]': 'selected()' },
})
export class TaskRow {
  private readonly navigation = inject(TaskNavigationService);
  private readonly router = inject(Router);
  readonly task = input.required<BoardTask>();
  readonly selected = input.required<boolean>();

  protected open(event: MouseEvent): void {
    this.navigation.openDialog(event, this.router, this.task().id);
  }

  protected href(): string {
    return this.navigation.canonicalHref(this.router, this.task().id);
  }

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
}
