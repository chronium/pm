import { TestBed } from '@angular/core/testing';

import { projectCheckpoints } from './agent-run-events';
import { AgentRunProgress } from './agent-run-progress';
import { runArtifacts, runInspection } from './agent-runs.fixtures';

describe('AgentRunProgress', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AgentRunProgress] }).compileComponents();
  });

  afterEach(() => TestBed.resetTestingModule());

  it('renders lifecycle checkpoints, run context, and artifact metadata', () => {
    const fixture = TestBed.createComponent(AgentRunProgress);
    fixture.componentRef.setInput('inspection', runInspection);
    fixture.componentRef.setInput(
      'checkpoints',
      projectCheckpoints(
        new Set(['accepted', 'queued', 'preparing_workspace', 'starting_runtime', 'running']),
        'running',
        'Codex thread started',
      ),
    );
    fixture.componentRef.setInput('artifacts', runArtifacts);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelectorAll('.checkpoint-list li')).toHaveLength(7);
    expect(element.querySelector('[data-status="active"]')?.textContent).toContain(
      'Codex execution',
    );
    expect(element.textContent).toContain('runner-linux');
    expect(element.textContent).toContain('changes.patch');
    expect(element.textContent).toContain('4.0 KiB');
    expect(element.querySelector<HTMLButtonElement>('.artifact-download')?.textContent).toContain(
      'Download',
    );
  });

  it('emits the selected artifact and renders isolated download state', () => {
    const fixture = TestBed.createComponent(AgentRunProgress);
    fixture.componentRef.setInput('inspection', runInspection);
    fixture.componentRef.setInput('checkpoints', []);
    fixture.componentRef.setInput('artifacts', runArtifacts);
    fixture.componentRef.setInput('artifactDownloads', {
      'event-log': { status: 'error', message: 'Integrity verification failed.' },
    });
    const emitted = vi.fn();
    fixture.componentInstance.downloadRequested.subscribe(emitted);
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const buttons = element.querySelectorAll<HTMLButtonElement>('.artifact-download');
    const firstDownload = [...buttons].find((button) => button.textContent?.trim() === 'Download')!;
    firstDownload.click();

    expect(emitted).toHaveBeenCalledWith(runArtifacts[0]);
    expect(firstDownload.textContent).toContain('Download');
    expect([...buttons].some((button) => button.textContent?.includes('Retry'))).toBe(true);
    expect(element.querySelector('[role="status"]')?.textContent).toContain(
      'Integrity verification failed.',
    );
  });

  it('offers collection only for a completed retained patch', () => {
    const fixture = TestBed.createComponent(AgentRunProgress);
    fixture.componentRef.setInput('inspection', {
      ...runInspection,
      run: { ...runInspection.run, state: 'completed' },
    });
    fixture.componentRef.setInput('checkpoints', []);
    fixture.componentRef.setInput('artifacts', runArtifacts);
    const collected = vi.fn();
    fixture.componentInstance.collectRequested.subscribe(collected);
    fixture.detectChanges();

    const collect = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.artifact-collect',
    )!;
    collect.click();

    expect(collect.textContent).toContain('Review & collect');
    expect(collected).toHaveBeenCalledOnce();
  });

  it('makes task-revision drift explicit without replacing run progress', () => {
    const fixture = TestBed.createComponent(AgentRunProgress);
    fixture.componentRef.setInput('inspection', {
      ...runInspection,
      taskChanged: true,
      currentTaskRevision: 'task-r3',
    });
    fixture.componentRef.setInput(
      'checkpoints',
      projectCheckpoints(new Set(['accepted', 'running']), 'running', null),
    );
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('.drift-notice')?.getAttribute('role')).toBe('status');
    expect(element.textContent).toContain('Task changed after launch');
    expect(element.textContent).toContain('task-r1');
    expect(element.textContent).toContain('task-r3');
    expect(element.textContent).toContain('Codex execution');
  });

  it('renders a stable failure code and operator action at the run outcome', () => {
    const fixture = TestBed.createComponent(AgentRunProgress);
    fixture.componentRef.setInput('inspection', {
      ...runInspection,
      run: { ...runInspection.run, state: 'failed' },
    });
    fixture.componentRef.setInput(
      'checkpoints',
      projectCheckpoints(
        new Set(['accepted', 'preparing_workspace', 'failed']),
        'failed',
        'The runner could not fetch the repository.',
        {
          code: 'repository_fetch_failed',
          stage: 'workspace',
          summary: 'The runner could not fetch the repository.',
          recommendedAction: 'Check runner network access and launch a new run.',
          retryable: true,
        },
      ),
    );
    fixture.detectChanges();

    const failure = (fixture.nativeElement as HTMLElement).querySelector('.failure-detail')!;
    expect(failure.getAttribute('role')).toBe('alert');
    expect(failure.textContent).toContain('repository_fetch_failed');
    expect(failure.textContent).toContain('Check runner network access and launch a new run.');
    expect(failure.textContent).toContain('A new run may be retried.');
  });
});
