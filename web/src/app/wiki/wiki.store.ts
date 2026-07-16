import { httpResource } from '@angular/common/http';
import { computed, effect, inject, Injectable, signal } from '@angular/core';

import { ProjectApiService } from '../api/project-api.service';
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
  private readonly retainedIndex = signal<WikiPageSummary[] | undefined>(undefined);
  private readonly retainedPage = signal<WikiPage | null>(null);
  private readonly retainedEtag = signal('');
  readonly selectedPath = signal('');

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

  constructor() {
    effect(() => {
      if (this.indexResource.hasValue()) this.retainedIndex.set(this.indexResource.value());
    });
    effect(() => {
      if (!this.pageResource.hasValue()) return;
      this.retainedPage.set(this.pageResource.value());
      this.retainedEtag.set(
        this.pageResource.headers()?.get('ETag') ?? `"${this.pageResource.value().revision}"`,
      );
    });
  }

  select(path: string): void {
    if (path !== this.selectedPath()) {
      this.retainedPage.set(null);
      this.retainedEtag.set('');
      this.selectedPath.set(path);
    }
  }

  clearSelection(): void {
    this.selectedPath.set('');
    this.retainedPage.set(null);
    this.retainedEtag.set('');
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
    this.upsertSummary(page, previousPath);
    return page;
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
