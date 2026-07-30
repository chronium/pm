import { Component, signal } from '@angular/core';

import { TopBarSearch, type TopBarSearchOption } from '../shared/top-bar-search/top-bar-search';

const results: readonly TopBarSearchOption[] = [
  {
    id: 'rendering-guide',
    primary: 'Rendering guide',
    leading: 'WIKI',
    secondary: 'guides/rendering · Jul 30, 2026',
    snippet: 'The rendering pipeline starts with…',
  },
  {
    id: 'render-task',
    primary: 'Improve Markdown rendering',
    leading: 'PM-0074',
    secondary: 'PM · component-gallery',
  },
];

@Component({
  selector: 'pm-search-gallery',
  imports: [TopBarSearch],
  template: `
    <section class="component-page pm-frosted-surface pm-scroll-surface pm-component-surface">
      <header class="component-header">
        <p>Content</p>
        <h1>Search</h1>
      </header>

      <section class="specimen" aria-labelledby="search-results">
        <h2 id="search-results">Results</h2>
        <div class="search-specimen">
          <pm-top-bar-search
            ariaLabel="Search result example"
            listboxLabel="Example search results"
            placeholder="Search examples"
            emptyMessage="Nothing found."
            [(query)]="resultsQuery"
            [options]="results"
          />
        </div>
      </section>

      <section class="specimen" aria-labelledby="search-loading">
        <h2 id="search-loading">Loading</h2>
        <div class="search-specimen">
          <pm-top-bar-search
            ariaLabel="Loading search example"
            listboxLabel="Loading search results"
            placeholder="Search examples"
            emptyMessage="Nothing found."
            [(query)]="loadingQuery"
            [options]="[]"
            [loading]="true"
          />
        </div>
      </section>

      <section class="specimen" aria-labelledby="search-empty">
        <h2 id="search-empty">Empty</h2>
        <div class="search-specimen">
          <pm-top-bar-search
            ariaLabel="Empty search example"
            listboxLabel="Empty search results"
            placeholder="Search examples"
            emptyMessage="Nothing found."
            [(query)]="emptyQuery"
            [options]="[]"
          />
        </div>
      </section>

      <section class="specimen" aria-labelledby="search-error">
        <h2 id="search-error">Error</h2>
        <div class="search-specimen">
          <pm-top-bar-search
            ariaLabel="Failed search example"
            listboxLabel="Failed search results"
            placeholder="Search examples"
            emptyMessage="Nothing found."
            [(query)]="errorQuery"
            [options]="[]"
            error="Search failed."
          />
        </div>
      </section>
    </section>
  `,
  styleUrls: ['./gallery-page.css', './search-gallery.css'],
})
export class SearchGallery {
  protected readonly results = results;
  protected readonly resultsQuery = signal('render');
  protected readonly loadingQuery = signal('tasks');
  protected readonly emptyQuery = signal('missing');
  protected readonly errorQuery = signal('wiki');
}
