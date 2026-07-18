import { Component, effect, inject, Injector, input, output, signal } from '@angular/core';
import { FormField, form, required, validate } from '@angular/forms/signals';

import type { components } from '../../api/generated/pm-api';
import { MarkdownEditor } from '../../markdown/markdown-editor';
import { PmFormField } from '../../ui/form-field/form-field';
import type { TaskResponse, UpdateTaskRequest } from '../task-api.service';

type BoardOption = components['schemas']['BoardOptionResponse'];

@Component({
  selector: 'pm-task-edit-form',
  imports: [FormField, MarkdownEditor, PmFormField],
  templateUrl: './task-edit-form.html',
  styleUrl: './task-form.css',
})
export class TaskEditForm {
  readonly task = input.required<TaskResponse>();
  readonly states = input.required<readonly BoardOption[]>();
  readonly tracks = input.required<readonly BoardOption[]>();
  readonly milestones = input.required<readonly BoardOption[]>();
  readonly pending = input(false);
  readonly stale = input(false);
  readonly apiError = input<string | null>(null);
  readonly submitted = output<UpdateTaskRequest>();
  readonly cancelled = output<void>();
  readonly reloadIntent = output<void>();
  private readonly injector = inject(Injector);
  readonly model = signal({
    title: '',
    state: '',
    priority: 'inherit',
    track: '',
    milestone: '',
    description: '',
  });
  readonly taskForm = form(
    this.model,
    (task) => {
      required(task.title, { message: 'Title is required.' });
      required(task.state, { message: 'State is required.' });
      required(task.priority, { message: 'Priority is required.' });
      required(task.track, { message: 'Track is required.' });
      validate(task.track, ({ value }) =>
        this.tracks().some((option) => option.key === value())
          ? undefined
          : { kind: 'configured-track', message: 'Choose a configured track before saving.' },
      );
      validate(task.milestone, ({ value }) =>
        !value() || this.milestones().some((option) => option.key === value())
          ? undefined
          : {
              kind: 'configured-milestone',
              message: 'Choose a configured milestone or No milestone before saving.',
            },
      );
    },
    { injector: this.injector },
  );
  private loadedRevision = '';

  constructor() {
    effect(() => {
      const task = this.task();
      if (task.revision === this.loadedRevision) return;
      this.loadedRevision = task.revision;
      this.model.set({
        title: task.title,
        state: task.state,
        priority: task.prioritySelection,
        track: task.track,
        milestone: task.milestone ?? '',
        description: task.description,
      });
      this.taskForm().reset();
    });
  }

  dirty(): boolean {
    return this.taskForm().dirty();
  }
  draft(): UpdateTaskRequest {
    const value = this.model();
    return {
      title: value.title,
      state: value.state,
      priority: value.priority,
      description: value.description,
      placement: { track: value.track, milestone: value.milestone || null },
    };
  }
  restoreDraft(draft: UpdateTaskRequest): void {
    this.model.set({
      title: draft.title,
      state: draft.state,
      priority: draft.priority,
      track: draft.placement?.track ?? this.task().track,
      milestone: draft.placement?.milestone ?? '',
      description: draft.description,
    });
    this.taskForm().markAsDirty();
  }
  protected trackConfigured(): boolean {
    return this.tracks().some((option) => option.key === this.model().track);
  }
  protected milestoneConfigured(): boolean {
    const milestone = this.model().milestone;
    return !milestone || this.milestones().some((option) => option.key === milestone);
  }
  protected submit(event: Event): void {
    event.preventDefault();
    this.taskForm().markAsTouched();
    if (!this.taskForm().valid() || this.pending() || this.stale()) return;
    this.submitted.emit({ ...this.draft(), title: this.model().title.trim() });
  }
  protected firstError(field: { errors(): readonly { message?: string }[] }): string | null {
    return field.errors()[0]?.message ?? null;
  }
}
