import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { encodeWikiPath, WikiApiService } from './wiki-api.service';

describe('WikiApiService', () => {
  let api: WikiApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(WikiApiService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('encodes every path segment without flattening the hierarchy', () => {
    expect(encodeWikiPath('guides/C# & APIs/100%')).toBe('guides/C%23%20%26%20APIs/100%25');
    expect(api.pageUrl('guides/C# & APIs')).toBe('/api/v1/wiki/pages/guides/C%23%20%26%20APIs');
  });

  it('creates with the client header and adopts the response ETag', () => {
    let etag = '';
    api
      .create({ path: 'guide', title: 'Guide', body: 'Body' })
      .subscribe((response) => (etag = api.etag(response)));
    const request = http.expectOne('/api/v1/wiki/pages');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-PM-Client')).toBe('angular-web');
    request.flush(page('guide'), { headers: { ETag: '"revision-2"' } });
    expect(etag).toBe('"revision-2"');
  });

  it.each([
    ['body', 'PUT'],
    ['metadata', 'PATCH'],
    ['delete', 'DELETE'],
  ])('sends an exact strong If-Match for %s mutations', (kind, method) => {
    if (kind === 'body') api.updateBody('a/b', { body: 'new' }, '"exact"').subscribe();
    else if (kind === 'metadata')
      api.updateMetadata('a/b', { path: 'c/d', title: 'D' }, '"exact"').subscribe();
    else api.remove('a/b', '"exact"').subscribe();
    const request = http.expectOne('/api/v1/wiki/pages/a/b');
    expect(request.request.method).toBe(method);
    expect(request.request.headers.get('If-Match')).toBe('"exact"');
    expect(request.request.headers.get('X-PM-Client')).toBe('angular-web');
    request.flush(kind === 'delete' ? null : page('a/b'));
  });

  it('maps Problem Details conflicts and duplicates into readable errors', () => {
    const conflict = api.error(
      new HttpErrorResponse({
        status: 412,
        error: { title: 'Conflict', detail: 'Reload the page.', errorCode: 'precondition_failed' },
      }),
      'Failed',
    );
    const duplicate = api.error(
      new HttpErrorResponse({
        status: 409,
        error: { title: 'Duplicate', errorCode: 'duplicate_wiki_page' },
      }),
      'Failed',
    );
    expect(conflict).toEqual({
      status: 412,
      message: 'Reload the page.',
      conflict: true,
      duplicate: false,
    });
    expect(duplicate).toEqual({
      status: 409,
      message: 'Duplicate',
      conflict: false,
      duplicate: true,
    });
  });
});

function page(path: string) {
  return {
    path,
    title: 'Guide',
    createdAt: '2026-01-01T00:00:00Z',
    modifiedAt: '2026-01-02T00:00:00Z',
    body: 'Body',
    revision: 'revision-1',
    localMetadata: { filePath: `.pm/wiki/${path}.md` },
  };
}
