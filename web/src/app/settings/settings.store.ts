import { HttpErrorResponse, HttpResponse, httpResource } from '@angular/common/http';
import { computed, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { firstValueFrom, type Observable } from 'rxjs';
import { PollingCoordinator } from '../core/polling-coordinator';

import {
  SettingsApiService,
  type CreateMilestoneRequest,
  type CreateSettingsOptionRequest,
  type RenameMilestoneRequest,
  type RenameSettingsOptionRequest,
  type SetMilestonePriorityRequest,
  type SettingsApiError,
  type SettingsMutationResponse,
  type SettingsResponse,
  type ValidationResponse,
} from './settings-api.service';

export type SettingsCollection = 'status' | 'track' | 'milestone';
export interface SettingsOperation {
  kind: 'create' | 'rename' | 'priority' | 'remove';
  collection: SettingsCollection;
  key: string | null;
}

@Injectable()
export class SettingsStore {
  private readonly api = inject(SettingsApiService);
  private readonly polling = inject(PollingCoordinator);
  private readonly retainedSettings = signal<SettingsResponse | undefined>(undefined);
  private readonly acceptedRevision = signal<string | null>(null);
  private readonly dirtyState = signal(false);
  private readonly pendingExternalSettings = signal<SettingsResponse | null>(null);
  private readonly mutationCount = signal(0);
  private readonly activeOperation = signal<SettingsOperation | null>(null);
  private readonly operationErrorState = signal<{
    operation: SettingsOperation;
    error: SettingsApiError;
  } | null>(null);
  private mutationQueue: Promise<void> = Promise.resolve();
  private reloadRevision: string | null = null;

  readonly settingsResource = httpResource<SettingsResponse>(() => '/api/v1/settings');
  readonly validationResource = httpResource<ValidationResponse>(() => '/api/v1/validation');
  readonly settings = computed(() => this.retainedSettings());
  readonly validation = computed(() =>
    this.validationResource.hasValue() ? this.validationResource.value() : undefined,
  );
  readonly loading = computed(() => this.settingsResource.isLoading() && !this.settings());
  readonly refreshing = computed(() => this.settingsResource.isLoading() && !!this.settings());
  readonly settingsError = computed(() =>
    this.readError(this.settingsResource.error(), 'Project settings could not be loaded.'),
  );
  readonly validationLoading = computed(
    () => this.validationResource.isLoading() && !this.validation(),
  );
  readonly validationRefreshing = computed(
    () => this.validationResource.isLoading() && !!this.validation(),
  );
  readonly validationError = computed(() =>
    this.readError(this.validationResource.error(), 'Project health could not be loaded.'),
  );
  readonly pending = computed(() => this.mutationCount() > 0);
  readonly operation = this.activeOperation.asReadonly();
  readonly operationError = this.operationErrorState.asReadonly();
  readonly stale = signal(false);
  readonly reloadGeneration = signal(0);
  readonly pendingExternal = computed(() => this.pendingExternalSettings());
  readonly pollSession = this.polling.create<SettingsResponse>({
    target: () =>
      this.settings() && this.acceptedRevision()
        ? { url: '/api/v1/settings', etag: `"${this.acceptedRevision()}"` }
        : null,
    accept: (response) => this.acceptExternal(response),
  });
  readonly liveUpdateUnavailable = computed(() => this.pollSession.state() === 'retrying');

  constructor() {
    effect(() => {
      if (!this.settingsResource.hasValue()) return;
      const settings = this.settingsResource.value();
      untracked(() => {
        this.acceptedRevision.set(settings.revision);
        if (this.dirtyState() && this.retainedSettings()?.revision !== settings.revision) {
          this.pendingExternalSettings.set(settings);
        } else {
          this.retainedSettings.set(settings);
        }
        if (this.reloadRevision !== null && settings.revision !== this.reloadRevision) {
          this.finishReload();
        } else if (this.reloadRevision !== null && !this.settingsResource.isLoading()) {
          this.finishReload();
        }
        this.pollSession.start();
      });
    });
  }

  createStatus(request: CreateSettingsOptionRequest) {
    return this.enqueue({ kind: 'create', collection: 'status', key: request.key }, (revision) =>
      this.api.createStatus(request, revision),
    );
  }
  renameStatus(key: string, request: RenameSettingsOptionRequest) {
    return this.enqueue({ kind: 'rename', collection: 'status', key }, (revision) =>
      this.api.renameStatus(key, request, revision),
    );
  }
  removeStatus(key: string) {
    return this.enqueue({ kind: 'remove', collection: 'status', key }, (revision) =>
      this.api.removeStatus(key, revision),
    );
  }
  createTrack(request: CreateSettingsOptionRequest) {
    return this.enqueue({ kind: 'create', collection: 'track', key: request.key }, (revision) =>
      this.api.createTrack(request, revision),
    );
  }
  renameTrack(key: string, request: RenameSettingsOptionRequest) {
    return this.enqueue({ kind: 'rename', collection: 'track', key }, (revision) =>
      this.api.renameTrack(key, request, revision),
    );
  }
  removeTrack(key: string) {
    return this.enqueue({ kind: 'remove', collection: 'track', key }, (revision) =>
      this.api.removeTrack(key, revision),
    );
  }
  createMilestone(request: CreateMilestoneRequest) {
    return this.enqueue({ kind: 'create', collection: 'milestone', key: request.key }, (revision) =>
      this.api.createMilestone(request, revision),
    );
  }
  renameMilestone(key: string, request: RenameMilestoneRequest) {
    return this.enqueue({ kind: 'rename', collection: 'milestone', key }, (revision) =>
      this.api.renameMilestone(key, request, revision),
    );
  }
  setMilestonePriority(key: string, request: SetMilestonePriorityRequest) {
    return this.enqueue({ kind: 'priority', collection: 'milestone', key }, (revision) =>
      this.api.setMilestonePriority(key, request, revision),
    );
  }
  removeMilestone(key: string) {
    return this.enqueue({ kind: 'remove', collection: 'milestone', key }, (revision) =>
      this.api.removeMilestone(key, revision),
    );
  }

  reloadLatest(): boolean {
    if (this.settingsResource.isLoading()) return false;
    this.reloadRevision = this.settings()?.revision ?? '';
    return this.settingsResource.reload();
  }

  setDirty(dirty: boolean): void {
    this.dirtyState.set(dirty);
  }

  reviewLatest(): SettingsResponse | null {
    const latest = this.pendingExternalSettings();
    if (!latest) return null;
    this.retainedSettings.set(latest);
    this.pendingExternalSettings.set(null);
    this.stale.set(true);
    return latest;
  }

  keepLatest(): void {
    this.pendingExternalSettings.set(null);
    this.dirtyState.set(false);
    this.stale.set(false);
    this.operationErrorState.set(null);
  }

  fetchLatest(): void {
    this.pollSession.restart(true);
  }

  reloadValidation(): boolean {
    return this.validationResource.reload();
  }
  clearOperationError(): void {
    this.operationErrorState.set(null);
  }

  errorFor(collection: SettingsCollection, key: string | null): string | null {
    const current = this.operationError();
    return current?.operation.collection === collection && current.operation.key === key
      ? current.error.message
      : null;
  }

  isPending(operation: SettingsOperation): boolean {
    const current = this.operation();
    return (
      !!current &&
      current.kind === operation.kind &&
      current.collection === operation.collection &&
      current.key === operation.key
    );
  }

  private enqueue(
    operation: SettingsOperation,
    request: (revision: string) => Observable<SettingsMutationResponse>,
  ): Promise<boolean> {
    if (this.stale()) return Promise.resolve(false);
    this.mutationCount.update((count) => count + 1);
    let succeeded = false;
    const execute = async () => {
      if (this.stale()) return;
      const revision = this.acceptedRevision() ?? this.settings()?.revision;
      if (!revision) return;
      this.activeOperation.set(operation);
      this.operationErrorState.set(null);
      try {
        const response = await firstValueFrom(request(revision));
        if (response.body) {
          this.retainedSettings.set(response.body);
          this.acceptedRevision.set(response.body.revision);
          this.pendingExternalSettings.set(null);
        }
        succeeded = true;
        this.validationResource.reload();
      } catch (error) {
        const mapped = this.api.error(error, 'The settings change failed.');
        this.operationErrorState.set({ operation, error: mapped });
        if (mapped.conflict) {
          this.stale.set(true);
          this.reloadLatest();
        }
      } finally {
        this.activeOperation.set(null);
      }
    };
    const result = this.mutationQueue
      .then(execute, execute)
      .then(() => succeeded)
      .finally(() => this.mutationCount.update((count) => count - 1));
    this.mutationQueue = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }

  private finishReload(): void {
    this.reloadRevision = null;
    if (!this.pendingExternalSettings()) {
      this.stale.set(false);
      this.operationErrorState.set(null);
      this.reloadGeneration.update((value) => value + 1);
    }
  }

  private acceptExternal(response: HttpResponse<SettingsResponse>): void {
    if (!response.body) return;
    this.acceptedRevision.set(response.body.revision);
    if (this.dirtyState()) {
      this.pendingExternalSettings.set(response.body);
      this.stale.set(true);
    } else {
      this.retainedSettings.set(response.body);
      this.pendingExternalSettings.set(null);
      this.stale.set(false);
      this.validationResource.reload();
    }
  }

  private readError(error: Error | undefined, fallback: string): string | null {
    if (!error) return null;
    if (error instanceof HttpErrorResponse) return this.api.error(error, fallback).message;
    return error.message || fallback;
  }
}
