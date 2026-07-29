import { DatePipe } from '@angular/common';
import { Component, effect, ElementRef, inject, Injector, signal, viewChild } from '@angular/core';
import { FormField, form, required } from '@angular/forms/signals';

import {
  AgentRunsApiService,
  type AgentRunnerRegistration,
  type AgentRunnerStatus,
} from '../agent-runs/agent-runs-api.service';
import { PmConfirmDialog } from '../ui/confirm-dialog/confirm-dialog';
import { PmEmptyState, PmErrorState, PmLoadingState } from '../ui/state/state';

interface RunnerStatusState {
  value: AgentRunnerStatus | null;
  loading: boolean;
  error: string | null;
}

type RunnerAction = { kind: 'rotate' | 'remove'; runner: AgentRunnerRegistration };

@Component({
  selector: 'pm-agent-runners',
  imports: [DatePipe, FormField, PmConfirmDialog, PmEmptyState, PmErrorState, PmLoadingState],
  templateUrl: './agent-runners.html',
  styleUrl: './agent-runners.css',
})
export class AgentRunners {
  private readonly api = inject(AgentRunsApiService);
  private readonly injector = inject(Injector);
  private readonly pairingDialog =
    viewChild.required<ElementRef<HTMLDialogElement>>('pairingDialog');

  protected readonly runners = signal<AgentRunnerRegistration[]>([]);
  protected readonly statuses = signal<Record<string, RunnerStatusState>>({});
  protected readonly loading = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly pending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly pairError = signal<string | null>(null);
  protected readonly pairingOpen = signal(false);
  protected readonly action = signal<RunnerAction | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly pairModel = signal({
    endpoint: '',
    runnerId: '',
    tlsFingerprint: '',
    pairingCode: '',
    replaceExisting: false,
  });
  protected readonly pairForm = form(
    this.pairModel,
    (item) => {
      required(item.endpoint, { message: 'Endpoint is required.' });
      required(item.runnerId, { message: 'Runner ID is required.' });
      required(item.tlsFingerprint, { message: 'TLS fingerprint is required.' });
      required(item.pairingCode, { message: 'Pairing code is required.' });
    },
    { injector: this.injector },
  );

  constructor() {
    effect(() => {
      const dialog = this.pairingDialog().nativeElement;
      if (this.pairingOpen() && !dialog.open) dialog.showModal?.();
      else if (!this.pairingOpen() && dialog.open) dialog.close?.();
    });
    this.reload(false);
  }

  protected reload(refreshing = true): void {
    this.error.set(null);
    if (refreshing && this.runners().length) this.refreshing.set(true);
    else this.loading.set(true);
    this.api.listRunners().subscribe({
      next: (response) => {
        const registrations = response.body ?? [];
        this.runners.set(registrations);
        this.loading.set(false);
        this.refreshing.set(false);
        this.statuses.update((current) =>
          Object.fromEntries(
            registrations.map((runner) => [
              runner.runnerId,
              current[runner.runnerId] ?? { value: null, loading: true, error: null },
            ]),
          ),
        );
        for (const runner of registrations) this.reloadStatus(runner.runnerId);
      },
      error: (error) => {
        this.loading.set(false);
        this.refreshing.set(false);
        this.error.set(this.api.error(error, 'Agent runners could not be loaded.').message);
      },
    });
  }

  protected reloadStatus(runnerId: string): void {
    this.setStatus(runnerId, { value: this.status(runnerId).value, loading: true, error: null });
    this.api.runnerStatus(runnerId).subscribe({
      next: (response) =>
        this.setStatus(runnerId, { value: response.body, loading: false, error: null }),
      error: (error) =>
        this.setStatus(runnerId, {
          value: null,
          loading: false,
          error: this.api.error(error, 'Runner status is unavailable.').message,
        }),
    });
  }

  protected openPairing(): void {
    this.pairError.set(null);
    this.pairModel.set({
      endpoint: '',
      runnerId: '',
      tlsFingerprint: '',
      pairingCode: '',
      replaceExisting: false,
    });
    this.pairForm().reset();
    this.pairingOpen.set(true);
  }

  protected closePairing(): void {
    if (this.pending()) return;
    this.pairForm.pairingCode().reset('');
    this.pairError.set(null);
    this.pairingOpen.set(false);
  }

  protected pair(event: Event): void {
    event.preventDefault();
    this.pairForm().markAsTouched();
    if (!this.pairForm().valid() || this.pending()) return;
    const value = this.pairModel();
    this.pending.set(true);
    this.pairError.set(null);
    this.api
      .pairRunner({
        endpoint: value.endpoint.trim(),
        runnerId: value.runnerId.trim(),
        tlsFingerprint: value.tlsFingerprint.trim(),
        pairingCode: value.pairingCode.trim(),
        replaceExisting: value.replaceExisting,
      })
      .subscribe({
        next: (response) => {
          this.pending.set(false);
          this.closePairing();
          if (response.body) {
            this.runners.update((items) => [
              ...items.filter((runner) => runner.runnerId !== response.body!.runnerId),
              response.body!,
            ]);
            this.reloadStatus(response.body.runnerId);
          }
        },
        error: (error) => {
          this.pending.set(false);
          this.pairForm.pairingCode().reset('');
          this.pairError.set(this.api.error(error, 'The runner could not be paired.').message);
        },
      });
  }

  protected requestAction(kind: RunnerAction['kind'], runner: AgentRunnerRegistration): void {
    this.actionError.set(null);
    this.action.set({ kind, runner });
  }

  protected confirmAction(): void {
    const action = this.action();
    if (!action || this.pending()) return;
    this.pending.set(true);
    this.actionError.set(null);
    if (action.kind === 'rotate') {
      this.api.rotateRunner(action.runner.runnerId).subscribe({
        next: () => this.completeAction(action),
        error: (error: unknown) => this.failAction(action, error),
      });
    } else {
      this.api.revokeRunner(action.runner.runnerId).subscribe({
        next: () => this.completeAction(action),
        error: (error: unknown) => this.failAction(action, error),
      });
    }
  }

  protected status(runnerId: string): RunnerStatusState {
    return this.statuses()[runnerId] ?? { value: null, loading: false, error: null };
  }

  protected formError(field: { errors(): readonly { message?: string }[] }): string | null {
    return field.errors()[0]?.message ?? null;
  }

  protected formatBytes(value: number | string): string {
    const bytes = Number(value);
    if (!Number.isFinite(bytes) || bytes < 0) return String(value);
    const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
    let size = bytes;
    let unit = 0;
    while (size >= 1024 && unit < units.length - 1) {
      size /= 1024;
      unit += 1;
    }
    return (size >= 10 || unit === 0 ? size.toFixed(0) : size.toFixed(1)) + ' ' + units[unit];
  }

  protected actionHeading(): string {
    return this.action()?.kind === 'rotate' ? 'Rotate runner credential?' : 'Remove agent runner?';
  }

  protected actionMessage(): string {
    const action = this.action();
    if (!action) return '';
    return action.kind === 'rotate'
      ? 'Rotate the PM credential used for ' +
          action.runner.displayName +
          '? Active runs are not affected.'
      : "Revoke this PM client's access to " +
          action.runner.displayName +
          ' and remove its local registration?';
  }

  private setStatus(runnerId: string, state: RunnerStatusState): void {
    this.statuses.update((current) => ({ ...current, [runnerId]: state }));
  }

  private completeAction(action: RunnerAction): void {
    this.pending.set(false);
    this.action.set(null);
    if (action.kind === 'remove') {
      this.runners.update((items) =>
        items.filter((runner) => runner.runnerId !== action.runner.runnerId),
      );
      this.statuses.update((current) => {
        const next = { ...current };
        delete next[action.runner.runnerId];
        return next;
      });
    } else {
      this.reloadStatus(action.runner.runnerId);
    }
  }

  private failAction(action: RunnerAction, error: unknown): void {
    this.pending.set(false);
    this.action.set(null);
    this.actionError.set(
      this.api.error(
        error,
        action.kind === 'rotate'
          ? 'The runner credential could not be rotated.'
          : 'The runner could not be removed.',
      ).message,
    );
  }
}
