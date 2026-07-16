import { Component, input, output } from '@angular/core';

export type ExternalChangePhase = 'pending' | 'reviewing' | 'preserved';

@Component({
  selector: 'pm-external-change-banner',
  template: `
    <div class="external-change-banner" role="alert">
      <div>
        <strong>{{ heading() }}</strong>
        <span>{{ message() }}</span>
      </div>
      <div class="external-change-actions">
        @if (phase() === 'pending') {
          <button type="button" class="pm-button pm-button--primary" (click)="review.emit()">
            Review latest
          </button>
        } @else {
          <button type="button" class="pm-button pm-button--secondary" (click)="restore.emit()">
            Restore draft
          </button>
          <button type="button" class="pm-button pm-button--primary" (click)="keep.emit()">
            Keep latest
          </button>
        }
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .external-change-banner {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      padding: 0.75rem 1rem;
      border: 1px solid var(--pm-color-warning-border, currentColor);
      background: var(--pm-color-warning-surface, transparent);
    }
    .external-change-banner div:first-child {
      display: grid;
      gap: 0.15rem;
    }
    .external-change-banner span {
      color: var(--pm-color-text-muted);
    }
    .external-change-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      flex: none;
    }
    @media (max-width: 40rem) {
      .external-change-banner {
        align-items: stretch;
        flex-direction: column;
      }
    }
  `,
})
export class ExternalChangeBanner {
  readonly phase = input.required<ExternalChangePhase>();
  readonly heading = input('A newer version is available.');
  readonly message = input('Review the latest version without losing your local draft.');
  readonly review = output<void>();
  readonly restore = output<void>();
  readonly keep = output<void>();
}
