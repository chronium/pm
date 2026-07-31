import { DatePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { PmEmptyState, PmErrorState, PmLoadingState } from '../ui/state/state';
import { WikiStore } from './wiki.store';
import { ProjectContextService } from '../core/project-context.service';

@Component({
  selector: 'pm-wiki-index',
  imports: [DatePipe, RouterLink, PmEmptyState, PmErrorState, PmLoadingState],
  template: ` <section class="wiki-page pm-frosted-surface pm-scroll-surface pm-wiki-surface">
    <header class="wiki-page-header">
      <div>
        <p class="wiki-eyebrow">Workspace</p>
        <h1>Wiki</h1>
      </div>
      @if (!projectContext.readOnly()) {
        <a class="pm-button pm-button--primary" routerLink="/wiki/new">New page</a>
      }
    </header>
    @if (store.indexLoading()) {
      <pm-loading-state>Loading wiki pages…</pm-loading-state>
    } @else if (store.indexError()) {
      <pm-error-state
        ><p>{{ store.indexError() }}</p>
        <button class="pm-button pm-button--secondary" (click)="store.reloadIndex()">
          Try again
        </button></pm-error-state
      >
    } @else if (!store.pages()?.length) {
      <pm-empty-state
        ><p>No wiki pages yet.</p>
        @if (!projectContext.readOnly()) {
          <a class="pm-button pm-button--primary" routerLink="/wiki/new">Create the first page</a>
        }
      </pm-empty-state>
    } @else {
      <div class="wiki-list" aria-label="All wiki pages">
        @for (page of store.pages(); track page.path) {
          <a class="wiki-list-row" [routerLink]="projectContext.wikiUrl(page.path)">
            <span class="wiki-list-title">{{ page.title }}</span
            ><code>{{ page.path }}</code
            ><time [attr.datetime]="page.modifiedAt">{{ page.modifiedAt | date: 'medium' }}</time>
          </a>
        }
      </div>
    }
  </section>`,
  styleUrl: './wiki.css',
})
export class WikiIndex {
  protected readonly store = inject(WikiStore);
  protected readonly projectContext = inject(ProjectContextService);
}
