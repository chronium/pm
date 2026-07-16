import { Component, effect, inject, Injector, input, signal } from '@angular/core';
import { disabled, FormField, form, required } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { PmConfirmDialog } from '../ui/confirm-dialog/confirm-dialog';
import { PmFormField } from '../ui/form-field/form-field';
import { PmErrorState, PmLoadingState } from '../ui/state/state';
import { WikiApiService } from './wiki-api.service';
import { WikiBreadcrumbs } from './wiki-breadcrumbs';
import { WikiDirtyForm } from './wiki-dirty-form';
import { WikiStore } from './wiki.store';

@Component({
  selector: 'pm-wiki-metadata',
  imports: [
    FormField,
    PmConfirmDialog,
    PmErrorState,
    PmFormField,
    PmLoadingState,
    RouterLink,
    WikiBreadcrumbs,
  ],
  template: ` <section class="wiki-page wiki-form-page">
      @if (store.pageLoading()) {
        <pm-loading-state>Loading metadata…</pm-loading-state>
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
          <p class="wiki-eyebrow">Wiki page</p>
          <h1>Metadata</h1>
        </header>
        @if (conflict()) {
          <div class="wiki-conflict" role="alert">
            <strong>This page changed elsewhere.</strong>
            <p>Your changes are preserved. Reload the latest version before editing again.</p>
            <button class="pm-button pm-button--secondary" type="button" (click)="reloadLatest()">
              Reload latest
            </button>
          </div>
        }
        <form class="wiki-form" (submit)="submit($event)" novalidate>
          @if (error()) {
            <p class="form-error" role="alert">{{ error() }}</p>
          }
          <pm-form-field
            ><label for="wiki-meta-path">Path</label
            ><input id="wiki-meta-path" pmControl [formField]="pageForm.path" autocomplete="off" />
            @if (pageForm.path().touched() && firstError(pageForm.path())) {
              <p pmFieldMessage class="field-error">{{ firstError(pageForm.path()) }}</p>
            }
          </pm-form-field>
          <pm-form-field
            ><label for="wiki-meta-title">Title</label
            ><input
              id="wiki-meta-title"
              pmControl
              [formField]="pageForm.title"
              autocomplete="off"
            />
            @if (pageForm.title().touched() && firstError(pageForm.title())) {
              <p pmFieldMessage class="field-error">{{ firstError(pageForm.title()) }}</p>
            }
          </pm-form-field>
          <div class="wiki-form-actions">
            <a
              class="pm-button pm-button--secondary"
              [routerLink]="['/wiki', ...page.path.split('/')]"
              >Cancel</a
            ><button
              class="pm-button pm-button--primary"
              type="submit"
              [disabled]="pending() || conflict() || !pageForm().valid()"
            >
              {{ pending() ? 'Saving…' : 'Save metadata' }}
            </button>
          </div>
        </form>
        <section class="wiki-danger" aria-labelledby="wiki-delete-heading">
          <h2 id="wiki-delete-heading">Delete page</h2>
          <p>Permanently remove this page. This cannot be undone.</p>
          <button
            class="pm-button pm-button--danger"
            type="button"
            [disabled]="pending() || conflict()"
            (click)="deleteOpen.set(true)"
          >
            Delete page
          </button>
        </section>
      }
    </section>
    <pm-confirm-dialog
      [open]="confirmDiscardOpen()"
      heading="Discard metadata changes?"
      message="Your unsaved path and title changes will be lost."
      confirmLabel="Discard"
      (confirmed)="discardChanges()"
      (cancelled)="keepEditing()"
    />
    <pm-confirm-dialog
      [open]="deleteOpen()"
      [pending]="pending()"
      heading="Delete wiki page?"
      message="This permanently removes the page and cannot be undone."
      confirmLabel="Delete page"
      (confirmed)="remove()"
      (cancelled)="deleteOpen.set(false)"
    />`,
  styleUrl: './wiki.css',
})
export class WikiMetadata extends WikiDirtyForm {
  readonly wikiPath = input('');
  protected readonly store = inject(WikiStore);
  private readonly api = inject(WikiApiService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  protected readonly pending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly conflict = signal(false);
  protected readonly deleteOpen = signal(false);
  readonly model = signal({ path: '', title: '' });
  readonly pageForm = form(
    this.model,
    (page) => {
      required(page.path, { message: 'Path is required.' });
      required(page.title, { message: 'Title is required.' });
      disabled(page.path, () => this.conflict());
      disabled(page.title, () => this.conflict());
    },
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
      this.model.set({ path: page.path, title: page.title });
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
  protected firstError(field: ReturnType<typeof this.pageForm.path>): string | null {
    return field.errors()[0]?.message ?? null;
  }
  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.pageForm().markAsTouched();
    if (!this.pageForm().valid() || this.pending() || this.conflict()) return;
    const oldPath = this.wikiPath();
    this.pending.set(true);
    this.error.set(null);
    try {
      const value = this.model();
      const response = await firstValueFrom(
        this.api.updateMetadata(
          oldPath,
          { path: value.path.trim(), title: value.title.trim() },
          this.store.etag(),
        ),
      );
      const page = this.store.accept(response, oldPath);
      this.loadedRevision = page.revision;
      this.allowLeave = true;
      await this.router.navigate(['/wiki', ...page.path.split('/')], { replaceUrl: true });
    } catch (error) {
      const mapped = this.api.error(error, 'The wiki metadata could not be saved.');
      this.error.set(mapped.message);
      this.conflict.set(mapped.conflict);
    } finally {
      this.pending.set(false);
    }
  }
  protected async remove(): Promise<void> {
    if (this.pending() || this.conflict()) return;
    this.pending.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.api.remove(this.wikiPath(), this.store.etag()));
      this.store.removeLocal(this.wikiPath());
      this.allowLeave = true;
      await this.router.navigate(['/wiki'], { replaceUrl: true });
    } catch (error) {
      const mapped = this.api.error(error, 'The wiki page could not be deleted.');
      this.error.set(mapped.message);
      this.conflict.set(mapped.conflict);
      this.deleteOpen.set(false);
    } finally {
      this.pending.set(false);
    }
  }
  protected reloadLatest(): void {
    this.store.reloadPage();
  }
}
