import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import {
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, catchError, debounceTime, distinctUntilChanged, map, of, switchMap } from 'rxjs';

import type { components } from '../api/generated/pm-api';
import { ProjectContextService } from '../core/project-context.service';
import { TopBarSearch, type TopBarSearchOption } from '../shared/top-bar-search/top-bar-search';
import type { ActivationRequirementRequest } from './activation-api.service';

type RequirementKind = 'task' | 'milestone';
type TaskSearchResult = components['schemas']['TaskSearchResultResponse'];

export interface ActivationRequirementMilestone {
  key: string;
  title: string;
}

interface RequirementOption extends TopBarSearchOption {
  kind: RequirementKind;
  source: string;
}

@Component({
  selector: 'pm-activation-requirement-editor',
  imports: [TopBarSearch],
  templateUrl: './activation-requirement-editor.html',
  styleUrl: './activation-requirement-editor.css',
})
export class ActivationRequirementEditor {
  private readonly http = inject(HttpClient);
  private readonly projectContext = inject(ProjectContextService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly search = viewChild(TopBarSearch);
  private readonly taskQueries = new Subject<string>();
  private readonly taskResults = signal<TaskSearchResult[]>([]);

  readonly requirements = input.required<readonly ActivationRequirementRequest[]>();
  readonly milestones = input<readonly ActivationRequirementMilestone[]>([]);
  readonly disabled = input(false);
  readonly requirementsChange = output<ActivationRequirementRequest[]>();

  protected readonly kind = signal<RequirementKind>('task');
  protected readonly query = signal('');
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly selected = computed(
    () => new Set(this.requirements().map((item) => `${item.kind}:${item.source}`)),
  );
  protected readonly options = computed<RequirementOption[]>(() => {
    const selected = this.selected();
    if (this.kind() === 'task') {
      return this.taskResults()
        .filter((result) => !selected.has(`task:${result.id}`))
        .map((result) => ({
          id: `task:${result.id}`,
          kind: 'task',
          source: result.id,
          leading: result.id,
          primary: result.title,
          secondary: [result.state, result.track, result.milestone].filter(Boolean).join(' · '),
        }));
    }

    const query = this.query().trim().toLowerCase();
    return this.milestones()
      .filter((milestone) => !selected.has(`milestone:${milestone.key}`))
      .filter(
        (milestone) =>
          !query ||
          milestone.key.toLowerCase().includes(query) ||
          milestone.title.toLowerCase().includes(query),
      )
      .map((milestone) => ({
        id: `milestone:${milestone.key}`,
        kind: 'milestone',
        source: milestone.key,
        leading: milestone.key,
        primary: milestone.title,
      }));
  });

  constructor() {
    this.taskQueries
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((query) => {
          if (!query) return of({ results: [] as TaskSearchResult[], error: null });
          this.loading.set(true);
          this.error.set(null);
          return this.http
            .get<TaskSearchResult[]>(this.projectContext.apiUrl('/tasks/search'), {
              params: new HttpParams().set('query', query).set('limit', 20),
            })
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
        this.taskResults.set(results);
      });
  }

  protected changeKind(event: Event): void {
    this.kind.set((event.target as HTMLSelectElement).value as RequirementKind);
    this.query.set('');
    this.taskResults.set([]);
    this.error.set(null);
    this.loading.set(false);
    this.taskQueries.next('');
  }

  protected editQuery(query: string): void {
    this.error.set(null);
    if (this.kind() === 'milestone') return;
    const normalized = query.trim();
    if (!normalized) {
      this.taskResults.set([]);
      this.loading.set(false);
    } else {
      this.loading.set(true);
    }
    this.taskQueries.next(normalized);
  }

  protected select(option: TopBarSearchOption): void {
    const selected = this.options().find((candidate) => candidate.id === option.id);
    if (!selected || this.disabled()) return;
    const identity = `${selected.kind}:${selected.source}`;
    if (this.selected().has(identity)) return;
    this.requirementsChange.emit([
      ...this.requirements(),
      { kind: selected.kind, source: selected.source },
    ]);
    this.query.set('');
    this.taskResults.set([]);
    this.error.set(null);
    this.search()?.close();
    queueMicrotask(() => this.search()?.focusAt(0));
  }

  protected remove(index: number): void {
    if (this.disabled()) return;
    this.requirementsChange.emit(this.requirements().filter((_, candidate) => candidate !== index));
  }

  protected milestoneTitle(source: string): string | null {
    return this.milestones().find((milestone) => milestone.key === source)?.title ?? null;
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
