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

  function render() {
    const fixture = TestBed.createComponent(WikiSearch);
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
    const { fixture, element } = render();
    const input = enter(element, '   ');
    await debounce();
    fixture.detectChanges();
    expect(input.getAttribute('aria-expanded')).toBe('false');
    TestBed.inject(HttpTestingController).expectNone('/api/v1/wiki/search');
  });

  it('debounces, cancels stale requests, and safely renders result content', async () => {
    const { fixture, element } = render();
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
    const { fixture, element } = render();
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
