import { Component, signal } from '@angular/core';
import type { Meta, StoryObj } from '@storybook/angular-vite';

import { PmBadge } from './badge/badge';
import { PmButton } from './button/button.directive';
import { PmConfirmDialog } from './confirm-dialog/confirm-dialog';
import { PmFormField } from './form-field/form-field';
import { PmEmptyState, PmErrorState, PmLoadingState } from './state/state';

@Component({
  selector: 'pm-foundation-gallery',
  imports: [
    PmBadge,
    PmButton,
    PmConfirmDialog,
    PmFormField,
    PmLoadingState,
    PmEmptyState,
    PmErrorState,
  ],
  template: `
    <main class="gallery">
      <header>
        <p class="eyebrow">PM design system</p>
        <h1>Foundation gallery</h1>
        <p class="intro">Tokens and reusable primitives for dense, accessible project workflows.</p>
      </header>

      <section aria-labelledby="surfaces-heading">
        <h2 id="surfaces-heading">Surfaces and semantic color</h2>
        <div class="swatches">
          @for (surface of surfaces; track surface.token) {
            <div class="swatch" [style.background]="'var(' + surface.token + ')'">
              <strong>{{ surface.name }}</strong>
              <code>{{ surface.token }}</code>
            </div>
          }
        </div>
      </section>

      <section aria-labelledby="type-heading">
        <h2 id="type-heading">Typography</h2>
        <div class="type-samples">
          <p class="type-lg">Milestone overview · Large</p>
          <p class="type-md">PM-0048 Add Storybook for Angular components · Medium</p>
          <p class="type-sm">Updated moments ago · Small</p>
          <code>dotnet PM.dll web --port 51237</code>
        </div>
      </section>

      <section aria-labelledby="spacing-heading">
        <h2 id="spacing-heading">Spacing</h2>
        <div class="spacing-scale">
          @for (space of spaces; track space.token) {
            <div class="spacing-item">
              <span [style.width]="'var(' + space.token + ')'" aria-hidden="true"></span>
              <code>{{ space.token }}</code>
            </div>
          }
        </div>
      </section>

      <section aria-labelledby="controls-heading">
        <h2 id="controls-heading">Controls and status</h2>
        <div class="row">
          <button type="button" pmButton="primary">Create task</button>
          <button type="button" pmButton="secondary">Edit details</button>
          <button type="button" pmButton="ghost">Cancel</button>
          <button type="button" pmButton="danger" (click)="dialogOpen.set(true)">
            Remove task
          </button>
        </div>
        <div class="row">
          <pm-badge tone="neutral">Backlog</pm-badge>
          <pm-badge tone="accent">In progress</pm-badge>
          <pm-badge tone="success">Done</pm-badge>
          <pm-badge tone="warning">Blocked</pm-badge>
          <pm-badge tone="danger">Failed</pm-badge>
        </div>
      </section>

      <section aria-labelledby="field-heading">
        <h2 id="field-heading">Form field</h2>
        <pm-form-field>
          <label for="gallery-task-title">Task title</label>
          <input
            pmControl
            id="gallery-task-title"
            aria-describedby="gallery-task-hint"
            value="Add Storybook for Angular components"
          />
          <p pmFieldMessage id="gallery-task-hint">Use a concise, action-oriented title.</p>
        </pm-form-field>
      </section>

      <section aria-labelledby="states-heading">
        <h2 id="states-heading">Workflow states</h2>
        <div class="state-grid">
          <pm-loading-state>Loading milestone tasks…</pm-loading-state>
          <pm-empty-state>No tasks match the active filters.</pm-empty-state>
          <pm-error-state>Tasks could not be loaded from the local server.</pm-error-state>
        </div>
      </section>

      <pm-confirm-dialog
        [open]="dialogOpen()"
        (openChange)="dialogOpen.set($event)"
        heading="Remove PM-0048?"
        message="The task will be removed from the project. This action cannot be undone."
        confirmLabel="Remove task"
      />
    </main>
  `,
  styles: `
    :host {
      display: block;
    }
    .gallery {
      max-width: 1040px;
      margin: 0 auto;
      padding: var(--pm-space-6);
    }
    header {
      margin-bottom: var(--pm-space-6);
    }
    .eyebrow {
      margin: 0 0 var(--pm-space-1);
      color: var(--pm-accent-strong);
      font-size: var(--pm-font-size-xs);
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }
    h1 {
      margin: 0;
      font-size: 1.5rem;
      line-height: 1.25;
    }
    .intro {
      margin: var(--pm-space-2) 0 0;
      color: var(--pm-text-muted);
    }
    section {
      padding: var(--pm-space-5) 0;
      border-top: 1px solid var(--pm-border-subtle);
    }
    h2 {
      margin: 0 0 var(--pm-space-3);
      font-size: var(--pm-font-size-lg);
    }
    .swatches {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
      gap: var(--pm-space-2);
    }
    .swatch {
      min-height: 88px;
      padding: var(--pm-space-3);
      border: 1px solid var(--pm-border-strong);
      border-radius: var(--pm-radius-md);
      color: var(--pm-text-primary);
    }
    .swatch strong,
    .swatch code {
      display: block;
    }
    .swatch code {
      margin-top: var(--pm-space-1);
      color: var(--pm-text-muted);
      font-size: var(--pm-font-size-xs);
    }
    .type-samples {
      display: grid;
      gap: var(--pm-space-2);
    }
    .type-samples p {
      margin: 0;
    }
    .type-lg {
      font-size: var(--pm-font-size-lg);
      font-weight: 600;
    }
    .type-md {
      font-size: var(--pm-font-size-md);
    }
    .type-sm {
      color: var(--pm-text-muted);
      font-size: var(--pm-font-size-sm);
    }
    code {
      font-family: var(--pm-font-family-mono);
    }
    .spacing-scale {
      display: grid;
      gap: var(--pm-space-2);
    }
    .spacing-item {
      display: flex;
      align-items: center;
      gap: var(--pm-space-3);
      min-height: 24px;
    }
    .spacing-item span {
      display: block;
      height: 12px;
      min-width: 4px;
      background: var(--pm-accent);
      border-radius: var(--pm-radius-sm);
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--pm-space-2);
    }
    .row + .row {
      margin-top: var(--pm-space-3);
    }
    pm-form-field {
      display: block;
      max-width: 520px;
    }
    .state-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: var(--pm-space-3);
    }
    @media (max-width: 760px) {
      .gallery {
        padding: var(--pm-space-4);
      }
      .state-grid {
        grid-template-columns: 1fr;
      }
    }
  `,
})
class FoundationGallery {
  readonly dialogOpen = signal(false);
  readonly surfaces = [
    { name: 'Canvas', token: '--pm-surface-canvas' },
    { name: 'Raised', token: '--pm-surface-raised' },
    { name: 'Sidebar', token: '--pm-surface-sidebar' },
    { name: 'Hover', token: '--pm-surface-hover' },
    { name: 'Accent soft', token: '--pm-accent-soft' },
    { name: 'Danger soft', token: '--pm-danger-soft' },
  ];
  readonly spaces = [
    { token: '--pm-space-1' },
    { token: '--pm-space-2' },
    { token: '--pm-space-3' },
    { token: '--pm-space-4' },
    { token: '--pm-space-5' },
    { token: '--pm-space-6' },
  ];
}

const meta = {
  title: 'Design System/Foundation',
  component: FoundationGallery,
  parameters: { layout: 'fullscreen' },
} satisfies Meta<FoundationGallery>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Light: Story = {
  globals: { theme: 'light' },
};

export const Dark: Story = {
  globals: { theme: 'dark' },
};
