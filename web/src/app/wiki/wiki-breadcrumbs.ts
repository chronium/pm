import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'pm-wiki-breadcrumbs',
  imports: [RouterLink],
  template: `<nav class="wiki-breadcrumbs" aria-label="Wiki breadcrumbs">
    <a routerLink="/wiki">Wiki</a>
    @for (crumb of crumbs(); track crumb.path) {
      <span aria-hidden="true">/</span
      ><a [routerLink]="['/wiki', ...crumb.path.split('/')]">{{ crumb.label }}</a>
    }
  </nav>`,
  styleUrl: './wiki-breadcrumbs.css',
})
export class WikiBreadcrumbs {
  readonly path = input('');
  protected readonly crumbs = computed(() => {
    let current = '';
    return this.path()
      .split('/')
      .filter(Boolean)
      .map((label) => ({ label, path: (current = current ? `${current}/${label}` : label) }));
  });
}
