import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { computed, inject, Injectable } from '@angular/core';

import type { components } from '../api/generated/pm-api';
import { ProjectContextService } from '../core/project-context.service';
import { StaticModeService } from '../static/static-mode.service';

export type OverviewDocument = components['schemas']['OverviewDocumentResponse'];

@Injectable({ providedIn: 'root' })
export class OverviewStore {
  private readonly projectContext = inject(ProjectContextService);
  private readonly staticMode = inject(StaticModeService);

  readonly resource = httpResource<OverviewDocument>(() =>
    this.staticMode.enabled ? undefined : this.projectContext.apiUrl('/overview'),
  );
  readonly document = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  readonly loading = computed(() => this.resource.isLoading() && !this.document());
  readonly available = computed(() => {
    const status = this.document()?.status;
    return status === 'ready' || status === 'invalid';
  });
  readonly error = computed(() => this.readableError(this.resource.error()));

  reload(): boolean {
    return this.resource.reload();
  }

  private readableError(error: Error | undefined): string | null {
    if (!error) return null;
    if (error instanceof HttpErrorResponse) {
      const body: unknown = error.error;
      if (typeof body === 'object' && body !== null) {
        const detail = (body as { detail?: unknown }).detail;
        if (typeof detail === 'string' && detail.trim()) return detail;
      }
      if (error.status === 0) return 'The Overview API could not be reached.';
      return `The Overview could not be loaded (${error.status}).`;
    }
    return error.message || 'The Overview could not be loaded.';
  }
}
