import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { LinkedProjects } from './linked-projects';

const current = {
  projectId: 'prj_current',
  name: 'Current',
  alias: null,
  relationship: 'current',
  status: 'available',
  source: 'current',
  readable: true,
  writeTrusted: false,
};
const child = {
  projectId: 'prj_child',
  name: 'Child',
  alias: 'child',
  relationship: 'child',
  status: 'available',
  source: 'registry',
  readable: true,
  writeTrusted: false,
};

describe('LinkedProjects', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [LinkedProjects],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
  });

  it('confirms and grants private write trust for a readable linked project', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const fixture = TestBed.createComponent(LinkedProjects);
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/project/links').flush({
      activeProjectId: current.projectId,
      members: [current, child],
      warnings: [],
    });
    await TestBed.tick();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const button = [...fixture.nativeElement.querySelectorAll('button')].find(
      (candidate: HTMLButtonElement) => candidate.textContent?.trim() === 'Trust writes',
    ) as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
    const request = http.expectOne('/api/v1/project/links/prj_child/write-trust');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('X-PM-Client')).toBe('angular-web');
    request.flush({
      activeProjectId: current.projectId,
      members: [current, { ...child, writeTrusted: true }],
      warnings: [],
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Write trusted');
    expect(fixture.nativeElement.textContent).toContain('Revoke trust');
  });
});
