import { HttpResponse, httpResource } from '@angular/common/http';
import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';

import { ProjectApiService } from '../api/project-api.service';
import { PollingCoordinator } from '../core/polling-coordinator';
import {
  WikiApiService,
  type WikiMutationResponse,
  type WikiPage,
  type WikiPageSummary,
} from './wiki-api.service';

export interface WikiTreeNode {
  name: string;
  path: string;
  page: WikiPageSummary | null;
  children: WikiTreeNode[];
}

export type WikiResolution =
  | { kind: 'page'; summary: WikiPageSummary }
  | { kind: 'folder'; pages: WikiPageSummary[] }
  | { kind: 'missing' };

@Injectable()
export class WikiStore {
  private readonly api = inject(WikiApiService);
  private readonly project = inject(ProjectApiService);
  private readonly router = inject(Router);
  private readonly polling = inject(PollingCoordinator);
  private readonly retainedIndex = signal<WikiPageSummary[] | undefined>(undefined);
  private readonly retainedIndexEtag = signal('');
  private readonly retainedPage = signal<WikiPage | null>(null);
  private readonly retainedEtag = signal('');
  private readonly dirtyState = signal(false);
  private readonly pendingExternalPage = signal<WikiPage | null>(null);
  readonly unavailable = signal(false);
  readonly selectedPath = signal('');
  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.currentNavigation()?.finalUrl?.toString() ?? this.router.url },
  );

  readonly indexResource = httpResource<WikiPageSummary[]>(() => '/api/v1/wiki/pages');
  readonly pageResource = httpResource<WikiPage>(() =>
    this.selectedPath() ? this.api.pageUrl(this.selectedPath()) : undefined,
  );
  readonly pages = computed(() => this.retainedIndex());
  readonly indexLoading = computed(() => this.indexResource.isLoading() && !this.pages());
  readonly indexRefreshing = computed(() => this.indexResource.isLoading() && !!this.pages());
  readonly indexError = computed(() =>
    this.indexResource.error()
      ? this.api.error(this.indexResource.error(), 'The wiki index could not be loaded.').message
      : null,
  );
  readonly page = computed(() => this.retainedPage());
  readonly etag = computed(() => this.retainedEtag());
  readonly pageLoading = computed(() => this.pageResource.isLoading() && !this.page());
  readonly pageError = computed(() =>
    this.pageResource.error()
      ? this.api.error(this.pageResource.error(), 'The wiki page could not be loaded.').message
      : null,
  );
  readonly tree = computed(() => buildWikiTree(this.pages() ?? []));
  readonly pendingExternal = computed(() => this.pendingExternalPage());
  readonly indexPoll = this.polling.create<WikiPageSummary[]>({
    target: () =>
      this.pages() && this.retainedIndexEtag()
        ? { url: '/api/v1/wiki/pages', etag: this.retainedIndexEtag() }
        : null,
    accept: (response) => this.acceptIndexPoll(response),
  });
  readonly pagePoll = this.polling.create<WikiPage>({
    target: () =>
      this.page() && this.etag() && this.selectedPath()
        ? { url: this.api.pageUrl(this.selectedPath()), etag: this.etag() }
        : null,
    accept: (response) => this.acceptPagePoll(response),
    missing: () => {
      this.unavailable.set(true);
      this.reloadIndex();
    },
  });
  readonly liveUpdateUnavailable = computed(
    () => this.indexPoll.state() === 'retrying' || this.pagePoll.state() === 'retrying',
  );
  private indexActive = false;
  private pageActive = false;

  constructor() {
    effect(() => {
      if (!this.indexResource.hasValue()) return;
      this.retainedIndex.set(this.indexResource.value());
      this.retainedIndexEtag.set(this.indexResource.headers()?.get('ETag') ?? '');
    });
    effect(() => {
      if (!this.pageResource.hasValue()) return;
      this.retainedPage.set(this.pageResource.value());
      this.retainedEtag.set(
        this.pageResource.headers()?.get('ETag') ?? `"${this.pageResource.value().revision}"`,
      );
      this.unavailable.set(false);
    });
    effect(() => {
      const mode = this.activeMode();
      const indexShouldRun = mode === 'index' && !!this.pages();
      const pageShouldRun = mode === 'page' && !!this.page();
      if (indexShouldRun !== this.indexActive) {
        this.indexActive = indexShouldRun;
        if (indexShouldRun) this.indexPoll.start(true);
        else this.indexPoll.stop();
      }
      if (pageShouldRun !== this.pageActive) {
        this.pageActive = pageShouldRun;
        if (pageShouldRun) this.pagePoll.start(true);
        else this.pagePoll.stop();
      }
    });
  }

  select(path: string): void {
    if (path !== this.selectedPath()) {
      this.retainedPage.set(null);
      this.retainedEtag.set('');
      this.pendingExternalPage.set(null);
      this.unavailable.set(false);
      this.selectedPath.set(path);
    }
  }

  clearSelection(): void {
    this.selectedPath.set('');
    this.retainedPage.set(null);
    this.retainedEtag.set('');
    this.pendingExternalPage.set(null);
  }
  reloadPage(): boolean {
    return this.pageResource.reload();
  }
  reloadIndex(): boolean {
    return this.indexResource.reload();
  }

  resolve(path: string): WikiResolution {
    const pages = this.pages() ?? [];
    const exact = pages.find((page) => page.path === path);
    if (exact) return { kind: 'page', summary: exact };
    const prefix = `${path}/`;
    const descendants = pages.filter((page) => page.path.startsWith(prefix));
    return descendants.length ? { kind: 'folder', pages: descendants } : { kind: 'missing' };
  }

  accept(response: WikiMutationResponse, previousPath?: string): WikiPage {
    const page = response.body!;
    this.retainedPage.set(page);
    this.retainedEtag.set(this.api.etag(response) || `"${page.revision}"`);
    this.pendingExternalPage.set(null);
    this.unavailable.set(false);
    this.upsertSummary(page, previousPath);
    return page;
  }

  setDirty(dirty: boolean): void {
    this.dirtyState.set(dirty);
  }

  reviewLatest(): WikiPage | null {
    const latest = this.pendingExternalPage();
    if (!latest) return null;
    this.retainedPage.set(latest);
    this.pendingExternalPage.set(null);
    return latest;
  }

  keepLatest(): void {
    this.pendingExternalPage.set(null);
    this.dirtyState.set(false);
  }

  fetchLatest(): void {
    this.pagePoll.restart(true);
  }

  refreshIndexNow(): void {
    this.indexPoll.restart(true);
  }

  removeLocal(path: string): void {
    this.retainedIndex.update((pages) => pages?.filter((page) => page.path !== path));
    if (this.selectedPath() === path) this.clearSelection();
  }

  expansionKey(): string {
    const name = this.project.project.hasValue()
      ? this.project.project.value().name
      : 'unknown-project';
    return `pm.wiki-tree.v1.${encodeURIComponent(name)}.expanded`;
  }

  private upsertSummary(page: WikiPage, previousPath?: string): void {
    const summary: WikiPageSummary = {
      path: page.path,
      title: page.title,
      modifiedAt: page.modifiedAt,
    };
    this.retainedIndex.update((current) => {
      const pages = (current ?? []).filter(
        (item) => item.path !== page.path && item.path !== previousPath,
      );
      return [...pages, summary].sort(comparePages);
    });
  }

  private acceptIndexPoll(response: HttpResponse<WikiPageSummary[]>): void {
    if (!response.body) return;
    this.retainedIndex.set(response.body);
    this.retainedIndexEtag.set(response.headers.get('ETag') ?? '');
  }

  private acceptPagePoll(response: HttpResponse<WikiPage>): void {
    if (!response.body) return;
    this.retainedEtag.set(response.headers.get('ETag') ?? `"${response.body.revision}"`);
    this.unavailable.set(false);
    if (this.dirtyState()) this.pendingExternalPage.set(response.body);
    else this.retainedPage.set(response.body);
    this.upsertSummary(response.body);
  }

  private activeMode(): 'index' | 'page' | 'none' {
    const tree = this.router.parseUrl(this.currentUrl());
    const segments = tree.root.children['primary']?.segments.map((segment) => segment.path) ?? [];
    if (segments[0] !== 'wiki') return 'none';
    const rest = segments.slice(1);
    if (!rest.length || rest[0] === 'new') return 'index';
    if (rest[0] === 'edit' || rest[0] === 'meta') return 'page';
    return this.resolve(rest.join('/')).kind === 'page' ? 'page' : 'index';
  }
}

export function comparePages(a: WikiPageSummary, b: WikiPageSummary): number {
  return (
    a.path.localeCompare(b.path, undefined, { sensitivity: 'base' }) || a.path.localeCompare(b.path)
  );
}

export function buildWikiTree(pages: readonly WikiPageSummary[]): WikiTreeNode[] {
  const roots: WikiTreeNode[] = [];
  for (const page of [...pages].sort(comparePages)) {
    let nodes = roots;
    let path = '';
    page.path.split('/').forEach((name, index, segments) => {
      path = path ? `${path}/${name}` : name;
      let node = nodes.find((item) => item.name === name);
      if (!node) {
        node = { name, path, page: null, children: [] };
        nodes.push(node);
      }
      if (index === segments.length - 1) node.page = page;
      nodes = node.children;
    });
  }
  const sort = (nodes: WikiTreeNode[]): WikiTreeNode[] =>
    nodes
      .sort(
        (a, b) =>
          a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }) ||
          a.name.localeCompare(b.name),
      )
      .map((node) => ({ ...node, children: sort(node.children) }));
  return sort(roots);
}
