import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ProjectContextService } from '../core/project-context.service';
import { ProjectLinksService } from '../core/project-links.service';
import type { BoardTask } from '../tasks/tasks-board.store';
import { OverviewInvalidState, type OverviewIssue } from './overview-invalid-state';
import { OverviewMarkdown } from './overview-markdown';
import { OverviewMilestone, type OverviewMilestoneData } from './overview-milestone';
import { OverviewTasks } from './overview-tasks';
import { OverviewWiki, type OverviewWikiPage } from './overview-wiki';

const milestone: OverviewMilestoneData = {
  key: 'public-beta',
  title: 'Public beta',
  description: 'Deliver an **installable beta** for the complete local workflow.',
  priority: 'high',
  lifecycle: 'active',
  assignedTaskCount: 4,
  doneTaskCount: 3,
  requiredActivationTriggers: ['beta-entry'],
  unmetActivationTriggers: [],
};

const task: BoardTask = {
  id: 'PM-0128',
  title: 'Publish static project Overview pages',
  track: 'PM',
  milestone: 'public-beta',
  priority: 'high',
  prioritySource: 'milestone',
  state: 'in-progress',
  dependencies: {
    ready: true,
    dependsOn: ['PM-0127'],
    waitingOn: [],
    missing: [],
    summary: 'all dependencies complete',
  },
  activation: {
    isEligible: true,
    milestoneLifecycle: 'active',
    requiredActivationTriggers: ['beta-entry'],
    unmetActivationTriggers: [],
    summary: 'Eligible: milestone public-beta is active.',
  },
  descriptionPreview: 'Publish the resolved Overview through the existing static snapshot.',
  modifiedAt: '2026-08-09T08:00:00Z',
};

describe('Overview resolved sections', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

  it('presents milestone lifecycle, Markdown, completion, and accessible progress', () => {
    const fixture = TestBed.createComponent(OverviewMilestone);
    fixture.componentRef.setInput('headingId', 'current-milestone');
    fixture.componentRef.setInput('title', 'Current milestone');
    fixture.componentRef.setInput('milestone', milestone);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const progress = element.querySelector('progress') as HTMLProgressElement;
    expect(element.querySelector('section')?.getAttribute('aria-labelledby')).toBe(
      'current-milestone',
    );
    expect(element.textContent).toContain('Public beta');
    expect(element.textContent).toContain('Active');
    expect(element.querySelector('.milestone-description strong')?.textContent).toBe(
      'installable beta',
    );
    expect(element.textContent).toContain('3 of 4 tasks complete');
    expect(element.textContent).toContain('75%');
    expect(progress.value).toBe(3);
    expect(progress.max).toBe(4);
    expect(progress.getAttribute('aria-label')).toBe('3 of 4 tasks complete');
  });

  it('keeps empty automatic selection and zero-task progress non-vacuous', () => {
    const emptyFixture = TestBed.createComponent(OverviewMilestone);
    emptyFixture.componentRef.setInput('headingId', 'empty-milestone');
    emptyFixture.componentRef.setInput('title', 'Current milestone');
    emptyFixture.componentRef.setInput('milestone', null);
    emptyFixture.detectChanges();
    expect((emptyFixture.nativeElement as HTMLElement).textContent).toContain(
      'No active milestone is available.',
    );

    const zeroFixture = TestBed.createComponent(OverviewMilestone);
    zeroFixture.componentRef.setInput('headingId', 'zero-milestone');
    zeroFixture.componentRef.setInput('title', 'Current milestone');
    zeroFixture.componentRef.setInput('milestone', {
      ...milestone,
      assignedTaskCount: 0,
      doneTaskCount: 0,
    });
    zeroFixture.detectChanges();
    const zero = zeroFixture.nativeElement as HTMLElement;
    expect(zero.textContent).toContain('No assigned tasks');
    expect(zero.querySelector('progress')).toBeNull();
    expect(zero.textContent).not.toContain('100%');
  });

  it('renders ordered compact tasks with visible state and an honest empty state', () => {
    const fixture = TestBed.createComponent(OverviewTasks);
    fixture.componentRef.setInput('headingId', 'current-work');
    fixture.componentRef.setInput('title', 'What is being worked on');
    fixture.componentRef.setInput('tasks', [task]);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const row = element.querySelector('li[pmTaskRow]') as HTMLElement;
    const link = element.querySelector('li[pmTaskRow] a') as HTMLAnchorElement;
    expect(element.querySelector('section')?.getAttribute('aria-labelledby')).toBe('current-work');
    expect(link.getAttribute('href')).toBe('/tasks/PM-0128');
    expect(row.dataset['layout']).toBe('overview');
    expect(link.textContent).toContain('PM-0128');
    expect(link.querySelector('pm-badge')?.textContent).toContain('in-progress');
    expect(link.querySelectorAll('.task-status')).toHaveLength(2);

    fixture.componentRef.setInput('tasks', []);
    fixture.detectChanges();
    expect(element.querySelector('li[pmTaskRow]')).toBeNull();
    expect(element.textContent).toContain('No tasks match this section.');
  });

  it('renders ordered documentation links through the selected project context', () => {
    const wikiUrl = vi.fn(
      (path: string) =>
        `/projects/prj_child/wiki/${path
          .split('/')
          .map((segment) => encodeURIComponent(segment))
          .join('/')}`,
    );
    TestBed.overrideProvider(ProjectContextService, { useValue: { wikiUrl } });
    const pages: readonly OverviewWikiPage[] = [
      {
        path: 'publishing/static site',
        title: 'Static publishing',
        modifiedAt: '2026-08-09T08:00:00Z',
      },
      {
        path: 'architecture',
        title: 'Architecture',
        modifiedAt: '2026-08-08T08:00:00Z',
      },
    ];
    const fixture = TestBed.createComponent(OverviewWiki);
    fixture.componentRef.setInput('headingId', 'documentation');
    fixture.componentRef.setInput('title', 'Documentation');
    fixture.componentRef.setInput('pages', pages);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const links = [...element.querySelectorAll<HTMLAnchorElement>('li a')];
    expect(links.map((link) => link.querySelector('.wiki-page-title')?.textContent)).toEqual([
      'Static publishing',
      'Architecture',
    ]);
    expect(links[0]?.getAttribute('href')).toBe(
      '/projects/prj_child/wiki/publishing/static%20site',
    );
    expect(wikiUrl).toHaveBeenCalledWith('publishing/static site');

    fixture.componentRef.setInput('pages', []);
    fixture.detectChanges();
    expect(element.querySelector('li')).toBeNull();
    expect(element.textContent).toContain('No documentation pages are available.');
  });

  it('renders sanitized source Markdown without changing authored heading levels', () => {
    TestBed.overrideProvider(ProjectContextService, {
      useValue: { wikiUrl: (path: string) => `/wiki/${path}` },
    });
    TestBed.overrideProvider(ProjectLinksService, {
      useValue: {
        resolve: (href: string) =>
          href === 'pm://project/child/wiki/guide'
            ? { kind: 'available', href: '/projects/child/wiki/guide', local: true }
            : { kind: 'not-project-link' },
      },
    });
    const fixture = TestBed.createComponent(OverviewMarkdown);
    fixture.componentRef.setInput('headingId', 'introduction');
    fixture.componentRef.setInput('title', 'Introduction');
    fixture.componentRef.setInput('sourcePath', 'overview');
    fixture.componentRef.setInput(
      'body',
      '### Start here\n\nRead the [child guide](pm://project/child/wiki/guide).<script>alert(1)</script>',
    );
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('h2')?.textContent).toBe('Introduction');
    expect(element.querySelector('.markdown-body h3')?.textContent).toBe('Start here');
    expect(element.querySelector('.markdown-body script')).toBeNull();
    expect(element.querySelector<HTMLAnchorElement>('.markdown-body a')?.getAttribute('href')).toBe(
      '/projects/child/wiki/guide',
    );
    expect(
      element.querySelector<HTMLAnchorElement>('.overview-markdown-source a')?.getAttribute('href'),
    ).toBe('/wiki/overview');

    fixture.componentRef.setInput('body', '   ');
    fixture.detectChanges();
    expect(element.querySelector('.markdown-body')).toBeNull();
    expect(element.textContent).toContain('This documentation page is empty.');
  });

  it('presents every invalid Overview issue in one page-level alert', () => {
    const issues: readonly OverviewIssue[] = [
      {
        code: 'missing_overview_wiki_page',
        message: 'Wiki page missing was not found.',
        path: 'site.home.sections[2].pages[0]',
      },
      {
        code: 'missing_overview_markdown_source',
        message: 'Markdown source wiki:introduction was not found.',
        path: 'site.home.sections[3].source',
      },
    ];
    const fixture = TestBed.createComponent(OverviewInvalidState);
    fixture.componentRef.setInput('issues', issues);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[role="alert"]')).not.toBeNull();
    expect(element.querySelectorAll('li')).toHaveLength(2);
    expect(element.textContent).toContain('missing_overview_wiki_page');
    expect(element.textContent).toContain('site.home.sections[3].source');
  });
});
