import { DatePipe } from '@angular/common';
import { Component, computed, effect, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { MarkdownDisplay } from '../markdown/markdown-display';
import { PmErrorState, PmLoadingState } from '../ui/state/state';
import { WikiBreadcrumbs } from './wiki-breadcrumbs';
import { WikiStore } from './wiki.store';

@Component({
  selector: 'pm-wiki-workspace',
  imports: [DatePipe, MarkdownDisplay, RouterLink, PmErrorState, PmLoadingState, WikiBreadcrumbs],
  template: ` <section class="wiki-page">
    @if (store.indexLoading()) {
      <pm-loading-state>Resolving wiki path…</pm-loading-state>
    } @else if (store.indexError()) {
      <pm-error-state
        ><p>{{ store.indexError() }}</p>
        <button class="pm-button pm-button--secondary" (click)="store.reloadIndex()">
          Try again
        </button></pm-error-state
      >
    } @else if (resolution().kind === 'folder') {
      <pm-wiki-breadcrumbs [path]="wikiPath()" />
      <header class="wiki-page-header">
        <div>
          <p class="wiki-eyebrow">Folder</p>
          <h1>{{ folderName() }}</h1>
        </div>
        <a class="pm-button pm-button--primary" routerLink="/wiki/new">New page</a>
      </header>
      <div class="wiki-list" aria-label="Pages in folder">
        @for (page of folderPages(); track page.path) {
          <a class="wiki-list-row" [routerLink]="['/wiki', ...page.path.split('/')]"
            ><span class="wiki-list-title">{{ page.title }}</span
            ><code>{{ page.path }}</code
            ><time [attr.datetime]="page.modifiedAt">{{
              page.modifiedAt | date: 'medium'
            }}</time></a
          >
        }
      </div>
    } @else if (resolution().kind === 'missing') {
      <pm-error-state
        ><h1>Page not found</h1>
        <p>
          No wiki page or folder exists at <code>{{ wikiPath() }}</code
          >.
        </p>
        <a class="pm-button pm-button--secondary" routerLink="/wiki"
          >Back to wiki</a
        ></pm-error-state
      >
    } @else if (store.pageLoading()) {
      <pm-loading-state>Loading wiki page…</pm-loading-state>
    } @else if (store.pageError()) {
      <pm-error-state
        ><h1>Page unavailable</h1>
        <p>{{ store.pageError() }}</p>
        <button class="pm-button pm-button--secondary" (click)="store.reloadPage()">
          Try again
        </button></pm-error-state
      >
    } @else if (store.page(); as page) {
      <article class="wiki-reader" [attr.aria-label]="page.title">
        <pm-wiki-breadcrumbs [path]="page.path" />
        <header class="wiki-reader-header">
          <div>
            <p class="wiki-eyebrow">
              <code>{{ page.path }}</code>
            </p>
            <p class="wiki-time">
              Updated
              <time [attr.datetime]="page.modifiedAt">{{ page.modifiedAt | date: 'medium' }}</time>
            </p>
          </div>
          <div class="wiki-actions">
            <a
              class="pm-button pm-button--secondary"
              [routerLink]="['/wiki/meta', ...page.path.split('/')]"
              >Metadata</a
            ><a
              class="pm-button pm-button--primary"
              [routerLink]="['/wiki/edit', ...page.path.split('/')]"
              >Edit</a
            >
          </div>
        </header>
        <pm-markdown-display [markdown]="page.body" />
      </article>
    }
  </section>`,
  styleUrl: './wiki.css',
})
export class WikiWorkspace {
  readonly wikiPath = input('');
  protected readonly store = inject(WikiStore);
  protected readonly resolution = computed(() => this.store.resolve(this.wikiPath()));
  protected readonly folderPages = computed(() => {
    const value = this.resolution();
    return value.kind === 'folder' ? value.pages : [];
  });
  protected readonly folderName = computed(() => this.wikiPath().split('/').at(-1));

  constructor() {
    effect(() => {
      if (this.wikiPath() && this.resolution().kind === 'page') this.store.select(this.wikiPath());
    });
  }
}
