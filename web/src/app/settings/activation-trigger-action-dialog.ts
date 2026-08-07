import { Component, effect, ElementRef, input, output, signal, viewChild } from '@angular/core';

import type { ActivationTrigger } from './activation-api.service';

export type ActivationTriggerAction = 'activate' | 'override' | 'reset';

@Component({
  selector: 'pm-activation-trigger-action-dialog',
  templateUrl: './activation-trigger-action-dialog.html',
  styleUrl: './activation-trigger-action-dialog.css',
})
export class ActivationTriggerActionDialog {
  readonly open = input(false);
  readonly action = input<ActivationTriggerAction>('activate');
  readonly trigger = input<ActivationTrigger | null>(null);
  readonly pending = input(false);
  readonly error = input<string | null>(null);

  readonly confirmed = output<string>();
  readonly closed = output<void>();
  readonly dirtyChange = output<boolean>();

  protected readonly reason = signal('');
  protected readonly discardPrompt = signal(false);
  protected readonly headingId = `activation-action-heading-${crypto.randomUUID()}`;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const dialog = this.dialog().nativeElement;
      if (this.open() && !dialog.open) {
        this.reason.set('');
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

  protected heading(): string {
    const verb =
      this.action() === 'activate'
        ? 'Activate'
        : this.action() === 'override'
          ? 'Override'
          : 'Reset';
    return `${verb} ${this.trigger()?.title ?? 'activation trigger'}?`;
  }

  protected message(): string {
    if (this.action() === 'activate')
      return 'This manual-only trigger will become active and make its consuming milestones eligible.';
    if (this.action() === 'reset')
      return 'The persisted activation record will be removed. Consuming milestones may become inactive.';
    return 'Explain why work may proceed before every factual requirement is satisfied.';
  }

  protected confirmLabel(): string {
    return this.action() === 'activate'
      ? 'Activate trigger'
      : this.action() === 'override'
        ? 'Apply override'
        : 'Reset trigger';
  }

  protected updateReason(event: Event): void {
    this.reason.set((event.target as HTMLTextAreaElement).value);
    this.dirtyChange.emit(this.reason().length > 0);
  }

  protected confirm(): void {
    if (this.pending() || (this.action() === 'override' && !this.reason().trim())) return;
    this.confirmed.emit(this.reason().trim());
  }

  protected close(): void {
    if (this.pending()) return;
    if (this.action() === 'override' && this.reason().length > 0) {
      this.discardPrompt.set(true);
      return;
    }
    this.closed.emit();
  }

  protected discard(): void {
    this.discardPrompt.set(false);
    this.reason.set('');
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
