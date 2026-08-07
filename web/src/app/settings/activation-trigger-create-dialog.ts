import { Component, effect, ElementRef, input, output, signal, viewChild } from '@angular/core';

import type {
  ActivationRequirementRequest,
  CreateActivationTriggerRequest,
} from './activation-api.service';
import {
  ActivationRequirementEditor,
  type ActivationRequirementMilestone,
} from './activation-requirement-editor';

@Component({
  selector: 'pm-activation-trigger-create-dialog',
  imports: [ActivationRequirementEditor],
  templateUrl: './activation-trigger-create-dialog.html',
  styleUrl: './activation-trigger-create-dialog.css',
})
export class ActivationTriggerCreateDialog {
  readonly open = input(false);
  readonly milestones = input<readonly ActivationRequirementMilestone[]>([]);
  readonly pending = input(false);
  readonly error = input<string | null>(null);

  readonly created = output<CreateActivationTriggerRequest>();
  readonly closed = output<void>();
  readonly dirtyChange = output<boolean>();

  protected readonly key = signal('');
  protected readonly title = signal('');
  protected readonly requirements = signal<ActivationRequirementRequest[]>([]);
  protected readonly dirty = signal(false);
  protected readonly discardPrompt = signal(false);
  protected readonly headingId = `activation-create-heading-${crypto.randomUUID()}`;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const dialog = this.dialog().nativeElement;
      if (this.open() && !dialog.open) {
        this.key.set('');
        this.title.set('');
        this.requirements.set([]);
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

  protected updateKey(event: Event): void {
    this.key.set((event.target as HTMLInputElement).value);
    this.markChanged();
  }

  protected updateTitle(event: Event): void {
    this.title.set((event.target as HTMLInputElement).value);
    this.markChanged();
  }

  protected updateRequirements(requirements: ActivationRequirementRequest[]): void {
    this.requirements.set(requirements);
    this.markChanged();
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const key = this.key().trim();
    const title = this.title().trim();
    if (this.pending() || !key || !title) return;
    this.created.emit({ key, title, requirements: this.requirements() });
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

  private markChanged(): void {
    if (this.dirty()) return;
    this.dirty.set(true);
    this.dirtyChange.emit(true);
  }
}
