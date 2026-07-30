import { computed, effect, inject, Injectable } from '@angular/core';
import { httpResource } from '@angular/common/http';

import type { operations } from './generated/pm-api';
import { AccentService } from '../core/accent.service';

export type ProjectResponse =
  operations['GetProject']['responses'][200]['content']['application/json'];

@Injectable({ providedIn: 'root' })
export class ProjectApiService {
  private readonly accent = inject(AccentService);
  readonly project = httpResource<ProjectResponse>(() => '/api/v1/project');
  readonly projectName = computed(() =>
    this.project.hasValue() ? this.project.value().name : 'PM',
  );

  constructor() {
    effect(() => {
      if (this.project.hasValue()) this.accent.applyProjectPreference(this.project.value().accent);
    });
  }
}
