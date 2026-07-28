import {
  HttpClient,
  HttpErrorResponse,
  HttpParams,
  HttpResponse,
  httpResource,
} from '@angular/common/http';
import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { PollingCoordinator } from '../core/polling-coordinator';

import type { components } from '../api/generated/pm-api';

export type TaskResponse = components['schemas']['TaskResponse'];
export type CreateTaskRequest = components['schemas']['CreateTaskRequest'];
export type UpdateTaskRequest = components['schemas']['UpdateTaskRequest'];
export type UpdateTaskStateRequest = components['schemas']['UpdateTaskStateRequest'];
export type AppendTaskNoteRequest = components['schemas']['AppendTaskNoteRequest'];
export type NextTaskResponse = components['schemas']['NextTaskResponse'];
export type ApiProblemDetails = components['schemas']['ApiProblemDetails'];
export type TaskMutationResponse = HttpResponse<TaskResponse>;

export interface TaskApiError {
  status: number;
  message: string;
  conflict: boolean;
}

@Injectable({ providedIn: 'root' })
export class TaskApiService {
  private readonly http = inject(HttpClient);
  private readonly mutationOptions = {
    observe: 'response' as const,
    headers: { 'X-PM-Client': 'angular-web' },
  };

  create(request: CreateTaskRequest) {
    return this.http.post<TaskResponse>('/api/v1/tasks', request, this.mutationOptions);
  }

  update(id: string, request: UpdateTaskRequest, etag: string) {
    return this.http.put<TaskResponse>(this.taskUrl(id), request, {
      ...this.mutationOptions,
      headers: { ...this.mutationOptions.headers, 'If-Match': etag },
    });
  }

  updateState(id: string, request: UpdateTaskStateRequest, etag: string) {
    return this.http.put<TaskResponse>(`${this.taskUrl(id)}/state`, request, {
      ...this.mutationOptions,
      headers: { ...this.mutationOptions.headers, 'If-Match': etag },
    });
  }

  appendNote(id: string, request: AppendTaskNoteRequest, etag: string) {
    return this.http.post<TaskResponse>(`${this.taskUrl(id)}/notes`, request, {
      ...this.mutationOptions,
      headers: { ...this.mutationOptions.headers, 'If-Match': etag },
    });
  }

  next(track: string | null, milestone: string | null, readyOnly = true) {
    let params = new HttpParams().set('readyOnly', readyOnly);
    if (track) params = params.set('track', track);
    if (milestone) params = params.set('milestone', milestone);
    return this.http.get<NextTaskResponse>('/api/v1/tasks/next', { params });
  }

  remove(id: string, etag: string) {
    return this.http.delete<void>(this.taskUrl(id), {
      ...this.mutationOptions,
      headers: { ...this.mutationOptions.headers, 'If-Match': etag },
    });
  }

  error(error: unknown, fallback: string): TaskApiError {
    if (!(error instanceof HttpErrorResponse))
      return { status: 0, message: fallback, conflict: false };
    const body: unknown = error.error;
    const problem = this.isProblem(body) ? body : null;
    const message =
      problem?.detail?.trim() ||
      problem?.title?.trim() ||
      (error.status === 0
        ? 'The task API could not be reached.'
        : `${fallback} (${error.status}).`);
    return { status: error.status, message, conflict: error.status === 412 };
  }

  etag(response: HttpResponse<unknown>): string {
    return response.headers.get('ETag') ?? '';
  }

  private taskUrl(id: string): string {
    return `/api/v1/tasks/${encodeURIComponent(id)}`;
  }

  private isProblem(value: unknown): value is ApiProblemDetails {
    return typeof value === 'object' && value !== null && 'errorCode' in value;
  }
}

@Injectable()
export class TaskDetailResource {
  private readonly api = inject(TaskApiService);
  private readonly polling = inject(PollingCoordinator);
  readonly taskId = signal('');
  private readonly retainedTask = signal<TaskResponse | null>(null);
  private readonly retainedEtag = signal('');
  private readonly dirtyState = signal(false);
  private readonly pendingExternalTask = signal<TaskResponse | null>(null);
  readonly unavailable = signal(false);
  readonly pollSession = this.polling.create<TaskResponse>({
    target: () =>
      this.task() && this.etag()
        ? { url: `/api/v1/tasks/${encodeURIComponent(this.taskId())}`, etag: this.etag() }
        : null,
    accept: (response) => this.acceptExternal(response),
    missing: () => {
      this.unavailable.set(true);
      this.pendingExternalTask.set(null);
    },
  });
  readonly resource = httpResource<TaskResponse>(() =>
    this.taskId() ? `/api/v1/tasks/${encodeURIComponent(this.taskId())}` : undefined,
  );
  readonly task = computed(() => this.retainedTask());
  readonly etag = computed(() => this.retainedEtag());
  readonly loading = computed(() => this.resource.isLoading() && !this.task());
  readonly error = computed(() =>
    this.resource.error()
      ? this.api.error(this.resource.error(), 'The task could not be loaded.').message
      : null,
  );
  readonly pendingExternal = computed(() => this.pendingExternalTask());
  readonly liveUpdateUnavailable = computed(() => this.pollSession.state() === 'retrying');

  constructor() {
    effect(() => {
      if (!this.resource.hasValue()) return;
      this.retainedTask.set(this.resource.value());
      this.retainedEtag.set(this.resource.headers()?.get('ETag') ?? '');
      this.unavailable.set(false);
      this.pollSession.start();
    });
  }

  load(id: string): void {
    if (this.taskId() !== id) {
      this.retainedTask.set(null);
      this.retainedEtag.set('');
      this.pendingExternalTask.set(null);
      this.unavailable.set(false);
    }
    this.taskId.set(id);
    this.pollSession.restart(false);
  }

  accept(response: TaskMutationResponse): void {
    if (response.body) this.retainedTask.set(response.body);
    this.retainedEtag.set(this.api.etag(response));
    this.pendingExternalTask.set(null);
    this.unavailable.set(false);
  }

  setDirty(dirty: boolean): void {
    this.dirtyState.set(dirty);
  }

  reviewLatest(): TaskResponse | null {
    const latest = this.pendingExternalTask();
    if (!latest) return null;
    this.retainedTask.set(latest);
    this.pendingExternalTask.set(null);
    return latest;
  }

  keepLatest(): void {
    this.pendingExternalTask.set(null);
    this.dirtyState.set(false);
  }

  fetchLatest(): void {
    this.pollSession.restart(true);
  }

  reload(): boolean {
    return this.resource.reload();
  }

  private acceptExternal(response: HttpResponse<TaskResponse>): void {
    if (!response.body) return;
    this.retainedEtag.set(response.headers.get('ETag') ?? `"${response.body.revision}"`);
    this.unavailable.set(false);
    if (this.dirtyState()) this.pendingExternalTask.set(response.body);
    else this.retainedTask.set(response.body);
  }
}
