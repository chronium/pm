import { HttpHeaders, HttpResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { buildWikiTree, comparePages, WikiStore } from './wiki.store';
import type { WikiPageSummary } from './wiki-api.service';

describe('wiki tree derivation', () => {
  const summary = (path: string, title = path): WikiPageSummary => ({
    path,
    title,
    modifiedAt: '2026-01-01T00:00:00Z',
  });

  it('derives folders from a flat list with deterministic ordering', () => {
    const tree = buildWikiTree([
      summary('z-last'),
      summary('architecture/rendering/canvas'),
      summary('architecture/overview'),
    ]);
    expect(tree.map((node) => node.path)).toEqual(['architecture', 'z-last']);
    expect(tree[0]!.children.map((node) => node.path)).toEqual([
      'architecture/overview',
      'architecture/rendering',
    ]);
    expect(tree[0]!.children[1]!.children[0]!.path).toBe('architecture/rendering/canvas');
  });

  it('preserves a page when the same path is also a folder', () => {
    const tree = buildWikiTree([
      summary('guides', 'Guides home'),
      summary('guides/start', 'Start'),
    ]);
    expect(tree[0]!.page?.title).toBe('Guides home');
    expect(tree[0]!.children[0]!.page?.title).toBe('Start');
  });

  it('orders equal case-insensitive paths consistently', () => {
    const pages = [summary('beta'), summary('Alpha'), summary('alpha')].sort(comparePages);
    expect(pages.map((page) => page.path)).toEqual(['alpha', 'Alpha', 'beta']);
  });
});

describe('WikiStore retained state', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [WikiStore, provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('resolves page/folder coexistence page-first and keeps mutations in the retained index', async () => {
    const store = TestBed.inject(WikiStore);
    TestBed.flushEffects();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/project').flush({ name: 'Atlas', revision: 'p1' });
    http.expectOne('/api/v1/wiki/pages').flush([
      { path: 'guides', title: 'Guides home', modifiedAt: '2026-01-01T00:00:00Z' },
      { path: 'guides/start', title: 'Start', modifiedAt: '2026-01-01T00:00:00Z' },
    ]);
    TestBed.flushEffects();
    await vi.waitFor(() => expect(store.pages()?.length).toBe(2));

    expect(store.resolve('guides').kind).toBe('page');
    expect(store.resolve('guides/start').kind).toBe('page');
    expect(store.resolve('missing').kind).toBe('missing');

    store.accept(
      new HttpResponse({
        body: {
          path: 'new/page',
          title: 'New page',
          createdAt: '2026-01-01T00:00:00Z',
          modifiedAt: '2026-01-02T00:00:00Z',
          body: '',
          revision: 'r2',
          localMetadata: { filePath: '.pm/wiki/new/page.md' },
        },
        headers: new HttpHeaders({ ETag: '"r2"' }),
      }),
    );
    expect(store.resolve('new/page').kind).toBe('page');
    expect(store.resolve('new').kind).toBe('folder');
    store.removeLocal('new/page');
    expect(store.resolve('new/page').kind).toBe('missing');
  });
});
