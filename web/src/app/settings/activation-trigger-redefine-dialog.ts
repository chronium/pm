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

@Component({
  selector: 'pm-activation-trigger-redefine-dialog',
  templateUrl: './activation-trigger-redefine-dialog.html',
  styleUrl: './activation-trigger-redefine-dialog.css',
})
export class ActivationTriggerRedefineDialog {
  readonly open = input(false);
  readonly trigger = input<ActivationTrigger | null>(null);
  readonly preview = input<ActivationRedefinitionPreview | null>(null);
  readonly pending = input(false);
  readonly error = input<string | null>(null);

  readonly review = output<ActivationRequirementRequest[]>();
  readonly apply = output<ActivationRequirementRequest[]>();
  readonly changed = output<void>();
  readonly closed = output<void>();
  readonly dirtyChange = output<boolean>();

  protected readonly requirements = signal<ActivationRequirementRequest[]>([]);
  protected readonly dirty = signal(false);
  protected readonly discardPrompt = signal(false);
  protected readonly headingId = `activation-redefine-heading-${crypto.randomUUID()}`;
  protected readonly validationError = computed(() => {
    const values = this.requirements();
    if (values.some((item) => !item.source.trim())) return 'Every requirement needs a source.';
    const identities = values.map((item) => `${item.kind}:${item.source.trim()}`);
    return new Set(identities).size === identities.length
      ? null
      : 'Duplicate requirements are not allowed.';
  });
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

  protected addRequirement(): void {
    this.requirements.update((items) => [...items, { kind: 'task', source: '' }]);
    this.markChanged();
  }

  protected removeRequirement(index: number): void {
    this.requirements.update((items) => items.filter((_, candidate) => candidate !== index));
    this.markChanged();
  }

  protected updateKind(index: number, event: Event): void {
    const kind = (event.target as HTMLSelectElement).value;
    this.requirements.update((items) =>
      items.map((item, candidate) => (candidate === index ? { ...item, kind } : item)),
    );
    this.markChanged();
  }

  protected updateSource(index: number, event: Event): void {
    const source = (event.target as HTMLInputElement).value;
    this.requirements.update((items) =>
      items.map((item, candidate) => (candidate === index ? { ...item, source } : item)),
    );
    this.markChanged();
  }

  protected requestReview(): void {
    if (!this.pending() && !this.validationError()) this.review.emit(this.normalizedRequirements());
  }

  protected requestApply(): void {
    if (!this.pending() && this.preview() && !this.validationError())
      this.apply.emit(this.normalizedRequirements());
  }

  protected close(): void {
    if (this.pending()) return;
    if (this.dirty()) {
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
