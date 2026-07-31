import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ProjectWikiLinkDialog } from './project-wiki-link-dialog';

describe('ProjectWikiLinkDialog', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ProjectWikiLinkDialog],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('inserts a canonical wiki reference using selected editor text', async () => {
    const fixture = TestBed.createComponent(ProjectWikiLinkDialog);
    const inserted: string[] = [];
    fixture.componentInstance.inserted.subscribe((value) => inserted.push(value));
    fixture.componentRef.setInput('initialLabel', 'Shared architecture');
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/v1/project/links').flush({
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
    http.expectOne('/api/v1/wiki/pages').flush([
      {
        path: 'architecture/shared model',
        title: 'Shared model',
        modifiedAt: '2026-07-31T10:00:00Z',
      },
    ]);
    await TestBed.tick();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('dialog')?.open).toBe(true);
    expect(element.querySelectorAll('[role="option"]')).toHaveLength(1);
    (element.querySelector('[role="option"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (element.querySelector('footer .pm-button--primary') as HTMLButtonElement).click();

    expect(inserted).toEqual([
      '[Shared architecture](pm://project/prj_current/wiki/architecture/shared%20model)',
    ]);
  });
});
