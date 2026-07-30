import { AfterViewInit, Component, ElementRef, input, output, viewChild } from '@angular/core';

@Component({
  selector: 'pm-task-dialog-shell',
  templateUrl: './task-dialog-shell.html',
  styleUrl: './task-dialog-shell.css',
})
export class TaskDialogShell implements AfterViewInit {
  private static nextId = 0;
  readonly dialogTitle = input.required<string>();
  readonly eyebrow = input('Task');
  readonly pending = input(false);
  readonly chrome = input(true);
  readonly closeIntent = output<void>();
  protected readonly headingId = `pm-task-dialog-${TaskDialogShell.nextId++}`;
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  ngAfterViewInit(): void {
    const dialog = this.dialog().nativeElement;
    if (typeof dialog.showModal === 'function') dialog.showModal();
    else dialog.setAttribute('open', '');
  }

  protected cancel(event: Event): void {
    event.preventDefault();
    if (!this.pending()) this.closeIntent.emit();
  }
}
