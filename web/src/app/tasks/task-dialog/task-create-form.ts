import { Component, effect, inject, Injector, input, output, signal } from '@angular/core';
import { FormField, form, required } from '@angular/forms/signals';

import type { components } from '../../api/generated/pm-api';
import { MarkdownEditor } from '../../markdown/markdown-editor';
import { PmFormField } from '../../ui/form-field/form-field';
import type { CreateTaskRequest } from '../task-api.service';

type BoardOption = components['schemas']['BoardOptionResponse'];

@Component({
  selector: 'pm-task-create-form',
  imports: [FormField, MarkdownEditor, PmFormField],
  templateUrl: './task-create-form.html',
  styleUrl: './task-form.css',
})
export class TaskCreateForm {
  readonly tracks = input.required<readonly BoardOption[]>();
  readonly milestones = input.required<readonly BoardOption[]>();
  readonly initialTrack = input<string | null>(null);
  readonly initialMilestone = input<string | null>(null);
  readonly pending = input(false);
  readonly apiError = input<string | null>(null);
  readonly submitted = output<CreateTaskRequest>();
  readonly cancelled = output<void>();

  private readonly injector = inject(Injector);
  private initialized = false;
  readonly model = signal({ title: '', track: '', milestone: '', description: '' });
  readonly taskForm = form(
    this.model,
    (task) => {
      required(task.title, { message: 'Title is required.' });
      required(task.track, { message: 'Track is required.' });
    },
    { injector: this.injector },
  );

  constructor() {
    effect(() => {
      const tracks = this.tracks();
      if (this.initialized || tracks.length === 0) return;
      this.initialized = true;
      const track = tracks.some((item) => item.key === this.initialTrack())
        ? this.initialTrack()!
        : tracks[0]!.key;
      const milestone = this.milestones().some((item) => item.key === this.initialMilestone())
        ? this.initialMilestone()!
        : '';
      this.model.set({ ...this.model(), track, milestone });
    });
  }

  dirty(): boolean {
    return this.taskForm().dirty();
  }

  protected submit(event: Event): void {
    event.preventDefault();
    this.taskForm().markAsTouched();
    if (!this.taskForm().valid() || this.pending()) return;
    const value = this.model();
    this.submitted.emit({
      title: value.title.trim(),
      track: value.track,
      milestone: value.milestone || null,
      description: value.description,
    });
  }

  protected firstError(field: ReturnType<typeof this.taskForm.title>): string | null {
    return field.errors()[0]?.message ?? null;
  }
}
