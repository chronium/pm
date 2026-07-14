import { Component, ElementRef, effect, input, output, viewChild } from '@angular/core';

@Component({
  selector: 'pm-confirm-dialog',
  templateUrl: './confirm-dialog.html',
  styleUrl: './confirm-dialog.css',
})
export class PmConfirmDialog {
  private static nextHeadingId = 0;

  readonly open = input(false);
  readonly pending = input(false);
  readonly heading = input('Confirm removal');
  readonly message = input('This action cannot be undone.');
  readonly confirmLabel = input('Remove');
  readonly cancelLabel = input('Cancel');

  readonly openChange = output<boolean>();
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  protected readonly headingId = `pm-confirm-dialog-heading-${PmConfirmDialog.nextHeadingId++}`;

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const dialog = this.dialog().nativeElement;
      if (this.open() && !dialog.open) {
        if (typeof dialog.showModal === 'function') {
          dialog.showModal();
        } else {
          dialog.setAttribute('open', '');
        }
      } else if (!this.open() && dialog.open) {
        if (typeof dialog.close === 'function') {
          dialog.close();
        } else {
          dialog.removeAttribute('open');
        }
      }
    });
  }

  protected confirm(): void {
    if (!this.pending()) {
      this.confirmed.emit();
    }
  }

  protected cancel(): void {
    if (!this.pending()) {
      this.cancelled.emit();
      this.openChange.emit(false);
    }
  }

  protected handleNativeCancel(event: Event): void {
    event.preventDefault();
    this.cancel();
  }
}
