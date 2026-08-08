import { Component } from '@angular/core';
import type { Meta, StoryObj } from '@storybook/angular-vite';
import { expect } from 'storybook/test';

import { PriorityIndicator, type PriorityIndicatorValue } from './priority-indicator';

@Component({
  selector: 'pm-priority-indicator-gallery',
  imports: [PriorityIndicator],
  template: `
    <div class="priority-scale">
      @for (priority of priorities; track priority) {
        <div class="priority-sample">
          <pm-priority-indicator [priority]="priority" source="task" />
          <span>{{ priority }}</span>
        </div>
      }
    </div>
  `,
  styles: `
    .priority-scale {
      display: flex;
      flex-wrap: wrap;
      gap: var(--pm-space-5);
      padding: var(--pm-space-5);
      background: var(--pm-surface-raised);
      color: var(--pm-text-primary);
    }
    .priority-sample {
      display: inline-flex;
      align-items: center;
      gap: var(--pm-space-2);
      text-transform: capitalize;
    }
  `,
})
class PriorityIndicatorGallery {
  readonly priorities: PriorityIndicatorValue[] = ['none', 'low', 'medium', 'high', 'urgent'];
}

const meta = {
  title: 'UI/Priority indicator',
  component: PriorityIndicatorGallery,
  parameters: { layout: 'fullscreen' },
} satisfies Meta<PriorityIndicatorGallery>;
export default meta;
type Story = StoryObj<typeof meta>;

export const Scale: Story = {
  play: async ({ canvasElement }) => {
    const indicators = [...canvasElement.querySelectorAll<HTMLElement>('pm-priority-indicator')];
    expect(indicators.map((indicator) => indicator.dataset['priority'])).toEqual([
      'none',
      'low',
      'medium',
      'high',
      'urgent',
    ]);
    expect(indicators.map((indicator) => indicator.getAttribute('aria-label'))).toEqual([
      'Priority: none',
      'Priority: low',
      'Priority: medium',
      'Priority: high',
      'Priority: urgent',
    ]);
  },
};
