import { Component, HostListener, inject, OnDestroy, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { PmConfirmDialog } from '../../ui/confirm-dialog/confirm-dialog';
import { TaskApiService, type CreateTaskRequest } from '../task-api.service';
import { TaskNavigationService } from '../task-navigation.service';
import { TasksBoardStore } from '../tasks-board.store';
import { TaskCreateForm } from './task-create-form';
import { TaskDialogShell } from './task-dialog-shell';
import type { DirtyDialogRoute } from './task-dialog.types';

@Component({
  selector: 'pm-task-create-dialog',
  imports: [PmConfirmDialog, TaskCreateForm, TaskDialogShell],
  templateUrl: './task-create-dialog.html',
})
export class TaskCreateDialog implements DirtyDialogRoute, OnDestroy {
  private readonly router = inject(Router);
  private readonly api = inject(TaskApiService);
  private readonly navigation = inject(TaskNavigationService);
  protected readonly board = inject(TasksBoardStore);
  private readonly form = viewChild(TaskCreateForm);
  protected readonly pending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly confirmOpen = signal(false);
  private leaveResolver: ((answer: boolean) => void) | null = null;
  private allowLeave = false;
  private transitioningToDetail = false;

  ngOnDestroy(): void {
    if (!this.transitioningToDetail) this.navigation.restoreFocus();
  }

  canDeactivate(): boolean | Promise<boolean> {
    if (this.allowLeave) return true;
    if (this.pending()) return false;
    if (!this.form()?.dirty()) return true;
    this.confirmOpen.set(true);
    return new Promise((resolve) => (this.leaveResolver = resolve));
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.form()?.dirty() && !this.allowLeave) event.preventDefault();
  }

  protected close(): void {
    void this.router.navigate(['/tasks'], { queryParamsHandling: 'preserve', replaceUrl: true });
  }

  protected async create(request: CreateTaskRequest): Promise<void> {
    this.pending.set(true);
    this.error.set(null);
    try {
      const response = await firstValueFrom(this.api.create(request));
      this.board.reload();
      this.navigation.requestNavigationRefresh();
      this.allowLeave = true;
      this.transitioningToDetail = true;
      await this.router.navigate(['/tasks', response.body!.id], {
        queryParamsHandling: 'preserve',
        replaceUrl: true,
      });
    } catch (error) {
      this.error.set(this.api.error(error, 'The task could not be created.').message);
    } finally {
      this.pending.set(false);
    }
  }

  protected discard(): void {
    this.confirmOpen.set(false);
    this.allowLeave = true;
    this.leaveResolver?.(true);
    this.leaveResolver = null;
  }

  protected keepEditing(): void {
    this.confirmOpen.set(false);
    this.leaveResolver?.(false);
    this.leaveResolver = null;
  }
}
