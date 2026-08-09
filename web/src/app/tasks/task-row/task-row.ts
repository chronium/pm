import { Component, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { cssBlock, cssLock, cssLockUnlock, cssUnblock } from '@ng-icons/css.gg';

import { PriorityIndicator } from '../../ui/priority-indicator/priority-indicator';
import { PmBadge } from '../../ui/badge/badge';
import type { BoardTask } from '../tasks-board.store';
import { TaskNavigationService } from '../task-navigation.service';

type StatusTone = 'neutral' | 'success' | 'warning' | 'danger';
type TaskRowLayout = 'board' | 'overview';

@Component({
  selector: 'li[pmTaskRow]',
  imports: [NgIcon, PmBadge, PriorityIndicator],
  templateUrl: './task-row.html',
  styleUrl: './task-row.css',
  providers: [provideIcons({ cssBlock, cssLock, cssLockUnlock, cssUnblock })],
  host: {
    '[class.selected]': 'selected()',
    '[attr.data-layout]': 'layout()',
  },
})
export class TaskRow {
  private readonly navigation = inject(TaskNavigationService);
  private readonly router = inject(Router);
  readonly task = input.required<BoardTask>();
  readonly selected = input.required<boolean>();
  readonly showState = input(false);
  readonly layout = input<TaskRowLayout>('board');

  protected open(event: MouseEvent): void {
    this.navigation.openDialog(event, this.router, this.task().id);
  }

  protected href(): string {
    return this.navigation.canonicalHref(this.router, this.task().id);
  }

  protected dependencyTone(task: BoardTask): StatusTone {
    if (task.dependencies.missing.length > 0) return 'danger';
    return task.dependencies.ready ? 'success' : 'warning';
  }

  protected dependencyIcon(task: BoardTask): string {
    return task.dependencies.ready ? 'cssUnblock' : 'cssBlock';
  }

  protected dependencyLabel(task: BoardTask): string {
    return `Dependencies: ${task.dependencies.ready ? 'ready' : 'blocked'}`;
  }

  protected dependencyTitle(task: BoardTask): string {
    return `${this.dependencyLabel(task)} — ${task.dependencies.summary}`;
  }

  protected activationTone(task: BoardTask): StatusTone {
    if (task.activation.isEligible) return 'success';
    return task.activation.milestoneLifecycle === 'inactive' ? 'warning' : 'neutral';
  }

  protected activationIcon(task: BoardTask): string {
    return task.activation.milestoneLifecycle === 'inactive' ? 'cssLock' : 'cssLockUnlock';
  }

  protected activationLabel(task: BoardTask): string {
    switch (task.activation.milestoneLifecycle) {
      case null:
        return 'Activation: ungated';
      case 'active':
        return 'Activation: eligible';
      case 'ready_to_deliver':
        return 'Activation: ready';
      case 'inactive':
        return 'Activation: inactive';
      case 'delivered':
        return 'Activation: delivered';
      default:
        return 'Activation: unavailable';
    }
  }

  protected activationTitle(task: BoardTask): string {
    return `${this.activationLabel(task)} — ${task.activation.summary}`;
  }
}
