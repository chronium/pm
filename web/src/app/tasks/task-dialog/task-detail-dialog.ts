import { DatePipe } from '@angular/common';
import {
  Component,
  computed,
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
import { ExternalChangeBanner, type ExternalChangePhase } from '../../core/external-change-banner';

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
    ExternalChangeBanner,
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
  protected readonly conflictPhase = signal<ExternalChangePhase | null>(null);
  protected readonly conflictBlocked = computed(
    () =>
      this.stale() ||
      this.detail.unavailable() ||
      this.conflictPhase() === 'pending' ||
      this.conflictPhase() === 'reviewing',
  );
  protected readonly error = signal<string | null>(null);
  protected readonly confirmKind = signal<ConfirmKind>(null);
  private leaveResolver: ((answer: boolean) => void) | null = null;
  private allowLeave = false;
  private draftSnapshot: UpdateTaskRequest | null = null;

  private readonly routeTaskId = toSignal(this.route.paramMap, {
    initialValue: this.route.snapshot.paramMap,
  });

  constructor() {
    effect(() => this.detail.load(this.routeTaskId().get('taskId') ?? ''));
    effect(() => {
      const dirty = !!this.editForm()?.dirty() || !!this.draftSnapshot;
      this.detail.setDirty(this.editing() && dirty);
    });
    effect(() => {
      if (this.detail.pendingExternal()) {
        this.conflictPhase.set('pending');
        this.stale.set(true);
      }
      if (this.detail.unavailable()) {
        this.stale.set(true);
        this.board.refreshNow();
      }
    });
  }
  ngOnDestroy(): void {
    this.navigation.restoreFocus();
  }

  canDeactivate(): boolean | Promise<boolean> {
    if (this.allowLeave) return true;
    if (this.pending()) return false;
    if (!this.editing() || (!this.editForm()?.dirty() && !this.draftSnapshot)) return true;
    this.confirmKind.set('discard');
    return new Promise((resolve) => (this.leaveResolver = resolve));
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.editing() && (this.editForm()?.dirty() || this.draftSnapshot) && !this.allowLeave)
      event.preventDefault();
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
      this.conflictPhase.set(null);
      this.draftSnapshot = null;
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
      this.navigation.requestNavigationRefresh();
      this.editing.set(false);
      this.stale.set(false);
      this.conflictPhase.set(null);
      this.draftSnapshot = null;
    } catch (error) {
      const failure = this.api.error(error, 'The task could not be saved.');
      this.error.set(
        failure.conflict
          ? 'This task changed elsewhere. Review the latest version.'
          : failure.message,
      );
      if (failure.conflict) {
        this.detail.setDirty(true);
        this.detail.fetchLatest();
      }
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
      this.navigation.requestNavigationRefresh();
    } catch (error) {
      const failure = this.api.error(error, 'The task state could not be changed.');
      this.error.set(
        failure.conflict
          ? 'The requested state change was not applied; the latest task is loading.'
          : failure.message,
      );
      if (failure.conflict) this.detail.fetchLatest();
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
  protected reviewLatest(): void {
    const form = this.editForm();
    if (!form || !this.detail.pendingExternal()) return;
    this.draftSnapshot = form.draft();
    this.detail.reviewLatest();
    this.conflictPhase.set('reviewing');
    this.stale.set(true);
  }

  protected restoreDraft(): void {
    if (!this.draftSnapshot) return;
    this.editForm()?.restoreDraft(this.draftSnapshot);
    this.conflictPhase.set('preserved');
    this.stale.set(false);
  }

  protected keepLatest(): void {
    this.draftSnapshot = null;
    this.detail.keepLatest();
    this.conflictPhase.set(null);
    this.stale.set(false);
    this.error.set(null);
  }
  private async remove(): Promise<void> {
    const task = this.detail.task();
    if (!task || !this.detail.etag()) return;
    this.pending.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.remove(task.id, this.detail.etag()));
      this.board.reload();
      this.navigation.requestNavigationRefresh();
      this.allowLeave = true;
      await this.router.navigate(['/tasks'], { queryParamsHandling: 'preserve', replaceUrl: true });
    } catch (error) {
      const failure = this.api.error(error, 'The task could not be removed.');
      this.error.set(
        failure.conflict
          ? 'The task was not removed; the latest version is loading.'
          : failure.message,
      );
      if (failure.conflict) this.detail.fetchLatest();
    } finally {
      this.pending.set(false);
      this.confirmKind.set(null);
    }
  }
}
