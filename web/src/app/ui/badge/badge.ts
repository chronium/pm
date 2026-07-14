import { Component, input } from '@angular/core';

export type BadgeTone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger';

@Component({
  selector: 'pm-badge',
  templateUrl: './badge.html',
  styleUrl: './badge.css',
})
export class PmBadge {
  readonly tone = input<BadgeTone>('neutral');
}
