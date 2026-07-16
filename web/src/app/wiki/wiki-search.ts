import { formatDate } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, DestroyRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';

import type { components } from '../api/generated/pm-api';
import { TopBarSearch, type TopBarSearchOption } from '../shared/top-bar-search/top-bar-search';

type WikiSearchResult = components['schemas']['WikiSearchResultResponse'];

interface WikiSearchOption extends TopBarSearchOption {
  result: WikiSearchResult;
}

@Component({
  selector: 'pm-wiki-search',
  imports: [TopBarSearch],
  template: `
    <pm-top-bar-search
      ariaLabel="Search wiki"
      listboxLabel="Wiki search results"
      placeholder="Search wiki"
      emptyMessage="No matching wiki pages."
      [(query)]="query"
      [options]="options()"
      [loading]="loading()"
      [error]="error()"
      (queryEdited)="onQueryEdited($event)"
      (optionSelected)="open($event)"
    />
  `,
})
export class WikiSearch {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly search = viewChild(TopBarSearch);
  private readonly requests = new Subject<string>();

  protected readonly query = signal('');
  protected readonly options = signal<WikiSearchOption[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.requests
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((query) => {
          if (!query) return of({ results: [] as WikiSearchResult[], error: null });
          this.loading.set(true);
          this.error.set(null);
          const params = new HttpParams().set('query', query).set('limit', 20);
          return this.http.get<WikiSearchResult[]>('/api/v1/wiki/search', { params }).pipe(
            map((results) => ({ results, error: null as string | null })),
            catchError((error: unknown) =>
              of({ results: [] as WikiSearchResult[], error: this.readError(error) }),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ results, error }) => {
        this.loading.set(false);
        this.error.set(error);
        this.options.set(
          results.map((result) => ({
            id: `wiki-${result.path}`,
            primary: result.title,
            secondary: `${result.path} · ${formatDate(result.modifiedAt, 'medium', 'en-US')}`,
            snippet: result.snippet,
            result,
          })),
        );
      });
  }

  protected onQueryEdited(query: string): void {
    const trimmed = query.trim();
    this.error.set(null);
    this.options.set([]);
    this.loading.set(!!trimmed);
    this.requests.next(trimmed);
  }

  protected async open(selected: TopBarSearchOption): Promise<void> {
    const option = this.options().find((candidate) => candidate.id === selected.id);
    if (!option) return;
    this.query.set('');
    this.options.set([]);
    this.search()?.close();
    await this.router.navigate(['/wiki', ...option.result.path.split('/')]);
  }

  private readError(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.error && typeof error.error === 'object') {
      const detail = (error.error as { detail?: unknown }).detail;
      if (typeof detail === 'string' && detail.trim()) return detail;
    }
    return error instanceof HttpErrorResponse && error.status === 0
      ? 'Wiki search is unavailable.'
      : 'Wiki search failed. Try again.';
  }
}
