import {
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { disabled, FormField, form, required } from '@angular/forms/signals';

import { MarkdownEditor } from '../markdown/markdown-editor';
import { PmFormField } from '../ui/form-field/form-field';
import type { SettingsActivationTrigger, SettingsMilestone } from './settings-api.service';
import { SettingsStore, type MilestoneGatePreview } from './settings.store';

interface DeliverableBaseline {
  key: string;
  title: string;
  priority: string;
  description: string;
  triggerKeys: string[];
}

@Component({
  selector: 'pm-milestone-deliverable-editor',
  imports: [FormField, MarkdownEditor, PmFormField],
  templateUrl: './milestone-deliverable-editor.html',
  styleUrl: './milestone-deliverable-editor.css',
})
export class MilestoneDeliverableEditor {
  private static nextHeadingId = 0;

  protected readonly store = inject(SettingsStore);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly titleInput = viewChild<ElementRef<HTMLInputElement>>('titleInput');

  readonly open = input(false);
  readonly milestone = input<SettingsMilestone | null>(null);
  readonly activationTriggers = input<SettingsActivationTrigger[]>([]);
  readonly priorityOptions = input<string[]>([]);
  readonly readOnly = input(false);

  readonly openChange = output<boolean>();
  readonly dirtyChange = output<boolean>();

  protected readonly headingId = `milestone-deliverable-heading-${MilestoneDeliverableEditor.nextHeadingId++}`;
  protected readonly baseline = signal<DeliverableBaseline | null>(null);
  protected readonly titleModel = signal({ value: '' });
  protected readonly priorityModel = signal({ value: '' });
  protected readonly descriptionModel = signal({ value: '' });
  protected readonly selectedTriggerKeys = signal<string[]>([]);
  protected readonly gatePreview = signal<MilestoneGatePreview | null>(null);
  protected readonly discardPrompt = signal(false);
  protected readonly conflictNotice = signal<string | null>(null);

  protected readonly titleForm = form(this.titleModel, (item) => {
    required(item.value, { message: 'Title is required.' });
    disabled(item.value, this.readOnly);
  });
  protected readonly priorityForm = form(this.priorityModel, (item) =>
    disabled(item.value, this.readOnly),
  );
  protected readonly descriptionForm = form(this.descriptionModel, (item) =>
    disabled(item.value, this.readOnly),
  );

  protected readonly titleDirty = computed(
    () => this.titleModel().value.trim() !== (this.baseline()?.title ?? ''),
  );
  protected readonly priorityDirty = computed(
    () => this.priorityModel().value !== (this.baseline()?.priority ?? ''),
  );
  protected readonly descriptionDirty = computed(
    () => this.descriptionModel().value !== (this.baseline()?.description ?? ''),
  );
  protected readonly gatesDirty = computed(
    () => !this.sameKeys(this.selectedTriggerKeys(), this.baseline()?.triggerKeys ?? []),
  );
  protected readonly dirty = computed(
    () => this.titleDirty() || this.priorityDirty() || this.descriptionDirty() || this.gatesDirty(),
  );

  constructor() {
    let activeKey: string | null = null;
    effect(() => {
      const milestone = this.milestone();
      if (!this.open() || !milestone) return;
      if (activeKey !== milestone.key || !this.dirty()) {
        activeKey = milestone.key;
        this.resetFromMilestone(milestone);
      }
    });
    effect(() => {
      const dialog = this.dialog().nativeElement;
      if (this.open() && !dialog.open) {
        if (typeof dialog.showModal === 'function') dialog.showModal();
        else dialog.setAttribute('open', '');
        queueMicrotask(() => this.titleInput()?.nativeElement.focus());
      } else if (!this.open() && dialog.open) {
        if (typeof dialog.close === 'function') dialog.close();
        else dialog.removeAttribute('open');
      }
    });
    effect(() => this.dirtyChange.emit(this.dirty()));
  }

  protected formError(): string | null {
    return this.titleForm.value().errors()[0]?.message ?? null;
  }

  protected async saveTitle(event: Event): Promise<void> {
    event.preventDefault();
    this.titleForm().markAsTouched();
    const baseline = this.baseline();
    if (!baseline || !this.titleForm().valid() || !this.titleDirty() || this.mutationsBlocked())
      return;
    const title = this.titleModel().value.trim();
    if (await this.store.renameMilestone(baseline.key, { title })) {
      this.baseline.update((value) => (value ? { ...value, title } : value));
      this.titleModel.set({ value: title });
      this.titleForm().reset();
      this.invalidatePreview();
    }
  }

  protected async savePriority(event: Event): Promise<void> {
    event.preventDefault();
    const baseline = this.baseline();
    if (!baseline || !this.priorityDirty() || this.mutationsBlocked()) return;
    const priority = this.priorityModel().value;
    if (await this.store.setMilestonePriority(baseline.key, { priority })) {
      this.baseline.update((value) => (value ? { ...value, priority } : value));
      this.priorityForm().reset();
      this.invalidatePreview();
    }
  }

  protected async saveDescription(event: Event): Promise<void> {
    event.preventDefault();
    const baseline = this.baseline();
    if (!baseline || !this.descriptionDirty() || this.mutationsBlocked()) return;
    const description = this.descriptionModel().value;
    if (await this.store.setMilestoneDescription(baseline.key, { description })) {
      this.baseline.update((value) => (value ? { ...value, description } : value));
      this.descriptionForm().reset();
      this.invalidatePreview();
    }
  }

  protected toggleTrigger(key: string, checked: boolean): void {
    this.selectedTriggerKeys.update((keys) =>
      checked ? [...keys, key] : keys.filter((candidate) => candidate !== key),
    );
    this.invalidatePreview();
  }

  protected triggerSelected(key: string): boolean {
    return this.selectedTriggerKeys().includes(key);
  }

  protected triggerSummary(trigger: SettingsActivationTrigger): string {
    return trigger.requirements.length === 0
      ? 'Manual only'
      : `${trigger.requirements.length} requirement${trigger.requirements.length === 1 ? '' : 's'}`;
  }

  protected async reviewGateChanges(): Promise<void> {
    const baseline = this.baseline();
    if (!baseline || !this.gatesDirty() || this.mutationsBlocked()) return;
    const preview = await this.store.previewMilestoneRequiredTriggers(
      baseline.key,
      this.selectedTriggerKeys(),
    );
    this.gatePreview.set(preview);
  }

  protected async applyGateChanges(): Promise<void> {
    const baseline = this.baseline();
    const preview = this.gatePreview();
    if (!baseline || !preview || this.mutationsBlocked()) return;
    if (
      await this.store.applyMilestoneRequiredTriggers(
        baseline.key,
        this.selectedTriggerKeys(),
        preview,
      )
    ) {
      this.baseline.update((value) =>
        value ? { ...value, triggerKeys: [...this.selectedTriggerKeys()] } : value,
      );
      this.gatePreview.set(null);
    } else if (this.store.operationError()?.error.conflict) {
      this.gatePreview.set(null);
    }
  }

  protected requestClose(): void {
    if (this.store.pending()) return;
    if (this.dirty()) {
      this.discardPrompt.set(true);
      return;
    }
    this.close();
  }

  protected discardAndClose(): void {
    const milestone = this.milestone();
    if (milestone) this.resetFromMilestone(milestone);
    this.close();
  }

  protected keepEditing(): void {
    this.discardPrompt.set(false);
  }

  protected preserveDraftAgainstLatest(): void {
    if (!this.store.pendingExternal()) return;
    this.store.reviewLatest();
    this.store.stale.set(false);
    this.gatePreview.set(null);
    this.conflictNotice.set('Latest settings loaded. Your milestone draft is preserved.');
  }

  protected useLatest(): void {
    this.store.reviewLatest();
    const latest = this.store
      .settings()
      ?.milestones.find((milestone) => milestone.key === this.baseline()?.key);
    if (latest) this.resetFromMilestone(latest);
    this.store.keepLatest();
    this.conflictNotice.set(null);
  }

  protected handleCancel(event: Event): void {
    event.preventDefault();
    this.requestClose();
  }

  protected pending(
    kind: 'rename' | 'priority' | 'description' | 'preview' | 'activation',
  ): boolean {
    const key = this.baseline()?.key;
    return !!key && this.store.isPending({ kind, collection: 'milestone', key });
  }

  protected error(
    kind: 'rename' | 'priority' | 'description' | 'preview' | 'activation',
  ): string | null {
    const key = this.baseline()?.key;
    return key ? this.store.errorForOperation(kind, key) : null;
  }

  private resetFromMilestone(milestone: SettingsMilestone): void {
    const baseline = {
      key: milestone.key,
      title: milestone.title,
      priority: milestone.priority,
      description: milestone.description,
      triggerKeys: [...milestone.requiredActivationTriggers],
    };
    this.baseline.set(baseline);
    this.titleModel.set({ value: baseline.title });
    this.priorityModel.set({ value: baseline.priority });
    this.descriptionModel.set({ value: baseline.description });
    this.selectedTriggerKeys.set([...baseline.triggerKeys]);
    this.titleForm().reset();
    this.priorityForm().reset();
    this.descriptionForm().reset();
    this.gatePreview.set(null);
    this.discardPrompt.set(false);
    this.conflictNotice.set(null);
    this.store.clearOperationError();
  }

  private close(): void {
    this.discardPrompt.set(false);
    this.openChange.emit(false);
  }

  protected mutationsBlocked(): boolean {
    return this.readOnly() || this.store.pending() || this.store.stale();
  }

  private invalidatePreview(): void {
    this.gatePreview.set(null);
    this.store.clearOperationError();
  }

  private sameKeys(left: string[], right: string[]): boolean {
    return left.length === right.length && left.every((key) => right.includes(key));
  }
}
