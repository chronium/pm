import { DatePipe } from '@angular/common';
import { Component, computed, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PollingCoordinator } from '../core/polling-coordinator';
import { PmConfirmDialog } from '../ui/confirm-dialog/confirm-dialog';
import { PmErrorState, PmLoadingState } from '../ui/state/state';
import type {
  ActivationRedefinitionPreview,
  ActivationRequirementRequest,
  ActivationTrigger,
} from './activation-api.service';
import {
  ActivationTriggerActionDialog,
  type ActivationTriggerAction,
} from './activation-trigger-action-dialog';
import { ActivationTriggerRedefineDialog } from './activation-trigger-redefine-dialog';
import { ActivationSwitchboardStore } from './activation-switchboard.store';

interface ActionSelection {
  action: ActivationTriggerAction;
  key: string;
}

@Component({
  selector: 'pm-activation-switchboard',
  imports: [
    ActivationTriggerActionDialog,
    ActivationTriggerRedefineDialog,
    DatePipe,
    PmConfirmDialog,
    PmErrorState,
    PmLoadingState,
    RouterLink,
  ],
  providers: [ActivationSwitchboardStore, PollingCoordinator],
  templateUrl: './activation-switchboard.html',
  styleUrl: './activation-switchboard.css',
})
export class ActivationSwitchboard {
  readonly readOnly = input(false);
  readonly dirtyChange = output<boolean>();

  protected readonly action = signal<ActionSelection | null>(null);
  protected readonly redefineKey = signal<string | null>(null);
  protected readonly redefinePreview = signal<ActivationRedefinitionPreview | null>(null);
  protected readonly reconcilePreview = signal<{
    triggerKeys: string[];
    milestoneKeys: string[];
  } | null>(null);
  protected readonly selectedActionTrigger = computed(() => this.findTrigger(this.action()?.key));
  protected readonly selectedRedefineTrigger = computed(() => this.findTrigger(this.redefineKey()));
  protected readonly reconciliationRequired = computed(() =>
    this.store.switchboard()?.issues.some((issue) => this.issueRequiresReconciliation(issue.code)),
  );

  constructor(protected readonly store: ActivationSwitchboardStore) {}

  protected status(trigger: ActivationTrigger): string {
    if (!trigger.isActive) {
      if (Number(trigger.requirementCount) === 0) return 'Manual activation required';
      if (trigger.requirementsSatisfied) return 'Reconciliation required';
      return `Pending — ${trigger.satisfiedRequirementCount} / ${trigger.requirementCount}`;
    }
    if (trigger.activation?.mode === 'manual') return 'Active manually';
    if (trigger.activation?.mode === 'override')
      return trigger.requirementsSatisfied
        ? 'Active by override — requirements now satisfied'
        : `Active by override — ${trigger.satisfiedRequirementCount} / ${trigger.requirementCount}`;
    return trigger.isLatchedDespiteUnmetRequirements
      ? 'Active automatically — latched'
      : 'Active automatically';
  }

  protected statusTone(trigger: ActivationTrigger): 'active' | 'pending' | 'warning' {
    if (trigger.isActive) return trigger.isLatchedDespiteUnmetRequirements ? 'warning' : 'active';
    return trigger.requirementsSatisfied ? 'warning' : 'pending';
  }

  protected canReset(trigger: ActivationTrigger): boolean {
    return trigger.isActive && !trigger.requirementsSatisfied;
  }

  protected openAction(action: ActivationTriggerAction, key: string): void {
    this.store.clearFailure();
    this.action.set({ action, key });
    this.store.suspendLiveUpdates();
  }

  protected closeAction(): void {
    this.action.set(null);
    this.dirtyChange.emit(false);
    this.store.resumeLiveUpdates();
  }

  protected async confirmAction(reason: string): Promise<void> {
    const selection = this.action();
    if (!selection) return;
    const success =
      selection.action === 'activate'
        ? await this.store.activate(selection.key)
        : selection.action === 'override'
          ? await this.store.override(selection.key, reason)
          : await this.store.reset(selection.key);
    if (success) this.closeAction();
  }

  protected openRedefinition(key: string): void {
    this.store.clearFailure();
    this.redefinePreview.set(null);
    this.redefineKey.set(key);
    this.store.suspendLiveUpdates();
  }

  protected closeRedefinition(): void {
    this.redefineKey.set(null);
    this.redefinePreview.set(null);
    this.dirtyChange.emit(false);
    this.store.resumeLiveUpdates();
  }

  protected async reviewRedefinition(requirements: ActivationRequirementRequest[]): Promise<void> {
    const key = this.redefineKey();
    if (key) this.redefinePreview.set(await this.store.previewRedefinition(key, requirements));
  }

  protected invalidateRedefinitionPreview(): void {
    this.redefinePreview.set(null);
  }

  protected async applyRedefinition(requirements: ActivationRequirementRequest[]): Promise<void> {
    const key = this.redefineKey();
    const preview = this.redefinePreview();
    if (key && preview && (await this.store.redefine(key, requirements, preview)))
      this.closeRedefinition();
    else if (this.store.failure()?.error.conflict) this.redefinePreview.set(null);
  }

  protected async reviewReconciliation(): Promise<void> {
    const preview = await this.store.previewReconciliation();
    if (!preview) return;
    this.reconcilePreview.set({
      triggerKeys: preview.triggerKeys,
      milestoneKeys: preview.milestoneKeys,
    });
    this.store.suspendLiveUpdates();
  }

  protected async confirmReconciliation(): Promise<void> {
    if (await this.store.reconcile()) this.closeReconciliation();
  }

  protected closeReconciliation(): void {
    this.reconcilePreview.set(null);
    this.store.resumeLiveUpdates();
  }

  protected reconciliationMessage(): string {
    const preview = this.reconcilePreview();
    if (!preview) return '';
    const triggers = preview.triggerKeys.length
      ? preview.triggerKeys.join(', ')
      : 'No triggers currently require reconciliation';
    const count = preview.milestoneKeys.length;
    return `${triggers}. ${count} milestone${count === 1 ? '' : 's'} affected.`;
  }

  protected requirementUrl(kind: string, source: string): string {
    return kind === 'task' ? `/tasks/${encodeURIComponent(source)}` : '/tasks';
  }

  protected requirementQuery(kind: string, source: string): Record<string, string> | null {
    return kind === 'milestone' ? { milestone: source } : null;
  }

  protected issueRequiresReconciliation(code: string): boolean {
    return code === 'activation_reconciliation_required';
  }

  private findTrigger(key: string | null | undefined): ActivationTrigger | null {
    return (
      this.store.switchboard()?.activationTriggers.find((trigger) => trigger.key === key) ?? null
    );
  }
}
