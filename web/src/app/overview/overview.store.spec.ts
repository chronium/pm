import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import type { OverviewDocument } from './overview.store';
import { OverviewStore } from './overview.store';

@Component({ template: '' })
class RouteTarget {}

const readyDocument: OverviewDocument = {
  status: 'ready',
  projectId: 'project-1',
  projectName: 'Project Model',
  documentTitle: 'PM home',
  composition: {
    layout: 'single',
    sections: [{ type: 'hero', title: 'PM home', description: '' }],
  },
  issues: [],
  revision: 'overview-ready',
};

describe('OverviewStore', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'tasks', component: RouteTarget },
          { path: 'projects/:projectId/tasks', component: RouteTarget },
        ]),
      ],
    }),
  );

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('loads the current document and exposes navigation only for ready or invalid states', async () => {
    const store = TestBed.inject(OverviewStore);
    expect(store.loading()).toBe(true);
    await TestBed.tick();
    TestBed.inject(HttpTestingController).expectOne('/api/v1/overview').flush(readyDocument);
    await vi.waitFor(() => expect(store.document()).toEqual(readyDocument));

    expect(store.loading()).toBe(false);
    expect(store.available()).toBe(true);
  });

  it('switches to the selected linked-project endpoint without retaining prior availability', async () => {
    const router = TestBed.inject(Router);
    const store = TestBed.inject(OverviewStore);
    const http = TestBed.inject(HttpTestingController);
    expect(store.loading()).toBe(true);
    await TestBed.tick();
    http.expectOne('/api/v1/overview').flush(readyDocument);
    await vi.waitFor(() => expect(store.available()).toBe(true));

    await router.navigateByUrl('/projects/child/tasks');
    await vi.waitFor(() => expect(store.available()).toBe(false));
    await TestBed.tick();
    http.expectOne('/api/v1/projects/child/overview').flush({
      ...readyDocument,
      status: 'disabled',
      projectId: 'child',
      projectName: 'Child',
      documentTitle: 'Child',
      composition: null,
    });
    await vi.waitFor(() => expect(store.document()?.projectId).toBe('child'));

    expect(store.available()).toBe(false);
  });

  it('keeps transport failure distinct from disabled and retries locally', async () => {
    const store = TestBed.inject(OverviewStore);
    const http = TestBed.inject(HttpTestingController);
    expect(store.loading()).toBe(true);
    await TestBed.tick();
    http
      .expectOne('/api/v1/overview')
      .flush(
        { detail: 'The linked project could not be read.' },
        { status: 503, statusText: 'Service Unavailable' },
      );
    await vi.waitFor(() => expect(store.error()).toBe('The linked project could not be read.'));

    expect(store.document()).toBeNull();
    expect(store.available()).toBe(false);
    expect(store.reload()).toBe(true);
    await TestBed.tick();
    http.expectOne('/api/v1/overview').flush(readyDocument);
    await vi.waitFor(() => expect(store.available()).toBe(true));
  });
});
