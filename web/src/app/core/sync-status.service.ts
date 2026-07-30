import { HttpInterceptorFn } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { finalize } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class SyncStatusService {
  private readonly activeRequests = signal(0);

  readonly syncing = computed(() => this.activeRequests() > 0);
  readonly label = computed(() =>
    this.syncing() ? 'Syncing project data' : 'Project data synced',
  );

  begin(): () => void {
    this.activeRequests.update((count) => count + 1);
    let finished = false;

    return () => {
      if (finished) return;
      finished = true;
      this.activeRequests.update((count) => Math.max(0, count - 1));
    };
  }
}

export const syncStatusInterceptor: HttpInterceptorFn = (request, next) => {
  const finish = inject(SyncStatusService).begin();
  return next(request).pipe(finalize(finish));
};
