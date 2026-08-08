import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

import type { components } from '../api/generated/pm-api';
import { ProjectContextService } from '../core/project-context.service';

export type ActivationSwitchboardResponse = components['schemas']['ActivationSwitchboardResponse'];
export type ActivationTrigger = components['schemas']['ActivationTriggerResponse'];
export type ActivationRequirement = components['schemas']['ActivationRequirementResponse'];
export type ActivationRequirementRequest = components['schemas']['ActivationRequirementRequest'];
export type CreateActivationTriggerRequest =
  components['schemas']['CreateActivationTriggerRequest'];
export type RenameActivationTriggerRequest =
  components['schemas']['RenameActivationTriggerRequest'];
export type ActivationMutationResponse = components['schemas']['ActivationMutationResponse'];
export type ActivationRedefinitionPreview =
  components['schemas']['ActivationTriggerRedefinitionPreviewResponse'];
export type MilestoneRequiredTriggersPreviewResponse =
  components['schemas']['MilestoneRequiredTriggersPreviewResponse'];

export interface ActivationApiError {
  status: number;
  message: string;
  conflict: boolean;
  code: string | null;
}

@Injectable({ providedIn: 'root' })
export class ActivationApiService {
  private readonly http = inject(HttpClient);
  private readonly projectContext = inject(ProjectContextService);

  read() {
    return this.http.get<ActivationSwitchboardResponse>(this.url('/activation'), {
      observe: 'response',
    });
  }

  create(request: CreateActivationTriggerRequest, revision: string) {
    return this.http.post<ActivationMutationResponse>(
      this.url('/activation/triggers'),
      request,
      this.options(revision),
    );
  }

  rename(key: string, request: RenameActivationTriggerRequest, revision: string) {
    return this.http.put<ActivationMutationResponse>(
      `${this.triggerUrl(key)}/title`,
      request,
      this.options(revision),
    );
  }

  setRequirements(key: string, requirements: ActivationRequirementRequest[], revision: string) {
    return this.http.put<ActivationMutationResponse>(
      `${this.triggerUrl(key)}/requirements`,
      { requirements },
      this.options(revision),
    );
  }

  remove(key: string, revision: string) {
    return this.http.delete<ActivationMutationResponse>(
      this.triggerUrl(key),
      this.options(revision),
    );
  }

  activate(key: string, revision: string) {
    return this.http.post<ActivationMutationResponse>(
      `${this.triggerUrl(key)}/activate`,
      {},
      this.options(revision),
    );
  }

  override(key: string, reason: string, revision: string) {
    return this.http.post<ActivationMutationResponse>(
      `${this.triggerUrl(key)}/override`,
      { reason },
      this.options(revision),
    );
  }

  reset(key: string, revision: string) {
    return this.http.delete<ActivationMutationResponse>(
      `${this.triggerUrl(key)}/activation`,
      this.options(revision),
    );
  }

  previewRedefinition(key: string, requirements: ActivationRequirementRequest[], revision: string) {
    return this.http.post<ActivationRedefinitionPreview>(
      `${this.triggerUrl(key)}/redefinition-preview`,
      { requirements },
      this.options(revision),
    );
  }

  redefine(
    key: string,
    requirements: ActivationRequirementRequest[],
    previewRevision: string,
    allowDeactivation: boolean,
    revision: string,
  ) {
    return this.http.put<ActivationMutationResponse>(
      `${this.triggerUrl(key)}/redefinition`,
      { requirements, previewRevision, allowDeactivation },
      this.options(revision),
    );
  }

  reconcile(dryRun: boolean, revision: string) {
    return this.http.post<ActivationMutationResponse>(
      this.url('/activation/reconcile'),
      { dryRun },
      this.options(revision),
    );
  }

  previewMilestoneRequiredTriggers(key: string, triggerKeys: string[], revision: string) {
    return this.http.post<MilestoneRequiredTriggersPreviewResponse>(
      this.url(`/activation/milestones/${encodeURIComponent(key)}/required-triggers-preview`),
      { triggerKeys },
      this.options(revision),
    );
  }

  setMilestoneRequiredTriggers(
    key: string,
    triggerKeys: string[],
    previewRevision: string,
    allowDeactivation: boolean,
    revision: string,
  ) {
    return this.http.put<ActivationMutationResponse>(
      this.url(`/activation/milestones/${encodeURIComponent(key)}/required-triggers`),
      { triggerKeys, previewRevision, allowDeactivation },
      this.options(revision),
    );
  }

  error(error: unknown, fallback: string): ActivationApiError {
    if (!(error instanceof HttpErrorResponse))
      return { status: 0, message: fallback, conflict: false, code: null };
    const problem = this.isProblem(error.error) ? error.error : null;
    return {
      status: error.status,
      message:
        problem?.detail?.trim() ||
        problem?.title?.trim() ||
        (error.status === 0
          ? 'The activation API could not be reached.'
          : `${fallback} (${error.status}).`),
      conflict: error.status === 412,
      code: problem?.errorCode ?? null,
    };
  }

  private triggerUrl(key: string): string {
    return this.url(`/activation/triggers/${encodeURIComponent(key)}`);
  }

  private url(path: string): string {
    return this.projectContext.apiUrl(path);
  }

  private options(revision: string) {
    return {
      observe: 'response' as const,
      headers: { 'X-PM-Client': 'angular-web', 'If-Match': `"${revision}"` },
    };
  }

  private isProblem(value: unknown): value is components['schemas']['ApiProblemDetails'] {
    return typeof value === 'object' && value !== null && 'errorCode' in value;
  }
}
