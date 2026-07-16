import { Component, inject, Injector, signal } from '@angular/core';
import { FormField, form, required } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { MarkdownEditor } from '../markdown/markdown-editor';
import { PmConfirmDialog } from '../ui/confirm-dialog/confirm-dialog';
import { PmFormField } from '../ui/form-field/form-field';
import { WikiApiService } from './wiki-api.service';
import { WikiDirtyForm } from './wiki-dirty-form';
import { WikiStore } from './wiki.store';

@Component({
  selector: 'pm-wiki-create',
  imports: [FormField, MarkdownEditor, PmConfirmDialog, PmFormField, RouterLink],
  template: `
    <section class="wiki-page wiki-form-page"><header><p class="wiki-eyebrow">Wiki</p><h1>New page</h1></header>
      <form class="wiki-form" (submit)="submit($event)" novalidate>
        @if (error()) { <p class="form-error" role="alert">{{ error() }}</p> }
        <pm-form-field><label for="wiki-path">Path</label><input id="wiki-path" pmControl [formField]="pageForm.path" autocomplete="off" autofocus placeholder="guides/getting-started" />@if (pageForm.path().touched() && firstError(pageForm.path())) { <p pmFieldMessage class="field-error">{{ firstError(pageForm.path()) }}</p> }<p pmFieldMessage>Use slashes to organize pages.</p></pm-form-field>
        <pm-form-field><label for="wiki-title">Title</label><input id="wiki-title" pmControl [formField]="pageForm.title" autocomplete="off" />@if (pageForm.title().touched() && firstError(pageForm.title())) { <p pmFieldMessage class="field-error">{{ firstError(pageForm.title()) }}</p> }</pm-form-field>
        <pm-form-field><label id="wiki-body-label">Body</label><pm-markdown-editor pmControl [formField]="pageForm.body" label="Wiki page body" aria-labelledby="wiki-body-label" /><p pmFieldMessage>Markdown supported.</p></pm-form-field>
        <div class="wiki-form-actions"><a class="pm-button pm-button--secondary" routerLink="/wiki">Cancel</a><button class="pm-button pm-button--primary" type="submit" [disabled]="pending() || !pageForm().valid()">{{ pending() ? 'Creating…' : 'Create page' }}</button></div>
      </form>
    </section>
    <pm-confirm-dialog [open]="confirmDiscardOpen()" heading="Discard wiki draft?" message="Your unsaved page will be lost." confirmLabel="Discard" (confirmed)="discardChanges()" (cancelled)="keepEditing()" />`,
  styleUrl: './wiki.css',
})
export class WikiCreate extends WikiDirtyForm {
  private readonly api = inject(WikiApiService); private readonly store = inject(WikiStore); private readonly router = inject(Router); private readonly injector = inject(Injector);
  protected readonly pending = signal(false); protected readonly error = signal<string | null>(null);
  readonly model = signal({ path: '', title: '', body: '' });
  readonly pageForm = form(this.model, (page) => { required(page.path, { message: 'Path is required.' }); required(page.title, { message: 'Title is required.' }); }, { injector: this.injector });
  protected dirty(): boolean { return this.pageForm().dirty(); } protected busy(): boolean { return this.pending(); }
  protected firstError(field: ReturnType<typeof this.pageForm.path>): string | null { return field.errors()[0]?.message ?? null; }
  protected async submit(event: Event): Promise<void> {
    event.preventDefault(); this.pageForm().markAsTouched(); if (!this.pageForm().valid() || this.pending()) return;
    this.pending.set(true); this.error.set(null);
    try { const value = this.model(); const response = await firstValueFrom(this.api.create({ path: value.path.trim(), title: value.title.trim(), body: value.body })); const page = this.store.accept(response); this.allowLeave = true; await this.router.navigate(['/wiki', ...page.path.split('/')], { replaceUrl: true }); }
    catch (error) { this.error.set(this.api.error(error, 'The wiki page could not be created.').message); } finally { this.pending.set(false); }
  }
}
