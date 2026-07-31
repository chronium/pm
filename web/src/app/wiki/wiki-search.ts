import { formatDate } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';

import type { components } from '../api/generated/pm-api';
import { ProjectContextService, type LinkedProjectMember } from '../core/project-context.service';
import { TopBarSearch, type TopBarSearchOption } from '../shared/top-bar-search/top-bar-search';

type WikiSearchResult = components['schemas']['WikiSearchResultResponse'];
type LinkedWikiSearchResponse = components['schemas']['LinkedWikiSearchResponse'];

interface OwnedWikiResult extends WikiSearchResult {
  projectId: string | null;
  projectName: string | null;
  alias: string | null;
  destination: string;
}

interface WikiSearchOption extends TopBarSearchOption {
  kind: 'pattern' | 'project' | 'result';
  value?: string;
  result?: OwnedWikiResult;
}

interface SearchRequest {
  query: string;
  endpoint: string;
  owner: LinkedProjectMember | null;
  family: boolean;
}

const projectPattern: WikiSearchOption = {
  id: 'pattern-project',
  kind: 'pattern',
  primary: 'project:',
  secondary: 'Search one linked project or the whole family',
};

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
      [notice]="notice()"
      (queryEdited)="onQueryEdited($event)"
      (optionSelected)="accept($event)"
    />
  `,
})
export class WikiSearch {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly search = viewChild(TopBarSearch);
  private readonly projectContext = inject(ProjectContextService);
  private readonly requests = new Subject<SearchRequest | null>();
  private readonly results = signal<WikiSearchOption[]>([]);
  private readonly suggestions = signal<WikiSearchOption[]>([]);

  protected readonly query = signal('');
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly options = computed(() => {
    if (!this.query().trim()) return [projectPattern];
    return this.suggestions().length ? this.suggestions() : this.results();
  });

  constructor() {
    this.projectContext.enableLinkedProjectFamily();
    this.requests
      .pipe(
        debounceTime(250),
        distinctUntilChanged((left, right) => JSON.stringify(left) === JSON.stringify(right)),
        switchMap((request) => {
          if (!request) return of({ results: [] as OwnedWikiResult[], error: null, notice: null });
          this.loading.set(true);
          this.error.set(null);
          this.notice.set(null);
          const params = new HttpParams().set('query', request.query).set('limit', 20);
          if (request.family) {
            return this.http.get<LinkedWikiSearchResponse>(request.endpoint, { params }).pipe(
              map((response) => ({
                results: response.pages.map((page) => ({
                  ...page,
                  destination: this.projectContext.projectModeUrl(page.projectId, 'wiki'),
                })),
                error: null as string | null,
                notice: response.warnings.length
                  ? `${response.warnings.length} linked project${response.warnings.length === 1 ? '' : 's'} unavailable.`
                  : null,
              })),
              catchError((error: unknown) => of(this.failed(error))),
            );
          }
          return this.http.get<WikiSearchResult[]>(request.endpoint, { params }).pipe(
            map((pages) => ({
              results: pages.map((page) => ({
                ...page,
                projectId: request.owner?.projectId ?? null,
                projectName: request.owner?.name ?? null,
                alias: request.owner?.alias ?? null,
                destination: request.owner
                  ? this.projectContext.projectModeUrl(request.owner.projectId, 'wiki')
                  : this.projectContext.wikiRoot(),
              })),
              error: null as string | null,
              notice: null as string | null,
            })),
            catchError((error: unknown) => of(this.failed(error))),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ results, error, notice }) => {
        this.loading.set(false);
        this.error.set(error);
        this.notice.set(notice);
        this.results.set(results.map((result) => this.resultOption(result)));
      });
  }

  protected onQueryEdited(query: string): void {
    this.error.set(null);
    this.notice.set(null);
    this.results.set([]);
    this.updateSuggestions();
    if (this.suggestions().length) {
      this.loading.set(false);
      this.requests.next(null);
      return;
    }
    const request = this.parseRequest(query);
    this.loading.set(!!request);
    this.requests.next(request);
  }

  protected accept(selected: TopBarSearchOption): void {
    const option = this.options().find((candidate) => candidate.id === selected.id);
    if (!option) return;
    if (option.kind === 'result') {
      void this.open(option.result!);
      return;
    }
    this.replaceProjectToken(option.kind === 'pattern' ? 'project:' : `project:${option.value} `);
  }

  private parseRequest(raw: string): SearchRequest | null {
    const operators = [...raw.matchAll(/(?:^|\s)project:([^\s]*)/gi)];
    if (operators.length > 1) {
      this.error.set('Use only one project: filter.');
      return null;
    }
    const query = raw.replace(/(?:^|\s)project:[^\s]*/gi, ' ').trim();
    if (!query) return null;
    if (!operators.length)
      return {
        query,
        endpoint: this.projectContext.apiUrl('/wiki/search'),
        owner: this.projectContext.selectedMember(),
        family: false,
      };

    const scope = operators[0]![1]!.toLowerCase();
    if (scope === 'family')
      return {
        query,
        endpoint: '/api/v1/project/links/wiki/search',
        owner: null,
        family: true,
      };
    const owner = this.readableMembers().find(
      (member) => member.projectId.toLowerCase() === scope || member.alias?.toLowerCase() === scope,
    );
    if (!owner) {
      this.error.set(`Unknown or unavailable project: ${operators[0]![1]}.`);
      return null;
    }
    const family = this.projectContext.family.hasValue()
      ? this.projectContext.family.value()
      : null;
    if (!family) {
      this.error.set('Linked project information is still loading.');
      return null;
    }
    return {
      query,
      endpoint:
        owner.projectId === family.activeProjectId
          ? '/api/v1/wiki/search'
          : `/api/v1/projects/${encodeURIComponent(owner.projectId)}/wiki/search`,
      owner,
      family: false,
    };
  }

  private updateSuggestions(): void {
    const match = this.query()
      .slice(0, this.search()?.caret() ?? this.query().length)
      .match(/(?:^|\s)project:([^\s]*)$/i);
    if (!match) {
      this.suggestions.set([]);
      return;
    }
    const value = match[1]!.toLowerCase();
    const projects = [
      { value: 'family', name: 'All readable linked projects' },
      ...this.readableMembers().map((member) => ({
        value: member.alias || member.projectId,
        name: `${member.name} · ${member.relationship}`,
      })),
    ];
    this.suggestions.set(
      projects
        .filter((project) => project.value.toLowerCase().startsWith(value))
        .map((project) => ({
          id: `project-${project.value}`,
          kind: 'project' as const,
          primary: project.value,
          secondary: project.name,
          value: project.value,
        })),
    );
  }

  private readableMembers(): LinkedProjectMember[] {
    return this.projectContext.family.hasValue()
      ? this.projectContext.family.value().members.filter((member) => member.readable)
      : [];
  }

  private replaceProjectToken(replacement: string): void {
    const query = this.query();
    const caret = this.search()?.caret() ?? query.length;
    const before = query.slice(0, caret);
    const match = before.match(/(?:^|\s)project:[^\s]*$/i);
    const start = match ? caret - match[0].trimStart().length : caret;
    const next = `${query.slice(0, start)}${replacement}${query.slice(caret)}`;
    this.query.set(next);
    this.suggestions.set([]);
    queueMicrotask(() => this.search()?.focusAt(start + replacement.length));
  }

  private resultOption(result: OwnedWikiResult): WikiSearchOption {
    const owner = result.projectName ? `${result.projectName} · ` : '';
    return {
      id: `wiki-${result.projectId ?? 'selected'}-${result.path}`,
      kind: 'result',
      primary: result.title,
      secondary: `${owner}${result.path} · ${formatDate(result.modifiedAt, 'medium', 'en-US')}`,
      snippet: result.snippet,
      result,
    };
  }

  private async open(result: OwnedWikiResult): Promise<void> {
    this.query.set('');
    this.results.set([]);
    this.suggestions.set([]);
    this.search()?.close();
    const path = result.path
      .split('/')
      .map((segment) => encodeURIComponent(segment))
      .join('/');
    await this.router.navigateByUrl(`${result.destination}/${path}`);
  }

  private failed(error: unknown) {
    return { results: [] as OwnedWikiResult[], error: this.readError(error), notice: null };
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
