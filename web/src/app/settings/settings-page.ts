import { Component, effect, inject, Injector, signal } from '@angular/core';
import { FormField, form, required } from '@angular/forms/signals';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { cssPen, cssTrash } from '@ng-icons/css.gg';

import { PmConfirmDialog } from '../ui/confirm-dialog/confirm-dialog';
import { PmFormField } from '../ui/form-field/form-field';
import { PmErrorState, PmLoadingState } from '../ui/state/state';
import { ProjectHealth } from './project-health';
import type { SettingsCollection, SettingsOperation } from './settings.store';
import { SettingsStore } from './settings.store';

interface Editor {
  collection: SettingsCollection;
  key: string;
  field: 'name' | 'title' | 'priority';
}
interface Removal {
  collection: SettingsCollection;
  key: string;
  label: string;
}

@Component({
  selector: 'pm-settings-page',
  imports: [
    FormField,
    NgIcon,
    PmConfirmDialog,
    PmErrorState,
    PmFormField,
    PmLoadingState,
    ProjectHealth,
  ],
  providers: [SettingsStore, provideIcons({ cssPen, cssTrash })],
  templateUrl: './settings-page.html',
  styleUrl: './settings-page.css',
})
export class SettingsPage {
  protected readonly store = inject(SettingsStore);
  private readonly injector = inject(Injector);
  protected readonly adding = signal<SettingsCollection | null>(null);
  protected readonly editor = signal<Editor | null>(null);
  protected readonly removal = signal<Removal | null>(null);

  protected readonly optionCreateModel = signal({ key: '', name: '' });
  protected readonly optionCreateForm = form(
    this.optionCreateModel,
    (item) => {
      required(item.key, { message: 'Key is required.' });
      required(item.name, { message: 'Name is required.' });
    },
    { injector: this.injector },
  );
  protected readonly milestoneCreateModel = signal({ key: '', title: '', priority: '' });
  protected readonly milestoneCreateForm = form(
    this.milestoneCreateModel,
    (item) => {
      required(item.key, { message: 'Key is required.' });
      required(item.title, { message: 'Title is required.' });
      required(item.priority, { message: 'Priority is required.' });
    },
    { injector: this.injector },
  );
  protected readonly editModel = signal({ value: '' });
  protected readonly editForm = form(
    this.editModel,
    (item) => required(item.value, { message: 'A value is required.' }),
    { injector: this.injector },
  );

  constructor() {
    let generation = this.store.reloadGeneration();
    effect(() => {
      const next = this.store.reloadGeneration();
      if (next === generation) return;
      generation = next;
      this.cancelAll();
    });
  }

  protected beginAdd(collection: SettingsCollection): void {
    this.store.clearOperationError();
    this.editor.set(null);
    this.adding.set(collection);
    if (collection === 'milestone') {
      this.milestoneCreateModel.set({
        key: '',
        title: '',
        priority: this.store.settings()?.priorityOptions[0] ?? '',
      });
      this.milestoneCreateForm().reset();
    } else {
      this.optionCreateModel.set({ key: '', name: '' });
      this.optionCreateForm().reset();
    }
  }

  protected beginEdit(editor: Editor, value: string): void {
    this.store.clearOperationError();
    this.adding.set(null);
    this.editor.set(editor);
    this.editModel.set({ value });
    this.editForm().reset();
  }

  protected cancelAll(): void {
    this.adding.set(null);
    this.editor.set(null);
    this.removal.set(null);
    this.optionCreateForm().reset();
    this.milestoneCreateForm().reset();
    this.editForm().reset();
  }

  protected async createOption(event: Event, collection: 'status' | 'track'): Promise<void> {
    event.preventDefault();
    this.optionCreateForm().markAsTouched();
    if (!this.optionCreateForm().valid() || this.store.pending() || this.store.stale()) return;
    const model = this.optionCreateModel();
    const request = { key: model.key.trim(), name: model.name.trim() };
    const success =
      collection === 'status'
        ? await this.store.createStatus(request)
        : await this.store.createTrack(request);
    if (success) this.adding.set(null);
  }

  protected async createMilestone(event: Event): Promise<void> {
    event.preventDefault();
    this.milestoneCreateForm().markAsTouched();
    if (!this.milestoneCreateForm().valid() || this.store.pending() || this.store.stale()) return;
    const value = this.milestoneCreateModel();
    if (
      await this.store.createMilestone({
        key: value.key.trim(),
        title: value.title.trim(),
        priority: value.priority,
      })
    ) {
      this.adding.set(null);
    }
  }

  protected async saveEdit(event: Event): Promise<void> {
    event.preventDefault();
    this.editForm().markAsTouched();
    const editor = this.editor();
    if (!editor || !this.editForm().valid() || this.store.pending() || this.store.stale()) return;
    const value = this.editModel().value.trim();
    let success = false;
    if (editor.collection === 'status')
      success = await this.store.renameStatus(editor.key, { name: value });
    if (editor.collection === 'track')
      success = await this.store.renameTrack(editor.key, { name: value });
    if (editor.collection === 'milestone' && editor.field === 'title')
      success = await this.store.renameMilestone(editor.key, { title: value });
    if (editor.collection === 'milestone' && editor.field === 'priority')
      success = await this.store.setMilestonePriority(editor.key, { priority: value });
    if (success) this.editor.set(null);
  }

  protected requestRemoval(collection: SettingsCollection, key: string, label: string): void {
    this.store.clearOperationError();
    this.removal.set({ collection, key, label });
  }

  protected async confirmRemoval(): Promise<void> {
    const removal = this.removal();
    if (!removal) return;
    let success = false;
    if (removal.collection === 'status') success = await this.store.removeStatus(removal.key);
    if (removal.collection === 'track') success = await this.store.removeTrack(removal.key);
    if (removal.collection === 'milestone') success = await this.store.removeMilestone(removal.key);
    this.removal.set(null);
    if (success) this.editor.set(null);
  }

  protected pending(
    kind: SettingsOperation['kind'],
    collection: SettingsCollection,
    key: string | null,
  ): boolean {
    return this.store.isPending({ kind, collection, key });
  }

  protected formError(field: ReturnType<typeof this.editForm.value>): string | null {
    return field.errors()[0]?.message ?? null;
  }

  protected collectionLabel(collection: SettingsCollection): string {
    return collection === 'status' ? 'status' : collection;
  }
}
