import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PmBadge, type BadgeTone } from '../../ui/badge/badge';
import type { BoardTask } from '../tasks-board.store';

@Component({
  selector: 'li[pmTaskRow]',
  imports: [PmBadge, RouterLink],
  templateUrl: './task-row.html',
  styleUrl: './task-row.css',
  host: { '[class.selected]': 'selected()' },
})
export class TaskRow {
  readonly task = input.required<BoardTask>();
  readonly selected = input.required<boolean>();

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
