import { Component, computed, inject, input } from '@angular/core';

import type { components } from '../api/generated/pm-api';
import { ProjectContextService } from '../core/project-context.service';
import { OverviewCopyright } from './overview-copyright';
import { OverviewHero } from './overview-hero';
import { OverviewMarkdown } from './overview-markdown';
import {
  OverviewMilestone,
  type OverviewMilestoneData,
  type OverviewMilestoneLifecycle,
} from './overview-milestone';
import { OverviewShell } from './overview-shell';
import { OverviewTasks } from './overview-tasks';
import { OverviewWiki } from './overview-wiki';

export type OverviewCompositionData = components['schemas']['OverviewCompositionResponse'];
export type OverviewSectionData = components['schemas']['OverviewSectionResponse'];
type OverviewMilestoneResponse = components['schemas']['OverviewMilestoneResponse'];

@Component({
  selector: 'pm-overview-composition-section',
  imports: [
    OverviewCopyright,
    OverviewHero,
    OverviewMarkdown,
    OverviewMilestone,
    OverviewTasks,
    OverviewWiki,
  ],
  template: `
    @let item = section();
    @switch (item.type) {
      @case ('hero') {
        <pm-overview-hero
          [projectName]="projectName()"
          [title]="item.title"
          [description]="item.description"
          [tasksUrl]="tasksUrl()"
          [wikiUrl]="wikiUrl()"
        />
      }
      @case ('markdown') {
        <pm-overview-markdown
          [headingId]="headingId()"
          [title]="item.title"
          [sourcePath]="item.sourcePath"
          [body]="item.body"
        />
      }
      @case ('milestone') {
        <pm-overview-milestone
          [headingId]="headingId()"
          [title]="item.title"
          [milestone]="milestone()"
        />
      }
      @case ('tasks') {
        <pm-overview-tasks [headingId]="headingId()" [title]="item.title" [tasks]="item.tasks" />
      }
      @case ('wiki') {
        <pm-overview-wiki [headingId]="headingId()" [title]="item.title" [pages]="item.pages" />
      }
      @case ('copyright') {
        <pm-overview-copyright [notice]="item.notice" />
      }
    }
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }

    :host
      > :is(
        pm-overview-hero,
        pm-overview-markdown,
        pm-overview-milestone,
        pm-overview-tasks,
        pm-overview-wiki
      ) {
      height: 100%;
    }
  `,
})
export class OverviewCompositionSection {
  private readonly projectContext = inject(ProjectContextService);

  readonly section = input.required<OverviewSectionData>();
  readonly projectName = input.required<string>();
  readonly region = input.required<string>();
  readonly sectionIndex = input.required<number>();

  protected readonly tasksUrl = this.projectContext.tasksRoot;
  protected readonly wikiUrl = this.projectContext.wikiRoot;
  protected readonly headingId = computed(
    () => `overview-${this.region()}-${this.sectionIndex()}-${this.section().type}`,
  );
  protected readonly milestone = computed(() => {
    const section = this.section();
    return section.type === 'milestone' ? normalizeMilestone(section.milestone) : null;
  });
}

@Component({
  selector: 'pm-overview-composition',
  imports: [OverviewCompositionSection, OverviewShell],
  templateUrl: './overview-composition.html',
  styleUrl: './overview-composition.css',
})
export class OverviewComposition {
  readonly composition = input.required<OverviewCompositionData>();
  readonly projectName = input.required<string>();

  protected singleSections(): readonly OverviewSectionData[] {
    const composition = this.composition();
    return composition.layout === 'single' ? composition.sections : [];
  }

  protected splitPrimary(): readonly OverviewSectionData[] {
    const composition = this.composition();
    return composition.layout === 'split' ? composition.primary : [];
  }

  protected splitSecondary(): readonly OverviewSectionData[] {
    const composition = this.composition();
    return composition.layout === 'split' ? composition.secondary : [];
  }

  protected splitAfter(): readonly OverviewSectionData[] {
    const composition = this.composition();
    return composition.layout === 'split' ? composition.after : [];
  }
}

function normalizeMilestone(
  milestone: OverviewMilestoneResponse | null,
): OverviewMilestoneData | null {
  if (!milestone || !isMilestoneLifecycle(milestone.lifecycle)) return null;
  return {
    ...milestone,
    lifecycle: milestone.lifecycle,
    assignedTaskCount: normalizeCount(milestone.assignedTaskCount),
    doneTaskCount: normalizeCount(milestone.doneTaskCount),
  };
}

function normalizeCount(value: number | string): number {
  const numeric = typeof value === 'number' ? value : Number(value);
  return Number.isFinite(numeric) ? Math.max(0, Math.trunc(numeric)) : 0;
}

function isMilestoneLifecycle(value: string): value is OverviewMilestoneLifecycle {
  return (
    value === 'inactive' ||
    value === 'active' ||
    value === 'ready_to_deliver' ||
    value === 'delivered'
  );
}
