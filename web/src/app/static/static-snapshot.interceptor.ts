import {
  HttpBackend,
  HttpClient,
  HttpErrorResponse,
  HttpEvent,
  HttpHandlerFn,
  HttpHeaders,
  HttpRequest,
  HttpResponse,
} from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable, catchError, map, shareReplay, throwError } from 'rxjs';

import type { components, operations } from '../api/generated/pm-api';
import { StaticModeService } from './static-mode.service';
import { searchSnapshotTasks, searchSnapshotWiki } from './static-search';

type ProjectResponse = operations['GetProject']['responses'][200]['content']['application/json'];
type BoardResponse = operations['GetBoard']['responses'][200]['content']['application/json'];
type NavigationResponse =
  operations['GetBoardNavigation']['responses'][200]['content']['application/json'];
type TaskResponse = components['schemas']['TaskResponse'];
type SettingsResponse = components['schemas']['SettingsResponse'];
type WikiSummary = components['schemas']['WikiPageSummaryResponse'];
type WikiPage = components['schemas']['WikiPageResponse'];

export interface StaticSnapshot {
  schemaVersion: number;
  generatedAt: string;
  projectId: string | null;
  linkedProjects: StaticLinkedProject[];
  project: ProjectResponse;
  settings: SettingsResponse;
  navigation: NavigationResponse;
  board: BoardResponse;
  tasks: Omit<TaskResponse, 'localMetadata'>[];
  wikiIndex: WikiSummary[];
  wikiPages: Omit<WikiPage, 'localMetadata'>[];
}

export interface StaticLinkedProject {
  projectId: string;
  name: string;
  alias: string | null;
  relationship: 'parent' | 'child' | 'sibling';
  publicSiteUrl: string | null;
}

@Injectable({ providedIn: 'root' })
export class StaticSnapshotStore {
  private readonly mode = inject(StaticModeService);
  private readonly http = new HttpClient(inject(HttpBackend));
  readonly snapshot = this.http.get<unknown>(this.mode.snapshotUrl).pipe(
    map((value) => validateSnapshot(value)),
    catchError((error: unknown) =>
      throwError(() => snapshotError(error instanceof Error ? error.message : undefined)),
    ),
    shareReplay({ bufferSize: 1, refCount: false }),
  );

  response(request: HttpRequest<unknown>): Observable<HttpEvent<unknown>> {
    return this.snapshot.pipe(
      map((snapshot) => {
        const body = adaptGet(snapshot, request);
        return new HttpResponse({
          status: 200,
          body,
          headers: new HttpHeaders({ ETag: '"static-snapshot"' }),
          url: request.urlWithParams,
        });
      }),
    );
  }
}

export function staticSnapshotInterceptor(
  request: HttpRequest<unknown>,
  next: HttpHandlerFn,
): Observable<HttpEvent<unknown>> {
  const mode = inject(StaticModeService);
  if (!mode.enabled || request.method !== 'GET' || !request.url.startsWith('/api/v1/'))
    return next(request);
  return inject(StaticSnapshotStore).response(request);
}

export function validateSnapshot(value: unknown): StaticSnapshot {
  if (!isRecord(value)) throw new Error('The static snapshot is malformed.');
  if (value['schemaVersion'] !== 3)
    throw new Error(
      `Unsupported static snapshot schema version: ${String(value['schemaVersion'])}.`,
    );
  for (const key of [
    'generatedAt',
    'projectId',
    'linkedProjects',
    'project',
    'settings',
    'navigation',
    'board',
    'tasks',
    'wikiIndex',
    'wikiPages',
  ]) {
    if (!(key in value)) throw new Error(`The static snapshot is malformed: missing ${key}.`);
  }
  if (
    !Array.isArray(value['linkedProjects']) ||
    !Array.isArray(value['tasks']) ||
    !Array.isArray(value['wikiIndex']) ||
    !Array.isArray(value['wikiPages'])
  )
    throw new Error('The static snapshot is malformed: expected snapshot collections.');
  if (value['projectId'] !== null && typeof value['projectId'] !== 'string')
    throw new Error('The static snapshot is malformed: invalid projectId.');
  if (!value['linkedProjects'].every(isLinkedProject))
    throw new Error('The static snapshot is malformed: invalid linkedProjects.');
  return value as unknown as StaticSnapshot;
}

function isLinkedProject(value: unknown): value is StaticLinkedProject {
  if (!isRecord(value)) return false;
  const publicSiteUrl = value['publicSiteUrl'];
  return (
    typeof value['projectId'] === 'string' &&
    typeof value['name'] === 'string' &&
    (value['alias'] === null || typeof value['alias'] === 'string') &&
    ['parent', 'child', 'sibling'].includes(String(value['relationship'])) &&
    (publicSiteUrl === null || (typeof publicSiteUrl === 'string' && isHttpUrl(publicSiteUrl)))
  );
}

function isHttpUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}

export function adaptGet(snapshot: StaticSnapshot, request: HttpRequest<unknown>): unknown {
  const url = request.url;
  if (url === '/api/v1/project') return snapshot.project;
  if (url === '/api/v1/settings') return snapshot.settings;
  if (url === '/api/v1/board/navigation') return snapshot.navigation;
  if (url === '/api/v1/board') return filterBoard(snapshot.board, request);
  if (url === '/api/v1/tasks/search') return searchSnapshotTasks(snapshot, request);
  if (url === '/api/v1/wiki/search') return searchSnapshotWiki(snapshot, request);
  if (url === '/api/v1/wiki/pages') return snapshot.wikiIndex;

  const taskPrefix = '/api/v1/tasks/';
  if (url.startsWith(taskPrefix)) {
    const id = decodeURIComponent(url.slice(taskPrefix.length));
    const task = snapshot.tasks.find((candidate) => candidate.id === id);
    if (task) return { ...task, localMetadata: { filePath: '' } } satisfies TaskResponse;
    throw notFound(`Task ${id} is not present in this snapshot.`);
  }

  const wikiPrefix = '/api/v1/wiki/pages/';
  if (url.startsWith(wikiPrefix)) {
    const path = url
      .slice(wikiPrefix.length)
      .split('/')
      .map((segment) => decodeURIComponent(segment))
      .join('/');
    const page = snapshot.wikiPages.find((candidate) => candidate.path === path);
    if (page) return { ...page, localMetadata: { filePath: '' } } satisfies WikiPage;
    throw notFound(`Wiki page ${path} is not present in this snapshot.`);
  }

  throw notFound('This endpoint is unavailable in a read-only snapshot.');
}

function filterBoard(board: BoardResponse, request: HttpRequest<unknown>): BoardResponse {
  const track = normalize(request.params.get('track'));
  const milestone = normalize(request.params.get('milestone'));
  const state = normalize(request.params.get('state'));
  const milestoneGroups = board.milestoneGroups
    .filter((group) => !milestone || group.key === milestone)
    .map((group) => ({
      ...group,
      states: group.states
        .filter((groupState) => !state || groupState.key === state)
        .map((groupState) => ({
          ...groupState,
          tasks: groupState.tasks.filter(
            (task) =>
              (!track || task.track === track) &&
              (!milestone || task.milestone === milestone) &&
              (!state || task.state === state),
          ),
        })),
    }));
  return {
    ...board,
    filters: { track, milestone, state },
    milestoneGroups,
  };
}

function normalize(value: string | null): string | null {
  return value?.trim() || null;
}

function snapshotError(detail?: string): HttpErrorResponse {
  const message =
    detail?.startsWith('Unsupported') || detail?.startsWith('The static snapshot')
      ? detail
      : 'The static snapshot is missing or could not be loaded.';
  return new HttpErrorResponse({
    status: 500,
    statusText: 'Static snapshot error',
    error: {
      title: 'Static snapshot unavailable',
      detail: message,
      errorCode: 'invalid_static_snapshot',
    },
  });
}

function notFound(detail: string): HttpErrorResponse {
  return new HttpErrorResponse({
    status: 404,
    statusText: 'Not Found',
    error: { title: 'Snapshot content not found', detail, errorCode: 'snapshot_content_missing' },
  });
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
