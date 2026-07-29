import { DOCUMENT } from '@angular/common';
import {
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { AgentRunLaunch } from './agent-run-launch';
import { AgentRunOutput } from './agent-run-output';
import { AgentRunProgress } from './agent-run-progress';
import { AgentRunSupervisionStore } from './agent-run-supervision.store';
import type { AgentRunRemoteStart } from './agent-runs-api.service';
import type { AgentRunConnectivity } from './agent-run-events';
import { PmConfirmDialog } from '../ui/confirm-dialog/confirm-dialog';
import { PmErrorState, PmLoadingState } from '../ui/state/state';

type MobileRunPane = 'progress' | 'output';

@Component({
  selector: 'pm-agent-run-workspace',
  imports: [
    AgentRunLaunch,
    AgentRunOutput,
    AgentRunProgress,
    PmConfirmDialog,
    PmErrorState,
    PmLoadingState,
    RouterLink,
  ],
  providers: [AgentRunSupervisionStore],
  templateUrl: './agent-run-workspace.html',
  styleUrl: './agent-run-workspace.css',
})
export class AgentRunWorkspace {
  readonly runId = input.required<string>();

  protected readonly store = inject(AgentRunSupervisionStore);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly mobilePane = signal<MobileRunPane>('progress');
  protected readonly cancelOpen = signal(false);
  protected readonly retryOpen = signal(false);
  private readonly output = viewChild(AgentRunOutput);
  private readonly now = signal(Date.now());
  private returnUrl = '/tasks';

  protected readonly elapsed = computed(() => {
    const run = this.store.run();
    if (!run) return '—';
    const start = new Date(run.acceptedAt).valueOf();
    const end = run.terminalAt ? new Date(run.terminalAt).valueOf() : this.now();
    if (!Number.isFinite(start) || !Number.isFinite(end)) return '—';
    const seconds = Math.max(0, Math.floor((end - start) / 1000));
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const remainder = seconds % 60;
    return hours
      ? `${hours}h ${String(minutes).padStart(2, '0')}m`
      : `${minutes}m ${String(remainder).padStart(2, '0')}s`;
  });

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 1000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
    effect(() => {
      const runId = this.runId();
      if (runId) this.store.load(runId);
    });
    const navigationState = this.router.getCurrentNavigation()?.extras.state ?? history.state;
    if (typeof navigationState?.['returnUrl'] === 'string')
      this.returnUrl = navigationState['returnUrl'];
  }

  protected connectivityLabel(connectivity: AgentRunConnectivity): string {
    const labels: Record<AgentRunConnectivity, string> = {
      loading: 'Loading',
      connecting: 'Connecting',
      live: 'Runner connected',
      reconnecting: 'Reconnecting',
      paused: 'Output paused',
      complete: 'Journal complete',
    };
    return labels[connectivity];
  }

  protected stateLabel(state: string): string {
    return state
      .split('_')
      .map((part) => part[0]?.toUpperCase() + part.slice(1))
      .join(' ');
  }

  protected showMobilePane(pane: MobileRunPane): void {
    this.mobilePane.set(pane);
    if (pane === 'output') setTimeout(() => this.output()?.revealLatest());
  }

  protected mobileTabKeydown(event: KeyboardEvent): void {
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
    event.preventDefault();
    const pane: MobileRunPane = this.mobilePane() === 'progress' ? 'output' : 'progress';
    this.showMobilePane(pane);
    this.document.getElementById(`run-${pane}-tab`)?.focus();
  }

  protected back(): void {
    const fallback = this.store.run()?.specification.task.taskId
      ? `/tasks/${encodeURIComponent(this.store.run()!.specification.task.taskId)}`
      : '/tasks';
    void this.router.navigateByUrl(this.returnUrl.startsWith('/tasks') ? this.returnUrl : fallback);
  }

  protected async confirmCancel(): Promise<void> {
    const accepted = await this.store.cancel();
    if (accepted) this.cancelOpen.set(false);
  }

  protected retryStarted(result: AgentRunRemoteStart): void {
    this.retryOpen.set(false);
    const taskId = result.run.specification.task.taskId;
    void this.router.navigate(['/tasks/runs', result.run.runId], {
      state: { returnUrl: `/tasks/${encodeURIComponent(taskId)}` },
    });
  }

  protected async downloadJournal(): Promise<void> {
    const blob = await this.store.eventJournal();
    if (!blob) return;
    const url = URL.createObjectURL(blob);
    const anchor = this.document.createElement('a');
    anchor.href = url;
    anchor.download = `${this.runId()}-events.jsonl`;
    anchor.click();
    URL.revokeObjectURL(url);
  }
}
