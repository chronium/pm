import { DatePipe, TitleCasePipe } from '@angular/common';
import {
  Component,
  computed,
  effect,
  HostListener,
  inject,
  Injector,
  input,
  output,
  signal,
} from '@angular/core';
import { FormField, form, required, validate } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { cssClose, cssMaximize, cssNotes, cssPen } from '@ng-icons/css.gg';
import { firstValueFrom } from 'rxjs';

import { ExternalChangeBanner, type ExternalChangePhase } from '../../core/external-change-banner';
import { MarkdownDisplay } from '../../markdown/markdown-display';
import { MarkdownEditor } from '../../markdown/markdown-editor';
import { PmConfirmDialog } from '../../ui/confirm-dialog/confirm-dialog';
import { PmErrorState, PmLoadingState } from '../../ui/state/state';
import {
  TaskApiService,
  TaskDetailResource,
  type CreateTaskRequest,
  type TaskResponse,
  type UpdateTaskRequest,
} from '../task-api.service';
import { TaskNavigationService } from '../task-navigation.service';
import { TaskOptionsResource } from '../task-options.resource';
import { StaticModeService } from '../../static/static-mode.service';
import { AgentRunLaunch } from '../../agent-runs/agent-run-launch';

export type TaskWorkspacePresentation = 'dialog' | 'page';
export type TaskWorkspaceMode = 'detail' | 'create';
export type WorkspaceField =
  'title' | 'status' | 'track' | 'milestone' | 'priority' | 'description';

interface DraftModel {
  title: string;
  state: string;
  track: string;
  milestone: string;
  priority: string;
  description: string;
}

interface CreatedIntent {
  id: string;
  close: boolean;
}

type ConfirmKind = 'discard' | 'remove' | null;

@Component({
  selector: 'pm-task-workspace',
  imports: [
    DatePipe,
    AgentRunLaunch,
    ExternalChangeBanner,
    FormField,
    MarkdownDisplay,
    MarkdownEditor,
    NgIcon,
    PmConfirmDialog,
    PmErrorState,
    PmLoadingState,
    RouterLink,
    TitleCasePipe,
  ],
  providers: [TaskDetailResource, provideIcons({ cssClose, cssMaximize, cssNotes, cssPen })],
  templateUrl: './task-workspace.html',
  styleUrl: './task-workspace.css',
})
export class TaskWorkspace {
  readonly presentation = input.required<TaskWorkspacePresentation>();
  readonly mode = input.required<TaskWorkspaceMode>();
  readonly taskId = input<string | null>(null);
  readonly closeIntent = output<void>();
  readonly fullscreenIntent = output<void>();
  readonly created = output<CreatedIntent>();

  private readonly injector = inject(Injector);
  private readonly router = inject(Router);
  private readonly api = inject(TaskApiService);
  private readonly navigation = inject(TaskNavigationService);
  protected readonly staticMode = inject(StaticModeService);
  protected readonly options = inject(TaskOptionsResource);
  protected readonly detail = inject(TaskDetailResource);
  protected readonly activeField = signal<WorkspaceField | null>(null);
  readonly pending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly confirmKind = signal<ConfirmKind>(null);
  protected readonly conflictPhase = signal<ExternalChangePhase | null>(null);
  protected readonly noteFormOpen = signal(false);
  protected readonly noteDraft = signal('');
  protected readonly noteError = signal<string | null>(null);
  protected readonly launchOpen = signal(false);
  protected readonly recommendationReason = this.navigation.recommendationReason();
  protected readonly model = signal<DraftModel>({
    title: '',
    state: '',
    track: '',
    milestone: '',
    priority: 'inherit',
    description: '',
  });
  protected readonly workspaceForm = form(
    this.model,
    (task) => {
      required(task.title, { message: 'Title is required.' });
      required(task.state, { message: 'Status is required.' });
      required(task.track, { message: 'Track is required.' });
      required(task.priority, { message: 'Priority is required.' });
      validate(task.state, ({ value }) =>
        this.options.options()?.statuses.some((option) => option.key === value())
          ? undefined
          : { kind: 'configured-status', message: 'Choose a configured status before saving.' },
      );
      validate(task.track, ({ value }) =>
        this.options.options()?.tracks.some((option) => option.key === value())
          ? undefined
          : { kind: 'configured-track', message: 'Choose a configured track before saving.' },
      );
      validate(task.milestone, ({ value }) =>
        !value() || this.options.options()?.milestones.some((option) => option.key === value())
          ? undefined
          : {
              kind: 'configured-milestone',
              message: 'Choose a configured milestone or No milestone before saving.',
            },
      );
      validate(task.priority, ({ value }) =>
        value() === 'inherit' || this.options.options()?.priorityOptions.includes(value())
          ? undefined
          : { kind: 'configured-priority', message: 'Choose a configured priority before saving.' },
      );
    },
    { injector: this.injector },
  );

  private readonly accepted = signal<DraftModel | null>(null);
  private loadedRevision = '';
  private draftSnapshot: UpdateTaskRequest | null = null;
  private leaveResolver: ((answer: boolean) => void) | null = null;
  private allowLeave = false;

  protected readonly dirty = computed(() => {
    const accepted = this.accepted();
    return !!accepted && JSON.stringify(this.model()) !== JSON.stringify(accepted);
  });
  protected readonly noteDirty = computed(() => this.noteDraft().length > 0);
  protected readonly hasUnsavedChanges = computed(() => this.dirty() || this.noteDirty());
  protected readonly blocked = computed(
    () =>
      this.detail.unavailable() ||
      this.conflictPhase() === 'pending' ||
      this.conflictPhase() === 'reviewing',
  );
  protected readonly statusName = computed(() => this.optionName('statuses', this.model().state));
  protected readonly trackName = computed(() => this.optionName('tracks', this.model().track));
  protected readonly milestoneName = computed(() =>
    this.model().milestone ? this.optionName('milestones', this.model().milestone) : 'No milestone',
  );
  protected readonly priorityName = computed(() =>
    this.model().priority === 'inherit'
      ? `Inherited (${this.detail.task()?.priority ?? this.creationPriority()})`
      : this.label(this.model().priority),
  );

  constructor() {
    effect(() => {
      if (this.mode() === 'detail') this.detail.load(this.taskId() ?? '');
    });
    effect(() => {
      const task = this.detail.task();
      if (this.mode() !== 'detail' || !task || task.revision === this.loadedRevision) return;
      if (!this.dirty()) this.acceptTask(task);
    });
    effect(() => {
      const settings = this.options.options();
      if (this.mode() !== 'create' || !settings || this.accepted()) return;
      const query = this.router.parseUrl(this.router.url).queryParams;
      const track = settings.tracks.some((item) => item.key === query['track'])
        ? String(query['track'])
        : (settings.tracks[0]?.key ?? '');
      const milestone = settings.milestones.some((item) => item.key === query['milestone'])
        ? String(query['milestone'])
        : '';
      this.accept({
        title: '',
        state: settings.statuses[0]?.key ?? '',
        track,
        milestone,
        priority: 'inherit',
        description: '',
      });
    });
    effect(() => this.detail.setDirty(this.hasUnsavedChanges()));
    effect(() => {
      if (this.detail.pendingExternal()) {
        this.conflictPhase.set('pending');
      }
      if (this.detail.unavailable()) this.navigation.requestNavigationRefresh();
    });
  }

  canDeactivate(): boolean | Promise<boolean> {
    if (this.allowLeave) return true;
    if (this.pending()) return false;
    if (!this.hasUnsavedChanges()) return true;
    this.confirmKind.set('discard');
    return new Promise((resolve) => (this.leaveResolver = resolve));
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges() && !this.allowLeave) event.preventDefault();
  }

  protected activate(field: WorkspaceField): void {
    if (!this.staticMode.enabled && !this.pending() && !this.blocked()) this.activeField.set(field);
  }

  protected cancel(): void {
    const accepted = this.accepted();
    if (accepted) this.model.set({ ...accepted });
    this.workspaceForm().reset();
    this.activeField.set(null);
    this.error.set(null);
    this.conflictPhase.set(null);
    this.draftSnapshot = null;
  }

  protected openNoteForm(): void {
    if (
      this.staticMode.enabled ||
      this.pending() ||
      this.blocked() ||
      this.dirty() ||
      this.activeField()
    )
      return;
    this.noteError.set(null);
    this.noteFormOpen.set(true);
  }

  protected cancelNote(): void {
    this.noteDraft.set('');
    this.noteError.set(null);
    this.noteFormOpen.set(false);
  }

  protected updateNote(event: Event): void {
    this.noteDraft.set((event.target as HTMLTextAreaElement).value);
    this.noteError.set(null);
  }

  protected async appendNote(): Promise<void> {
    const task = this.detail.task();
    const note = this.noteDraft();
    if (!note.trim()) {
      this.noteError.set('Note text is required.');
      return;
    }
    if (!task || !this.detail.etag() || this.pending() || this.blocked() || this.dirty()) return;

    this.pending.set(true);
    this.noteError.set(null);
    try {
      const response = await firstValueFrom(
        this.api.appendNote(task.id, { note }, this.detail.etag()),
      );
      this.detail.accept(response);
      if (response.body) this.acceptTask(response.body);
      this.noteDraft.set('');
      this.noteFormOpen.set(false);
      this.navigation.requestNavigationRefresh();
    } catch (error) {
      const failure = this.api.error(error, 'The task note could not be added.');
      this.noteError.set(
        failure.conflict
          ? 'This task changed elsewhere. Review the latest version before adding the note.'
          : failure.message,
      );
      if (failure.conflict) this.detail.fetchLatest();
    } finally {
      this.pending.set(false);
    }
  }

  protected async save(close: boolean): Promise<void> {
    this.workspaceForm().markAsTouched();
    if (!this.workspaceForm().valid() || this.pending() || this.blocked()) return;
    this.pending.set(true);
    this.error.set(null);
    try {
      if (this.mode() === 'create') {
        const response = await firstValueFrom(this.api.create(this.createRequest()));
        if (!response.body) return;
        this.navigation.requestNavigationRefresh();
        this.allowLeave = true;
        this.created.emit({ id: response.body.id, close });
        return;
      }
      const task = this.detail.task();
      if (!task || !this.detail.etag()) return;
      const response = await firstValueFrom(
        this.api.update(task.id, this.updateRequest(), this.detail.etag()),
      );
      this.detail.accept(response);
      if (response.body) this.acceptTask(response.body);
      this.navigation.requestNavigationRefresh();
      this.activeField.set(null);
      this.conflictPhase.set(null);
      this.draftSnapshot = null;
      if (close) {
        this.allowLeave = true;
        this.closeIntent.emit();
      }
    } catch (error) {
      const failure = this.api.error(
        error,
        this.mode() === 'create'
          ? 'The task could not be created.'
          : 'The task could not be saved.',
      );
      this.error.set(
        failure.conflict
          ? 'This task changed elsewhere. Review the latest version.'
          : failure.message,
      );
      if (failure.conflict) this.detail.fetchLatest();
    } finally {
      this.pending.set(false);
    }
  }

  protected requestClose(): void {
    this.closeIntent.emit();
  }

  protected dependencyClick(event: MouseEvent, id: string): void {
    if (this.presentation() === 'dialog') this.navigation.openDialog(event, this.router, id);
  }

  protected reviewLatest(): void {
    if (!this.detail.pendingExternal()) return;
    this.draftSnapshot = this.updateRequest();
    this.detail.reviewLatest();
    this.conflictPhase.set('reviewing');
  }

  protected restoreDraft(): void {
    if (!this.draftSnapshot) return;
    const draft = this.draftSnapshot;
    this.model.set({
      title: draft.title,
      state: draft.state,
      track: draft.placement?.track ?? '',
      milestone: draft.placement?.milestone ?? '',
      priority: draft.priority,
      description: draft.description,
    });
    this.conflictPhase.set('preserved');
  }

  protected keepLatest(): void {
    const task = this.detail.task();
    if (task) this.acceptTask(task);
    this.detail.keepLatest();
    this.draftSnapshot = null;
    this.conflictPhase.set(null);
    this.error.set(null);
  }

  protected confirm(): void {
    if (this.confirmKind() === 'remove') void this.remove();
    else {
      this.confirmKind.set(null);
      this.allowLeave = true;
      this.leaveResolver?.(true);
      this.leaveResolver = null;
    }
  }

  protected cancelConfirm(): void {
    this.confirmKind.set(null);
    this.leaveResolver?.(false);
    this.leaveResolver = null;
  }

  protected firstError(field: { errors(): readonly { message?: string }[] }): string | null {
    return field.errors()[0]?.message ?? null;
  }

  private async remove(): Promise<void> {
    const task = this.detail.task();
    if (!task || !this.detail.etag()) return;
    this.pending.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.remove(task.id, this.detail.etag()));
      this.navigation.requestNavigationRefresh();
      this.allowLeave = true;
      this.closeIntent.emit();
    } catch (error) {
      const failure = this.api.error(error, 'The task could not be removed.');
      this.error.set(
        failure.conflict ? 'The task was not removed; review the latest version.' : failure.message,
      );
      if (failure.conflict) this.detail.fetchLatest();
    } finally {
      this.pending.set(false);
      this.confirmKind.set(null);
    }
  }

  private acceptTask(task: TaskResponse): void {
    this.loadedRevision = task.revision;
    this.accept({
      title: task.title,
      state: task.state,
      track: task.track,
      milestone: task.milestone ?? '',
      priority: task.prioritySelection,
      description: task.description,
    });
  }

  private accept(value: DraftModel): void {
    this.accepted.set({ ...value });
    this.model.set({ ...value });
    this.workspaceForm().reset();
  }

  private updateRequest(): UpdateTaskRequest {
    const value = this.model();
    return {
      title: value.title.trim(),
      state: value.state,
      priority: value.priority,
      description: value.description,
      placement: { track: value.track, milestone: value.milestone || null },
    };
  }

  private createRequest(): CreateTaskRequest {
    const value = this.model();
    return {
      title: value.title.trim(),
      track: value.track,
      milestone: value.milestone || null,
      description: value.description,
    };
  }

  private optionName(collection: 'statuses' | 'tracks' | 'milestones', key: string): string {
    const option = this.options.options()?.[collection].find((item) => item.key === key);
    return option ? ('name' in option ? option.name : option.title) : key;
  }

  protected creationPriority(): string {
    const milestone = this.options
      .options()
      ?.milestones.find((item) => item.key === this.model().milestone);
    return milestone?.priority ?? 'project default';
  }

  private label(value: string): string {
    return value ? value.charAt(0).toUpperCase() + value.slice(1) : value;
  }
}
