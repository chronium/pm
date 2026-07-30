import { Component, effect, inject, Injector, input, signal } from '@angular/core';
import { disabled, FormField, form } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { PmConfirmDialog } from '../ui/confirm-dialog/confirm-dialog';
import { PmErrorState, PmLoadingState } from '../ui/state/state';
import { WikiApiService } from './wiki-api.service';
import { WikiBreadcrumbs } from './wiki-breadcrumbs';
import { WikiDirtyForm } from './wiki-dirty-form';
import { WikiMarkdownWorkspace } from './wiki-markdown-workspace';
import { WikiStore } from './wiki.store';
import { ExternalChangeBanner, type ExternalChangePhase } from '../core/external-change-banner';
import type { UpdateWikiPageBodyRequest } from './wiki-api.service';

@Component({
  selector: 'pm-wiki-edit',
  imports: [
    FormField,
    PmConfirmDialog,
    PmErrorState,
    PmLoadingState,
    RouterLink,
    WikiBreadcrumbs,
    WikiMarkdownWorkspace,
    ExternalChangeBanner,
  ],
  template: ` <section
      class="wiki-page wiki-form-page wiki-edit-page pm-frosted-surface pm-scroll-surface pm-wiki-surface"
    >
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
        <header class="wiki-form-header">
          <div>
            <p class="wiki-eyebrow">
              <code>{{ page.path }}</code>
            </p>
            <h1>Edit {{ page.title }}</h1>
          </div>
          <div class="wiki-form-actions">
            <a
              class="pm-button pm-button--secondary"
              [routerLink]="['/wiki', ...page.path.split('/')]"
              >Cancel</a
            ><button
              class="pm-button pm-button--primary"
              type="submit"
              form="wiki-edit-form"
              [disabled]="
                pending() ||
                store.unavailable() ||
                conflict() === 'pending' ||
                conflict() === 'reviewing'
              "
            >
              {{ pending() ? 'Saving…' : 'Save body' }}
            </button>
          </div>
        </header>
        @if (conflict()) {
          <pm-external-change-banner
            [phase]="conflict()!"
            heading="This page changed elsewhere."
            (review)="reviewLatest()"
            (restore)="restoreDraft()"
            (keep)="keepLatest()"
          />
        }
        @if (store.liveUpdateUnavailable()) {
          <p class="live-update-status" role="status">Live updates unavailable; retrying</p>
        }
        @if (store.unavailable()) {
          <p class="form-error" role="alert">
            This page was removed or renamed elsewhere. Your draft is preserved, but it cannot be
            saved here.
          </p>
        }
        <form id="wiki-edit-form" class="wiki-form" (submit)="submit($event)">
          @if (error()) {
            <p class="form-error" role="alert">{{ error() }}</p>
          }
          <div class="wiki-body-field">
            <span id="wiki-edit-body" class="wiki-body-label">Markdown body</span>
            <pm-wiki-markdown-workspace
              pmControl
              [formField]="pageForm.body"
              label="Wiki page Markdown body"
              aria-labelledby="wiki-edit-body"
            />
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
  protected readonly conflict = signal<ExternalChangePhase | null>(null);
  readonly model = signal({ body: '' });
  readonly pageForm = form(
    this.model,
    (page) =>
      disabled(
        page.body,
        () => this.pending() || this.conflict() === 'pending' || this.conflict() === 'reviewing',
      ),
    { injector: this.injector },
  );
  private loadedRevision = '';
  private draftSnapshot: UpdateWikiPageBodyRequest | null = null;

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
      if (this.conflict() !== 'reviewing') this.conflict.set(null);
      this.error.set(null);
    });
    effect(() => this.store.setDirty(this.dirty()));
    effect(() => {
      if (this.store.pendingExternal()) this.conflict.set('pending');
    });
  }
  protected dirty(): boolean {
    return this.pageForm().dirty() || !!this.draftSnapshot;
  }
  protected busy(): boolean {
    return this.pending();
  }
  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (
      this.pending() ||
      this.conflict() === 'pending' ||
      this.conflict() === 'reviewing' ||
      !this.store.page() ||
      this.store.unavailable()
    )
      return;
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
      if (mapped.conflict) {
        this.store.setDirty(true);
        this.store.fetchLatest();
      }
    } finally {
      this.pending.set(false);
    }
  }
  protected reloadLatest(): void {
    this.store.fetchLatest();
  }
  protected reviewLatest(): void {
    if (!this.store.pendingExternal()) return;
    this.draftSnapshot = { body: this.model().body };
    this.store.reviewLatest();
    this.conflict.set('reviewing');
  }
  protected restoreDraft(): void {
    if (!this.draftSnapshot) return;
    this.model.set({ body: this.draftSnapshot.body });
    this.pageForm().markAsDirty();
    this.conflict.set('preserved');
  }
  protected keepLatest(): void {
    this.draftSnapshot = null;
    this.store.keepLatest();
    this.pageForm().reset();
    this.conflict.set(null);
    this.error.set(null);
  }
}
