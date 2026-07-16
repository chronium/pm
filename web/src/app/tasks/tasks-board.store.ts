import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';

import type { components, operations } from '../api/generated/pm-api';
import { TaskNavigationService } from './task-navigation.service';

export type BoardResponse = operations['GetBoard']['responses'][200]['content']['application/json'];
export type BoardQuery = NonNullable<operations['GetBoard']['parameters']['query']>;
export type BoardTask = components['schemas']['BoardTaskSummaryResponse'];
export type BoardMilestoneGroup = components['schemas']['BoardMilestoneGroupResponse'];
export type BoardStateGroup = components['schemas']['BoardStateGroupResponse'];
export type BoardFilter = keyof BoardQuery;
export interface StatusOpenIntent {
  milestone: BoardMilestoneGroup;
  state: BoardStateGroup;
  open: boolean;
}

@Injectable()
export class TasksBoardStore {
  private readonly router = inject(Router);
  private readonly taskNavigation = inject(TaskNavigationService);
  private readonly retainedBoard = signal<BoardResponse | undefined>(undefined);
  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.currentNavigation()?.finalUrl?.toString() ?? this.router.url },
  );

  readonly filters = computed<BoardQuery>(() => ({
    ...(this.queryValue('track') ? { track: this.queryValue('track')! } : {}),
    ...(this.queryValue('milestone') ? { milestone: this.queryValue('milestone')! } : {}),
    ...(this.queryValue('state') ? { state: this.queryValue('state')! } : {}),
  }));

  readonly resource = httpResource<BoardResponse>(() => ({
    url: '/api/v1/board',
    params: this.filters(),
  }));

  readonly board = computed(() =>
    this.resource.hasValue() ? this.resource.value() : this.retainedBoard(),
  );
  readonly loading = computed(() => this.resource.isLoading() && !this.board());
  readonly refreshing = computed(() => this.resource.isLoading() && !!this.board());
  readonly error = computed(() => this.readableError(this.resource.error()));
  readonly revision = computed(() => this.board()?.revision ?? null);
  readonly taskCount = computed(
    () =>
      this.board()?.milestoneGroups.reduce(
        (boardTotal, milestone) => boardTotal + this.milestoneTaskCount(milestone),
        0,
      ) ?? 0,
  );
  readonly empty = computed(() => !!this.board() && this.taskCount() === 0 && !this.loading());
  readonly selectedTaskId = computed(() => this.taskIdFromUrl());
  readonly hasFilters = computed(() => Object.keys(this.filters()).length > 0);

  constructor() {
    effect(() => {
      if (this.resource.hasValue()) {
        const board = this.resource.value();
        this.retainedBoard.set(board);
        if (Object.values(board.filters).every((value) => value === null)) {
          this.taskNavigation.setRemainingCount(this.remainingTaskCount(board));
        }
      }
    });
  }

  setFilter(filterName: BoardFilter, value: string | null): Promise<boolean> {
    const tree = this.router.parseUrl(this.router.url);
    const normalized = value?.trim() || null;
    if (normalized) {
      tree.queryParams[filterName] = normalized;
    } else {
      delete tree.queryParams[filterName];
    }
    return this.router.navigateByUrl(tree);
  }

  clearFilters(): Promise<boolean> {
    const tree = this.router.parseUrl(this.router.url);
    delete tree.queryParams['track'];
    delete tree.queryParams['milestone'];
    delete tree.queryParams['state'];
    return this.router.navigateByUrl(tree);
  }

  reload(): boolean {
    return this.resource.reload();
  }

  milestoneTaskCount(group: BoardMilestoneGroup): number {
    return group.states.reduce((total, state) => total + state.tasks.length, 0);
  }

  private remainingTaskCount(board: BoardResponse): number {
    return board.milestoneGroups.reduce(
      (total, milestone) =>
        total +
        milestone.states.reduce(
          (milestoneTotal, state) =>
            milestoneTotal + (state.key === 'done' ? 0 : state.tasks.length),
          0,
        ),
      0,
    );
  }

  isGroupOpen(milestone: BoardMilestoneGroup, state: BoardStateGroup): boolean {
    const stored = this.readCollapsePreference(milestone, state);
    return stored === null ? state.key !== 'done' : stored;
  }

  groupOpenStates(milestone: BoardMilestoneGroup): Readonly<Record<string, boolean>> {
    return Object.fromEntries(
      milestone.states.map((state) => [state.key, this.isGroupOpen(milestone, state)]),
    );
  }

  rememberGroupOpen({ milestone, state, open }: StatusOpenIntent): void {
    try {
      sessionStorage.setItem(this.collapseKey(milestone, state), String(open));
    } catch {
      // The board remains usable when storage is disabled or unavailable.
    }
  }

  private queryValue(name: BoardFilter): string | null {
    const value: unknown = this.router.parseUrl(this.currentUrl()).queryParams[name];
    return typeof value === 'string' ? value.trim() || null : null;
  }

  private taskIdFromUrl(): string | null {
    const primary = this.router.parseUrl(this.currentUrl()).root.children['primary'];
    const segments = primary?.segments ?? [];
    const tasksIndex = segments.findIndex((segment) => segment.path === 'tasks');
    return tasksIndex >= 0 && segments.length > tasksIndex + 1
      ? segments[tasksIndex + 1]!.path
      : null;
  }

  private collapseKey(milestone: BoardMilestoneGroup, state: BoardStateGroup): string {
    const project = encodeURIComponent(this.board()?.projectName ?? 'unknown-project');
    const milestoneKey = encodeURIComponent(milestone.key ?? 'unassigned');
    return `pm.tasks-board.v1.${project}.${milestoneKey}.${encodeURIComponent(state.key)}.open`;
  }

  private readCollapsePreference(
    milestone: BoardMilestoneGroup,
    state: BoardStateGroup,
  ): boolean | null {
    try {
      const stored = sessionStorage.getItem(this.collapseKey(milestone, state));
      return stored === null ? null : stored === 'true';
    } catch {
      return null;
    }
  }

  private readableError(error: Error | undefined): string | null {
    if (!error) return null;
    if (error instanceof HttpErrorResponse) {
      const body: unknown = error.error;
      if (this.isProblemDetails(body)) {
        return (
          body.detail?.trim() || body.title?.trim() || `The board request failed (${error.status}).`
        );
      }
      if (error.status === 0) return 'The board API could not be reached.';
      return `The board request failed (${error.status} ${error.statusText || 'Unknown error'}).`;
    }
    return error.message || 'The board could not be loaded.';
  }

  private isProblemDetails(value: unknown): value is { detail?: string; title?: string } {
    return typeof value === 'object' && value !== null;
  }
}
