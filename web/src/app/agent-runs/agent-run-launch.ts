import { TitleCasePipe } from '@angular/common';
import {
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { form, required } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';

import {
  AgentRunsApiService,
  type AgentRunnerProvider,
  type AgentRunnerRegistration,
  type AgentRunnerStatus,
  type AgentRunPreflightResult,
  type AgentRunRemoteStart,
  type AgentRunRuntimeProfile,
} from './agent-runs-api.service';
import { PmEmptyState, PmErrorState, PmLoadingState } from '../ui/state/state';

interface LaunchSelection {
  runnerId: string;
  profileId: string;
  providerId: string;
  modelId: string;
  effortId: string;
}

interface LaunchRunnerState {
  value: AgentRunnerStatus | null;
  loading: boolean;
  error: string | null;
}

@Component({
  selector: 'pm-agent-run-launch',
  imports: [PmEmptyState, PmErrorState, PmLoadingState, RouterLink, TitleCasePipe],
  templateUrl: './agent-run-launch.html',
  styleUrl: './agent-run-launch.css',
})
export class AgentRunLaunch {
  readonly open = input(false);
  readonly taskId = input.required<string>();
  readonly taskTitle = input.required<string>();
  readonly openChange = output<boolean>();
  readonly runStarted = output<AgentRunRemoteStart>();

  private readonly api = inject(AgentRunsApiService);
  private readonly injector = inject(Injector);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private statusRequestsRemaining = 0;

  protected readonly runners = signal<AgentRunnerRegistration[]>([]);
  protected readonly runnerStates = signal<Record<string, LaunchRunnerState>>({});
  protected readonly loading = signal(false);
  protected readonly checking = signal(false);
  protected readonly starting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly selectionModel = signal<LaunchSelection>({
    runnerId: '',
    profileId: '',
    providerId: '',
    modelId: '',
    effortId: '',
  });
  protected readonly selectionForm = form(
    this.selectionModel,
    (selection) => {
      required(selection.runnerId);
      required(selection.profileId);
      required(selection.providerId);
      required(selection.modelId);
      required(selection.effortId);
    },
    { injector: this.injector },
  );
  protected readonly preflight = signal<AgentRunPreflightResult | null>(null);
  protected readonly preflightEtag = signal('');
  protected readonly accepted = signal<AgentRunRemoteStart | null>(null);

  protected readonly selectedRunnerState = computed(
    () => this.runnerStates()[this.selectionModel().runnerId] ?? null,
  );
  protected readonly selectedStatus = computed(() => this.selectedRunnerState()?.value ?? null);
  protected readonly profiles = computed(
    () => this.selectedStatus()?.capabilities.runtimeProfiles ?? [],
  );
  protected readonly providers = computed(() =>
    (this.selectedStatus()?.capabilities.agentProviders ?? []).filter(
      (provider) => provider.providerId === 'codex',
    ),
  );
  protected readonly selectedProvider = computed(
    () =>
      this.providers().find(
        (provider) => provider.providerId === this.selectionModel().providerId,
      ) ?? null,
  );
  protected readonly canCheck = computed(
    () =>
      this.selectionForm().valid() &&
      !!this.selectedStatus() &&
      !this.checking() &&
      !this.starting() &&
      !this.accepted(),
  );
  protected readonly canStart = computed(
    () =>
      !!this.preflight()?.ready &&
      !!this.preflight()?.runId &&
      !!this.preflight()?.request &&
      !!this.preflightEtag() &&
      !this.checking() &&
      !this.starting() &&
      !this.accepted(),
  );

  constructor() {
    let previouslyOpen = false;
    effect(() => {
      const open = this.open();
      const dialog = this.dialog().nativeElement;
      if (open && !previouslyOpen) {
        this.reset();
        this.loadRunners();
      }
      if (open && !dialog.open) dialog.showModal?.();
      else if (!open && dialog.open) dialog.close?.();
      previouslyOpen = open;
    });
  }

  protected close(): void {
    if (this.checking() || this.starting()) return;
    this.openChange.emit(false);
  }

  protected selectRunner(event: Event): void {
    const runnerId = (event.target as HTMLSelectElement).value;
    this.applyDefaults(runnerId);
  }

  protected selectProfile(event: Event): void {
    this.selectionModel.update((value) => ({
      ...value,
      profileId: (event.target as HTMLSelectElement).value,
    }));
    this.invalidatePreflight();
  }

  protected selectProvider(event: Event): void {
    const providerId = (event.target as HTMLSelectElement).value;
    const provider = this.providers().find((item) => item.providerId === providerId);
    this.selectionModel.update((value) => ({
      ...value,
      providerId,
      modelId: this.defaultModel(provider),
      effortId: this.defaultEffort(provider),
    }));
    this.invalidatePreflight();
  }

  protected selectModel(event: Event): void {
    this.selectionModel.update((value) => ({
      ...value,
      modelId: (event.target as HTMLSelectElement).value,
    }));
    this.invalidatePreflight();
  }

  protected selectEffort(event: Event): void {
    this.selectionModel.update((value) => ({
      ...value,
      effortId: (event.target as HTMLSelectElement).value,
    }));
    this.invalidatePreflight();
  }

  protected checkReadiness(): void {
    if (!this.canCheck()) return;
    const selection = this.selectionModel();
    this.checking.set(true);
    this.error.set(null);
    this.invalidatePreflight(false);
    this.api
      .preflight({
        taskId: this.taskId(),
        runnerId: selection.runnerId,
        profileId: selection.profileId,
        providerId: selection.providerId,
        modelId: selection.modelId,
        effortId: selection.effortId,
      })
      .subscribe({
        next: (response) => {
          this.checking.set(false);
          const result = response.body;
          this.preflight.set(result);
          this.preflightEtag.set(this.api.etag(response));
          if (result?.ready && !this.preflightEtag()) {
            this.error.set('The ready preflight response did not include a strong ETag.');
          }
        },
        error: (error: unknown) => {
          this.checking.set(false);
          this.error.set(this.api.error(error, 'Run readiness could not be checked.').message);
        },
      });
  }

  protected start(): void {
    const preflight = this.preflight();
    if (!this.canStart() || !preflight?.runId) return;
    this.starting.set(true);
    this.error.set(null);
    this.api.start(preflight.runId, this.preflightEtag()).subscribe({
      next: (response) => {
        this.starting.set(false);
        if (!response.body) {
          this.error.set('The runner accepted the request without returning run details.');
          return;
        }
        this.accepted.set(response.body);
        this.runStarted.emit(response.body);
      },
      error: (error: unknown) => {
        this.starting.set(false);
        const failure = this.api.error(error, 'The run could not be started.');
        this.error.set(
          failure.stale
            ? 'The preflight is stale. Check readiness again before starting.'
            : failure.message,
        );
        if (failure.stale) this.invalidatePreflight(false);
      },
    });
  }

  protected state(runnerId: string): LaunchRunnerState {
    return this.runnerStates()[runnerId] ?? { value: null, loading: false, error: null };
  }

  protected selectedProfile(): AgentRunRuntimeProfile | null {
    return (
      this.profiles().find((profile) => profile.profileId === this.selectionModel().profileId) ??
      null
    );
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

  protected formatCpu(value: number | string): string {
    const millicores = Number(value);
    return Number.isFinite(millicores) ? millicores / 1000 + ' CPU' : String(value);
  }

  protected formatDuration(seconds: number | string): string {
    const value = Number(seconds);
    if (!Number.isFinite(value)) return String(seconds);
    if (value % 3600 === 0) return value / 3600 + 'h';
    if (value % 60 === 0) return value / 60 + 'm';
    return value + 's';
  }

  protected command(executable: string, args: string[]): string {
    return [executable, ...args].join(' ');
  }

  private reset(): void {
    this.runners.set([]);
    this.runnerStates.set({});
    this.selectionModel.set({
      runnerId: '',
      profileId: '',
      providerId: '',
      modelId: '',
      effortId: '',
    });
    this.selectionForm().reset();
    this.loading.set(false);
    this.checking.set(false);
    this.starting.set(false);
    this.error.set(null);
    this.preflight.set(null);
    this.preflightEtag.set('');
    this.accepted.set(null);
    this.statusRequestsRemaining = 0;
  }

  private loadRunners(): void {
    this.loading.set(true);
    this.api.listRunners().subscribe({
      next: (response) => {
        const registrations = response.body ?? [];
        this.runners.set(registrations);
        this.loading.set(false);
        this.statusRequestsRemaining = registrations.length;
        if (!registrations.length) return;
        this.runnerStates.set(
          Object.fromEntries(
            registrations.map((runner) => [
              runner.runnerId,
              { value: null, loading: true, error: null },
            ]),
          ),
        );
        for (const runner of registrations) this.loadStatus(runner.runnerId);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(this.api.error(error, 'Agent runners could not be loaded.').message);
      },
    });
  }

  private loadStatus(runnerId: string): void {
    this.api.runnerStatus(runnerId).subscribe({
      next: (response) => {
        this.setRunnerState(runnerId, { value: response.body, loading: false, error: null });
        this.finishStatusRequest();
      },
      error: (error: unknown) => {
        this.setRunnerState(runnerId, {
          value: null,
          loading: false,
          error: this.api.error(error, 'Runner status is unavailable.').message,
        });
        this.finishStatusRequest();
      },
    });
  }

  private finishStatusRequest(): void {
    this.statusRequestsRemaining -= 1;
    if (this.statusRequestsRemaining > 0) return;
    const firstOnline = this.runners().find((runner) => !!this.state(runner.runnerId).value);
    if (firstOnline) this.applyDefaults(firstOnline.runnerId);
    else if (this.runners()[0]) this.applyDefaults(this.runners()[0]!.runnerId);
  }

  private applyDefaults(runnerId: string): void {
    const status = this.state(runnerId).value;
    const profile = status?.capabilities.runtimeProfiles[0];
    const provider = status?.capabilities.agentProviders.find(
      (candidate) => candidate.providerId === 'codex',
    );
    this.selectionModel.set({
      runnerId,
      profileId: profile?.profileId ?? '',
      providerId: provider?.providerId ?? '',
      modelId: this.defaultModel(provider),
      effortId: this.defaultEffort(provider),
    });
    this.invalidatePreflight();
  }

  private defaultModel(provider: AgentRunnerProvider | undefined): string {
    return provider?.defaultModelId ?? provider?.modelIds[0] ?? '';
  }

  private defaultEffort(provider: AgentRunnerProvider | undefined): string {
    return provider?.defaultEffortId ?? provider?.effortIds[0] ?? '';
  }

  private invalidatePreflight(clearError = true): void {
    this.preflight.set(null);
    this.preflightEtag.set('');
    if (clearError) this.error.set(null);
  }

  private setRunnerState(runnerId: string, value: LaunchRunnerState): void {
    this.runnerStates.update((current) => ({ ...current, [runnerId]: value }));
  }
}
