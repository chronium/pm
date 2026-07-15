import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PmBadge, type BadgeTone } from '../../ui/badge/badge';
import type { BoardTask } from '../tasks-board.store';
import { TaskNavigationService } from '../task-navigation.service';

@Component({
  selector: 'li[pmTaskRow]',
  imports: [PmBadge, RouterLink],
  templateUrl: './task-row.html',
  styleUrl: './task-row.css',
  host: { '[class.selected]': 'selected()' },
})
export class TaskRow {
  private readonly navigation = inject(TaskNavigationService);
  readonly task = input.required<BoardTask>();
  readonly selected = input.required<boolean>();

  protected captureOrigin(event: MouseEvent): void {
    this.navigation.captureOrigin(event.currentTarget);
  }

  protected priorityTone(priority: string): BadgeTone {
    switch (priority.toLowerCase()) {
      case 'critical': case 'urgent': case 'high': return 'danger';
      case 'medium': return 'warning';
      case 'low': return 'neutral';
      default: return 'accent';
    }
  }

  protected dependencyTone(task: BoardTask): BadgeTone {
    if (task.dependencies.missing.length > 0) return 'danger';
    return task.dependencies.ready ? 'success' : 'warning';
  }
}
