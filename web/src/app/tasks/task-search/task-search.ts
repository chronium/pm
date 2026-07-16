import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { cssClose, cssSearch } from '@ng-icons/css.gg';
import { Router } from '@angular/router';
import { Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';

import type { components } from '../../api/generated/pm-api';

type TaskSearchResult = components['schemas']['TaskSearchResultResponse'];
type SettingsResponse = components['schemas']['SettingsResponse'];
type SearchField = 'state' | 'id' | 'track' | 'milestone';

interface SearchOption {
  id: string;
  kind: 'pattern' | 'value' | 'result';
  primary: string;
  secondary: string;
  field?: SearchField;
  value?: string;
  result?: TaskSearchResult;
}

const patterns: SearchOption[] = [
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
    secondary: 'Match a task ID prefix',
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
  imports: [NgIcon],
  providers: [provideIcons({ cssClose, cssSearch })],
  templateUrl: './task-search.html',
  styleUrl: './task-search.css',
})
export class TaskSearch {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly input = viewChild<ElementRef<HTMLInputElement>>('searchInput');
  private readonly requests = new Subject<string>();
  private readonly settings = signal<SettingsResponse | null>(null);
  private readonly resultOptions = signal<SearchOption[]>([]);
  private readonly suggestionOptions = signal<SearchOption[]>([]);

  protected readonly query = signal('');
  protected readonly focused = signal(false);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly mobileExpanded = signal(false);
  protected readonly activeIndex = signal(0);
  protected readonly options = computed(() => {
    if (!this.query().trim()) return this.focused() ? patterns : [];
    return this.suggestionOptions().length ? this.suggestionOptions() : this.resultOptions();
  });
  protected readonly popupOpen = computed(
    () =>
      this.focused() &&
      (this.options().length > 0 || this.loading() || !!this.error() || !!this.query().trim()),
  );

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
        this.ensureActiveIndex();
      });
  }

  protected expandMobile(): void {
    this.mobileExpanded.set(true);
    setTimeout(() => this.input()?.nativeElement.focus());
  }

  protected close(): void {
    this.focused.set(false);
    this.mobileExpanded.set(false);
    this.activeIndex.set(0);
  }

  protected onFocus(): void {
    this.focused.set(true);
    this.updateSuggestions();
  }

  protected onBlur(): void {
    setTimeout(() => {
      const host = this.input()?.nativeElement.closest('pm-task-search');
      if (!host?.contains(document.activeElement)) this.close();
    });
  }

  protected onInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
    this.error.set(null);
    this.resultOptions.set([]);
    this.activeIndex.set(0);
    this.updateSuggestions();
    const query = this.completeQuery() ? this.query().trim() : '';
    this.loading.set(!!query && this.suggestionOptions().length === 0);
    this.requests.next(query);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const options = this.options();
    if (event.key === 'ArrowDown' && options.length) {
      event.preventDefault();
      this.activeIndex.set((this.activeIndex() + 1) % options.length);
    } else if (event.key === 'ArrowUp' && options.length) {
      event.preventDefault();
      this.activeIndex.set((this.activeIndex() - 1 + options.length) % options.length);
    } else if (event.key === 'Enter' && options[this.activeIndex()]) {
      event.preventDefault();
      this.accept(options[this.activeIndex()]!);
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.close();
      this.input()?.nativeElement.blur();
    }
  }

  protected accept(option: SearchOption): void {
    if (option.kind === 'result') {
      void this.openTask(option.result!);
      return;
    }
    const replacement =
      option.kind === 'pattern' ? `${option.field}:` : `${option.field}:${option.value}`;
    this.replaceActiveToken(replacement);
  }

  protected optionId(index: number): string {
    return `task-search-option-${index}`;
  }

  private updateSuggestions(): void {
    const active = this.activeField();
    if (!active) {
      this.suggestionOptions.set([]);
      return;
    }
    const { field, value } = active;
    if (field === 'id') {
      this.suggestionOptions.set([]);
      return;
    }
    const settings = this.settings();
    if (!settings) return;
    const configured =
      field === 'state'
        ? settings.statuses
        : field === 'track'
          ? settings.tracks
          : settings.milestones;
    const lowered = value.toLowerCase();
    if (configured.some((item) => item.key.toLowerCase() === lowered)) {
      this.suggestionOptions.set([]);
      return;
    }
    this.suggestionOptions.set(
      configured
        .filter((item) => item.key.toLowerCase().startsWith(lowered))
        .map((item) => ({
          id: `value-${field}-${item.key}`,
          kind: 'value' as const,
          primary: item.key,
          secondary: 'name' in item ? item.name : item.title,
          field,
          value: item.key,
        })),
    );
    this.ensureActiveIndex();
  }

  private activeField(): { field: SearchField; value: string } | null {
    const input = this.input()?.nativeElement;
    const caret =
      input?.value === this.query()
        ? (input.selectionStart ?? this.query().length)
        : this.query().length;
    const before = this.query().slice(0, caret);
    const match = before.match(/(?:^|\s)(state|id|track|milestone):\s*([^\s]*)$/i);
    return match ? { field: match[1]!.toLowerCase() as SearchField, value: match[2] ?? '' } : null;
  }

  private completeQuery(): boolean {
    const value = this.query().trim();
    if (!value) return false;
    return !/(?:^|\s)(?:state|id|track|milestone):\s*$/i.test(value);
  }

  private replaceActiveToken(replacement: string): void {
    const input = this.input()?.nativeElement;
    const caret = input?.selectionStart ?? this.query().length;
    const before = this.query().slice(0, caret);
    const fieldMatch = before.match(/(?:^|\s)(state|id|track|milestone):\s*[^\s]*$/i);
    const tokenMatch = before.match(/[^\s]*$/);
    const start = fieldMatch
      ? caret - fieldMatch[0].trimStart().length
      : caret - (tokenMatch?.[0].length ?? 0);
    const tokenEnd = this.query().slice(caret).search(/\s/);
    const end = tokenEnd < 0 ? this.query().length : caret + tokenEnd;
    const suffix = this.query().slice(end);
    const separator = suffix && !/^\s/.test(suffix) ? ' ' : '';
    const next = `${this.query().slice(0, start)}${replacement}${separator}${suffix}`;
    this.query.set(next);
    this.resultOptions.set([]);
    this.activeIndex.set(0);
    queueMicrotask(() => {
      const element = this.input()?.nativeElement;
      const nextCaret = start + replacement.length;
      element?.focus();
      element?.setSelectionRange(nextCaret, nextCaret);
      this.updateSuggestions();
      const query = this.completeQuery() ? this.query().trim() : '';
      this.loading.set(!!query && this.suggestionOptions().length === 0);
      this.requests.next(query);
    });
  }

  private searchParams(query: string): HttpParams {
    let params = new HttpParams().set('query', query).set('limit', 20);
    const current = this.router.parseUrl(this.router.url).queryParams;
    for (const field of ['track', 'milestone', 'state'] as const) {
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
      result,
    };
  }

  private async openTask(result: TaskSearchResult): Promise<void> {
    const queryParams = this.router.parseUrl(this.router.url).queryParams;
    this.query.set('');
    this.resultOptions.set([]);
    this.suggestionOptions.set([]);
    this.close();
    await this.router.navigate(['/tasks', result.id], { queryParams });
  }

  private ensureActiveIndex(): void {
    if (this.activeIndex() >= this.options().length) this.activeIndex.set(0);
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
