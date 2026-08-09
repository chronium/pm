import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ProjectContextService } from '../core/project-context.service';
import type { BoardTask } from '../tasks/tasks-board.store';
import {
  OverviewComposition,
  type OverviewCompositionData,
  type OverviewSectionData,
} from './overview-composition';

const task: BoardTask = {
  id: 'PM-0109',
  title: 'Render production Overview compositions',
  track: 'PM',
  milestone: 'site-home-prototype',
  priority: 'high',
  prioritySource: 'task',
  state: 'in-progress',
  dependencies: {
    ready: true,
    dependsOn: ['PM-0108'],
    waitingOn: [],
    missing: [],
    summary: 'ready',
  },
  activation: {
    isEligible: true,
    milestoneLifecycle: 'active',
    requiredActivationTriggers: [],
    unmetActivationTriggers: [],
    summary: 'Eligible: milestone site-home-prototype is active.',
  },
  descriptionPreview: 'Promote the approved compositions into production presentation.',
  modifiedAt: '2026-08-09T12:00:00Z',
};

const sections: OverviewSectionData[] = [
  {
    type: 'hero',
    title: 'Project home',
    description: 'A resolved project Overview.',
  },
  {
    type: 'markdown',
    title: 'Introduction',
    sourcePath: 'overview',
    body: 'Welcome to the **project**.',
  },
  {
    type: 'milestone',
    title: 'Current delivery',
    milestone: {
      key: 'site-home-prototype',
      title: 'Site home prototype',
      description: 'Prove the public Overview composition.',
      priority: 'high',
      lifecycle: 'active',
      assignedTaskCount: '4',
      doneTaskCount: '3',
      requiredActivationTriggers: [],
      unmetActivationTriggers: [],
    },
  },
  {
    type: 'tasks',
    title: 'Current work',
    tasks: [task],
  },
  {
    type: 'wiki',
    title: 'Documentation',
    pages: [{ path: 'architecture', title: 'Architecture', modifiedAt: '2026-08-09T12:00:00Z' }],
  },
  {
    type: 'copyright',
    notice: '© 2026 Project contributors.',
  },
];

describe('OverviewComposition', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter([])] }));

  function render(composition: OverviewCompositionData, projectName = 'Project Model') {
    const fixture = TestBed.createComponent(OverviewComposition);
    fixture.componentRef.setInput('composition', composition);
    fixture.componentRef.setInput('projectName', projectName);
    fixture.detectChanges();
    return fixture;
  }

  it('renders every resolved section in document order through production components', () => {
    const fixture = render({ layout: 'single', sections });
    const element = fixture.nativeElement as HTMLElement;
    const headings = [...element.querySelectorAll<HTMLElement>('main h1, main h2')].map((heading) =>
      heading.textContent?.trim(),
    );

    expect(headings).toEqual([
      'Project home',
      'Introduction',
      'Current delivery',
      'Current work',
      'Documentation',
    ]);
    expect(element.querySelector('.overview-project-context')?.textContent).toContain(
      'Project Model',
    );
    expect(element.textContent).toContain('3 of 4 tasks complete');
    expect(element.querySelector('.markdown-body strong')?.textContent).toBe('project');
    expect(element.querySelector<HTMLAnchorElement>('li[pmTaskRow] a')?.getAttribute('href')).toBe(
      '/tasks/PM-0109',
    );
    expect(
      element.querySelector<HTMLAnchorElement>('.overview-wiki-grid a')?.getAttribute('href'),
    ).toBe('/wiki/architecture');
    expect(element.querySelector('footer')?.textContent).toContain('© 2026 Project contributors.');
    expect(
      [...element.querySelectorAll<HTMLElement>('main h2')].map((heading) => heading.id),
    ).toEqual([
      'overview-single-1-markdown',
      'overview-single-2-milestone',
      'overview-single-3-tasks',
      'overview-single-4-wiki',
    ]);
  });

  it('uses selected-project destinations throughout the composition', () => {
    TestBed.overrideProvider(ProjectContextService, {
      useValue: {
        tasksRoot: () => '/projects/child/tasks',
        wikiRoot: () => '/projects/child/wiki',
        taskUrl: (taskId: string) => `/projects/child/tasks/${taskId}`,
        wikiUrl: (path?: string) => `/projects/child/wiki${path ? `/${path}` : ''}`,
      },
    });
    const fixture = render({ layout: 'single', sections });
    const element = fixture.nativeElement as HTMLElement;
    const heroLinks = [...element.querySelectorAll<HTMLAnchorElement>('.overview-actions a')];

    expect(heroLinks.map((link) => link.getAttribute('href'))).toEqual([
      '/projects/child/tasks',
      '/projects/child/wiki',
    ]);
    expect(element.querySelector<HTMLAnchorElement>('li[pmTaskRow] a')?.getAttribute('href')).toBe(
      '/projects/child/tasks/PM-0109',
    );
    expect(
      element.querySelector<HTMLAnchorElement>('.overview-wiki-grid a')?.getAttribute('href'),
    ).toBe('/projects/child/wiki/architecture');
  });

  it('preserves primary, secondary, and after DOM order for split compositions', () => {
    const fixture = render({
      layout: 'split',
      primary: [sections[0]!, sections[1]!],
      secondary: [sections[2]!, sections[3]!],
      after: [sections[4]!, sections[5]!],
    });
    const element = fixture.nativeElement as HTMLElement;

    expect(
      [...element.querySelectorAll<HTMLElement>('[data-region]')].map(
        (region) => region.dataset['region'],
      ),
    ).toEqual(['primary', 'secondary', 'after']);
    expect(
      [...element.querySelectorAll<HTMLElement>('main h1, main h2')].map((heading) =>
        heading.textContent?.trim(),
      ),
    ).toEqual([
      'Project home',
      'Introduction',
      'Current delivery',
      'Current work',
      'Documentation',
    ]);
  });

  it('renders empty regions without inventing content', () => {
    const single = render({ layout: 'single', sections: [] });
    expect(
      (single.nativeElement as HTMLElement).querySelector('[data-region="single"]'),
    ).not.toBeNull();
    expect((single.nativeElement as HTMLElement).querySelector('h1, h2, footer')).toBeNull();

    const split = render({ layout: 'split', primary: [], secondary: [], after: [] });
    const splitElement = split.nativeElement as HTMLElement;
    expect(splitElement.querySelector('[data-region="primary"]')).not.toBeNull();
    expect(splitElement.querySelector('[data-region="secondary"]')).not.toBeNull();
    expect(splitElement.querySelector('[data-region="after"]')).toBeNull();
  });

  it('fails closed for unsupported runtime layouts and section discriminators', () => {
    const invalidLayout = render({ layout: 'unsupported' } as unknown as OverviewCompositionData);
    expect((invalidLayout.nativeElement as HTMLElement).querySelector('[data-region]')).toBeNull();

    const invalidSection = render({
      layout: 'single',
      sections: [{ type: 'unsupported', title: 'Do not render' } as unknown as OverviewSectionData],
    });
    const invalidSectionElement = invalidSection.nativeElement as HTMLElement;
    expect(invalidSectionElement.textContent).not.toContain('Do not render');
    expect(invalidSectionElement.querySelector('pm-overview-composition-section')).not.toBeNull();
  });
});
