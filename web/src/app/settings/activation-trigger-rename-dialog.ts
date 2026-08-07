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

import type { ActivationTrigger } from './activation-api.service';

@Component({
  selector: 'pm-activation-trigger-rename-dialog',
  templateUrl: './activation-trigger-rename-dialog.html',
  styleUrl: './activation-trigger-rename-dialog.css',
})
export class ActivationTriggerRenameDialog {
  readonly open = input(false);
  readonly trigger = input<ActivationTrigger | null>(null);
  readonly pending = input(false);
  readonly error = input<string | null>(null);

  readonly renamed = output<string>();
  readonly closed = output<void>();
  readonly dirtyChange = output<boolean>();

  protected readonly title = signal('');
  protected readonly dirty = signal(false);
  protected readonly discardPrompt = signal(false);
  protected readonly changed = computed(
    () => this.title().trim() !== (this.trigger()?.title ?? '').trim(),
  );
  protected readonly headingId = `activation-rename-heading-${crypto.randomUUID()}`;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const dialog = this.dialog().nativeElement;
      if (this.open() && !dialog.open) {
        this.title.set(this.trigger()?.title ?? '');
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

  protected updateTitle(event: Event): void {
    this.title.set((event.target as HTMLInputElement).value);
    if (!this.dirty()) {
      this.dirty.set(true);
      this.dirtyChange.emit(true);
    }
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const title = this.title().trim();
    if (!this.pending() && title && this.changed()) this.renamed.emit(title);
  }

  protected close(): void {
    if (this.pending()) return;
    if (this.dirty() && this.changed()) {
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
}
