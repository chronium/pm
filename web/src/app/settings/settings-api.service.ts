import { HttpClient, HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

import type { components } from '../api/generated/pm-api';
import { ProjectContextService } from '../core/project-context.service';

export type SettingsResponse = components['schemas']['SettingsResponse'];
export type SettingsOption = components['schemas']['SettingsOptionResponse'];
export type SettingsMilestone = components['schemas']['SettingsMilestoneResponse'];
export type ValidationResponse = components['schemas']['ValidationResponse'];
export type ValidationIssue = components['schemas']['ValidationIssueResponse'];
export type CreateSettingsOptionRequest = components['schemas']['CreateSettingsOptionRequest'];
export type RenameSettingsOptionRequest = components['schemas']['RenameSettingsOptionRequest'];
export type CreateMilestoneRequest = components['schemas']['CreateMilestoneRequest'];
export type RenameMilestoneRequest = components['schemas']['RenameMilestoneRequest'];
export type SetMilestonePriorityRequest = components['schemas']['SetMilestonePriorityRequest'];
export type SetMilestoneDescriptionRequest =
  components['schemas']['SetMilestoneDescriptionRequest'];
export type SetProjectAccentRequest = components['schemas']['SetProjectAccentRequest'];
export type SettingsActivationTrigger = components['schemas']['SettingsActivationTriggerResponse'];
export type SettingsMutationResponse = HttpResponse<SettingsResponse>;

export interface SettingsApiError {
  status: number;
  message: string;
  conflict: boolean;
  code: string | null;
}

@Injectable({ providedIn: 'root' })
export class SettingsApiService {
  private readonly http = inject(HttpClient);
  private readonly projectContext = inject(ProjectContextService);

  setAccent(request: SetProjectAccentRequest, revision: string) {
    return this.http.put<SettingsResponse>(
      this.url('/settings/accent'),
      request,
      this.options(revision),
    );
  }

  createStatus(request: CreateSettingsOptionRequest, revision: string) {
    return this.http.post<SettingsResponse>(
      this.url('/settings/statuses'),
      request,
      this.options(revision),
    );
  }

  renameStatus(key: string, request: RenameSettingsOptionRequest, revision: string) {
    return this.http.put<SettingsResponse>(
      this.optionUrl('statuses', key),
      request,
      this.options(revision),
    );
  }

  removeStatus(key: string, revision: string) {
    return this.http.delete<SettingsResponse>(
      this.optionUrl('statuses', key),
      this.options(revision),
    );
  }

  createTrack(request: CreateSettingsOptionRequest, revision: string) {
    return this.http.post<SettingsResponse>(
      this.url('/settings/tracks'),
      request,
      this.options(revision),
    );
  }

  renameTrack(key: string, request: RenameSettingsOptionRequest, revision: string) {
    return this.http.put<SettingsResponse>(
      this.optionUrl('tracks', key),
      request,
      this.options(revision),
    );
  }

  removeTrack(key: string, revision: string) {
    return this.http.delete<SettingsResponse>(
      this.optionUrl('tracks', key),
      this.options(revision),
    );
  }

  createMilestone(request: CreateMilestoneRequest, revision: string) {
    return this.http.post<SettingsResponse>(
      this.url('/settings/milestones'),
      request,
      this.options(revision),
    );
  }

  readSettings() {
    return this.http.get<SettingsResponse>(this.url('/settings'), { observe: 'response' });
  }

  renameMilestone(key: string, request: RenameMilestoneRequest, revision: string) {
    return this.http.put<SettingsResponse>(this.milestoneUrl(key), request, this.options(revision));
  }

  setMilestonePriority(key: string, request: SetMilestonePriorityRequest, revision: string) {
    return this.http.put<SettingsResponse>(
      `${this.milestoneUrl(key)}/priority`,
      request,
      this.options(revision),
    );
  }

  setMilestoneDescription(key: string, request: SetMilestoneDescriptionRequest, revision: string) {
    return this.http.put<SettingsResponse>(
      `${this.milestoneUrl(key)}/description`,
      request,
      this.options(revision),
    );
  }

  removeMilestone(key: string, revision: string) {
    return this.http.delete<SettingsResponse>(this.milestoneUrl(key), this.options(revision));
  }

  error(error: unknown, fallback: string): SettingsApiError {
    if (!(error instanceof HttpErrorResponse)) {
      return { status: 0, message: fallback, conflict: false, code: null };
    }
    const problem = this.isProblem(error.error) ? error.error : null;
    const message =
      problem?.detail?.trim() ||
      problem?.title?.trim() ||
      (error.status === 0
        ? 'The settings API could not be reached.'
        : `${fallback} (${error.status}).`);
    return {
      status: error.status,
      message,
      conflict: error.status === 412,
      code: problem?.errorCode ?? null,
    };
  }

  private options(revision: string) {
    return {
      observe: 'response' as const,
      headers: {
        'X-PM-Client': 'angular-web',
        'If-Match': `"${revision}"`,
      },
    };
  }

  private optionUrl(collection: 'statuses' | 'tracks', key: string): string {
    return this.url(`/settings/${collection}/${encodeURIComponent(key)}`);
  }

  private milestoneUrl(key: string): string {
    return this.url(`/settings/milestones/${encodeURIComponent(key)}`);
  }

  private url(path: string): string {
    return this.projectContext.apiUrl(path);
  }

  private isProblem(value: unknown): value is components['schemas']['ApiProblemDetails'] {
    return typeof value === 'object' && value !== null && 'errorCode' in value;
  }
}
