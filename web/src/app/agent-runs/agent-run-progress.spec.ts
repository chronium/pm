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
    buttons[0]!.click();

    expect(emitted).toHaveBeenCalledWith(runArtifacts[0]);
    expect(buttons[0]!.textContent).toContain('Download');
    expect(buttons[1]!.textContent).toContain('Retry');
    expect(element.querySelector('[role="status"]')?.textContent).toContain(
      'Integrity verification failed.',
    );
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
});
