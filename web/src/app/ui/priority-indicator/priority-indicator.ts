import { Component, computed, input } from '@angular/core';

export type PriorityIndicatorValue = 'none' | 'low' | 'medium' | 'high' | 'urgent';

const priorities = new Set<PriorityIndicatorValue>(['none', 'low', 'medium', 'high', 'urgent']);

@Component({
  selector: 'pm-priority-indicator',
  templateUrl: './priority-indicator.html',
  styleUrl: './priority-indicator.css',
  host: {
    role: 'img',
    '[attr.aria-label]': 'label()',
    '[attr.title]': 'tooltip()',
    '[attr.data-priority]': 'normalizedPriority()',
  },
})
export class PriorityIndicator {
  readonly priority = input.required<string>();
  readonly source = input<string | null>(null);

  protected readonly normalizedPriority = computed<PriorityIndicatorValue>(() => {
    const priority = this.priority().toLowerCase() as PriorityIndicatorValue;
    return priorities.has(priority) ? priority : 'none';
  });

  protected readonly fillPath = computed(() => {
    switch (this.normalizedPriority()) {
      case 'low':
        return 'M12 12V5.5A6.5 6.5 0 0 1 18.5 12Z';
      case 'medium':
        return 'M12 12V5.5A6.5 6.5 0 0 1 12 18.5Z';
      case 'high':
        return 'M12 12V5.5A6.5 6.5 0 1 1 5.5 12Z';
      case 'urgent':
        return 'M12 5.5a6.5 6.5 0 1 0 0 13a6.5 6.5 0 1 0 0-13zM11.25 7.5h1.5V14h-1.5zM11.25 16h1.5v1.5h-1.5z';
      case 'none':
        return null;
    }
  });

  protected readonly label = computed(() => `Priority: ${this.normalizedPriority()}`);
  protected readonly tooltip = computed(() => {
    const source = this.source();
    return source ? `${this.label()} — effective priority from ${source}` : this.label();
  });
}
