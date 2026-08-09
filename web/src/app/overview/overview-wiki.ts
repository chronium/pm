import { Component, inject, input } from '@angular/core';
import { Router, RouterLink, type UrlTree } from '@angular/router';

import { ProjectContextService } from '../core/project-context.service';
import { OverviewSection } from './overview-section';

export interface OverviewWikiPage {
  path: string;
  title: string;
  modifiedAt: string;
}

@Component({
  selector: 'pm-overview-wiki',
  imports: [OverviewSection, RouterLink],
  templateUrl: './overview-wiki.html',
  styleUrl: './overview-wiki.css',
})
export class OverviewWiki {
  protected readonly projectContext = inject(ProjectContextService);
  private readonly router = inject(Router);

  readonly headingId = input.required<string>();
  readonly title = input.required<string>();
  readonly pages = input.required<readonly OverviewWikiPage[]>();

  protected wikiLink(path: string): UrlTree {
    return this.router.parseUrl(this.projectContext.wikiUrl(path));
  }
}
