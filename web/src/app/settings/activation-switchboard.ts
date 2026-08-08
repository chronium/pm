import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PollingCoordinator } from '../core/polling-coordinator';
import { ProjectContextService } from '../core/project-context.service';
import { PmConfirmDialog } from '../ui/confirm-dialog/confirm-dialog';
import { PmErrorState, PmLoadingState } from '../ui/state/state';
import type {
  ActivationRedefinitionPreview,
  ActivationRequirementRequest,
  ActivationTrigger,
  CreateActivationTriggerRequest,
} from './activation-api.service';
import { ActivationTriggerCreateDialog } from './activation-trigger-create-dialog';
import {
  ActivationTriggerActionDialog,
  type ActivationTriggerAction,
} from './activation-trigger-action-dialog';
import { ActivationTriggerRenameDialog } from './activation-trigger-rename-dialog';
import { ActivationTriggerRequirementsDialog } from './activation-trigger-requirements-dialog';
import { ActivationSwitchboardStore } from './activation-switchboard.store';

interface ActionSelection {
  action: ActivationTriggerAction;
  key: string;
}

@Component({
  selector: 'pm-activation-switchboard',
  imports: [
    ActivationTriggerActionDialog,
    ActivationTriggerCreateDialog,
    ActivationTriggerRenameDialog,
    ActivationTriggerRequirementsDialog,
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
  protected readonly projectContext = inject(ProjectContextService);
  readonly readOnly = input(false);
  readonly dirtyChange = output<boolean>();
  readonly definitionChanged = output<void>();

  protected readonly createOpen = signal(false);
  protected readonly action = signal<ActionSelection | null>(null);
  protected readonly renameKey = signal<string | null>(null);
  protected readonly requirementsKey = signal<string | null>(null);
  protected readonly requirementsPreview = signal<ActivationRedefinitionPreview | null>(null);
  protected readonly removeKey = signal<string | null>(null);
  protected readonly reconcilePreview = signal<{
    triggerKeys: string[];
    milestoneKeys: string[];
  } | null>(null);
  protected readonly selectedActionTrigger = computed(() => this.findTrigger(this.action()?.key));
  protected readonly selectedRenameTrigger = computed(() => this.findTrigger(this.renameKey()));
  protected readonly selectedRequirementsTrigger = computed(() =>
    this.findTrigger(this.requirementsKey()),
  );
  protected readonly selectedRemoveTrigger = computed(() => this.findTrigger(this.removeKey()));
  protected readonly reconciliationRequired = computed(() =>
    this.store.switchboard()?.issues.some((issue) => this.issueRequiresReconciliation(issue.code)),
  );
  protected readonly createError = computed(() =>
    this.store.failure()?.operation.kind === 'create' ? this.store.failure()!.error.message : null,
  );
  protected readonly removeError = computed(() =>
    this.store.failure()?.operation.kind === 'remove' ? this.store.failure()!.error.message : null,
  );

  constructor(protected readonly store: ActivationSwitchboardStore) {}

  protected openCreate(): void {
    this.store.clearFailure();
    this.createOpen.set(true);
    this.store.suspendLiveUpdates();
  }

  protected closeCreate(): void {
    this.createOpen.set(false);
    this.dirtyChange.emit(false);
    this.store.resumeLiveUpdates();
  }

  protected async createTrigger(request: CreateActivationTriggerRequest): Promise<void> {
    if (await this.store.create(request)) {
      this.definitionChanged.emit();
      this.closeCreate();
    }
  }

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

  protected openRename(key: string): void {
    this.store.clearFailure();
    this.renameKey.set(key);
    this.store.suspendLiveUpdates();
  }

  protected closeRename(): void {
    this.renameKey.set(null);
    this.dirtyChange.emit(false);
    this.store.resumeLiveUpdates();
  }

  protected async renameTrigger(title: string): Promise<void> {
    const key = this.renameKey();
    if (key && (await this.store.rename(key, title))) {
      this.definitionChanged.emit();
      this.closeRename();
    }
  }

  protected openRequirements(key: string): void {
    this.store.clearFailure();
    this.requirementsPreview.set(null);
    this.requirementsKey.set(key);
    this.store.suspendLiveUpdates();
  }

  protected closeRequirements(): void {
    this.requirementsKey.set(null);
    this.requirementsPreview.set(null);
    this.dirtyChange.emit(false);
    this.store.resumeLiveUpdates();
  }

  protected async saveRequirements(requirements: ActivationRequirementRequest[]): Promise<void> {
    const key = this.requirementsKey();
    if (!key) return;
    if (this.findTrigger(key)?.isActive) {
      this.requirementsPreview.set(await this.store.previewRedefinition(key, requirements));
      return;
    }
    if (await this.store.setRequirements(key, requirements)) {
      this.definitionChanged.emit();
      this.closeRequirements();
    }
  }

  protected async reviewRedefinition(requirements: ActivationRequirementRequest[]): Promise<void> {
    const key = this.requirementsKey();
    if (key) this.requirementsPreview.set(await this.store.previewRedefinition(key, requirements));
  }

  protected invalidateRequirementsPreview(): void {
    this.requirementsPreview.set(null);
  }

  protected async applyRedefinition(requirements: ActivationRequirementRequest[]): Promise<void> {
    const key = this.requirementsKey();
    const preview = this.requirementsPreview();
    if (key && preview && (await this.store.redefine(key, requirements, preview))) {
      this.definitionChanged.emit();
      this.closeRequirements();
    } else if (this.store.failure()?.error.conflict) this.requirementsPreview.set(null);
  }

  protected openRemoval(trigger: ActivationTrigger): void {
    if (trigger.consumingMilestones.length) return;
    this.store.clearFailure();
    this.removeKey.set(trigger.key);
    this.store.suspendLiveUpdates();
  }

  protected closeRemoval(): void {
    this.removeKey.set(null);
    this.store.resumeLiveUpdates();
  }

  protected async confirmRemoval(): Promise<void> {
    const key = this.removeKey();
    if (!key) return;
    if (await this.store.remove(key)) {
      this.definitionChanged.emit();
      this.closeRemoval();
    } else if (this.store.failure()?.error.conflict && !this.findTrigger(key)) {
      this.closeRemoval();
    }
  }

  protected removalMessage(): string {
    const trigger = this.selectedRemoveTrigger();
    if (!trigger) return '';
    return trigger.isActive
      ? `This active trigger and its activation provenance will be deleted permanently.`
      : 'This trigger definition will be deleted permanently.';
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
    return kind === 'task' ? this.projectContext.taskUrl(source) : this.projectContext.tasksRoot();
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
