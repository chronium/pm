import { Component } from '@angular/core';

import { PmBadge } from '../ui/badge/badge';

@Component({
  selector: 'pm-badge-gallery',
  imports: [PmBadge],
  template: `
    <section class="component-page pm-frosted-surface pm-scroll-surface pm-component-surface">
      <header class="component-header">
        <p>Foundation</p>
        <h1>Badges</h1>
      </header>

      <section class="specimen" aria-labelledby="badge-tones">
        <h2 id="badge-tones">Tones</h2>
        <div class="specimen-content">
          <pm-badge tone="neutral">Backlog</pm-badge>
          <pm-badge tone="accent">In progress</pm-badge>
          <pm-badge tone="success">Done</pm-badge>
          <pm-badge tone="warning">Blocked</pm-badge>
          <pm-badge tone="danger">Failed</pm-badge>
        </div>
      </section>

      <section class="specimen" aria-labelledby="badge-task-context">
        <h2 id="badge-task-context">Task context</h2>
        <div class="specimen-content">
          <pm-badge tone="danger">Priority: urgent</pm-badge>
          <pm-badge tone="warning">Priority: medium</pm-badge>
          <pm-badge tone="success">Ready</pm-badge>
          <pm-badge tone="warning">Blocked</pm-badge>
        </div>
      </section>

      <section class="specimen" aria-labelledby="badge-long-content">
        <h2 id="badge-long-content">Long content</h2>
        <div class="specimen-content">
          <pm-badge tone="warning">Waiting for architecture review from the platform team</pm-badge>
        </div>
      </section>
    </section>
  `,
  styleUrl: './gallery-page.css',
})
export class BadgeGallery {}
