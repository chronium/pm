import {
  Component,
  computed,
  effect,
  ElementRef,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';

import type {
  ActivationRedefinitionPreview,
  ActivationRequirementRequest,
  ActivationTrigger,
} from './activation-api.service';
import {
  ActivationRequirementEditor,
  type ActivationRequirementMilestone,
} from './activation-requirement-editor';

@Component({
  selector: 'pm-activation-trigger-requirements-dialog',
  imports: [ActivationRequirementEditor],
  templateUrl: './activation-trigger-requirements-dialog.html',
  styleUrl: './activation-trigger-requirements-dialog.css',
})
export class ActivationTriggerRequirementsDialog {
  readonly open = input(false);
  readonly trigger = input<ActivationTrigger | null>(null);
  readonly milestones = input<readonly ActivationRequirementMilestone[]>([]);
  readonly preview = input<ActivationRedefinitionPreview | null>(null);
  readonly pending = input(false);
  readonly error = input<string | null>(null);

  readonly review = output<ActivationRequirementRequest[]>();
  readonly apply = output<ActivationRequirementRequest[]>();
  readonly save = output<ActivationRequirementRequest[]>();
  readonly changed = output<void>();
  readonly closed = output<void>();
  readonly dirtyChange = output<boolean>();

  protected readonly requirements = signal<ActivationRequirementRequest[]>([]);
  protected readonly dirty = signal(false);
  protected readonly discardPrompt = signal(false);
  protected readonly active = computed(() => !!this.trigger()?.isActive);
  protected readonly requirementsChanged = computed(() => {
    const baseline = (this.trigger()?.requirements ?? []).map(({ kind, source }) => ({
      kind,
      source,
    }));
    const current = this.requirements();
    return (
      baseline.length !== current.length ||
      baseline.some((item, index) => {
        const candidate = current[index];
        return candidate?.kind !== item.kind || candidate.source !== item.source;
      })
    );
  });
  protected readonly headingId = `activation-requirements-heading-${crypto.randomUUID()}`;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const dialog = this.dialog().nativeElement;
      if (this.open() && !dialog.open) {
        this.requirements.set(
          (this.trigger()?.requirements ?? []).map(({ kind, source }) => ({ kind, source })),
        );
        this.dirty.set(false);
        this.discardPrompt.set(false);
        this.dirtyChange.emit(false);
        if (typeof dialog.showModal === 'function') dialog.showModal();
        else dialog.setAttribute('open', '');
      } else if (!this.open() && dialog.open) {
        if (typeof dialog.close === 'function') dialog.close();
        else dialog.removeAttribute('open');
      }
    });
  }

  protected updateRequirements(requirements: ActivationRequirementRequest[]): void {
    this.requirements.set(requirements);
    this.markChanged();
  }

  protected requestReview(): void {
    if (!this.pending()) this.review.emit(this.normalizedRequirements());
  }

  protected requestApply(): void {
    if (!this.pending() && this.preview()) this.apply.emit(this.normalizedRequirements());
  }

  protected requestSave(): void {
    if (!this.pending() && !this.active() && this.requirementsChanged())
      this.save.emit(this.normalizedRequirements());
  }

  protected close(): void {
    if (this.pending()) return;
    if (this.dirty() && this.requirementsChanged()) {
      this.discardPrompt.set(true);
      return;
    }
    this.closed.emit();
  }

  protected discard(): void {
    this.dirty.set(false);
    this.discardPrompt.set(false);
    this.dirtyChange.emit(false);
    this.closed.emit();
  }
  protected cancel(event: Event): void {
    event.preventDefault();
    this.close();
  }
  protected backdrop(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) this.close();
  }

  private normalizedRequirements(): ActivationRequirementRequest[] {
    return this.requirements().map((item) => ({ kind: item.kind, source: item.source.trim() }));
  }

  private markChanged(): void {
    this.dirty.set(true);
    this.changed.emit();
    this.dirtyChange.emit(true);
  }
}
