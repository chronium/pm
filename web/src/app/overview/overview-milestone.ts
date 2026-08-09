import { Component, computed, input } from '@angular/core';

import { MarkdownDisplay } from '../markdown/markdown-display';
import { PmBadge, type BadgeTone } from '../ui/badge/badge';

export type OverviewMilestoneLifecycle = 'inactive' | 'active' | 'ready_to_deliver' | 'delivered';

export interface OverviewMilestoneData {
  key: string;
  title: string;
  description: string;
  priority: string;
  lifecycle: OverviewMilestoneLifecycle;
  assignedTaskCount: number;
  doneTaskCount: number;
  requiredActivationTriggers: readonly string[];
  unmetActivationTriggers: readonly string[];
}

@Component({
  selector: 'pm-overview-milestone',
  imports: [MarkdownDisplay, PmBadge],
  templateUrl: './overview-milestone.html',
  styleUrl: './overview-milestone.css',
})
export class OverviewMilestone {
  readonly headingId = input.required<string>();
  readonly title = input.required<string>();
  readonly milestone = input.required<OverviewMilestoneData | null>();

  protected readonly progress = computed(() => {
    const milestone = this.milestone();
    if (!milestone || milestone.assignedTaskCount <= 0) return null;
    const ratio = milestone.doneTaskCount / milestone.assignedTaskCount;
    return Math.round(Math.min(1, Math.max(0, ratio)) * 100);
  });

  protected lifecycleLabel(lifecycle: OverviewMilestoneLifecycle): string {
    switch (lifecycle) {
      case 'active':
        return 'Active';
      case 'inactive':
        return 'Inactive';
      case 'ready_to_deliver':
        return 'Ready to deliver';
      case 'delivered':
        return 'Delivered';
    }
  }

  protected lifecycleTone(lifecycle: OverviewMilestoneLifecycle): BadgeTone {
    switch (lifecycle) {
      case 'active':
      case 'ready_to_deliver':
        return 'success';
      case 'inactive':
        return 'warning';
      case 'delivered':
        return 'neutral';
    }
  }

  protected priorityLabel(priority: string): string {
    const normalized = priority.trim().toLowerCase();
    return normalized === 'none' ? 'No priority' : `${this.titleCase(normalized)} priority`;
  }

  protected progressLabel(milestone: OverviewMilestoneData): string {
    return `${milestone.doneTaskCount} of ${milestone.assignedTaskCount} tasks complete`;
  }

  private titleCase(value: string): string {
    return value ? value[0]!.toUpperCase() + value.slice(1) : 'No';
  }
}
