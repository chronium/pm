import { DatePipe } from '@angular/common';
import {
  Component,
  effect,
  HostListener,
  inject,
  OnDestroy,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { MarkdownDisplay } from '../../markdown/markdown-display';
import { PmConfirmDialog } from '../../ui/confirm-dialog/confirm-dialog';
import { PmErrorState, PmLoadingState } from '../../ui/state/state';
import { TaskApiService, TaskDetailResource, type UpdateTaskRequest } from '../task-api.service';
import { TaskNavigationService } from '../task-navigation.service';
import { TasksBoardStore } from '../tasks-board.store';
import { TaskDialogShell } from './task-dialog-shell';
import type { DirtyDialogRoute } from './task-dialog.types';
import { TaskEditForm } from './task-edit-form';

type ConfirmKind = 'discard' | 'remove' | null;

@Component({
  selector: 'pm-task-detail-dialog',
  imports: [
    DatePipe,
    MarkdownDisplay,
    PmConfirmDialog,
    PmErrorState,
    PmLoadingState,
    RouterLink,
    TaskDialogShell,
    TaskEditForm,
  ],
  providers: [TaskDetailResource],
  templateUrl: './task-detail-dialog.html',
  styleUrl: './task-detail-dialog.css',
})
export class TaskDetailDialog implements DirtyDialogRoute, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(TaskApiService);
  private readonly navigation = inject(TaskNavigationService);
  protected readonly board = inject(TasksBoardStore);
  protected readonly detail = inject(TaskDetailResource);
  private readonly editForm = viewChild(TaskEditForm);
  protected readonly editing = signal(false);
  protected readonly pending = signal(false);
  protected readonly stale = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly confirmKind = signal<ConfirmKind>(null);
  private leaveResolver: ((answer: boolean) => void) | null = null;
  private allowLeave = false;

  private readonly routeTaskId = toSignal(this.route.paramMap, {
    initialValue: this.route.snapshot.paramMap,
  });

  constructor() {
    effect(() => this.detail.load(this.routeTaskId().get('taskId') ?? ''));
  }
  ngOnDestroy(): void {
    this.navigation.restoreFocus();
  }

  canDeactivate(): boolean | Promise<boolean> {
    if (this.allowLeave) return true;
    if (this.pending()) return false;
    if (!this.editing() || !this.editForm()?.dirty()) return true;
    this.confirmKind.set('discard');
    return new Promise((resolve) => (this.leaveResolver = resolve));
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.editing() && this.editForm()?.dirty() && !this.allowLeave) event.preventDefault();
  }
  protected close(): void {
    void this.router.navigate(['/tasks'], { queryParamsHandling: 'preserve', replaceUrl: true });
  }
  protected cancelEdit(): void {
    if (this.editForm()?.dirty()) this.confirmKind.set('discard');
    else this.editing.set(false);
  }
  protected confirm(): void {
    if (this.confirmKind() === 'remove') void this.remove();
    else {
      this.confirmKind.set(null);
      this.editing.set(false);
      this.stale.set(false);
      this.error.set(null);
      this.leaveResolver?.(true);
      this.leaveResolver = null;
    }
  }
  protected cancelConfirm(): void {
    this.confirmKind.set(null);
    this.leaveResolver?.(false);
    this.leaveResolver = null;
  }

  protected async save(request: UpdateTaskRequest): Promise<void> {
    const task = this.detail.task();
    if (!task || !this.detail.etag()) return;
    this.pending.set(true);
    this.error.set(null);
    try {
      const response = await firstValueFrom(this.api.update(task.id, request, this.detail.etag()));
      this.detail.accept(response);
      this.board.reload();
      this.editing.set(false);
      this.stale.set(false);
    } catch (error) {
      const failure = this.api.error(error, 'The task could not be saved.');
      this.error.set(
        failure.conflict
          ? 'This task changed elsewhere. Reload latest before saving again.'
          : failure.message,
      );
      this.stale.set(failure.conflict);
    } finally {
      this.pending.set(false);
    }
  }

  protected async changeState(state: string): Promise<void> {
    const task = this.detail.task();
    if (!task || state === task.state || !this.detail.etag()) return;
    this.pending.set(true);
    this.error.set(null);
    try {
      const response = await firstValueFrom(
        this.api.updateState(task.id, { state }, this.detail.etag()),
      );
      this.detail.accept(response);
      this.board.reload();
    } catch (error) {
      const failure = this.api.error(error, 'The task state could not be changed.');
      this.error.set(
        failure.conflict
          ? 'This task changed elsewhere. Reload it before changing state.'
          : failure.message,
      );
      this.stale.set(failure.conflict);
    } finally {
      this.pending.set(false);
    }
  }

  protected reloadLatest(): void {
    this.editing.set(false);
    this.stale.set(false);
    this.error.set(null);
    this.detail.reload();
  }
  private async remove(): Promise<void> {
    const task = this.detail.task();
    if (!task || !this.detail.etag()) return;
    this.pending.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.remove(task.id, this.detail.etag()));
      this.board.reload();
      this.allowLeave = true;
      await this.router.navigate(['/tasks'], { queryParamsHandling: 'preserve', replaceUrl: true });
    } catch (error) {
      const failure = this.api.error(error, 'The task could not be removed.');
      this.error.set(
        failure.conflict
          ? 'This task changed elsewhere. Reload it before removing.'
          : failure.message,
      );
      this.stale.set(failure.conflict);
    } finally {
      this.pending.set(false);
      this.confirmKind.set(null);
    }
  }
}
