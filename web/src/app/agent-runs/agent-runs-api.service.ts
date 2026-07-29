import { HttpClient, HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import type { components } from '../api/generated/pm-api';
import { sanitizeRunEvent } from './agent-run-events';

export type AgentRunnerRegistration = components['schemas']['AgentRunnerRegistration'];
export type AgentRunnerStatus = components['schemas']['AgentRunnerStatusResponse'];
export type PairAgentRunnerRequest = components['schemas']['PairAgentRunnerRequest'];
export type AgentRunPreflightRequest = components['schemas']['AgentRunPreflightRequest'];
export type AgentRunPreflightResult = components['schemas']['AgentRunPreflightResult'];
export type AgentRunRemoteStart = components['schemas']['AgentRunRemoteStart'];
export type AgentRunRuntimeProfile = components['schemas']['AgentRunRuntimeProfile'];
export type AgentRunnerProvider = components['schemas']['AgentRunnerProviderCapability'];
export type AgentRunInspection = components['schemas']['AgentRunInspection'];
export type AgentRunEvent = components['schemas']['AgentRunEvent'];
export type AgentRunEventPage = components['schemas']['AgentRunEventPage'];
export type AgentRunArtifact = components['schemas']['AgentRunArtifact'];
export type AgentRunCancellation = components['schemas']['AgentRunCancellation'];
export type AgentRunState = components['schemas']['AgentRunState'];

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

  inspect(runId: string) {
    return this.http.get<AgentRunInspection>(this.runUrl(runId), {
      observe: 'response' as const,
    });
  }

  events(runId: string, afterSequence: number, limit = 500) {
    return this.http.get<AgentRunEventPage>(`${this.runUrl(runId)}/events`, {
      observe: 'response' as const,
      params: { afterSequence, limit },
    });
  }

  cancel(runId: string) {
    return this.http.post<AgentRunCancellation>(
      `${this.runUrl(runId)}/cancel`,
      {},
      {
        observe: 'response' as const,
        headers: this.mutationHeaders,
      },
    );
  }

  artifacts(runId: string) {
    return this.http.get<AgentRunArtifact[]>(`${this.runUrl(runId)}/artifacts`, {
      observe: 'response' as const,
    });
  }

  artifactContent(runId: string, artifactId: string) {
    return this.http.get(
      `${this.runUrl(runId)}/artifacts/${encodeURIComponent(artifactId)}/content`,
      {
        observe: 'response' as const,
        responseType: 'arraybuffer' as const,
      },
    );
  }

  async eventJournal(runId: string): Promise<Blob> {
    const lines: string[] = [];
    let afterSequence = 0;
    let hasMore = true;
    while (hasMore) {
      const response = await firstValueFrom(this.events(runId, afterSequence));
      const page = response.body;
      if (!page) throw new Error('The run event journal returned an empty page.');
      for (const event of page.events) lines.push(JSON.stringify(sanitizeRunEvent(event)) + '\n');
      const next = Number(page.nextAfterSequence);
      if (page.hasMore && next <= afterSequence)
        throw new Error('The run event journal did not advance its sequence cursor.');
      afterSequence = next;
      hasMore = page.hasMore;
    }
    return new Blob(lines, { type: 'application/x-ndjson' });
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

  private runUrl(runId: string): string {
    return `/api/v1/runs/${encodeURIComponent(runId)}`;
  }

  private isProblem(value: unknown): value is components['schemas']['ApiProblemDetails'] {
    return typeof value === 'object' && value !== null && 'errorCode' in value;
  }
}
