import { computed, Injectable } from '@angular/core';
import { httpResource } from '@angular/common/http';

import type { operations } from './generated/pm-api';

export type ProjectResponse =
  operations['GetProject']['responses'][200]['content']['application/json'];

@Injectable({ providedIn: 'root' })
export class ProjectApiService {
  readonly project = httpResource<ProjectResponse>(() => '/api/v1/project');
  readonly projectName = computed(() =>
    this.project.hasValue() ? this.project.value().name : 'PM',
  );
}
