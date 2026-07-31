import { computed, inject, Injectable } from '@angular/core';
import {
  ProjectContextService,
  type ProjectContextResponse,
} from '../core/project-context.service';

export type ProjectResponse = ProjectContextResponse;

@Injectable({ providedIn: 'root' })
export class ProjectApiService {
  private readonly context = inject(ProjectContextService);
  readonly project = this.context.project;
  readonly projectName = computed(() =>
    this.project.hasValue() ? this.project.value().name : 'PM',
  );

  constructor() {
    this.context.enableProjectMetadata();
  }
}
