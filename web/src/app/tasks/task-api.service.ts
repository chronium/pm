import { HttpClient, HttpErrorResponse, HttpResponse, httpResource } from '@angular/common/http';
import { computed, effect, inject, Injectable, signal } from '@angular/core';

import type { components } from '../api/generated/pm-api';

export type TaskResponse = components['schemas']['TaskResponse'];
export type CreateTaskRequest = components['schemas']['CreateTaskRequest'];
export type UpdateTaskRequest = components['schemas']['UpdateTaskRequest'];
export type UpdateTaskStateRequest = components['schemas']['UpdateTaskStateRequest'];
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
  private readonly mutationOptions = { observe: 'response' as const, headers: { 'X-PM-Client': 'angular-web' } };

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

  remove(id: string, etag: string) {
    return this.http.delete<void>(this.taskUrl(id), {
      ...this.mutationOptions,
      headers: { ...this.mutationOptions.headers, 'If-Match': etag },
    });
  }

  error(error: unknown, fallback: string): TaskApiError {
    if (!(error instanceof HttpErrorResponse)) return { status: 0, message: fallback, conflict: false };
    const body: unknown = error.error;
    const problem = this.isProblem(body) ? body : null;
    const message = problem?.detail?.trim() || problem?.title?.trim()
      || (error.status === 0 ? 'The task API could not be reached.' : `${fallback} (${error.status}).`);
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
  readonly taskId = signal('');
  private readonly retainedTask = signal<TaskResponse | null>(null);
  private readonly retainedEtag = signal('');
  readonly resource = httpResource<TaskResponse>(() => this.taskId()
    ? `/api/v1/tasks/${encodeURIComponent(this.taskId())}`
    : undefined);
  readonly task = computed(() => this.retainedTask());
  readonly etag = computed(() => this.retainedEtag());
  readonly loading = computed(() => this.resource.isLoading() && !this.task());
  readonly error = computed(() => this.resource.error()
    ? this.api.error(this.resource.error(), 'The task could not be loaded.').message
    : null);

  constructor() {
    effect(() => {
      if (!this.resource.hasValue()) return;
      this.retainedTask.set(this.resource.value());
      this.retainedEtag.set(this.resource.headers()?.get('ETag') ?? '');
    });
  }

  load(id: string): void {
    if (this.taskId() !== id) {
      this.retainedTask.set(null);
      this.retainedEtag.set('');
    }
    this.taskId.set(id);
  }

  accept(response: TaskMutationResponse): void {
    if (response.body) this.retainedTask.set(response.body);
    this.retainedEtag.set(this.api.etag(response));
  }

  reload(): boolean {
    return this.resource.reload();
  }
}
