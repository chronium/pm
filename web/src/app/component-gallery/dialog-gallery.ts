import { Component, signal } from '@angular/core';

import { PmButton } from '../ui/button/button.directive';
import { PmConfirmDialog } from '../ui/confirm-dialog/confirm-dialog';

@Component({
  selector: 'pm-dialog-gallery',
  imports: [PmButton, PmConfirmDialog],
  template: `
    <section class="component-page pm-frosted-surface pm-scroll-surface pm-component-surface">
      <header class="component-header">
        <p>Foundation</p>
        <h1>Dialogs</h1>
      </header>

      <section class="specimen" aria-labelledby="dialog-confirmation">
        <h2 id="dialog-confirmation">Confirmation</h2>
        <div class="specimen-content">
          <button type="button" pmButton="danger" (click)="openDialog('default')">
            Remove task
          </button>
          <button type="button" pmButton="secondary" (click)="openDialog('long')">
            Long content
          </button>
        </div>
      </section>

      @if (outcome(); as message) {
        <p class="gallery-outcome" role="status">{{ message }}</p>
      }

      <pm-confirm-dialog
        [open]="dialogOpen()"
        [heading]="heading()"
        [message]="message()"
        confirmLabel="Remove task"
        (openChange)="dialogOpen.set($event)"
        (confirmed)="confirm()"
        (cancelled)="outcome.set('Removal cancelled')"
      />
    </section>
  `,
  styleUrls: ['./gallery-page.css', './dialog-gallery.css'],
})
export class DialogGallery {
  protected readonly dialogOpen = signal(false);
  protected readonly heading = signal('Remove PM-0073?');
  protected readonly message = signal('The task will be removed from the project.');
  protected readonly outcome = signal<string | null>(null);

  protected openDialog(kind: 'default' | 'long'): void {
    this.outcome.set(null);
    this.heading.set(
      kind === 'long'
        ? 'Remove the component-gallery task from the current project milestone?'
        : 'Remove PM-0073?',
    );
    this.message.set(
      kind === 'long'
        ? 'This task records the current visual exploration, component inventory, and decisions that future interface work depends on.'
        : 'The task will be removed from the project.',
    );
    this.dialogOpen.set(true);
  }

  protected confirm(): void {
    this.dialogOpen.set(false);
    this.outcome.set('Removal confirmed');
  }
}
