import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { ProjectApiService } from './project-api.service';

describe('ProjectApiService', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('loads typed project metadata from the versioned API', async () => {
    const service = TestBed.inject(ProjectApiService);
    expect(service.projectName()).toBe('PM');
    TestBed.tick();

    const request = TestBed.inject(HttpTestingController).expectOne('/api/v1/project');
    expect(request.request.method).toBe('GET');
    request.flush({
      projectId: 'project-1',
      name: 'Typed Project',
      accent: 'amber',
      relationship: 'current',
      readOnly: false,
      revision: 'project-revision',
    });
    await TestBed.tick();
    TestBed.flushEffects();

    expect(service.projectName()).toBe('Typed Project');
    expect(document.documentElement.dataset['accent']).toBe('amber');
  });
});
