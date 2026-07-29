import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AgentRunPatchCollection } from './agent-run-patch-collection';
import type { AgentRunPatchPreflightResult } from './agent-runs-api.service';

const preflight: AgentRunPatchPreflightResult = {
  ready: true,
  revision: 'patch-r1',
  artifactId: 'changes-patch',
  artifactSha256: 'ab'.repeat(32),
  baseCommit: '12'.repeat(20),
  currentHead: '12'.repeat(20),
  taskRevision: 'cd'.repeat(32),
  currentTaskRevision: 'cd'.repeat(32),
  checks: [{ id: 'base', label: 'Exact base commit', status: 'passed', summary: 'Base matches.' }],
  warnings: ['One non-overlapping local path will be preserved.'],
  paths: [
    { path: 'PM/TaskService.cs', status: 'modified', insertions: 3, deletions: 1, binary: false },
  ],
  statistics: { filesChanged: 1, insertions: 3, deletions: 1, binaryFiles: 0 },
};

describe('AgentRunPatchCollection', () => {
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AgentRunPatchCollection],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('reviews an ETag-bound preflight before applying the verified artifact', async () => {
    const fixture = TestBed.createComponent(AgentRunPatchCollection);
    fixture.componentRef.setInput('runId', 'run-01K123');
    fixture.componentRef.setInput('open', true);
    const collected = vi.fn();
    fixture.componentInstance.collected.subscribe(collected);
    fixture.detectChanges();

    http
      .expectOne('/api/v1/runs/run-01K123/patch-collection/preflight')
      .flush(preflight, { headers: { ETag: '"patch-r1"' } });
    await Promise.resolve();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('PM/TaskService.cs');
    expect(element.textContent).toContain('One non-overlapping local path');
    element.querySelector<HTMLButtonElement>('.pm-button--primary')!.click();

    const apply = http.expectOne('/api/v1/runs/run-01K123/patch-collection/apply');
    expect(apply.request.headers.get('If-Match')).toBe('"patch-r1"');
    expect(apply.request.body).toEqual({ artifactSha256: preflight.artifactSha256 });
    apply.flush({
      runId: 'run-01K123',
      artifactId: preflight.artifactId,
      artifactSha256: preflight.artifactSha256,
      baseCommit: preflight.baseCommit,
      headCommit: preflight.currentHead,
      paths: ['PM/TaskService.cs'],
      appliedAt: '2026-07-29T13:00:00Z',
    });
    await Promise.resolve();

    expect(collected).toHaveBeenCalledOnce();
  });

  it('keeps collection disabled when a safety check fails', async () => {
    const fixture = TestBed.createComponent(AgentRunPatchCollection);
    fixture.componentRef.setInput('runId', 'run-01K123');
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    http.expectOne('/api/v1/runs/run-01K123/patch-collection/preflight').flush({
      ...preflight,
      ready: false,
      checks: [
        {
          id: 'overlap',
          label: 'Local worktree overlap',
          status: 'failed',
          summary: 'Local changes overlap PM/TaskService.cs.',
        },
      ],
    });
    await Promise.resolve();
    fixture.detectChanges();

    expect(
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('.pm-button--primary')
        ?.disabled,
    ).toBe(true);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Local changes overlap');
  });
});
