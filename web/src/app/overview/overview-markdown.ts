import { Component, inject, input } from '@angular/core';
import { Router, RouterLink, type UrlTree } from '@angular/router';

import { ProjectContextService } from '../core/project-context.service';
import { MarkdownDisplay } from '../markdown/markdown-display';
import { OverviewSection } from './overview-section';

@Component({
  selector: 'pm-overview-markdown',
  imports: [MarkdownDisplay, OverviewSection, RouterLink],
  templateUrl: './overview-markdown.html',
  styleUrl: './overview-markdown.css',
})
export class OverviewMarkdown {
  protected readonly projectContext = inject(ProjectContextService);
  private readonly router = inject(Router);

  readonly headingId = input.required<string>();
  readonly title = input.required<string>();
  readonly sourcePath = input.required<string>();
  readonly body = input.required<string>();

  protected sourceLink(): UrlTree {
    return this.router.parseUrl(this.projectContext.wikiUrl(this.sourcePath()));
  }
}
