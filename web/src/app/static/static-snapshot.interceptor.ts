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
type ActivationResponse = components['schemas']['ActivationSwitchboardResponse'];
type WikiSummary = components['schemas']['WikiPageSummaryResponse'];
type WikiPage = components['schemas']['WikiPageResponse'];
type OverviewDocument = components['schemas']['OverviewDocumentResponse'];

export interface StaticSnapshot {
  schemaVersion: number;
  generatedAt: string;
  projectId: string | null;
  linkedProjects: StaticLinkedProject[];
  project: ProjectResponse;
  overview: OverviewDocument;
  settings: SettingsResponse;
  activation: ActivationResponse;
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
  if (value['schemaVersion'] !== 6)
    throw new Error(
      `Unsupported static snapshot schema version: ${String(value['schemaVersion'])}.`,
    );
  for (const key of [
    'generatedAt',
    'projectId',
    'linkedProjects',
    'project',
    'overview',
    'settings',
    'activation',
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
  if (!isOverviewDocument(value['overview']))
    throw new Error('The static snapshot is malformed: invalid overview.');
  return value as unknown as StaticSnapshot;
}

function isOverviewDocument(value: unknown): value is OverviewDocument {
  if (!isRecord(value)) return false;
  const status = String(value['status']);
  const composition = value['composition'];
  const issues = value['issues'];
  if (
    !['disabled', 'ready', 'invalid'].includes(status) ||
    (value['projectId'] !== null && typeof value['projectId'] !== 'string') ||
    typeof value['projectName'] !== 'string' ||
    typeof value['documentTitle'] !== 'string' ||
    typeof value['revision'] !== 'string' ||
    !Array.isArray(issues) ||
    !issues.every(isOverviewIssue)
  )
    return false;
  if (status === 'ready')
    return isRecord(composition) && ['single', 'split'].includes(String(composition['layout']));
  return composition === null;
}

function isOverviewIssue(value: unknown): boolean {
  return (
    isRecord(value) &&
    typeof value['code'] === 'string' &&
    typeof value['message'] === 'string' &&
    typeof value['path'] === 'string'
  );
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
  if (url === '/api/v1/overview') return snapshot.overview;
  if (url === '/api/v1/settings') return snapshot.settings;
  if (url === '/api/v1/activation') return snapshot.activation;
  if (url === '/api/v1/board/navigation') return filterNavigation(snapshot, request);
  if (url === '/api/v1/board') return filterBoard(snapshot, request);
  if (url === '/api/v1/tasks/search')
    return searchSnapshotTasks(snapshot, request, includeDelivered(request));
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

function filterNavigation(
  snapshot: StaticSnapshot,
  request: HttpRequest<unknown>,
): NavigationResponse {
  const include = includeDelivered(request);
  const delivered = deliveredMilestoneKeys(snapshot);
  const remaining = snapshot.tasks.filter(
    (task) =>
      task.state !== 'done' && (include || !task.milestone || !delivered.has(task.milestone)),
  );
  const eligible = remaining.filter((task) => task.activation.isEligible);
  return {
    ...snapshot.navigation,
    remainingCount: remaining.length,
    activationEligibleCount: eligible.length,
    tracks: snapshot.navigation.tracks.map((track) => ({
      ...track,
      remainingCount: remaining.filter((task) => task.track === track.key).length,
      activationEligibleCount: eligible.filter((task) => task.track === track.key).length,
    })),
    milestones: snapshot.navigation.milestones
      .filter((milestone) => include || !delivered.has(milestone.key))
      .map((milestone) => ({
        ...milestone,
        remainingCount: remaining.filter((task) => task.milestone === milestone.key).length,
        activationEligibleCount: eligible.filter((task) => task.milestone === milestone.key).length,
      })),
  };
}

function filterBoard(snapshot: StaticSnapshot, request: HttpRequest<unknown>): BoardResponse {
  const board = snapshot.board;
  const track = normalize(request.params.get('track'));
  const milestone = normalize(request.params.get('milestone'));
  const state = normalize(request.params.get('state'));
  const include = includeDelivered(request);
  const delivered = deliveredMilestoneKeys(snapshot);
  const milestoneGroups = board.milestoneGroups
    .filter((group) => include || !group.key || !delivered.has(group.key))
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
    filters: { track, milestone, state, includeDelivered: include },
    milestones: board.milestones.filter((option) => include || !delivered.has(option.key)),
    milestoneGroups,
  };
}

function includeDelivered(request: HttpRequest<unknown>): boolean {
  const value = request.params.get('includeDelivered');
  if (value === null || value.toLowerCase() === 'false') return false;
  if (value.toLowerCase() === 'true') return true;
  throw new HttpErrorResponse({
    status: 400,
    statusText: 'Bad Request',
    error: {
      title: 'Invalid query parameter',
      detail: 'includeDelivered must be true or false.',
      errorCode: 'invalid_query_parameter',
    },
  });
}

function deliveredMilestoneKeys(snapshot: StaticSnapshot): ReadonlySet<string> {
  return new Set(
    snapshot.activation.milestones
      .filter((milestone) => milestone.lifecycle === 'delivered')
      .map((milestone) => milestone.key),
  );
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
