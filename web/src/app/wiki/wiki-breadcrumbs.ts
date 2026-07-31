import { Component, computed, input } from '@angular/core';
import { inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProjectContextService } from '../core/project-context.service';

@Component({
  selector: 'pm-wiki-breadcrumbs',
  imports: [RouterLink],
  template: `<nav class="wiki-breadcrumbs" aria-label="Wiki breadcrumbs">
    <a [routerLink]="projectContext.wikiRoot()">Wiki</a>
    @for (crumb of crumbs(); track crumb.path) {
      <span aria-hidden="true">/</span
      ><a [routerLink]="projectContext.wikiUrl(crumb.path)">{{ crumb.label }}</a>
    }
  </nav>`,
  styleUrl: './wiki-breadcrumbs.css',
})
export class WikiBreadcrumbs {
  protected readonly projectContext = inject(ProjectContextService);
  readonly path = input('');
  protected readonly crumbs = computed(() => {
    let current = '';
    return this.path()
      .split('/')
      .filter(Boolean)
      .map((label) => ({ label, path: (current = current ? `${current}/${label}` : label) }));
  });
}
