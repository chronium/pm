import { HttpErrorResponse, HttpRequest } from '@angular/common/http';

import type { components } from '../api/generated/pm-api';
import type { StaticSnapshot } from './static-snapshot.interceptor';

type TaskSearchResult = components['schemas']['TaskSearchResultResponse'];
type SettingsResponse = components['schemas']['SettingsResponse'];
type WikiSearchResult = components['schemas']['WikiSearchResultResponse'];
type TaskSearchField = 'state' | 'id' | 'track' | 'milestone' | 'in';

interface TaskSearchQuery {
  freeText: string;
  states: string[];
  ids: string[];
  tracks: string[];
  milestones: string[];
  scope: 'selection' | 'all';
  hasScopePredicate: boolean;
}

export function searchSnapshotTasks(
  snapshot: StaticSnapshot,
  request: HttpRequest<unknown>,
): TaskSearchResult[] {
  const query = parseTaskSearchQuery(request.params.get('query') ?? '');
  const context = {
    track: normalize(request.params.get('track')),
    milestone: normalize(request.params.get('milestone')),
    state: normalize(request.params.get('state')),
  };
  validateTaskSearchContext(snapshot.settings, query.scope, context);

  const results = snapshot.tasks
    .filter(
      (task) =>
        matchesAny(query.states, task.state) &&
        matchesAny(query.tracks, task.track) &&
        matchesAny(query.milestones, task.milestone ?? '') &&
        matchesAnyTaskId(query.ids, task.id) &&
        matchesContext(context.state, task.state) &&
        (query.scope === 'all' ||
          (matchesContext(context.track, task.track) &&
            matchesContext(context.milestone, task.milestone ?? ''))),
    )
    .map((task): TaskSearchResult | null => {
      const fields = taskSearchFields(task);
      const matchCount = query.freeText
        ? fields.reduce((total, field) => total + countMatches(field.value, query.freeText), 0)
        : 0;
      if (query.freeText && matchCount === 0) return null;
      return {
        id: task.id,
        title: task.title,
        state: task.state,
        track: task.track,
        milestone: task.milestone,
        matchCount,
        snippet: query.freeText
          ? buildTaskSnippet(fields, query.freeText)
          : descriptionPreview(task.description),
      };
    })
    .filter((result): result is TaskSearchResult => result !== null);

  results.sort((left, right) => {
    const byMatches = query.freeText ? Number(right.matchCount) - Number(left.matchCount) : 0;
    return byMatches || compareOrdinal(left.id, right.id);
  });
  return results.slice(0, searchLimit(request));
}

export function searchSnapshotWiki(
  snapshot: StaticSnapshot,
  request: HttpRequest<unknown>,
): WikiSearchResult[] {
  const query = request.params.get('query')?.trim() ?? '';
  if (!query) throw badRequest('Wiki search query is required.', 'invalid_wiki_query');

  return snapshot.wikiPages
    .map((page): WikiSearchResult | null => {
      const matchCount =
        countMatches(page.title, query) +
        countMatches(page.path, query) +
        countMatches(page.body, query);
      if (matchCount === 0) return null;
      return {
        path: page.path,
        title: page.title,
        modifiedAt: page.modifiedAt,
        matchCount,
        snippet: buildWikiSnippet(page, query),
      };
    })
    .filter((result): result is WikiSearchResult => result !== null)
    .sort(
      (left, right) =>
        Number(right.matchCount) - Number(left.matchCount) || compareOrdinal(left.path, right.path),
    )
    .slice(0, searchLimit(request));
}

function parseTaskSearchQuery(value: string): TaskSearchQuery {
  if (!value.trim()) throw badRequest('Task search query is required.', 'invalid_task_query');

  const states: string[] = [];
  const ids: string[] = [];
  const tracks: string[] = [];
  const milestones: string[] = [];
  const freeText: string[] = [];
  let scope: TaskSearchQuery['scope'] | null = null;
  const tokens = value.trim().split(/\s+/u);

  for (let index = 0; index < tokens.length; index += 1) {
    const token = tokens[index]!;
    const colon = token.indexOf(':');
    const field = token.slice(0, Math.max(0, colon)).toLowerCase();
    if (colon < 0 || !isTaskSearchField(field)) {
      freeText.push(token);
      continue;
    }

    let fieldValue = token.slice(colon + 1);
    if (!fieldValue) {
      const next = tokens[index + 1];
      if (!next || isRecognizedFieldToken(next))
        throw badRequest(`Task search field ${field}: requires a value.`, 'invalid_task_query');
      fieldValue = next;
      index += 1;
    }

    switch (field) {
      case 'state':
        states.push(fieldValue);
        break;
      case 'id':
        ids.push(fieldValue);
        break;
      case 'track':
        tracks.push(fieldValue);
        break;
      case 'milestone':
        milestones.push(fieldValue);
        break;
      case 'in': {
        if (scope)
          throw badRequest(
            'Task search field in: may only be specified once.',
            'invalid_task_query',
          );
        const normalizedScope = fieldValue.toLowerCase();
        if (normalizedScope !== 'selection' && normalizedScope !== 'all')
          throw badRequest(
            `Task search field in: does not support value ${fieldValue}. Use selection or all.`,
            'invalid_task_query',
          );
        scope = normalizedScope;
        break;
      }
    }
  }

  const query: TaskSearchQuery = {
    freeText: freeText.join(' '),
    states,
    ids,
    tracks,
    milestones,
    scope: scope ?? 'selection',
    hasScopePredicate: scope !== null,
  };
  if (
    !query.freeText &&
    !query.states.length &&
    !query.ids.length &&
    !query.tracks.length &&
    !query.milestones.length &&
    !query.hasScopePredicate
  )
    throw badRequest('Task search query is required.', 'invalid_task_query');
  return query;
}

function validateTaskSearchContext(
  settings: SettingsResponse,
  scope: TaskSearchQuery['scope'],
  context: { track: string | null; milestone: string | null; state: string | null },
): void {
  if (scope === 'selection' && context.track && !hasKey(settings.tracks, context.track))
    throw badRequest(`Track ${context.track} not found.`, 'invalid_track');
  if (scope === 'selection' && context.milestone && !hasKey(settings.milestones, context.milestone))
    throw badRequest(`Milestone ${context.milestone} not found.`, 'invalid_milestone');
  if (context.state && !hasKey(settings.statuses, context.state))
    throw badRequest(`State ${context.state} not found.`, 'invalid_state');
}

function taskSearchFields(
  task: StaticSnapshot['tasks'][number],
): { label: string; value: string }[] {
  return [
    { label: 'Description', value: task.description },
    { label: 'Title', value: task.title },
    { label: 'ID', value: task.id },
    { label: 'Track', value: task.track },
    { label: 'Milestone', value: task.milestone ?? '' },
    { label: 'State', value: task.state },
    { label: 'Priority', value: task.priority },
    { label: 'Dependencies', value: task.dependencies.dependsOn.join(' ') },
    { label: 'Markdown', value: taskSearchMarkdown(task) },
  ];
}

function taskSearchMarkdown(task: StaticSnapshot['tasks'][number]): string {
  const lines = [
    '---',
    `id: ${task.id}`,
    `title: ${task.title}`,
    `track: ${task.track}`,
    ...(task.milestone ? [`milestone: ${task.milestone}`] : []),
    ...(task.prioritySelection !== 'inherit' ? [`priority: ${task.prioritySelection}`] : []),
    ...(task.dependencies.dependsOn.length
      ? ['dependsOn:', ...task.dependencies.dependsOn.map((id) => `- ${id}`)]
      : []),
    `createdAt: ${task.createdAt}`,
    `modifiedAt: ${task.modifiedAt}`,
    '---',
    '',
    task.description,
  ];
  return lines.join('\n');
}

function matchesAny(values: readonly string[], actual: string): boolean {
  return !values.length || values.some((value) => equalsIgnoreCase(value, actual));
}

function matchesAnyTaskId(values: readonly string[], actual: string): boolean {
  return !values.length || values.some((value) => matchesTaskId(value, actual));
}

function matchesTaskId(value: string, actual: string): boolean {
  if (!/^\d+$/u.test(value)) return actual.toLowerCase().startsWith(value.toLowerCase());
  const suffix = actual.match(/\d+$/u)?.[0];
  return suffix !== undefined && normalizeTaskNumber(suffix) === normalizeTaskNumber(value);
}

function normalizeTaskNumber(value: string): string {
  return value.replace(/^0+/u, '') || '0';
}

function matchesContext(expected: string | null, actual: string): boolean {
  return expected === null || equalsIgnoreCase(expected, actual);
}

function hasKey(items: readonly { key: string }[], key: string): boolean {
  return items.some((item) => item.key === key);
}

function equalsIgnoreCase(left: string, right: string): boolean {
  return left.toLowerCase() === right.toLowerCase();
}

function countMatches(value: string, query: string): number {
  const haystack = value.toLowerCase();
  const needle = query.toLowerCase();
  let count = 0;
  let index = 0;
  while ((index = haystack.indexOf(needle, index)) >= 0) {
    count += 1;
    index += needle.length;
  }
  return count;
}

function buildTaskSnippet(
  fields: readonly { label: string; value: string }[],
  query: string,
): string {
  const field =
    fields.find((candidate) => candidate.value.toLowerCase().includes(query.toLowerCase())) ??
    fields.find((candidate) => candidate.value.trim());
  if (!field?.value.trim()) return '';
  return `${field.label}: ${snippet(normalizeSnippetText(field.value), query)}`;
}

function buildWikiSnippet(page: StaticSnapshot['wikiPages'][number], query: string): string {
  const haystack = page.body.trim()
    ? page.body.replace(/\r\n?/gu, '\n').replaceAll('\n', ' ')
    : `${page.title} ${page.path}`;
  return snippet(haystack, query);
}

function snippet(value: string, query: string): string {
  let index = value.toLowerCase().indexOf(query.toLowerCase());
  if (index < 0) index = 0;
  const start = Math.max(0, index - 40);
  const length = Math.min(120, value.length - start);
  let result = value.slice(start, start + length).trim();
  if (start > 0) result = `...${result}`;
  if (start + length < value.length) result += '...';
  return result;
}

function normalizeSnippetText(value: string): string {
  return value.replace(/\r\n?/gu, '\n').split(/\s+/u).filter(Boolean).join(' ');
}

function descriptionPreview(value: string): string {
  const firstLine = value
    .replace(/\r\n?/gu, '\n')
    .split('\n')
    .map((line) =>
      line
        .trim()
        .replace(/^(#{1,6}\s+|(?:[-*+]\s+)?\[[ xX]\]\s+|[-*+]\s+|\d+[.)]\s+|>\s+)/u, '')
        .trim(),
    )
    .find(Boolean);
  if (!firstLine) return '';
  return firstLine.length <= 96 ? firstLine : `${firstLine.slice(0, 93)}...`;
}

function searchLimit(request: HttpRequest<unknown>): number {
  const value = Number(request.params.get('limit') ?? 20);
  return Math.min(100, Math.max(1, Number.isFinite(value) ? Math.trunc(value) : 20));
}

function compareOrdinal(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

function normalize(value: string | null): string | null {
  return value?.trim() || null;
}

function isTaskSearchField(value: string): value is TaskSearchField {
  return ['state', 'id', 'track', 'milestone', 'in'].includes(value);
}

function isRecognizedFieldToken(value: string): boolean {
  const colon = value.indexOf(':');
  return colon >= 0 && isTaskSearchField(value.slice(0, colon).toLowerCase());
}

function badRequest(detail: string, errorCode: string): HttpErrorResponse {
  return new HttpErrorResponse({
    status: 400,
    statusText: 'Bad Request',
    error: { title: 'Search failed', detail, errorCode },
  });
}
