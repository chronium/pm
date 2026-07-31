import { HttpErrorResponse, HttpResponse, httpResource } from '@angular/common/http';
import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import type { operations } from '../../api/generated/pm-api';
import { PollingCoordinator } from '../../core/polling-coordinator';
import { TaskNavigationService } from '../task-navigation.service';
import { TaskApiService, type NextTaskResponse } from '../task-api.service';
import { ProjectContextService } from '../../core/project-context.service';

export type BoardNavigationResponse =
  operations['GetBoardNavigation']['responses'][200]['content']['application/json'];

@Injectable()
export class TaskSidebarStore {
  private readonly taskNavigation = inject(TaskNavigationService);
  private readonly polling = inject(PollingCoordinator);
  private readonly api = inject(TaskApiService);
  private readonly projectContext = inject(ProjectContextService);
  private readonly retained = signal<BoardNavigationResponse | undefined>(undefined);
  private readonly etag = signal('');
  private lastRefreshRequest = this.taskNavigation.refreshRequest();

  readonly resource = httpResource<BoardNavigationResponse>(() =>
    this.projectContext.apiUrl('/board/navigation'),
  );
  readonly navigation = computed(() =>
    this.resource.hasValue() ? this.resource.value() : this.retained(),
  );
  readonly loading = computed(() => this.resource.isLoading() && !this.navigation());
  readonly error = computed(() => this.readableError(this.resource.error()));
  readonly recommendationPending = signal(false);
  readonly recommendationMessage = signal<string | null>(null);
  readonly recommendationError = signal<string | null>(null);
  readonly pollStatus = this.polling.create<BoardNavigationResponse>({
    target: () => {
      const navigation = this.navigation();
      return navigation
        ? {
            url: this.projectContext.apiUrl('/board/navigation'),
            etag: this.etag() || `"${navigation.revision}"`,
          }
        : null;
    },
    accept: (response) => this.accept(response),
  });

  constructor() {
    let apiPrefix = this.projectContext.apiPrefix();
    effect(() => {
      const nextPrefix = this.projectContext.apiPrefix();
      if (nextPrefix === apiPrefix) return;
      apiPrefix = nextPrefix;
      this.retained.set(undefined);
      this.etag.set('');
      this.pollStatus.stop();
      this.taskNavigation.setRemainingCount(0);
      this.resource.reload();
    });
    effect(() => {
      if (!this.resource.hasValue()) return;
      const value = this.resource.value();
      this.retained.set(value);
      this.etag.set(this.resource.headers()?.get('ETag') ?? `"${value.revision}"`);
      this.taskNavigation.setRemainingCount(Number(value.remainingCount));
      this.pollStatus.start();
    });
    effect(() => {
      const request = this.taskNavigation.refreshRequest();
      if (request === this.lastRefreshRequest) return;
      this.lastRefreshRequest = request;
      this.resource.reload();
    });
  }

  reload(): boolean {
    return this.resource.reload();
  }

  async recommend(
    track: string | null,
    milestone: string | null,
  ): Promise<NextTaskResponse | null> {
    if (this.recommendationPending()) return null;
    this.recommendationPending.set(true);
    this.recommendationMessage.set(null);
    this.recommendationError.set(null);
    try {
      const result = await firstValueFrom(this.api.next(track, milestone));
      if (!result.found || !result.task) this.recommendationMessage.set(result.reason);
      return result;
    } catch (error) {
      this.recommendationError.set(
        this.api.error(error, 'The next task could not be recommended.').message,
      );
      return null;
    } finally {
      this.recommendationPending.set(false);
    }
  }

  private accept(response: HttpResponse<BoardNavigationResponse>): void {
    if (!response.body) return;
    this.retained.set(response.body);
    this.etag.set(response.headers.get('ETag') ?? `"${response.body.revision}"`);
    this.taskNavigation.setRemainingCount(Number(response.body.remainingCount));
  }

  private readableError(error: Error | undefined): string | null {
    if (!error) return null;
    if (error instanceof HttpErrorResponse) {
      const body: unknown = error.error;
      if (typeof body === 'object' && body !== null) {
        const problem = body as { detail?: string; title?: string };
        return problem.detail?.trim() || problem.title?.trim() || 'Navigation could not be loaded.';
      }
      if (error.status === 0) return 'The navigation API could not be reached.';
    }
    return error.message || 'Navigation could not be loaded.';
  }
}
