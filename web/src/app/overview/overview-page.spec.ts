import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';

import type { OverviewDocument } from './overview.store';
import { OverviewPage } from './overview-page';
import { OverviewStore } from './overview.store';

const readyDocument: OverviewDocument = {
  status: 'ready',
  projectId: 'project-1',
  projectName: 'Project Model',
  documentTitle: 'Project Model home',
  composition: {
    layout: 'single',
    sections: [{ type: 'hero', title: 'Project Model home', description: 'Local-first PM.' }],
  },
  issues: [],
  revision: 'overview-ready',
};

describe('OverviewPage', () => {
  const document = signal<OverviewDocument | null>(null);
  const error = signal<string | null>(null);
  const reload = vi.fn(() => true);

  beforeEach(async () => {
    document.set(null);
    error.set(null);
    reload.mockClear();
    await TestBed.configureTestingModule({
      imports: [OverviewPage],
      providers: [
        provideRouter([
          { path: 'overview', component: OverviewPage },
          { path: 'tasks', component: OverviewPage },
          { path: 'projects/:projectId/overview', component: OverviewPage },
          { path: 'projects/:projectId/tasks', component: OverviewPage },
        ]),
        {
          provide: OverviewStore,
          useValue: {
            document,
            error,
            loading: () => !document() && !error(),
            available: () => document()?.status === 'ready' || document()?.status === 'invalid',
            reload,
          },
        },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.inject(Title).setTitle('PM');
    TestBed.resetTestingModule();
  });

  function render() {
    const fixture = TestBed.createComponent(OverviewPage);
    fixture.detectChanges();
    return fixture;
  }

  it('renders one page-level loading state and a localized retryable failure', () => {
    const fixture = render();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Loading Overview…');

    error.set('The Overview API could not be reached.');
    fixture.detectChanges();
    expect(element.textContent).toContain('Could not load this Overview');
    expect(element.textContent).toContain('The Overview API could not be reached.');
    (element.querySelector('button') as HTMLButtonElement).click();
    expect(reload).toHaveBeenCalledOnce();
  });

  it('renders the atomic ready composition and owns the Overview document title', () => {
    document.set(readyDocument);
    const fixture = render();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('h1')?.textContent).toBe('Project Model home');
    expect(element.textContent).toContain('Local-first PM.');
    expect(TestBed.inject(Title).getTitle()).toBe('Project Model home');

    fixture.destroy();
    expect(TestBed.inject(Title).getTitle()).toBe('PM');
  });

  it('shows every invalid configuration issue without rendering partial content', () => {
    document.set({
      ...readyDocument,
      status: 'invalid',
      composition: null,
      issues: [
        { code: 'missing_milestone', message: 'Milestone does not exist.', path: 'site.home' },
        { code: 'invalid_filter', message: 'Task filter is invalid.', path: 'site.home.sections' },
      ],
    });
    const fixture = render();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.textContent).toContain('This Overview needs attention');
    expect(element.textContent).toContain('Milestone does not exist.');
    expect(element.textContent).toContain('Task filter is invalid.');
    expect(element.querySelector('pm-overview-composition')).toBeNull();
  });

  it('redirects a disabled linked Overview to that project Tasks root without filters', async () => {
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/projects/child/overview');
    const fixture = render();

    document.set({
      ...readyDocument,
      status: 'disabled',
      projectId: 'child',
      projectName: 'Child',
      documentTitle: 'Child',
      composition: null,
    });
    fixture.detectChanges();
    await vi.waitFor(() => expect(router.url).toBe('/projects/child/tasks'));
  });
});
