import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ProjectSwitcher } from './project-switcher';

describe('ProjectSwitcher', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      imports: [ProjectSwitcher],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('renders readable family members and keeps unavailable projects disabled', async () => {
    const fixture = await renderSwitcher();

    const element = fixture.nativeElement as HTMLElement;
    const child = findChildLink(element);
    expect(element.querySelector('summary')?.textContent).toContain('Active');
    expect(child).toBeDefined();
    expect(child!.textContent).toContain('Read-only');
    expect(child!.getAttribute('href')).toBe('/projects/prj_child/tasks');
    expect(element.querySelector('.project-switcher-unavailable')?.textContent).toContain(
      'Missing',
    );
    expect(element.querySelector('a[href*="prj_missing"]')).toBeNull();
  });

  it('restores filters remembered for the selected project', async () => {
    sessionStorage.setItem('pm.task-filters.v1.prj_child', 'track=GAME&state=todo');

    const fixture = await renderSwitcher();

    expect(findChildLink(fixture.nativeElement as HTMLElement)?.getAttribute('href')).toBe(
      '/projects/prj_child/tasks?track=GAME&state=todo',
    );
  });
});

async function renderSwitcher() {
  const fixture = TestBed.createComponent(ProjectSwitcher);
  fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  http.expectOne('/api/v1/project').flush({
    projectId: 'prj_active',
    name: 'Active',
    accent: 'teal',
    relationship: 'current',
    readOnly: false,
    revision: 'active-revision',
  });
  http.expectOne('/api/v1/project/links').flush({
    activeProjectId: 'prj_active',
    members: [
      {
        projectId: 'prj_active',
        name: 'Active',
        alias: null,
        relationship: 'current',
        status: 'resolved',
        source: 'current',
        readable: true,
        writeTrusted: true,
      },
      {
        projectId: 'prj_child',
        name: 'Child',
        alias: 'child',
        relationship: 'child',
        status: 'resolved',
        source: 'manifest',
        readable: true,
        writeTrusted: false,
      },
      {
        projectId: 'prj_missing',
        name: 'Missing',
        alias: 'missing',
        relationship: 'child',
        status: 'missing',
        source: 'manifest',
        readable: false,
        writeTrusted: false,
      },
    ],
    warnings: [],
  });
  await TestBed.tick();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture;
}

function findChildLink(element: HTMLElement): HTMLAnchorElement | undefined {
  return [...element.querySelectorAll<HTMLAnchorElement>('.project-switcher-menu a')].find((link) =>
    link.textContent?.includes('Child'),
  );
}
