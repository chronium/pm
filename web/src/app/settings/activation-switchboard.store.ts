import { HttpResponse } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom, type Observable } from 'rxjs';

import { PollingCoordinator } from '../core/polling-coordinator';
import {
  ActivationApiService,
  type ActivationApiError,
  type ActivationMutationResponse,
  type ActivationRedefinitionPreview,
  type ActivationRequirementRequest,
  type ActivationSwitchboardResponse,
  type CreateActivationTriggerRequest,
} from './activation-api.service';

export type ActivationOperationKind =
  | 'create'
  | 'activate'
  | 'override'
  | 'reset'
  | 'preview-redefine'
  | 'redefine'
  | 'preview-reconcile'
  | 'reconcile';

export interface ActivationOperation {
  kind: ActivationOperationKind;
  key: string | null;
}

export interface ActivationOperationFailure {
  operation: ActivationOperation;
  error: ActivationApiError;
}

export interface ActivationReconciliationPreview {
  revision: string;
  triggerKeys: string[];
  milestoneKeys: string[];
}

@Injectable()
export class ActivationSwitchboardStore {
  private readonly api = inject(ActivationApiService);
  private readonly polling = inject(PollingCoordinator);
  private readonly retained = signal<ActivationSwitchboardResponse | null>(null);
  private readonly operationState = signal<ActivationOperation | null>(null);
  private readonly failureState = signal<ActivationOperationFailure | null>(null);

  readonly switchboard = computed(() => this.retained());
  readonly loading = signal(true);
  readonly refreshing = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly operation = this.operationState.asReadonly();
  readonly failure = this.failureState.asReadonly();
  readonly pending = computed(() => this.operationState() !== null);
  readonly statusMessage = signal<string | null>(null);
  readonly pollSession = this.polling.create<ActivationSwitchboardResponse>({
    target: () => {
      const current = this.switchboard();
      return current ? { url: '/api/v1/activation', etag: `"${current.revision}"` } : null;
    },
    accept: (response) => {
      if (response.body) this.accept(response.body);
    },
  });
  readonly liveUpdateUnavailable = computed(() => this.pollSession.state() === 'retrying');

  constructor() {
    void this.reload(false);
  }

  async reload(refreshing = true): Promise<boolean> {
    this.loadError.set(null);
    if (refreshing && this.switchboard()) this.refreshing.set(true);
    else this.loading.set(true);
    try {
      const response = await firstValueFrom(this.api.read());
      if (!response.body) throw new Error('The activation switchboard returned no data.');
      this.accept(response.body);
      this.pollSession.start();
      return true;
    } catch (error) {
      this.loadError.set(
        this.api.error(error, 'The activation switchboard could not be loaded.').message,
      );
      return false;
    } finally {
      this.loading.set(false);
      this.refreshing.set(false);
    }
  }

  suspendLiveUpdates(): void {
    this.pollSession.stop();
  }

  resumeLiveUpdates(): void {
    this.pollSession.start(true);
  }

  clearFailure(): void {
    this.failureState.set(null);
  }

  isPending(kind: ActivationOperationKind, key: string | null): boolean {
    return this.operation()?.kind === kind && this.operation()?.key === key;
  }

  errorFor(key: string | null): string | null {
    return this.failure()?.operation.key === key ? this.failure()!.error.message : null;
  }

  create(request: CreateActivationTriggerRequest): Promise<boolean> {
    return this.mutate(
      { kind: 'create', key: request.key },
      'The activation trigger could not be created.',
      (revision) => this.api.create(request, revision),
      'Activation trigger created.',
    );
  }

  activate(key: string): Promise<boolean> {
    return this.mutate(
      { kind: 'activate', key },
      'The trigger could not be activated.',
      (revision) => this.api.activate(key, revision),
      'Trigger activated.',
    );
  }

  override(key: string, reason: string): Promise<boolean> {
    return this.mutate(
      { kind: 'override', key },
      'The trigger could not be overridden.',
      (revision) => this.api.override(key, reason, revision),
      'Trigger activated by override.',
    );
  }

  reset(key: string): Promise<boolean> {
    return this.mutate(
      { kind: 'reset', key },
      'The trigger could not be reset.',
      (revision) => this.api.reset(key, revision),
      'Trigger reset.',
    );
  }

  async previewRedefinition(
    key: string,
    requirements: ActivationRequirementRequest[],
  ): Promise<ActivationRedefinitionPreview | null> {
    const operation = { kind: 'preview-redefine', key } satisfies ActivationOperation;
    const revision = this.switchboard()?.revision;
    if (!revision || this.pending()) return null;
    this.begin(operation);
    try {
      const response = await firstValueFrom(
        this.api.previewRedefinition(key, requirements, revision),
      );
      if (!response.body) throw new Error('The redefinition preview returned no data.');
      return response.body;
    } catch (error) {
      await this.fail(operation, error, 'The redefinition impact could not be reviewed.');
      return null;
    } finally {
      this.operationState.set(null);
    }
  }

  redefine(
    key: string,
    requirements: ActivationRequirementRequest[],
    preview: ActivationRedefinitionPreview,
  ): Promise<boolean> {
    return this.mutate(
      { kind: 'redefine', key },
      'The trigger could not be redefined.',
      (revision) =>
        this.api.redefine(
          key,
          requirements,
          preview.previewRevision,
          preview.requiresConfirmation,
          revision,
        ),
      'Trigger definition updated.',
    );
  }

  async previewReconciliation(): Promise<ActivationReconciliationPreview | null> {
    const operation = { kind: 'preview-reconcile', key: null } satisfies ActivationOperation;
    const revision = this.switchboard()?.revision;
    if (!revision || this.pending()) return null;
    this.begin(operation);
    try {
      const response = await firstValueFrom(this.api.reconcile(true, revision));
      if (!response.body) throw new Error('The reconciliation preview returned no data.');
      this.accept(response.body.switchboard);
      return {
        revision: response.body.switchboard.revision,
        triggerKeys: response.body.impact?.automaticallyActivatedTriggers ?? [],
        milestoneKeys: response.body.impact?.affectedMilestones ?? [],
      };
    } catch (error) {
      await this.fail(operation, error, 'Reconciliation impact could not be reviewed.');
      return null;
    } finally {
      this.operationState.set(null);
    }
  }

  reconcile(): Promise<boolean> {
    return this.mutate(
      { kind: 'reconcile', key: null },
      'Activation reconciliation failed.',
      (revision) => this.api.reconcile(false, revision),
      'Activation records reconciled.',
    );
  }

  private async mutate(
    operation: ActivationOperation,
    fallback: string,
    request: (revision: string) => Observable<HttpResponse<ActivationMutationResponse>>,
    success: string,
  ): Promise<boolean> {
    const revision = this.switchboard()?.revision;
    if (!revision || this.pending()) return false;
    this.begin(operation);
    try {
      const response = await firstValueFrom(request(revision));
      if (!response.body) throw new Error('The activation mutation returned no data.');
      this.accept(response.body.switchboard);
      this.statusMessage.set(success);
      return true;
    } catch (error) {
      await this.fail(operation, error, fallback);
      return false;
    } finally {
      this.operationState.set(null);
    }
  }

  private begin(operation: ActivationOperation): void {
    this.operationState.set(operation);
    this.failureState.set(null);
    this.statusMessage.set(null);
  }

  private async fail(
    operation: ActivationOperation,
    error: unknown,
    fallback: string,
  ): Promise<void> {
    const mapped = this.api.error(error, fallback);
    this.failureState.set({ operation, error: mapped });
    if (mapped.conflict) {
      await this.reload(false);
      this.statusMessage.set(
        'Activation state changed elsewhere. Review the latest state and try again.',
      );
    }
  }

  private accept(value: ActivationSwitchboardResponse): void {
    this.retained.set(value);
    this.loadError.set(null);
  }
}
