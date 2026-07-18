import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { computed, Injectable } from '@angular/core';

import type { components } from '../api/generated/pm-api';

export type TaskOptionsResponse = components['schemas']['SettingsResponse'];
export type TaskOption = components['schemas']['SettingsOptionResponse'];
export type TaskMilestoneOption = components['schemas']['SettingsMilestoneResponse'];

@Injectable({ providedIn: 'root' })
export class TaskOptionsResource {
  readonly resource = httpResource<TaskOptionsResponse>(() => '/api/v1/settings');
  readonly options = computed(() => (this.resource.hasValue() ? this.resource.value() : null));
  readonly loading = computed(() => this.resource.isLoading() && !this.options());
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
      if (error.status === 0) return 'The task options API could not be reached.';
      return `Task options could not be loaded (${error.status}).`;
    }
    return error.message || 'Task options could not be loaded.';
  }
}
