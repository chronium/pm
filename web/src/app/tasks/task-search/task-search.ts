import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';

import type { components } from '../../api/generated/pm-api';
import { TopBarSearch, type TopBarSearchOption } from '../../shared/top-bar-search/top-bar-search';
import { TaskNavigationService } from '../task-navigation.service';

type TaskSearchResult = components['schemas']['TaskSearchResultResponse'];
type SettingsResponse = components['schemas']['SettingsResponse'];
type SearchField = 'state' | 'id' | 'track' | 'milestone' | 'in';

interface SearchOption extends TopBarSearchOption {
  kind: 'pattern' | 'value' | 'result';
  field?: SearchField;
  value?: string;
  result?: TaskSearchResult;
}

const patterns: SearchOption[] = [
  {
    id: 'pattern-in',
    kind: 'pattern',
    primary: 'in:',
    secondary: 'Search the sidebar selection or whole project',
    field: 'in',
  },
  {
    id: 'pattern-state',
    kind: 'pattern',
    primary: 'state:',
    secondary: 'Filter by task state',
    field: 'state',
  },
  {
    id: 'pattern-id',
    kind: 'pattern',
    primary: 'id:',
    secondary: 'Match a task ID prefix or number',
    field: 'id',
  },
  {
    id: 'pattern-track',
    kind: 'pattern',
    primary: 'track:',
    secondary: 'Filter by track',
    field: 'track',
  },
  {
    id: 'pattern-milestone',
    kind: 'pattern',
    primary: 'milestone:',
    secondary: 'Filter by milestone',
    field: 'milestone',
  },
];

@Component({
  selector: 'pm-task-search',
  imports: [TopBarSearch],
  template: `
    <pm-top-bar-search
      ariaLabel="Search tasks"
      listboxLabel="Task search options"
      placeholder="Search tasks"
      emptyMessage="No matching tasks."
      [(query)]="query"
      [options]="options()"
      [loading]="loading()"
      [error]="error()"
      (queryEdited)="onQueryEdited($event)"
      (optionSelected)="accept($event)"
    />
  `,
})
export class TaskSearch {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly navigation = inject(TaskNavigationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly search = viewChild(TopBarSearch);
  private readonly requests = new Subject<string>();
  private readonly settings = signal<SettingsResponse | null>(null);
  private readonly resultOptions = signal<SearchOption[]>([]);
  private readonly suggestionOptions = signal<SearchOption[]>([]);

  protected readonly query = signal('');
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly options = computed(() => {
    if (!this.query().trim()) return patterns;
    return this.suggestionOptions().length ? this.suggestionOptions() : this.resultOptions();
  });

  constructor() {
    this.http
      .get<SettingsResponse>('/api/v1/settings')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (settings) => this.settings.set(settings) });

    this.requests
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((query) => {
          if (!query) return of({ results: [] as TaskSearchResult[], error: null });
          this.loading.set(true);
          this.error.set(null);
          return this.http
            .get<TaskSearchResult[]>('/api/v1/tasks/search', { params: this.searchParams(query) })
            .pipe(
              map((results) => ({ results, error: null as string | null })),
              catchError((error: unknown) =>
                of({ results: [] as TaskSearchResult[], error: this.readError(error) }),
              ),
            );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ results, error }) => {
        this.loading.set(false);
        this.error.set(error);
        this.resultOptions.set(results.map((result) => this.resultOption(result)));
      });
  }

  protected onQueryEdited(query: string): void {
    this.error.set(null);
    this.resultOptions.set([]);
    this.updateSuggestions();
    const requestQuery = this.completeQuery() ? query.trim() : '';
    this.loading.set(!!requestQuery && this.suggestionOptions().length === 0);
    this.requests.next(requestQuery);
  }

  protected accept(selected: TopBarSearchOption): void {
    const option = this.options().find((candidate) => candidate.id === selected.id);
    if (!option) return;
    if (option.kind === 'result') {
      void this.openTask(option.result!);
      return;
    }
    this.replaceActiveToken(
      option.kind === 'pattern' ? `${option.field}:` : `${option.field}:${option.value}`,
    );
  }

  private updateSuggestions(caret?: number): void {
    const active = this.activeField(caret);
    if (!active || active.field === 'id') {
      this.suggestionOptions.set([]);
      return;
    }
    const settings = this.settings();
    if (!settings && active.field !== 'in') return;
    const configured =
      active.field === 'in'
        ? [
            { key: 'selection', name: 'Current sidebar selection' },
            { key: 'all', name: 'Whole project' },
          ]
        : this.configuredValues(active.field, settings!);
    const lowered = active.value.toLowerCase();
    if (configured.some((item) => item.key.toLowerCase() === lowered)) {
      this.suggestionOptions.set([]);
      return;
    }
    this.suggestionOptions.set(
      configured
        .filter((item) => item.key.toLowerCase().startsWith(lowered))
        .map((item) => ({
          id: `value-${active.field}-${item.key}`,
          kind: 'value' as const,
          primary: item.key,
          secondary: 'name' in item ? item.name : item.title,
          field: active.field,
          value: item.key,
        })),
    );
  }

  private configuredValues(field: Exclude<SearchField, 'id' | 'in'>, settings: SettingsResponse) {
    return field === 'state'
      ? settings.statuses
      : field === 'track'
        ? settings.tracks
        : settings.milestones;
  }

  private activeField(knownCaret?: number): { field: SearchField; value: string } | null {
    const caret = knownCaret ?? this.search()?.caret() ?? this.query().length;
    const before = this.query().slice(0, caret);
    const match = before.match(/(?:^|\s)(state|id|track|milestone|in):\s*([^\s]*)$/i);
    return match ? { field: match[1]!.toLowerCase() as SearchField, value: match[2] ?? '' } : null;
  }

  private completeQuery(): boolean {
    const value = this.query().trim();
    return !!value && !/(?:^|\s)(?:state|id|track|milestone|in):\s*$/i.test(value);
  }

  private replaceActiveToken(replacement: string): void {
    const caret = this.search()?.caret() ?? this.query().length;
    const before = this.query().slice(0, caret);
    const fieldMatch = before.match(/(?:^|\s)(state|id|track|milestone|in):\s*[^\s]*$/i);
    const tokenMatch = before.match(/[^\s]*$/);
    const start = fieldMatch
      ? caret - fieldMatch[0].trimStart().length
      : caret - (tokenMatch?.[0].length ?? 0);
    const tokenEnd = this.query().slice(caret).search(/\s/);
    const end = tokenEnd < 0 ? this.query().length : caret + tokenEnd;
    const suffix = this.query().slice(end);
    const next = `${this.query().slice(0, start)}${replacement}${suffix && !/^\s/.test(suffix) ? ' ' : ''}${suffix}`;
    this.query.set(next);
    this.resultOptions.set([]);
    queueMicrotask(() => {
      this.search()?.focusAt(start + replacement.length);
      this.updateSuggestions(start + replacement.length);
      const requestQuery = this.completeQuery() ? this.query().trim() : '';
      this.loading.set(!!requestQuery && this.suggestionOptions().length === 0);
      this.requests.next(requestQuery);
    });
  }

  private searchParams(query: string): HttpParams {
    let params = new HttpParams().set('query', query).set('limit', 20);
    const current = this.router.parseUrl(this.router.url).queryParams;
    for (const field of ['track', 'milestone'] as const) {
      if (typeof current[field] === 'string' && current[field].trim())
        params = params.set(field, current[field]);
    }
    return params;
  }

  private resultOption(result: TaskSearchResult): SearchOption {
    return {
      id: `result-${result.id}`,
      kind: 'result',
      primary: result.title,
      secondary: [result.id, result.state, result.track, result.milestone]
        .filter(Boolean)
        .join(' · '),
      leading: result.id,
      snippet: result.snippet,
      result,
    };
  }

  private async openTask(result: TaskSearchResult): Promise<void> {
    this.query.set('');
    this.resultOptions.set([]);
    this.suggestionOptions.set([]);
    this.search()?.close();
    await this.navigation.navigateToTask(this.router, result.id);
  }

  private readError(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.error && typeof error.error === 'object') {
      const detail = (error.error as { detail?: unknown }).detail;
      if (typeof detail === 'string' && detail.trim()) return detail;
    }
    return error instanceof HttpErrorResponse && error.status === 0
      ? 'Task search is unavailable.'
      : 'Task search failed. Try again.';
  }
}
