import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { WikiSearch } from './wiki-search';

@Component({ template: '' })
class EmptyRoute {}

describe('WikiSearch', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WikiSearch],
      providers: [
        provideRouter([
          { path: 'wiki', component: EmptyRoute },
          { path: 'wiki/guides/:page', component: EmptyRoute },
          { path: 'projects/:projectId/wiki/guides/:page', component: EmptyRoute },
        ]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  async function render() {
    const fixture = TestBed.createComponent(WikiSearch);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/project/links')
      .flush({
        activeProjectId: 'prj_current',
        members: [
          {
            projectId: 'prj_current',
            name: 'Current project',
            alias: null,
            relationship: 'current',
            status: 'resolved',
            source: 'current',
            readable: true,
            writeTrusted: true,
          },
          {
            projectId: 'prj_child',
            name: 'Child project',
            alias: 'child',
            relationship: 'child',
            status: 'resolved',
            source: 'manifest',
            readable: true,
            writeTrusted: false,
          },
        ],
        warnings: [],
      });
    await TestBed.tick();
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function enter(element: HTMLElement, value: string): HTMLInputElement {
    const input = element.querySelector('input')!;
    input.focus();
    input.value = value;
    input.dispatchEvent(new Event('input'));
    return input;
  }

  async function debounce(): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, 275));
  }

  it('does not open or request for an empty query', async () => {
    const { fixture, element } = await render();
    const input = enter(element, '   ');
    await debounce();
    fixture.detectChanges();
    expect(input.getAttribute('aria-expanded')).toBe('true');
    expect(element.textContent).toContain('project:');
    TestBed.inject(HttpTestingController).expectNone('/api/v1/wiki/search');
  });

  it('suggests projects and searches a named linked project', async () => {
    const { fixture, element } = await render();
    const input = enter(element, 'project:ch');
    fixture.detectChanges();
    expect(element.textContent).toContain('child');
    (element.querySelector('[role="option"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    input.value = `${input.value}guide`;
    input.dispatchEvent(new Event('input'));
    await debounce();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/projects/prj_child/wiki/search?query=guide&limit=20')
      .flush([
        {
          path: 'guides/start',
          title: 'Child guide',
          modifiedAt: '2026-07-16T10:00:00Z',
          matchCount: 1,
          snippet: 'Guide',
        },
      ]);
    fixture.detectChanges();
    expect(element.textContent).toContain('Child project');
    (element.querySelector('[role="option"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/projects/prj_child/wiki/guides/start');
  });

  it('searches the family and reports degraded linked projects', async () => {
    const { fixture, element } = await render();
    enter(element, 'project:family rendering');
    await debounce();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/v1/project/links/wiki/search?query=rendering&limit=20')
      .flush({
        pages: [
          {
            projectId: 'prj_child',
            projectName: 'Child project',
            alias: 'child',
            relationship: 'child',
            path: 'architecture/rendering',
            title: 'Rendering',
            modifiedAt: '2026-07-16T10:00:00Z',
            matchCount: 2,
            snippet: 'Rendering pipeline',
          },
        ],
        warnings: [
          {
            code: 'missing',
            message: 'Sibling unavailable.',
            declaringProjectId: 'prj_current',
            targetProjectId: 'prj_missing',
            alias: 'missing',
            status: 'missing',
            repairCommand: null,
          },
        ],
      });
    fixture.detectChanges();
    expect(element.textContent).toContain('Child project');
    expect(element.textContent).toContain('1 linked project unavailable.');
  });

  it('debounces, cancels stale requests, and safely renders result content', async () => {
    const { fixture, element } = await render();
    enter(element, 'first');
    await debounce();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne((request) => request.params.get('query') === 'first');
    enter(element, 'second');
    await debounce();
    expect(first.cancelled).toBe(true);
    http.expectOne('/api/v1/wiki/search?query=second&limit=20').flush([
      {
        path: 'guides/C# & APIs',
        title: '<img src=x onerror=alert(1)>',
        modifiedAt: '2026-07-16T10:00:00Z',
        matchCount: 2,
        snippet: '<script>unsafe()</script> matched text',
      },
    ]);
    fixture.detectChanges();
    expect(element.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(element.textContent).toContain('<script>unsafe()</script> matched text');
    expect(element.querySelector('img')).toBeNull();
    expect(element.querySelector('script')).toBeNull();
  });

  it('shows API errors and navigates nested special-character paths', async () => {
    const { fixture, element } = await render();
    const input = enter(element, 'guide');
    await debounce();
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/v1/wiki/search?query=guide&limit=20')
      .flush({ detail: 'Wiki markdown is invalid.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(element.querySelector('[role="alert"]')?.textContent).toContain(
      'Wiki markdown is invalid.',
    );

    input.value = 'special';
    input.dispatchEvent(new Event('input'));
    await debounce();
    http.expectOne('/api/v1/wiki/search?query=special&limit=20').flush([
      {
        path: 'guides/C# & APIs',
        title: 'API guide',
        modifiedAt: '2026-07-16T10:00:00Z',
        matchCount: 1,
        snippet: 'Special',
      },
    ]);
    fixture.detectChanges();
    (element.querySelector('[role="option"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/wiki/guides/C%23%20&%20APIs');
    expect(input.value).toBe('');
  });
});
