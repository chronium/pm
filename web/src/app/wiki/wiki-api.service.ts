import { HttpClient, HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

import type { components } from '../api/generated/pm-api';

export type WikiPage = components['schemas']['WikiPageResponse'];
export type WikiPageSummary = components['schemas']['WikiPageSummaryResponse'];
export type CreateWikiPageRequest = components['schemas']['CreateWikiPageRequest'];
export type UpdateWikiPageBodyRequest = components['schemas']['UpdateWikiPageBodyRequest'];
export type UpdateWikiPageMetadataRequest = components['schemas']['UpdateWikiPageMetadataRequest'];
export type ApiProblemDetails = components['schemas']['ApiProblemDetails'];
export type WikiMutationResponse = HttpResponse<WikiPage>;

export interface WikiApiError {
  status: number;
  message: string;
  conflict: boolean;
  duplicate: boolean;
}

export function encodeWikiPath(path: string): string {
  return path.split('/').map((segment) => encodeURIComponent(segment)).join('/');
}

@Injectable({ providedIn: 'root' })
export class WikiApiService {
  private readonly http = inject(HttpClient);
  private readonly mutationOptions = { observe: 'response' as const, headers: { 'X-PM-Client': 'angular-web' } };

  create(request: CreateWikiPageRequest) {
    return this.http.post<WikiPage>('/api/v1/wiki/pages', request, this.mutationOptions);
  }

  updateBody(path: string, request: UpdateWikiPageBodyRequest, etag: string) {
    return this.http.put<WikiPage>(this.pageUrl(path), request, this.options(etag));
  }

  updateMetadata(path: string, request: UpdateWikiPageMetadataRequest, etag: string) {
    return this.http.patch<WikiPage>(this.pageUrl(path), request, this.options(etag));
  }

  remove(path: string, etag: string) {
    return this.http.delete<void>(this.pageUrl(path), this.options(etag));
  }

  pageUrl(path: string): string {
    return `/api/v1/wiki/pages/${encodeWikiPath(path)}`;
  }

  etag(response: HttpResponse<unknown>): string {
    return response.headers.get('ETag') ?? '';
  }

  error(error: unknown, fallback: string): WikiApiError {
    if (!(error instanceof HttpErrorResponse)) return { status: 0, message: fallback, conflict: false, duplicate: false };
    const problem = this.isProblem(error.error) ? error.error : null;
    const message = problem?.detail?.trim() || problem?.title?.trim()
      || (error.status === 0 ? 'The wiki API could not be reached.' : `${fallback} (${error.status}).`);
    return { status: error.status, message, conflict: error.status === 412, duplicate: error.status === 409 };
  }

  private options(etag: string) {
    return { ...this.mutationOptions, headers: { ...this.mutationOptions.headers, 'If-Match': etag } };
  }

  private isProblem(value: unknown): value is ApiProblemDetails {
    return typeof value === 'object' && value !== null && ('errorCode' in value || 'title' in value);
  }
}
