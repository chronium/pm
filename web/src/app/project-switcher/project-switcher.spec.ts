import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ProjectSwitcher } from './project-switcher';

describe('ProjectSwitcher', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [ProjectSwitcher],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }),
  );

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('renders readable family members and keeps unavailable projects disabled', async () => {
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
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('summary')?.textContent).toContain('Active');
    expect(element.querySelector('a[href="/projects/prj_child/tasks"]')?.textContent).toContain(
      'Read-only',
    );
    expect(element.querySelector('.project-switcher-unavailable')?.textContent).toContain(
      'Missing',
    );
    expect(element.querySelector('a[href*="prj_missing"]')).toBeNull();
  });
});
