import { Component, effect, inject, Injector, input, signal } from '@angular/core';
import { disabled, FormField, form } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { MarkdownEditor } from '../markdown/markdown-editor';
import { PmConfirmDialog } from '../ui/confirm-dialog/confirm-dialog';
import { PmErrorState, PmLoadingState } from '../ui/state/state';
import { WikiApiService } from './wiki-api.service';
import { WikiBreadcrumbs } from './wiki-breadcrumbs';
import { WikiDirtyForm } from './wiki-dirty-form';
import { WikiStore } from './wiki.store';

@Component({
  selector: 'pm-wiki-edit',
  imports: [
    FormField,
    MarkdownEditor,
    PmConfirmDialog,
    PmErrorState,
    PmLoadingState,
    RouterLink,
    WikiBreadcrumbs,
  ],
  template: ` <section class="wiki-page wiki-form-page">
      @if (store.pageLoading()) {
        <pm-loading-state>Loading editor…</pm-loading-state>
      } @else if (store.pageError()) {
        <pm-error-state
          ><h1>Page unavailable</h1>
          <p>{{ store.pageError() }}</p>
          <a class="pm-button pm-button--secondary" routerLink="/wiki"
            >Back to wiki</a
          ></pm-error-state
        >
      } @else if (store.page(); as page) {
        <pm-wiki-breadcrumbs [path]="page.path" />
        <header>
          <p class="wiki-eyebrow">
            <code>{{ page.path }}</code>
          </p>
          <h1>Edit {{ page.title }}</h1>
        </header>
        @if (conflict()) {
          <div class="wiki-conflict" role="alert">
            <strong>This page changed elsewhere.</strong>
            <p>Your draft is preserved. Reload the latest version before editing again.</p>
            <button class="pm-button pm-button--secondary" type="button" (click)="reloadLatest()">
              Reload latest
            </button>
          </div>
        }
        <form class="wiki-form" (submit)="submit($event)">
          @if (error()) {
            <p class="form-error" role="alert">{{ error() }}</p>
          }
          <label id="wiki-edit-body">Markdown body</label
          ><pm-markdown-editor
            pmControl
            [formField]="pageForm.body"
            label="Wiki page Markdown body"
            aria-labelledby="wiki-edit-body"
          />
          <div class="wiki-form-actions">
            <a
              class="pm-button pm-button--secondary"
              [routerLink]="['/wiki', ...page.path.split('/')]"
              >Cancel</a
            ><button
              class="pm-button pm-button--primary"
              type="submit"
              [disabled]="pending() || conflict()"
            >
              {{ pending() ? 'Saving…' : 'Save body' }}
            </button>
          </div>
        </form>
      }
    </section>
    <pm-confirm-dialog
      [open]="confirmDiscardOpen()"
      heading="Discard body changes?"
      message="Your unsaved Markdown will be lost."
      confirmLabel="Discard"
      (confirmed)="discardChanges()"
      (cancelled)="keepEditing()"
    />`,
  styleUrl: './wiki.css',
})
export class WikiEdit extends WikiDirtyForm {
  readonly wikiPath = input('');
  protected readonly store = inject(WikiStore);
  private readonly api = inject(WikiApiService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  protected readonly pending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly conflict = signal(false);
  readonly model = signal({ body: '' });
  readonly pageForm = form(
    this.model,
    (page) => disabled(page.body, () => this.pending() || this.conflict()),
    { injector: this.injector },
  );
  private loadedRevision = '';

  constructor() {
    super();
    effect(() => {
      if (this.wikiPath()) this.store.select(this.wikiPath());
    });
    effect(() => {
      const page = this.store.page();
      if (!page || page.revision === this.loadedRevision) return;
      this.loadedRevision = page.revision;
      this.model.set({ body: page.body });
      this.pageForm().reset();
      this.conflict.set(false);
      this.error.set(null);
    });
  }
  protected dirty(): boolean {
    return this.pageForm().dirty();
  }
  protected busy(): boolean {
    return this.pending();
  }
  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.pending() || this.conflict() || !this.store.page()) return;
    this.pending.set(true);
    this.error.set(null);
    try {
      const response = await firstValueFrom(
        this.api.updateBody(this.wikiPath(), { body: this.model().body }, this.store.etag()),
      );
      const page = this.store.accept(response);
      this.loadedRevision = page.revision;
      this.pageForm().reset();
      this.allowLeave = true;
      await this.router.navigate(['/wiki', ...page.path.split('/')], { replaceUrl: true });
    } catch (error) {
      const mapped = this.api.error(error, 'The wiki body could not be saved.');
      this.error.set(mapped.message);
      this.conflict.set(mapped.conflict);
    } finally {
      this.pending.set(false);
    }
  }
  protected reloadLatest(): void {
    this.store.reloadPage();
  }
}
