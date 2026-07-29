import { HttpClient, HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

import type { components } from '../api/generated/pm-api';

export type AgentRunnerRegistration = components['schemas']['AgentRunnerRegistration'];
export type AgentRunnerStatus = components['schemas']['AgentRunnerStatusResponse'];
export type PairAgentRunnerRequest = components['schemas']['PairAgentRunnerRequest'];
export type AgentRunPreflightRequest = components['schemas']['AgentRunPreflightRequest'];
export type AgentRunPreflightResult = components['schemas']['AgentRunPreflightResult'];
export type AgentRunRemoteStart = components['schemas']['AgentRunRemoteStart'];
export type AgentRunRuntimeProfile = components['schemas']['AgentRunRuntimeProfile'];
export type AgentRunnerProvider = components['schemas']['AgentRunnerProviderCapability'];

export interface AgentRunsApiError {
  status: number;
  code: string | null;
  message: string;
  stale: boolean;
}

@Injectable({ providedIn: 'root' })
export class AgentRunsApiService {
  private readonly http = inject(HttpClient);
  private readonly mutationHeaders = { 'X-PM-Client': 'angular-web' };

  listRunners() {
    return this.http.get<AgentRunnerRegistration[]>('/api/v1/runners', {
      observe: 'response' as const,
    });
  }

  runnerStatus(runnerId: string) {
    return this.http.get<AgentRunnerStatus>(`${this.runnerUrl(runnerId)}/status`, {
      observe: 'response' as const,
    });
  }

  pairRunner(request: PairAgentRunnerRequest) {
    return this.http.post<AgentRunnerRegistration>('/api/v1/runners/pair', request, {
      observe: 'response' as const,
      headers: this.mutationHeaders,
    });
  }

  rotateRunner(runnerId: string) {
    return this.http.post<AgentRunnerRegistration>(
      `${this.runnerUrl(runnerId)}/rotate`,
      {},
      {
        observe: 'response' as const,
        headers: this.mutationHeaders,
      },
    );
  }

  revokeRunner(runnerId: string) {
    return this.http.delete<void>(this.runnerUrl(runnerId), {
      observe: 'response' as const,
      headers: this.mutationHeaders,
    });
  }

  preflight(request: AgentRunPreflightRequest) {
    return this.http.post<AgentRunPreflightResult>('/api/v1/runs/preflight', request, {
      observe: 'response' as const,
      headers: this.mutationHeaders,
    });
  }

  start(runId: string, etag: string) {
    return this.http.post<AgentRunRemoteStart>(
      `/api/v1/runs/${encodeURIComponent(runId)}/start`,
      {},
      {
        observe: 'response' as const,
        headers: { ...this.mutationHeaders, 'If-Match': etag },
      },
    );
  }

  etag(response: HttpResponse<unknown>): string {
    return response.headers.get('ETag') ?? '';
  }

  error(error: unknown, fallback: string): AgentRunsApiError {
    if (!(error instanceof HttpErrorResponse)) {
      return { status: 0, code: null, message: fallback, stale: false };
    }
    const problem = this.isProblem(error.error) ? error.error : null;
    const message =
      problem?.detail?.trim() ||
      problem?.title?.trim() ||
      (error.status === 0
        ? 'The agent runner API could not be reached.'
        : `${fallback} (${error.status}).`);
    return {
      status: error.status,
      code: problem?.errorCode ?? null,
      message,
      stale: error.status === 409 || error.status === 412 || error.status === 428,
    };
  }

  private runnerUrl(runnerId: string): string {
    return `/api/v1/runners/${encodeURIComponent(runnerId)}`;
  }

  private isProblem(value: unknown): value is components['schemas']['ApiProblemDetails'] {
    return typeof value === 'object' && value !== null && 'errorCode' in value;
  }
}
