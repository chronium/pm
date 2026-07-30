import { Component } from '@angular/core';

import { PmEmptyState, PmErrorState, PmLoadingState } from '../ui/state/state';

@Component({
  selector: 'pm-state-gallery',
  imports: [PmEmptyState, PmErrorState, PmLoadingState],
  template: `
    <section class="component-page pm-frosted-surface pm-scroll-surface pm-component-surface">
      <header class="component-header">
        <p>Foundation</p>
        <h1>Status states</h1>
      </header>

      <section class="specimen" aria-labelledby="state-loading">
        <h2 id="state-loading">Loading</h2>
        <div class="specimen-content specimen-content--wide">
          <pm-loading-state>Loading tasks for the selected milestone…</pm-loading-state>
        </div>
      </section>

      <section class="specimen" aria-labelledby="state-empty">
        <h2 id="state-empty">Empty</h2>
        <div class="specimen-content specimen-content--wide">
          <pm-empty-state>No tasks match the active filters.</pm-empty-state>
        </div>
      </section>

      <section class="specimen" aria-labelledby="state-error">
        <h2 id="state-error">Error</h2>
        <div class="specimen-content specimen-content--wide">
          <pm-error-state>Tasks could not be loaded. Check the local PM server.</pm-error-state>
        </div>
      </section>
    </section>
  `,
  styleUrl: './gallery-page.css',
})
export class StateGallery {}
